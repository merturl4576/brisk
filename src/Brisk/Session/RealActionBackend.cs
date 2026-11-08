using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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

    // The 30 s deferred-cleanup timer and a run (or undo) the user starts
    // write the same files — the deferred queue, run records, the WAL — so
    // they take turns.
    private readonly object _writes = new();

    // One sampler and one battery probe for the app's lifetime: the sampler
    // keeps the last reading of every delta (CPU, network, per-app CPU) and
    // its PDH queries, the battery probe its wear cache and the guard that
    // keeps a hung WMI read from leaking threads. The temperatures come from
    // the engine's one sensor probe, which serialises LibreHardwareMonitor
    // between this sampler and a scan's thermals rule.
    private readonly SystemMonitor _monitor;

    // Read once, by the first caller; a second caller waits for that read
    // instead of starting its own set of WMI queries.
    private readonly Lazy<SpecsInfo> _specs;

    public RealActionBackend(ActionEngine engine, Scanner scanner)
    {
        _engine = engine;
        _scanner = scanner;
        _monitor = new SystemMonitor(engine.Context.Sensors, new RealBatteryProbe(), new RealAppNamer());
        var specs = new RealSpecsProbe();
        _specs = new Lazy<SpecsInfo>(specs.Read, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyList<ActionGroup> BuildFast() => _engine.Builder.BuildFast();

    /// Every registry target, the ones the console only takes when named
    /// (profile-git, stale-project-deps, archive-beside-extracted,
    /// restore-points) included: that opt-in is the console's rule. Here the
    /// Yer aç level decides, and the runner never reads the flag.
    public ScanResult ScanSpace(Action<TargetScanResult> onTarget) =>
        _scanner.Scan(progress: new Inline(p => { if (p.Result is { } r) onTarget(r); }));

    public ActionGroup? BuildSpace(ScanResult scan) => _engine.Builder.BuildSpace(scan);

    public RunRecord Run(ActionPlan plan, ActionSelection selection, Action<ActionItem> onRowStart,
        Action<RowResult> onRow)
    {
        lock (_writes) return _engine.Runner.Run(plan, selection, onRow, onRowStart);
    }

    public IReadOnlyList<RowResult> Undo(string runId, IReadOnlySet<string> ruleIds)
    {
        lock (_writes) return _engine.Runner.Undo(runId, ruleIds);
    }

    public RunRecord? LatestRun() => _engine.Runs.Latest();
    public Receipt? RefreshReceipt(string runId) => _engine.Receipts.Refresh(runId);

    public void ProcessDeferred()
    {
        lock (_writes) _engine.ProcessDeferred();
    }

    public IReadOnlyList<DeferredCleanQueue.Entry> DeferredPending() => _engine.Deferred.Pending();

    public IReadOnlyList<string> DeferredApps() => _engine.Deferred.Pending()
        .Select(e => CleanupTargetRegistry.All.FirstOrDefault(t => t.Id == e.TargetId)?.AppDisplayName)
        .OfType<string>().Distinct().ToList();

    public IReadOnlyList<PersonalFile> PersonalFiles(CancellationToken ct) =>
        BriskEngine.Cleaning.PersonalFiles.Find(BriskEngine.Cleaning.PersonalFiles.DefaultRoots(), ct);

    public long? SystemDriveFreeBytes() => FreeBytesOn(SystemDriveRoot());

    /// What Explorer calls free on that drive; null when it cannot be read
    /// (no such drive, not ready, access refused).
    internal static long? FreeBytesOn(string root)
    {
        try { return new DriveInfo(root).AvailableFreeSpace; }
        catch (Exception) { return null; }
    }

    /// "C:\" from %SystemDrive%; the Windows folder's drive when the
    /// variable is missing.
    private static string SystemDriveRoot()
    {
        var drive = Environment.GetEnvironmentVariable("SystemDrive");
        return string.IsNullOrWhiteSpace(drive)
            ? Path.GetPathRoot(Environment.SystemDirectory)!
            : drive.TrimEnd('\\') + "\\";
    }

    public SpecsInfo Specs() => _specs.Value;

    public MonitorSample SampleMonitor() => _monitor.Sample();

    /// Progress<T> posts to a captured context; the scan reports from its own
    /// threads and the session marshals, so this one calls straight through.
    private sealed class Inline : IProgress<ScanProgress>
    {
        private readonly Action<ScanProgress> _report;
        public Inline(Action<ScanProgress> report) { _report = report; }
        public void Report(ScanProgress value) => _report(value);
    }
}
