using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BriskEngine.Actions;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Live;
using BriskEngine.Models;

namespace Brisk.Session;

public sealed class RealActionBackend : IActionBackend
{
    private readonly ActionEngine _engine;
    private readonly Scanner _scanner;

    public RealActionBackend(ActionEngine engine, Scanner scanner)
    {
        _engine = engine;
        _scanner = scanner;
    }

    public IReadOnlyList<ActionGroup> BuildFast() => _engine.Builder.BuildFast();

    public ScanResult ScanSpace(Action<TargetScanResult> onTarget) =>
        _scanner.Scan(progress: new Inline(p => { if (p.Result is { } r) onTarget(r); }));

    public ActionGroup? BuildSpace(ScanResult scan) => _engine.Builder.BuildSpace(scan);

    public RunRecord Run(ActionPlan plan, ActionSelection selection, Action<ActionItem> onRowStart,
        Action<RowResult> onRow) => _engine.Runner.Run(plan, selection, onRow, onRowStart);

    public IReadOnlyList<RowResult> Undo(string runId) => _engine.Runner.Undo(runId);
    public RunRecord? LatestRun() => _engine.Runs.Latest();
    public Receipt? RefreshReceipt(string runId) => _engine.Receipts.Refresh(runId);
    public void ProcessDeferred() => _engine.ProcessDeferred();
    public IReadOnlyList<DeferredCleanQueue.Entry> DeferredPending() => _engine.Deferred.Pending();

    public IReadOnlyList<string> DeferredApps() => _engine.Deferred.Pending()
        .Select(e => CleanupTargetRegistry.All.FirstOrDefault(t => t.Id == e.TargetId)?.AppDisplayName)
        .OfType<string>().Distinct().ToList();

    public int EnabledStartupCount() => _engine.Startup.List().Count(e => e.Enabled);
    public IReadOnlyList<StartupEntry> StartupEntries() => _engine.Startup.List();
    public IReadOnlyList<AppLoad> SampleLoad() => _engine.Load.Sample();
    public ConnectionSnapshot ReadConnections() => _engine.Connections.Read();
    public IReadOnlyList<UsageRow>? ReadUsage() => _engine.Usage.Top();
    public void Close(IReadOnlyList<int> pids) => _engine.Closer.RequestClose(pids);
    public void Kill(IReadOnlyList<int> pids) => _engine.Closer.Kill(pids);

    public bool IsRunning(IReadOnlyList<int> pids) => pids.Any(pid =>
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (Exception) { return false; }
    });

    /// Progress<T> posts to a captured context; the scan reports from its own
    /// threads and the session marshals, so this one calls straight through.
    private sealed class Inline : IProgress<ScanProgress>
    {
        private readonly Action<ScanProgress> _report;
        public Inline(Action<ScanProgress> report) { _report = report; }
        public void Report(ScanProgress value) => _report(value);
    }
}
