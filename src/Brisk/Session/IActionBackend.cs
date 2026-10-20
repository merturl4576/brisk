using System;
using System.Collections.Generic;
using BriskEngine.Actions;
using BriskEngine.Diagnostics;
using BriskEngine.Live;
using BriskEngine.Models;

namespace Brisk.Session;

/// Every engine call the window makes. Calls may block (a scan, a run, a
/// ten-second load sample); callers run them off the UI thread.
public interface IActionBackend
{
    IReadOnlyList<ActionGroup> BuildFast();
    ScanResult ScanSpace(Action<TargetScanResult> onTarget);
    ActionGroup? BuildSpace(ScanResult scan);
    RunRecord Run(ActionPlan plan, ActionSelection selection, Action<ActionItem> onRowStart, Action<RowResult> onRow);
    IReadOnlyList<RowResult> Undo(string runId);
    RunRecord? LatestRun();
    Receipt? RefreshReceipt(string runId);
    void ProcessDeferred();
    IReadOnlyList<DeferredCleanQueue.Entry> DeferredPending();
    IReadOnlyList<string> DeferredApps();
    int EnabledStartupCount();
    IReadOnlyList<StartupEntry> StartupEntries();
    IReadOnlyList<AppLoad> SampleLoad();
    ConnectionSnapshot ReadConnections();
    IReadOnlyList<UsageRow>? ReadUsage();
    void Close(IReadOnlyList<int> pids);
    void Kill(IReadOnlyList<int> pids);
    bool IsRunning(IReadOnlyList<int> pids);
}
