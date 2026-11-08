using System;
using System.Collections.Generic;
using System.Threading;
using BriskEngine.Actions;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Live;
using BriskEngine.Models;

namespace Brisk.Session;

/// Every engine call the window makes. Calls may block (a scan, a run, a
/// walk of the user's folders, a WMI read);
/// callers run them off the UI thread.
public interface IActionBackend
{
    IReadOnlyList<ActionGroup> BuildFast();
    ScanResult ScanSpace(Action<TargetScanResult> onTarget);
    ActionGroup? BuildSpace(ScanResult scan);
    RunRecord Run(ActionPlan plan, ActionSelection selection, Action<ActionItem> onRowStart, Action<RowResult> onRow);
    /// Reverses only these rules of the run (Geri al names its Speed and
    /// Privacy rules); the run's other rules stay as it left them.
    IReadOnlyList<RowResult> Undo(string runId, IReadOnlySet<string> ruleIds);
    RunRecord? LatestRun();
    Receipt? RefreshReceipt(string runId);
    void ProcessDeferred();
    IReadOnlyList<DeferredCleanQueue.Entry> DeferredPending();
    IReadOnlyList<string> DeferredApps();

    /// The user's own large files (VM disks, ISOs, emulators): listed locked,
    /// never counted, never deleted. A walk of seconds; throws
    /// OperationCanceledException once ct is cancelled.
    IReadOnlyList<PersonalFile> PersonalFiles(CancellationToken ct);

    /// Free bytes on the drive that holds %SystemDrive%, as Explorer shows
    /// them; null when the drive cannot be read (the page shows "—"). Never
    /// throws, never a guessed 0.
    long? SystemDriveFreeBytes();

    /// The machine's specs, read once and kept: the first call may block for
    /// seconds on a broken WMI, every later one returns at once.
    SpecsInfo Specs();

    /// One second of the live monitor, from one sampler kept for the app's
    /// lifetime. The first call primes the deltas (~0.8 s) and returns nulls.
    MonitorSample SampleMonitor();
}
