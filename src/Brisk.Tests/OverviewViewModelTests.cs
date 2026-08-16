using System;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

public class OverviewViewModelTests
{
    private static Loc EnglishLoc()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        return loc;
    }

    /// Trivial ILiveMetrics fake (Fakes.cs is locked): canned reading, and
    /// Start/Stop bookkeeping that mirrors the real timer's idempotent Start.
    private sealed class FakeLive : ILiveMetrics
    {
        public LiveReading Next { get; set; } = new(null, null, null, null, 0);
        public bool IsTicking { get; private set; }
        public int StartCalls { get; private set; }
        public LiveReading Read() => Next;

        public void Start(Action onTick)
        {
            if (IsTicking) return;
            IsTicking = true;
            StartCalls++;
            onTick();
        }

        public void Stop() => IsTicking = false;
    }

    private static (OverviewViewModel Vm, FakeEngineHost Host, AppState State) Build(
        Func<bool>? isDryRun = null, FakeLive? live = null)
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(
            new[]
            {
                TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
                TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
            },
            TestData.Target("user-temp", CleanupLevel.Safe, 2048));
        var state = new AppState(host);
        var vm = new OverviewViewModel(state, host, new FixAllService(host),
            new CleanService(host, new Settings()), live ?? new FakeLive(),
            EnglishLoc(), isDryRun ?? (() => false));
        return (vm, host, state);
    }

    [Fact]
    public async Task Refresh_PopulatesHeroAndSummary()
    {
        var loc = EnglishLoc();
        var (vm, _, state) = Build();
        await state.ScanAsync();

        Assert.Equal("72", vm.ScoreText);
        Assert.Equal("SeverityWarning", vm.ScoreBrushKey);
        Assert.Equal(loc["overview.status.attention"], vm.StatusText);
        Assert.Contains("2 findings", vm.SummaryText);
        Assert.Contains("1 one-click fixable", vm.SummaryText);
        Assert.Contains("2 KB", vm.SummaryText);
        Assert.Contains("Last scan:", vm.SummaryText);
        Assert.True(vm.HasSnapshot);
    }

    [Fact]
    public async Task Refresh_GoodScore_ShowsGoodStatus()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build();
        host.NextSnapshot = new ScanSnapshot(Array.Empty<DiagnosticFinding>(),
            new ScanResult(Array.Empty<TargetScanResult>()), 95,
            new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc));
        await state.ScanAsync();

        Assert.Equal("Good", vm.ScoreBrushKey);
        Assert.Equal(loc["overview.status.good"], vm.StatusText);
    }

    [Fact]
    public async Task Refresh_OnlyRecommendationsLeft_PositiveStatus_NoFixablePhrase()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(
            new[]
            {
                TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
                TestData.Finding("ram-pressure", cat: RuleCategory.Advise, canFix: false),
            },
            TestData.Target("user-temp", CleanupLevel.Safe, 2048));
        await state.ScanAsync();

        Assert.Equal(loc.F("overview.status.advise", 2), vm.StatusText);
        Assert.Equal("Your PC is in good shape — 2 recommendations to review",
            vm.StatusText);
        // no "0 one-click fixable" — that phrase only appears as a promise
        Assert.DoesNotContain("one-click", vm.SummaryText);
        Assert.Contains("2 KB", vm.SummaryText);
        Assert.Contains("Last scan:", vm.SummaryText);
    }

    [Fact]
    public async Task FixAllButton_EnabledOnlyWhenFixAllHasWork()
    {
        var (vm, host, state) = Build();
        Assert.False(vm.FixAllCommand.CanExecute(null));   // no snapshot yet

        await state.ScanAsync();                           // fixable power-plan
        Assert.True(vm.FixAllCommand.CanExecute(null));

        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
        });
        await state.ScanAsync();                           // only advice remains
        Assert.False(vm.FixAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task FixAll_ReportsPastTenseOutcomes_AndBottomLine_ThenRescans()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build();
        await state.ScanAsync();

        await vm.FixAllAsync();

        Assert.Equal(new[] { "power-plan" }, host.Fixed);
        // the line is the rule's past-tense outcome, not its problem title
        Assert.Equal(new[] { "Power plan switched to high performance" },
            vm.ReportLines);
        Assert.Equal(
            loc.F("overview.report.summary", loc.F("overview.report.part.fixes", 1)),
            vm.ReportSummary);
        Assert.Equal(2, host.ScanCalls);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task FixAll_RuleWithoutDoneKey_FallsBackToFixedTitleComposition()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("custom-x", cat: RuleCategory.Auto, canFix: true),
        });
        await state.ScanAsync();

        await vm.FixAllAsync();

        // rule.custom-x.done and rule.custom-x.title are both missing:
        // generic "Fixed: <engine English>" keeps the line an outcome.
        Assert.Equal(new[] { loc.F("overview.report.fixed", "Title custom-x") },
            vm.ReportLines);
    }

    [Fact]
    public async Task FixAll_ReportsDisabledStartupItems_ByName()
    {
        var loc = EnglishLoc();
        var host = new StartupDisablingHost();
        host.Inner.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("startup-bloat", cat: RuleCategory.Confirm, canFix: true),
        });
        host.Inner.Startup.Add(new StartupEntry("HKCU", "Discord", true, true));
        host.Inner.Startup.Add(new StartupEntry("HKCU", "MyTool", true, false));
        var state = new AppState(host);
        var vm = new OverviewViewModel(state, host, new FixAllService(host),
            new CleanService(host, new Settings()), new FakeLive(), loc, () => false);
        await state.ScanAsync();

        await vm.FixAllAsync();

        Assert.Equal(new[] { loc.F("overview.report.disabled", "Discord") },
            vm.ReportLines);
        Assert.Equal(
            loc.F("overview.report.summary", loc.F("overview.report.part.startup", 1)),
            vm.ReportSummary);
    }

    [Fact]
    public async Task FixAll_BottomLine_JoinsStartupAndFixParts()
    {
        var loc = EnglishLoc();
        var host = new StartupDisablingHost();
        host.Inner.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
            TestData.Finding("startup-bloat", cat: RuleCategory.Confirm, canFix: true),
        });
        host.Inner.Startup.Add(new StartupEntry("HKCU", "Discord", true, true));
        host.Inner.Startup.Add(new StartupEntry("HKCU", "Steam", true, true));
        var state = new AppState(host);
        var vm = new OverviewViewModel(state, host, new FixAllService(host),
            new CleanService(host, new Settings()), new FakeLive(), loc, () => false);
        await state.ScanAsync();

        await vm.FixAllAsync();

        Assert.Equal("Result: 2 programs removed from startup · 1 fixes applied",
            vm.ReportSummary);
    }

    [Fact]
    public async Task FixAll_DryRun_BlocksWithFeedback_NeverCallsHost()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build(() => true);
        await state.ScanAsync();

        await vm.FixAllAsync();

        Assert.Empty(host.Fixed);
        Assert.Equal(new[] { loc["dryrun.blocked"] }, vm.ReportLines);
        Assert.Equal(1, host.ScanCalls);   // only the initial scan, no rescan
    }

    [Fact]
    public async Task FixAll_NothingFixable_ReportsItPlainly_NoEnjoyLine()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
        });
        await state.ScanAsync();

        await vm.FixAllAsync();

        Assert.Empty(host.Fixed);
        Assert.Equal(new[] { loc["health.nofixables"] }, vm.ReportLines);
        Assert.Equal("", vm.ReportSummary);   // nothing ran — no bottom line
    }

    [Fact]
    public async Task CleanSafe_ReportsRecycledLine_AndFreedBottomLine_ThenRescans()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build();
        await state.ScanAsync();

        await vm.CleanSafeAsync();

        Assert.Equal("user-temp", Assert.Single(host.Cleans).TargetId);
        Assert.Equal(new[] { loc.F("clean.recycled", 1, "2 KB") }, vm.ReportLines);
        Assert.Equal(
            loc.F("overview.report.summary", loc.F("overview.report.part.freed", "2 KB")),
            vm.ReportSummary);
        Assert.Equal(2, host.ScanCalls);
    }

    [Fact]
    public async Task CleanSafe_DryRun_BlocksWithFeedback()
    {
        var loc = EnglishLoc();
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(null,
            TestData.Target("user-temp", CleanupLevel.Safe, 2048));
        var state = new AppState(host);
        var settings = new Settings { DryRun = true };
        var vm = new OverviewViewModel(state, host, new FixAllService(host),
            new CleanService(host, settings), new FakeLive(), loc,
            () => settings.DryRun);
        await state.ScanAsync();

        await vm.CleanSafeAsync();

        Assert.Equal(new[] { loc["dryrun.blocked"] }, vm.ReportLines);
        Assert.All(host.Cleans, c => Assert.True(c.DryRun));
    }

    [Fact]
    public async Task Recent_ListsUndoables_UndoCallsHost_ThenRescans()
    {
        var (vm, host, state) = Build();
        host.Undoable.Add(new UndoableFix("power-plan",
            new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc)));
        await state.ScanAsync();

        var row = Assert.Single(vm.Recent);
        Assert.Equal("power-plan", row.RuleId);
        // a completed fix reads as its outcome, never as the problem title
        Assert.Equal("Power plan switched to high performance", row.Title);

        await vm.UndoAsync(row);

        Assert.Equal(new[] { "power-plan" }, host.Undone);
        Assert.Equal(2, host.ScanCalls);
    }

    [Fact]
    public async Task Undo_DryRun_BlocksWithFeedback()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build(() => true);
        host.Undoable.Add(new UndoableFix("power-plan",
            new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc)));
        await state.ScanAsync();

        await vm.UndoAsync(Assert.Single(vm.Recent));

        Assert.Empty(host.Undone);
        Assert.Equal(new[] { loc["dryrun.blocked"] }, vm.ReportLines);
        Assert.Equal(1, host.ScanCalls);   // only the initial scan, no rescan
    }

    [Fact]
    public async Task Scan_ClearsThePreviousReport()
    {
        var (vm, _, state) = Build();
        await state.ScanAsync();
        await vm.FixAllAsync();
        Assert.NotEmpty(vm.ReportLines);
        Assert.NotEqual("", vm.ReportSummary);

        vm.ScanCommand.Execute(null);
        await Task.Yield();

        Assert.Empty(vm.ReportLines);
        Assert.Equal("", vm.ReportSummary);
    }

    [Fact]
    public async Task Recent_RuleWithoutDoneKey_FallsBackToFixedComposition()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build();
        host.Undoable.Add(new UndoableFix("custom-x",
            new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc)));
        await state.ScanAsync();

        var row = vm.Recent.Single(r => r.RuleId == "custom-x");
        // no rule.custom-x.done and no rule.custom-x.title in the resx —
        // still an outcome via the generic composition, ruleId as last resort
        Assert.Equal(loc.F("overview.report.fixed", "custom-x"), row.Title);
    }

    [Fact]
    public async Task BusyGuard_BlocksOtherActionsWhileOneIsInFlight()
    {
        var (vm, host, state) = Build();
        await state.ScanAsync();

        // OnClean blocks on the background thread until the test releases it,
        // so CleanSafeAsync is provably still in flight (not a timing
        // assumption) when FixAllAsync is attempted.
        using var gate = new System.Threading.ManualResetEventSlim(false);
        host.OnClean = (scan, dryRun) =>
        {
            gate.Wait();
            return new CleanReport(scan.Items
                .Select(i => new CleanEntry(scan.Target.Id, i.Path, i.Bytes, "recycled"))
                .ToList());
        };

        var clean = vm.CleanSafeAsync();
        var fixAll = vm.FixAllAsync();   // same busy flag guards every action

        Assert.True(fixAll.IsCompleted);
        Assert.False(clean.IsCompleted);
        Assert.Empty(host.Fixed);

        gate.Set();
        await clean;

        Assert.Single(host.Cleans);
    }

    [Fact]
    public void LiveTiles_VisibilityStartsAndStopsTheTicking()
    {
        var live = new FakeLive
        {
            Next = new LiveReading(37.4, 61.8, 71.2, "GPU", 122L << 30),
        };
        var (vm, _, _) = Build(live: live);

        vm.SetLiveVisible(true);
        Assert.True(live.IsTicking);
        Assert.Equal(1, live.StartCalls);   // Start fires one immediate tick

        vm.SetLiveVisible(true);            // idempotent while visible
        Assert.Equal(1, live.StartCalls);

        vm.SetLiveVisible(false);           // hidden/minimized: nothing ticks
        Assert.False(live.IsTicking);
    }

    [Fact]
    public async Task LiveTick_FormatsValues_InvariantWithUnits()
    {
        var live = new FakeLive
        {
            Next = new LiveReading(37.4, 61.8, 71.2, "GPU", 122L << 30),
        };
        var (vm, _, _) = Build(live: live);

        await vm.LiveTickAsync();

        Assert.Equal("37%", vm.LiveCpuText);
        Assert.Equal("62%", vm.LiveRamText);
        Assert.Equal("71°C", vm.LiveTempText);
        Assert.Equal("Temperature · GPU", vm.LiveTempCaption);
        Assert.Equal("122.0 GB", vm.LiveDiskText);
    }

    [Fact]
    public async Task LiveTick_MissingSensors_ShowDashPlaceholders()
    {
        var live = new FakeLive { Next = new LiveReading(null, null, null, null, 0) };
        var (vm, _, _) = Build(live: live);

        await vm.LiveTickAsync();

        Assert.Equal("—", vm.LiveCpuText);
        Assert.Equal("—", vm.LiveRamText);
        Assert.Equal("—", vm.LiveTempText);
        Assert.Equal("Temperature", vm.LiveTempCaption);   // no source suffix
    }

    /// Fakes.cs is locked; startup-disable semantics are simulated with a
    /// decorator whose Fix("startup-bloat") disables the heavy entries,
    /// exactly like the real StartupBloatRule.Fix does.
    private sealed class StartupDisablingHost : IEngineHost
    {
        public FakeEngineHost Inner { get; } = new();

        public FixOutcome Fix(string ruleId)
        {
            if (string.Equals(ruleId, "startup-bloat", StringComparison.OrdinalIgnoreCase))
                for (var i = 0; i < Inner.Startup.Count; i++)
                    if (Inner.Startup[i].KnownHeavy)
                        Inner.Startup[i] = Inner.Startup[i] with { Enabled = false };
            return Inner.Fix(ruleId);
        }

        public Task<ScanSnapshot> ScanAsync(IProgress<string>? progress = null,
            System.Threading.CancellationToken ct = default) => Inner.ScanAsync(progress, ct);
        public FixOutcome Undo(string ruleId) => Inner.Undo(ruleId);
        public CleanReport Clean(TargetScanResult scan, bool dryRun) =>
            Inner.Clean(scan, dryRun);
        public System.Collections.Generic.IReadOnlyList<UndoableFix> ListUndoable() =>
            Inner.ListUndoable();
        public System.Collections.Generic.IReadOnlyList<BriskEngine.Logging.ActionLogEntry>
            ReadLog(int max = 200) => Inner.ReadLog(max);
        public System.Collections.Generic.IReadOnlyList<StartupEntry> ListStartup() =>
            Inner.ListStartup();
        public bool SetStartupEnabled(string hive, string name, bool enabled) =>
            Inner.SetStartupEnabled(hive, name, enabled);
        public bool RunElevated(string cliArgs) => Inner.RunElevated(cliArgs);
        public bool CreateRestorePoint() => Inner.CreateRestorePoint();
        public long FreeDiskBytes() => Inner.FreeDiskBytes();
        public long LifetimeReclaimedBytes() => Inner.LifetimeReclaimedBytes();
        public bool IsElevated() => Inner.IsElevated();
    }
}
