using BriskEngine.Actions;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Live;
using BriskEngine.Models;
using Brisk.Session;

namespace Brisk.Tests.Session;

/// The engine as the window sees it, scripted. Everything is synchronous;
/// HoldRun blocks a run until a test releases it, HoldPersonal the walk for
/// personal files. Nothing here touches the machine (ruling R6).
public sealed class FakeBackend : IActionBackend
{
    public List<ActionGroup> Fast = new();
    public ActionGroup? Space;
    public List<TargetScanResult> Sized = new();
    public Func<ActionItem, RowResult> Outcome = i => new RowResult(i.Id, RowOutcome.Done, null, null, i.Bytes ?? 0, 0);
    public Exception? FailFast, FailSpace;
    public ManualResetEventSlim? HoldRun;
    public List<ActionSelection> Runs = new();
    public RunRecord? Latest;
    public Receipt? ReceiptToReturn;
    public int ReceiptRefreshes;
    public List<string> Undone = new();
    public int DeferredProcessed;
    public List<DeferredCleanQueue.Entry> Deferred = new();
    public List<string> DeferredAppNames = new();
    public int Enabled = 14;
    public List<StartupEntry> Startup = new();
    public List<AppLoad> Load = new();
    public int LoadSamples;
    public ConnectionSnapshot Connections = new(Array.Empty<ConnectionRow>(), true);
    public Exception? FailConnections;
    public int ConnectionReads;
    public IReadOnlyList<UsageRow>? Usage = Array.Empty<UsageRow>();
    public int UsageReads;
    public List<int> Closed = new(), Killed = new();
    public HashSet<int> Alive = new();

    public IReadOnlyList<ActionGroup> BuildFast() => FailFast is null ? Fast : throw FailFast;

    /// Blocks the space half of a look until a test releases it.
    public ManualResetEventSlim? HoldScan;

    public ScanResult ScanSpace(Action<TargetScanResult> onTarget)
    {
        HoldScan?.Wait(TimeSpan.FromSeconds(10));
        if (FailSpace is not null) throw FailSpace;
        foreach (var t in Sized) onTarget(t);
        return new ScanResult(Sized);
    }

    public ActionGroup? BuildSpace(ScanResult scan) => Space;

    public RunRecord Run(ActionPlan plan, ActionSelection selection, Action<ActionItem> onRowStart, Action<RowResult> onRow)
    {
        Runs.Add(selection);
        HoldRun?.Wait(TimeSpan.FromSeconds(10));
        var rows = new List<RowResult>();
        var undoable = new List<string>();
        foreach (var item in plan.Items.Where(i => selection.RowIds.Contains(i.Id)))
        {
            onRowStart(item);
            var r = Outcome(item);
            rows.Add(r);
            onRow(r);
            // Like the engine: a reversible settings row that went through
            // records its rules, whatever group it sits in.
            if (item.LinkKind == ActionLinkKind.Rules && item.Reversible
                && r.Outcome is RowOutcome.Done or RowOutcome.PendingRestart)
                undoable.AddRange(item.LinkIds);
        }
        Latest = new RunRecord("run-1", DateTime.UtcNow, DateTime.UtcNow, rows, undoable);
        return Latest;
    }

    public Exception? FailUndo;
    public Func<string, RowResult>? UndoOutcome;
    public ManualResetEventSlim? HoldUndo;
    /// Every rule an undo was asked to reverse, in the order asked.
    public List<string> UndoneRules = new();

    /// Like the engine: only the rules asked for, newest first; the run is
    /// stamped undone only when every one of them came back.
    public IReadOnlyList<RowResult> Undo(string runId, IReadOnlySet<string> ruleIds)
    {
        HoldUndo?.Wait(TimeSpan.FromSeconds(10));
        if (FailUndo is not null) throw FailUndo;
        Undone.Add(runId);
        var asked = Latest!.UndoableRuleIds.Reverse().Where(ruleIds.Contains).ToList();
        UndoneRules.AddRange(asked);
        var results = asked
            .Select(id => UndoOutcome?.Invoke(id) ?? new RowResult(id, RowOutcome.Done, null, null, 0, 0)).ToList();
        if (results.All(r => r.Outcome == RowOutcome.Done)) Latest = Latest with { UndoneUtc = DateTime.UtcNow };
        return results;
    }

    public RunRecord? LatestRun() => Latest;
    /// When set, decides each refresh by its call number (1-based).
    public Func<int, Receipt?>? ReceiptByCall;

    public Receipt? RefreshReceipt(string runId)
    {
        var n = Interlocked.Increment(ref ReceiptRefreshes);
        return ReceiptByCall is { } by ? by(n) : ReceiptToReturn;
    }
    public void ProcessDeferred() => DeferredProcessed++;
    public IReadOnlyList<DeferredCleanQueue.Entry> DeferredPending() => Deferred;
    public IReadOnlyList<string> DeferredApps() => DeferredAppNames;
    public int EnabledStartupCount() => Enabled;
    public IReadOnlyList<StartupEntry> StartupEntries() => Startup;
    public IReadOnlyList<AppLoad> SampleLoad() { LoadSamples++; return Load; }

    public ConnectionSnapshot ReadConnections()
    {
        ConnectionReads++;
        return FailConnections is null ? Connections : throw FailConnections;
    }

