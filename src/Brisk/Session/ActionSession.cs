using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BriskEngine.Actions;
using BriskEngine.Actions.Rules;
using BriskEngine.Cleaning;
using BriskEngine.Models;

namespace Brisk.Session;

public enum SessionState { Looking, Ready, Running, Done, Failed }

/// The window's one state machine: the plan, the Yer aç level, the skips,
/// the run and its receipt. No WPF. Every change from a background thread is
/// applied through `post` (the dispatcher in the app, a lock in tests) and
/// announced by Changed. One operation at a time: a scan, run or undo asked
/// for while another is going is refused.
///
/// What runs:
/// - a Space row when its level is at or under the chosen level and it is
///   not skipped; a row above the level is locked;
/// - a Speed or Privacy row when it is on by default and not skipped, or off
///   by default and added by a tap;
/// - Hepsini yap (RunAllAsync): Yer aç at MIN and the default Speed and
///   Privacy rows, minus every skip; a row added by a tap is a page's alone.
/// Nothing asks for a confirmation first.
public sealed class ActionSession
{
    private readonly IActionBackend _backend;
    private readonly Action<Action> _post;
    private readonly object _gate = new();
    // A row's state where the user changed it: false = skipped, true = taken
    // (a row off by default that a tap added). No entry = the row's default.
    private readonly Dictionary<string, bool> _rowOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _fileOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RowResult> _results = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TargetScanResult> _sized = new();
    private bool _busy;
    // Busy with a run or an undo, not a look: from the press until it ends.
    private bool _acting;
    private CancellationTokenSource? _personalWalk;

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
    /// The group the run was pressed on; null for Hepsini yap.
    public ActionGroupKind? RunGroup { get; private set; }
    public RunRecord? LastRun { get; private set; }
    public bool RanThisSession { get; private set; }
    public Receipt? Receipt { get; private set; }
    public IReadOnlyList<RowResult>? UndoResults { get; private set; }
    /// Why the last undo did not happen; kept through the rescan after it.
    public string? UndoError { get; private set; }

    /// How far Yer aç goes. MIN until the user picks another; kept through a
    /// rescan.
    public SpaceLevel Level { get; private set; } = SpaceLevel.Min;

    /// The user's own large files, locked and never counted. Each look walks
    /// for them off the UI thread; until the walk lands the list is empty and
    /// PersonalFilesReady false. A walk that cannot read is an empty list.
    public IReadOnlyList<PersonalFile> PersonalFiles { get; private set; } = Array.Empty<PersonalFile>();
    public bool PersonalFilesReady { get; private set; }

    public bool Busy { get { lock (_gate) return _busy; } }

    /// Every row known so far: the plan once it is whole, the arrived halves before.
    public IEnumerable<ActionItem> Items => Plan?.Items
        ?? (SpaceGroup?.Items ?? Enumerable.Empty<ActionItem>()).Concat(FastGroups.SelectMany(g => g.Items));

    /// Geri al: only for a run that changed a Speed or Privacy setting. Yer aç
    /// has none, so a run that holds Space rules alone (0.11.0 recorded
    /// hibernation as undoable) offers no undo.
    public bool CanUndo => !Busy && LastRun is { UndoneUtc: null } run && run.UndoableRuleIds.Any(UndoesSpeedOrPrivacy);

    /// The rules of the Speed and Privacy rows, and every startup entry. A
    /// row's own id is taken too: it names the same switch, and no Space rule
    /// shares it.
    private static readonly HashSet<string> SpeedAndPrivacyRules = ActionCatalog.Rows
        .Where(r => r.Group != ActionGroupKind.Space)
        .SelectMany(r => r.LinkIds.Append(r.Id))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool UndoesSpeedOrPrivacy(string ruleId) =>
        SpeedAndPrivacyRules.Contains(ruleId) || StartupEntryRule.TryParseId(ruleId, out _, out _);

