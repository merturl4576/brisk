using BriskEngine.Actions;
using BriskEngine.Diagnostics;
using BriskEngine.Live;
using BriskEngine.Models;
using Brisk.Session;

namespace Brisk.Tests.Session;

/// The engine as the window sees it, scripted. Everything is synchronous;
/// HoldRun blocks a run until a test releases it.
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

    public ScanResult ScanSpace(Action<TargetScanResult> onTarget)
    {
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
        foreach (var item in plan.Items.Where(i => selection.RowIds.Contains(i.Id)))
        {
            onRowStart(item);
            var r = Outcome(item);
            rows.Add(r);
            onRow(r);
        }
        var undoable = rows.Where(r => r.Outcome == RowOutcome.Done && !r.RowId.StartsWith("space."))
            .Select(r => r.RowId).ToList();
        Latest = new RunRecord("run-1", DateTime.UtcNow, DateTime.UtcNow, rows, undoable);
        return Latest;
    }

    public IReadOnlyList<RowResult> Undo(string runId)
    {
        Undone.Add(runId);
        Latest = Latest! with { UndoneUtc = DateTime.UtcNow };
        return Latest.UndoableRuleIds.Select(id => new RowResult(id, RowOutcome.Done, null, null, 0, 0)).ToList();
    }

    public RunRecord? LatestRun() => Latest;
    public Receipt? RefreshReceipt(string runId) { ReceiptRefreshes++; return ReceiptToReturn; }
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

    public static ActionItem Row(string id, ActionGroupKind g, long? bytes = null, bool on = true,
        IReadOnlyList<ActionFile>? files = null, bool reversible = true, string? title = null) =>
        new(id, g, null, title ?? id, null, null, null, null, bytes, on, false, false, reversible,
            g == ActionGroupKind.Space ? ActionLinkKind.Cleanup : ActionLinkKind.Rules,
            new[] { id }, files ?? Array.Empty<ActionFile>());

    /// Six ticked rows: caches, downloads (two files), recycle bin, Spotify,
    /// animations, ads. Unticked: shaders, OneDrive, location.
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
            Row("space.downloads", ActionGroupKind.Space, 300, reversible: false, files: new[]
            {
                new ActionFile(@"C:\D\a.exe", 200, null), new ActionFile(@"C:\D\b.msi", 100, null),
            }),
            Row("space.recycle-bin", ActionGroupKind.Space, 8_000_000_000, reversible: false),
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
