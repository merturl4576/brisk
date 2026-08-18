using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

/// The owner's live scenario, end-to-end at the GUI seam: undo the
/// browser-gpu fix from the "What brisk did" report → the journal is
/// updated → the follow-up rescan re-detects → the finding reappears on
/// the PERFORMANS page (category split), with Sağlık pointing at it
/// through the cross-page link. The engine half of the chain (registry
/// restore → Detect fires again) is covered by BrowserGpuRuleTests.
public class UndoRoundTripTests
{
    [Fact]
    public async Task UndoBrowserGpu_JournalThenRescan_FindingReturnsOnPerformans()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        var host = new JournalingHost
        {
            // what the post-undo scan will re-detect
            AfterUndoSnapshot = TestData.Snapshot(new[]
            {
                TestData.Finding("browser-gpu", cat: RuleCategory.Auto, canFix: true),
            }),
        };
        // fixed state: journal holds the undoable fix, scan finds nothing
        host.Inner.Undoable.Add(new UndoableFix("browser-gpu", DateTime.UtcNow));
        host.Inner.NextSnapshot = TestData.Snapshot();
        var state = new AppState(host);
        var fixAll = new FixAllService(host);
        var overview = new OverviewViewModel(state, host, fixAll,
            new SafeCleanRunner(new CleanService(host, new Settings()), new FakeBin()),
            new NoopLive(), loc, () => false);
        var health = new HealthViewModel(state, host, loc, () => false, fixAll,
            FindingSections.IsHealth, doneFilter: FindingSections.IsHealth,
            crossLinkKey: "health.crosslink",
            morphPause: () => Task.CompletedTask);
        var perf = new HealthViewModel(state, host, loc, () => false, fixAll,
            FindingSections.IsPerformance,
            doneFilter: FindingSections.IsPerformance,
            crossLinkKey: "performance.crosslink",
            morphPause: () => Task.CompletedTask);
        await state.ScanAsync();

        Assert.Empty(perf.Rows);                       // nothing to fix while fixed
        // the journal-driven done report carries the fix, on Performans only
        Assert.Equal(loc["rule.browser-gpu.done"],
            Assert.Single(perf.DoneRows).Title);
        Assert.Empty(health.DoneRows);
        var reportRow = Assert.Single(overview.DoneRows);
        Assert.Equal("browser-gpu", reportRow.RuleId);

        await overview.UndoAsync(reportRow);

        // journal written…
        Assert.Equal(new[] { "browser-gpu" }, host.Inner.Undone);
        // …the rescan ran and the finding reappears — on Performans …
        var row = Assert.Single(perf.Rows);
        Assert.Equal("browser-gpu", row.RuleId);
        Assert.False(row.IsFixed);                     // fresh Normal row
        Assert.False(row.CanUndo);                     // journal entry consumed
        Assert.Empty(perf.DoneRows);   // the done report no longer claims it
        // …never on Sağlık, which instead points across the split
        Assert.DoesNotContain(health.Rows, r => r.RuleId == "browser-gpu");
        Assert.True(health.HasCrossLink);
        Assert.Equal(loc.F("health.crosslink", 1), health.CrossLinkText);
        Assert.False(perf.HasCrossLink);
        // and the undone fix left the "What brisk did" report
        Assert.Empty(overview.DoneRows);
    }

    private sealed class NoopLive : ILiveMetrics
    {
        public bool IsTicking { get; private set; }
        public LiveReading Read() => new(null, null, null, null, 0);
        public void Start(Action onTick) => IsTicking = true;
        public void Stop() => IsTicking = false;
    }

    /// Fakes.cs is a locked contract; the journal-consuming undo (remove the
    /// undoable entry, let the next scan re-detect) is simulated with a thin
    /// decorator that only overrides Undo — exactly what FixJournal.RecordUndo
    /// plus a real rescan produce.
    private sealed class JournalingHost : IEngineHost
    {
        public FakeEngineHost Inner { get; } = new();
        public ScanSnapshot AfterUndoSnapshot { get; set; } = TestData.Snapshot();

        public FixOutcome Undo(string ruleId)
        {
            var outcome = Inner.Undo(ruleId);
            Inner.Undoable.RemoveAll(u =>
                string.Equals(u.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));
            Inner.NextSnapshot = AfterUndoSnapshot;
            return outcome;
        }

        public Task<ScanSnapshot> ScanAsync(IProgress<string>? progress = null,
            CancellationToken ct = default) => Inner.ScanAsync(progress, ct);
        public FixOutcome Fix(string ruleId) => Inner.Fix(ruleId);
        public CleanReport Clean(TargetScanResult scan, bool dryRun,
                Action<CleanEntry>? onEntry = null) =>
            Inner.Clean(scan, dryRun, onEntry);
        public IReadOnlyList<UndoableFix> ListUndoable() => Inner.ListUndoable();
        public IReadOnlyList<ActionLogEntry> ReadLog(int max = 200) => Inner.ReadLog(max);
        public IReadOnlyList<StartupEntry> ListStartup() => Inner.ListStartup();
        public bool SetStartupEnabled(string hive, string name, bool enabled) =>
            Inner.SetStartupEnabled(hive, name, enabled);
        public bool RunElevated(string cliArgs) => Inner.RunElevated(cliArgs);
        public bool CreateRestorePoint() => Inner.CreateRestorePoint();
        public long FreeDiskBytes() => Inner.FreeDiskBytes();
        public long LifetimeReclaimedBytes() => Inner.LifetimeReclaimedBytes();
        public FixOutcome KeepDisplayFix() => Inner.KeepDisplayFix();
        public bool IsElevated() => Inner.IsElevated();
    }
}
