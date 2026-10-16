using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BriskEngine.Actions;
using BriskEngine.Cleaning;
using BriskEngine.Models;

namespace Brisk.Session;

public enum SessionState { Looking, Ready, Running, Done, Failed }

/// The window's one state machine: the plan, the ticks, the run and its
/// receipt. No WPF. Every change is applied through `post` (the dispatcher
/// in the app, a lock in tests) and announced by Changed. One operation at a
/// time: a scan, run or undo asked for while another is going is refused.
public sealed class ActionSession
{
    /// Rows whose deletions cannot come back; they get the one confirmation.
    public static readonly IReadOnlySet<string> Unrecoverable =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "space.downloads", "space.recycle-bin", "space.windows-old" };

    private readonly IActionBackend _backend;
    private readonly Action<Action> _post;
    private readonly object _gate = new();
    private readonly Dictionary<string, bool> _rowOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _fileOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RowResult> _results = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TargetScanResult> _sized = new();
    private bool _busy;

    public ActionSession(IActionBackend backend, Action<Action> post)
    {
        _backend = backend;
        _post = post;
    }

    public event Action? Changed;

    public SessionState State { get; private set; } = SessionState.Looking;
    public IReadOnlyList<ActionGroup> FastGroups { get; private set; } = Array.Empty<ActionGroup>();
    public ActionGroup? SpaceGroup { get; private set; }
    public bool SpaceReady { get; private set; }
    public ActionPlan? Plan { get; private set; }
    public IReadOnlyList<TargetScanResult> SizedSoFar => _sized;
    public int TargetCount { get; } = CleanupTargetRegistry.All.Count;
    public string? Error { get; private set; }
    public string? CurrentRowId { get; private set; }
    public IReadOnlyDictionary<string, RowResult> Results => _results;
    public ActionGroupKind? RunGroup { get; private set; }
    public RunRecord? LastRun { get; private set; }
    public bool RanThisSession { get; private set; }
    public Receipt? Receipt { get; private set; }
    public IReadOnlyList<RowResult>? UndoResults { get; private set; }

    public bool Busy { get { lock (_gate) return _busy; } }

    /// Every row known so far: the plan once it is whole, the arrived halves before.
    public IEnumerable<ActionItem> Items => Plan?.Items
        ?? (SpaceGroup?.Items ?? Enumerable.Empty<ActionItem>()).Concat(FastGroups.SelectMany(g => g.Items));

    public bool CanUndo => !Busy && LastRun is { UndoneUtc: null, UndoableRuleIds.Count: > 0 };

    // ---- ticks

    public bool IsTicked(string rowId) => _rowOverrides.TryGetValue(rowId, out var on)
        ? on
        : Items.FirstOrDefault(i => string.Equals(i.Id, rowId, StringComparison.OrdinalIgnoreCase))?.DefaultOn ?? false;

    public void SetTicked(string rowId, bool on)
    {
        _rowOverrides[rowId] = on;
        Changed?.Invoke();
    }

    public bool IsFileTicked(string path) => !_fileOverrides.TryGetValue(path, out var on) || on;

    public void SetFileTicked(string path, bool on)
    {
        _fileOverrides[path] = on;
        Changed?.Invoke();
    }

    /// Bytes a row would free as ticked now (the Downloads row: its ticked files).
    public long SelectedBytes(ActionItem item) => item.Files.Count > 0
        ? item.Files.Where(f => IsFileTicked(f.Path)).Sum(f => f.Bytes)
        : item.Bytes ?? 0;

    /// Ticked and able to do something: a Downloads row with no ticked file is not.
    public bool WillRun(ActionItem item) =>
        IsTicked(item.Id) && (item.Files.Count == 0 || item.Files.Any(f => IsFileTicked(f.Path)));

    public ActionSelection Selection(ActionGroupKind? group) => new(
        Items.Where(i => (group is null || i.Group == group) && WillRun(i)).Select(i => i.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase),
        Items.SelectMany(i => i.Files).Where(f => IsFileTicked(f.Path)).Select(f => f.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase));

    public IReadOnlyList<ActionItem> NeedsConfirmation(ActionGroupKind? group) => Items
        .Where(i => Unrecoverable.Contains(i.Id) && (group is null || i.Group == group) && WillRun(i))
        .ToList();

    // ---- operations

    private bool TryBegin()
    {
        lock (_gate)
        {
            if (_busy) return false;
            _busy = true;
            return true;
        }
    }

    private void End()
    {
        lock (_gate) _busy = false;
    }

    private void Apply(Action change) => _post(() =>
    {
        change();
        Changed?.Invoke();
    });

    /// The last change of an operation: busy ends first, so the change that
    /// announces the new state already allows the next operation (and undo).
    /// No `finally` ends it again — that could clear an operation the
    /// announcement itself started.
    private void Finish(Action change)
    {
        End();
        Apply(change);
    }

    public Task LookAsync() => TryBegin() ? LookCore() : Task.CompletedTask;

    public Task RescanAsync() => LookAsync();

    private async Task LookCore()
    {
        Apply(() =>
        {
            State = SessionState.Looking;
            Error = null;
            Plan = null;
            SpaceGroup = null;
            SpaceReady = false;
            FastGroups = Array.Empty<ActionGroup>();
            _sized.Clear();
            _results.Clear();
            CurrentRowId = null;
        });
        try
        {
            var fast = Task.Run(() =>
            {
                var groups = _backend.BuildFast();
                Apply(() => FastGroups = groups);
                return groups;
            });
            var space = Task.Run(() =>
            {
                var scan = _backend.ScanSpace(t => Apply(() => _sized.Add(t)));
                var group = _backend.BuildSpace(scan);
                Apply(() =>
                {
                    SpaceGroup = group;
                    SpaceReady = true;
                });
                return (Scan: scan, Group: group);
            });
            var previous = Task.Run(() =>
            {
                var run = _backend.LatestRun();
                return (Run: run, Receipt: run is null ? null : _backend.RefreshReceipt(run.RunId));
            });
            await Task.WhenAll(fast, space, previous).ConfigureAwait(false);
            var plan = ActionPlanBuilder.Compose(DateTime.UtcNow, space.Result.Group, fast.Result, space.Result.Scan);
            Finish(() =>
            {
                Plan = plan;
                // This session's own run (and its receipt) outlives "scan again".
                if (!RanThisSession)
                {
                    LastRun = previous.Result.Run;
                    Receipt = previous.Result.Receipt;
                }
                State = SessionState.Ready;
            });
        }
        catch (Exception ex)
        {
            var message = (ex as AggregateException)?.InnerException?.Message ?? ex.Message;
            Finish(() =>
            {
                State = SessionState.Failed;
                Error = message;
            });
        }
    }

    public Task RunAsync(ActionGroupKind? group, bool skipUnrecoverable = false)
    {
        if (State != SessionState.Ready || Plan is null || !TryBegin()) return Task.CompletedTask;
        return RunCore(group, skipUnrecoverable);
    }

    private async Task RunCore(ActionGroupKind? group, bool skipUnrecoverable)
    {
        if (skipUnrecoverable)
            foreach (var item in NeedsConfirmation(group)) _rowOverrides[item.Id] = false;
        var plan = Plan!;
        var selection = Selection(group);
        Apply(() =>
        {
            State = SessionState.Running;
            RunGroup = group;
            _results.Clear();
            UndoResults = null;
        });
        try
        {
            var (run, receipt) = await Task.Run(() =>
            {
                // As in the console: the queue is processed only when space is in the run.
                if (group is null or ActionGroupKind.Space) _backend.ProcessDeferred();
                var record = _backend.Run(plan, selection,
                    start => Apply(() => CurrentRowId = start.Id),
                    row => Apply(() => _results[row.RowId] = row));
                return (record, _backend.RefreshReceipt(record.RunId));
            }).ConfigureAwait(false);
            Finish(() =>
            {
                LastRun = run;
                Receipt = receipt;
                RanThisSession = true;
                CurrentRowId = null;
                State = SessionState.Done;
            });
        }
        catch (Exception ex)
        {
            Finish(() =>
            {
                CurrentRowId = null;
                State = SessionState.Failed;
                Error = ex.Message;
            });
        }
    }

    public async Task UndoAsync()
    {
        if (!CanUndo || !TryBegin()) return;
        var runId = LastRun!.RunId;
        try
        {
            var results = await Task.Run(() => _backend.Undo(runId)).ConfigureAwait(false);
            var latest = await Task.Run(_backend.LatestRun).ConfigureAwait(false);
            Finish(() =>
            {
                UndoResults = results;
                LastRun = latest;
                RanThisSession = false;
            });
        }
        catch (Exception ex)
        {
            Finish(() => Error = ex.Message);
        }
        await LookAsync().ConfigureAwait(false);
    }

    private int _receiptSeq;

    /// Fills in the after-restart numbers once they exist. Only the latest
    /// refresh lands: an older one finishing late must not put back a stale
    /// receipt over a newer one.
    public async Task RefreshReceiptAsync()
    {
        if (LastRun is not { } run) return;
        var seq = System.Threading.Interlocked.Increment(ref _receiptSeq);
        var receipt = await Task.Run(() => _backend.RefreshReceipt(run.RunId)).ConfigureAwait(false);
        Apply(() =>
        {
            if (seq == System.Threading.Volatile.Read(ref _receiptSeq)) Receipt = receipt;
        });
    }
}