    /// The page each Speed or Privacy rule belongs to: its catalog row's
    /// group; a startup entry is Hızlandır's.
    private static readonly Dictionary<string, ActionGroupKind> RuleGroups = ActionCatalog.Rows
        .Where(r => r.Group != ActionGroupKind.Space)
        .SelectMany(r => r.LinkIds.Append(r.Id).Select(id => (Id: id, r.Group)))
        .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().Group, StringComparer.OrdinalIgnoreCase);

    private static ActionGroupKind? GroupOfRule(string ruleId) =>
        RuleGroups.TryGetValue(ruleId, out var g) ? g
        : StartupEntryRule.TryParseId(ruleId, out _, out _) ? ActionGroupKind.Speed
        : null;

    /// Geri al on a page: the last run can be undone and changed one of
    /// this page's settings (ruling R31: also after a rescan).
    public bool CanUndoFor(ActionGroupKind group) =>
        CanUndo && LastRun!.UndoableRuleIds.Any(r => GroupOfRule(r) == group);

    // ---- level and skips

    private ActionItem? Find(string rowId) =>
        Items.FirstOrDefault(i => string.Equals(i.Id, rowId, StringComparison.OrdinalIgnoreCase));

    private static bool InLevel(ActionItem item, SpaceLevel level) => item.Level is { } l && l <= level;

    /// A Space row above the chosen level: shown, dimmed, not tappable.
    public bool IsLocked(ActionItem item) => IsLockedAt(item, Level);

    /// The same at a given level: Yer aç shows MIN's locks during and after
    /// a Hepsini yap run, whatever level is chosen (ruling R49).
    public static bool IsLockedAt(ActionItem item, SpaceLevel level) =>
        item.Group == ActionGroupKind.Space && !InLevel(item, level);

    /// A row's default before any tap: a Space row is taken (its level
    /// decides), a Speed or Privacy row as the plan ticks it.
    private static bool DefaultTaken(ActionItem item) => item.Group == ActionGroupKind.Space || item.DefaultOn;

    /// True when the row will not run although its page could take it: tapped
    /// out, or off by default and not added. A Space row keeps its skip while
    /// it is locked, and gets it back when its level is chosen again.
    public bool IsSkipped(string rowId)
    {
        if (_rowOverrides.TryGetValue(rowId, out var taken)) return !taken;
        return Find(rowId) is { } item && !DefaultTaken(item);
    }

    /// A run or an undo holds the session, from the press on (before the
    /// Running state lands). A look does not: rows may be tapped and the
    /// level picked while it goes.
    private bool Acting { get { lock (_gate) return _acting; } }

    /// One tap on a row: skip it, or take it back. Does nothing to a locked
    /// row, an unknown row, or while a run or undo is going.
    public void ToggleSkip(string rowId)
    {
        if (Acting || Find(rowId) is not { } item || IsLocked(item)) return;
        Override(item, taken: IsSkipped(item.Id));
        Changed?.Invoke();
    }

    private void Override(ActionItem item, bool taken)
    {
        if (taken == DefaultTaken(item)) _rowOverrides.Remove(item.Id);
        else _rowOverrides[item.Id] = taken;
    }

    /// Refused while a run or an undo is going: the run takes the selection
    /// of the moment it was pressed, and nothing moves under it. Free during
    /// a look, which does not read the level.
    public void SetLevel(SpaceLevel level)
    {
        if (Acting || level == Level) return;
        Level = level;
        Changed?.Invoke();
    }

    /// Σ known sizes of the Space rows at or under `level` that are not
    /// skipped (the Downloads row: its ticked files). A row with no size
    /// adds nothing; personal files are not rows and never count.
    public long LevelBytes(SpaceLevel level) => Items
        .Where(i => i.Group == ActionGroupKind.Space && InLevel(i, level) && !IsSkipped(i.Id))
        .Sum(i => i.Bytes is null ? 0 : SelectedBytes(i));

    // ---- the older tick words, kept for the pages until they move to taps

    /// Will run in its page's run (a row's files aside).
    public bool IsTicked(string rowId) => Find(rowId) is { } item
        ? !IsLocked(item) && !IsSkipped(rowId)
        : _rowOverrides.TryGetValue(rowId, out var taken) && taken;

    /// Sets a row as taken or skipped; a locked row, a run and an undo
    /// ignore it (Changed still fires so a box can spring back).
    public void SetTicked(string rowId, bool on)
    {
        if (!Acting)
        {
            if (Find(rowId) is not { } item) _rowOverrides[rowId] = on;
            else if (!IsLocked(item)) Override(item, on);
        }
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

    private bool HasWork(ActionItem item) => item.Files.Count == 0 || item.Files.Any(f => IsFileTicked(f.Path));

    /// In its page's run: within the level, not skipped, able to do
    /// something (a Downloads row with no ticked file is not).
    public bool WillRun(ActionItem item) => !IsLocked(item) && !IsSkipped(item.Id) && HasWork(item);

    /// In Hepsini yap: a Space row at MIN, a Speed or Privacy row on by
    /// default; not skipped, able to do something.
    public bool InRunAll(ActionItem item) =>
        (item.Group == ActionGroupKind.Space ? InLevel(item, SpaceLevel.Min) : item.DefaultOn)
        && !IsSkipped(item.Id) && HasWork(item);

    private HashSet<string> TickedFiles(ActionPlan plan) => plan.Items.SelectMany(i => i.Files)
        .Where(f => IsFileTicked(f.Path)).Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private ActionSelection PageSelection(ActionPlan plan, ActionGroupKind group) => new(
        plan.Items.Where(i => i.Group == group && WillRun(i)).Select(i => i.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase),
        TickedFiles(plan));

    /// The engine's own level selection at MIN; a Downloads row with no
    /// ticked file counts as skipped.
    private ActionSelection RunAllSelection(ActionPlan plan)
    {
        var skipped = plan.Items.Where(i => IsSkipped(i.Id) || !HasWork(i)).Select(i => i.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ActionSelection.ForLevel(plan, SpaceLevel.Min, skipped) with { Files = TickedFiles(plan) };
    }

    // ---- operations

    /// acting: a run or an undo, as opposed to a look.
    private bool TryBegin(bool acting)
    {
        lock (_gate)
        {
            if (_busy) return false;
            _busy = true;
            _acting = acting;
            return true;
        }
    }

    private void End()
    {
        lock (_gate)
        {
            _busy = false;
            _acting = false;
        }
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

    public Task LookAsync() => TryBegin(acting: false) ? LookCore() : Task.CompletedTask;

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
            PersonalFiles = Array.Empty<PersonalFile>();
            PersonalFilesReady = false;
        });
        WalkPersonalFiles();
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
                return (Run: run, Receipt: run is null ? null : SafeReceipt(run.RunId));
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

    /// The personal-files walk takes seconds (~4 s warm, ~16 s cold on the
    /// maintainer's laptop), so it runs beside the look without holding up
    /// Ready: the plan and Hepsini yap do not wait for a list that never
    /// counts. A newer look cancels the older walk, and an answer from a
    /// cancelled walk never lands.
    private void WalkPersonalFiles()
    {
        var walk = new CancellationTokenSource();
        Interlocked.Exchange(ref _personalWalk, walk)?.Cancel();
        var ct = walk.Token;
        _ = Task.Run(() =>
        {
            IReadOnlyList<PersonalFile> found;
            try { found = _backend.PersonalFiles(ct); }
            catch (Exception) { found = Array.Empty<PersonalFile>(); }
            if (ct.IsCancellationRequested) return;
            Apply(() =>
            {
                if (ct.IsCancellationRequested) return;
                PersonalFiles = found;
                PersonalFilesReady = true;
            });
        });
    }

    /// Hepsini yap: one run of Yer aç at MIN and the default Speed and
    /// Privacy rows, minus every skip, whatever level the Yer aç page shows.
    /// The selection is taken now, at the press.
    public Task RunAllAsync()
    {
        if (State != SessionState.Ready || Plan is not { } plan || !TryBegin(acting: true)) return Task.CompletedTask;
        return RunCore(null, plan, RunAllSelection(plan));
    }

    /// One page's run: Space at the chosen level, Speed or Privacy as tapped.
    /// The selection is taken now, at the press; a level picked during the
    /// run is refused and a tap ignored.
    public Task RunAsync(ActionGroupKind group)
    {
        if (State != SessionState.Ready || Plan is not { } plan || !TryBegin(acting: true)) return Task.CompletedTask;
        return RunCore(group, plan, PageSelection(plan, group));
    }

    private async Task RunCore(ActionGroupKind? group, ActionPlan plan, ActionSelection selection)
    {
        // Skips can leave nothing: then nothing runs, and no empty run is
        // recorded over the last real one.
        if (selection.RowIds.Count == 0)
        {
            Finish(() => { });
            return;
        }
        Interlocked.Increment(ref _receiptSeq);
        Apply(() =>
        {
            State = SessionState.Running;
            RunGroup = group;
            _results.Clear();
            UndoResults = null;
            UndoError = null;
            // The last run's receipt says nothing about this one.
            Receipt = null;
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
                return (record, SafeReceipt(record.RunId));
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
        if (!CanUndo || !TryBegin(acting: true)) return;
        var run = LastRun!;
        // Geri al reverses Speed and Privacy alone: a Space rule the run holds
        // (0.11.0 recorded hibernation as undoable) stays as the run left it.
        var rules = run.UndoableRuleIds.Where(UndoesSpeedOrPrivacy).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Apply(() => UndoError = null);
        try
        {
            var results = await Task.Run(() => _backend.Undo(run.RunId, rules)).ConfigureAwait(false);
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
            // Kept through the rescan that follows; the run stays undoable.
            Finish(() => UndoError = ex.Message);
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
        var seq = Interlocked.Increment(ref _receiptSeq);
        var receipt = await Task.Run(() => SafeReceipt(run.RunId)).ConfigureAwait(false);
        Apply(() =>
        {
            // Not if a newer refresh started, nor if a newer run has taken over.
            if (receipt is not null && seq == Volatile.Read(ref _receiptSeq)
                && LastRun?.RunId == run.RunId)
                Receipt = receipt;
        });
    }

    /// A receipt that cannot be read is no receipt: it never fails a run or a plan.
    private Receipt? SafeReceipt(string runId)
    {
        try { return _backend.RefreshReceipt(runId); }
        catch (Exception) { return null; }
    }
}