    public IReadOnlyList<UsageRow>? ReadUsage() { UsageReads++; return Usage; }
    public void Close(IReadOnlyList<int> pids) => Closed.AddRange(pids);
    public void Kill(IReadOnlyList<int> pids) { Killed.AddRange(pids); foreach (var p in pids) Alive.Remove(p); }
    public bool IsRunning(IReadOnlyList<int> pids) => pids.Any(Alive.Contains);

    public List<PersonalFile> Personal = new();
    /// When set, decides each walk's answer by its call number (1-based).
    public Func<int, IReadOnlyList<PersonalFile>>? PersonalByCall;
    public ManualResetEventSlim? HoldPersonal;
    /// A walk that does not look at its token and answers anyway.
    public bool PersonalIgnoresCancel;
    public Exception? FailPersonal;
    public List<CancellationToken> PersonalTokens = new();
    public int PersonalCalls;

    /// Waits while HoldPersonal is shut (or until the token is cancelled),
    /// then throws for a cancelled token, as PersonalFiles.Find does.
    public IReadOnlyList<PersonalFile> PersonalFiles(CancellationToken ct)
    {
        lock (PersonalTokens) PersonalTokens.Add(ct);
        var call = Interlocked.Increment(ref PersonalCalls);
        if (HoldPersonal is { } hold)
        {
            if (PersonalIgnoresCancel) hold.Wait(TimeSpan.FromSeconds(10));
            else WaitHandle.WaitAny(new[] { hold.WaitHandle, ct.WaitHandle }, TimeSpan.FromSeconds(10));
        }
        if (!PersonalIgnoresCancel) ct.ThrowIfCancellationRequested();
        if (FailPersonal is not null) throw FailPersonal;
        return PersonalByCall is { } by ? by(call) : Personal.ToList();
    }

    public long? FreeBytes = 100_000_000_000;
    public long? SystemDriveFreeBytes() => FreeBytes;

    public SpecsInfo SpecsToReturn = new("Test CPU", 2600, 6, 12, new[] { "Test GPU" }, 32L << 30, 31L << 30,
        "Test disk", 1_000_000_000_000);
    public int SpecsReads;
    public SpecsInfo Specs()
    {
        Interlocked.Increment(ref SpecsReads);
        return SpecsToReturn;
    }

    public MonitorSample MonitorToReturn = new(10, 5, 50, 16L << 30, 50, 45, 1_000, 20_000, null,
        Array.Empty<ProcLoad>(), Array.Empty<ProcLoad>(), Array.Empty<ProcLoad>());
    public int MonitorSamples;
    public MonitorSample SampleMonitor()
    {
        Interlocked.Increment(ref MonitorSamples);
        return MonitorToReturn;
    }

    /// A Space row's level defaults from its tick, as in the catalog: on =
    /// MIN, off = MID.
    public static ActionItem Row(string id, ActionGroupKind g, long? bytes = null, bool on = true,
        IReadOnlyList<ActionFile>? files = null, bool reversible = true, string? title = null,
        SpaceLevel? level = null, bool lost = false) =>
        new(id, g, null, title ?? id, null, null, null, null, bytes, on, false, false, reversible,
            g == ActionGroupKind.Space ? ActionLinkKind.Cleanup : ActionLinkKind.Rules,
            new[] { id }, files ?? Array.Empty<ActionFile>(),
            g == ActionGroupKind.Space ? level ?? (on ? SpaceLevel.Min : SpaceLevel.Mid) : null, lost);

    /// Levels as in the catalog: caches MIN; shaders, downloads (two files)
    /// and the recycle bin MID. Hepsini yap takes four rows: caches,
    /// Spotify, animations, ads. Not taken: the MID rows, OneDrive, location.
    public static FakeBackend Typical()
    {
        var b = new FakeBackend();
        b.Fast.Add(new ActionGroup(ActionGroupKind.Speed, new[]
        {
            Row("startup:HKCU:Spotify", ActionGroupKind.Speed, title: "Spotify"),
            Row("startup:HKCU:OneDrive", ActionGroupKind.Speed, on: false, title: "OneDrive"),
            Row("speed.animations", ActionGroupKind.Speed),
        }));
        b.Fast.Add(new ActionGroup(ActionGroupKind.Privacy, new[]
        {
            Row("privacy.ads", ActionGroupKind.Privacy),
            Row("privacy.location", ActionGroupKind.Privacy, on: false),
        }));
        b.Space = new ActionGroup(ActionGroupKind.Space, new[]
        {
            Row("space.caches", ActionGroupKind.Space, 6_000_000_000, reversible: false),
            Row("space.shaders", ActionGroupKind.Space, 2_000_000_000, on: false, reversible: false),
            Row("space.downloads", ActionGroupKind.Space, 300, on: false, reversible: false, lost: true, files: new[]
            {
                new ActionFile(@"C:\D\a.exe", 200, null), new ActionFile(@"C:\D\b.msi", 100, null),
            }),
            Row("space.recycle-bin", ActionGroupKind.Space, 8_000_000_000, on: false, reversible: false, lost: true),
        });
        return b;
    }
}

public static class TestReceipts
{
    public static Receipt Freed(string runId, long freed, int? bootBefore = null, int? bootAfter = null,
        int startupBefore = 14, int? startupAfter = 3, int privacyOff = 0) => new(
        runId, DateTime.UtcNow, DateTime.UtcNow, 100_000_000_000, 100_000_000_000 + freed, 0,
        startupBefore, startupAfter, Array.Empty<StartupRam>(), Array.Empty<string>(), false,
        bootBefore, bootAfter, privacyOff, 0, null, null);
}
