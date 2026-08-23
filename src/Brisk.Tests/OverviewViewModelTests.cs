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
        Func<bool>? isDryRun = null, FakeLive? live = null,
        Func<ReportCardModel, string, bool>? renderReport = null)
    {
        var (vm, host, state, _) = BuildWithBin(isDryRun, live,
            renderReport: renderReport);
        return (vm, host, state);
    }

    /// Round 13: the overview's clean is the same ONE-STEP recycle→purge
    /// flow the Depolama page runs, so its seam needs the bin in view.
    private static (OverviewViewModel Vm, FakeEngineHost Host, AppState State, FakeBin Bin)
        BuildWithBin(Func<bool>? isDryRun = null, FakeLive? live = null,
            Settings? settings = null,
            Func<ReportCardModel, string, bool>? renderReport = null)
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
        var bin = new FakeBin();
        var fixAll = new FixAllService(host);
        // Wired exactly as App.xaml.cs wires it: the confirmation is raised
        // as each rule is fixed, not from a loop over the finished batch.
        state.TrackFixes(fixAll);
        var vm = new OverviewViewModel(state, host, fixAll,
            new SafeCleanRunner(new CleanService(host, settings ?? new Settings()), bin),
            live ?? new FakeLive(), EnglishLoc(), isDryRun ?? (() => false),
            renderReport);
        return (vm, host, state, bin);
    }

    /// ROUND 11 (workstream B): the clean button wears its benefit with the
    /// live HONEST figure — before a scan it stays the plain generic label.
    [Fact]
    public async Task CleanButton_WearsTheHonestReclaimableFigure()
    {
        var loc = EnglishLoc();
        var (vm, _, state) = Build();

        Assert.Equal(loc["overview.cleanspace.none"], vm.CleanSafeText);
        await state.ScanAsync();
        Assert.Equal(loc.F("overview.cleanspace", "2 KB"), vm.CleanSafeText);
    }

    [Fact]
    public async Task Refresh_PopulatesHeroAndSummary()
    {
        var loc = EnglishLoc();
        var (vm, _, state) = Build();
        await state.ScanAsync();

        Assert.Equal("72", vm.ScoreText);
        Assert.Equal(72.0, vm.ScoreValue);   // numeric twin drives the gauge sweep
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
            new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc),
            new SensorStatus(false, false, null));
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
        // the line is the rule's past-tense outcome, not its problem title —
        // and a real outcome wears the report's green dot
        Assert.Equal(new[] { "Power plan switched to high performance" },
            vm.ReportLines.Select(l => l.Text));
        Assert.All(vm.ReportLines, l => Assert.True(l.IsDone));
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
            vm.ReportLines.Select(l => l.Text));
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
            new SafeCleanRunner(new CleanService(host, new Settings()), new FakeBin()),
            new FakeLive(), loc, () => false);
        await state.ScanAsync();

        await vm.FixAllAsync();

        Assert.Equal(new[] { loc.F("overview.report.disabled", "Discord") },
            vm.ReportLines.Select(l => l.Text));
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
            new SafeCleanRunner(new CleanService(host, new Settings()), new FakeBin()),
            new FakeLive(), loc, () => false);
        await state.ScanAsync();

        await vm.FixAllAsync();

        Assert.Equal("Result: 2 programs removed from startup · 1 fixes applied",
            vm.ReportSummary);
    }

    /// Fix round 1 (Critical): FixAllService is unfiltered, and the
    /// overview's Fix all is one of the four surfaces that can fix
    /// display-refresh — a display mode change that can blank the screen.
    /// The confirmation must reach the shared AppState from here too, not
    /// just from the findings pages.
    [Fact]
    public async Task FixAll_RaisesTheDisplayConfirmation_WhenItFixesDisplayRefresh()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("display-refresh", Severity.Critical, RuleCategory.Auto,
                stars: 5, canFix: true),
        });
        await state.ScanAsync();

        await vm.FixAllAsync();

        Assert.NotNull(state.PendingConfirmation);
        // Resolve rather than leave the real 15-second window's background
        // timer running past this test's return.
        state.KeepDisplayCommand.Execute(null);
        await state.PendingConfirmTask!;
    }

    /// WAVE B, B1. The rollback sentence now lives on ONE window-level
    /// banner instead of three page subscriptions — which also keeps it OUT
    /// of this page's ReportLines, where appending it flipped ShowDoneReport
    /// to false and hid the journal panel until the next scan. A notice is
    /// not a run report.
    [Fact]
    public async Task RollbackNotice_GoesToTheBanner_NotThisPagesReport()
    {
        var loc = EnglishLoc();
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("display-refresh", Severity.Critical, RuleCategory.Auto,
                stars: 5, canFix: true),
        });
        var state = new AppState(host, loc);
        var fixAll = new FixAllService(host);
        state.TrackFixes(fixAll);
        var vm = new OverviewViewModel(state, host, fixAll,
            new SafeCleanRunner(new CleanService(host, new Settings()), new FakeBin()),
            new FakeLive(), loc, () => false);
        await state.ScanAsync();

        state.ConfirmationWindow = TimeSpan.Zero;
        await vm.FixAllAsync();
        await state.PendingConfirmTask!;

        Assert.Equal(loc["display-confirm.rolledback"], state.DisplayNotice);
        Assert.DoesNotContain(vm.ReportLines,
            line => line.Text == loc["display-confirm.rolledback"]);
    }

    [Fact]
    public async Task FixAll_DryRun_BlocksWithFeedback_NeverCallsHost()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build(() => true);
        await state.ScanAsync();

        await vm.FixAllAsync();

        Assert.Empty(host.Fixed);
        var line = Assert.Single(vm.ReportLines);
        Assert.Equal(loc["dryrun.blocked"], line.Text);
        Assert.False(line.IsDone);   // a caveat never wears the green dot
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
        var line = Assert.Single(vm.ReportLines);
        Assert.Equal(loc["health.nofixables"], line.Text);
        Assert.False(line.IsDone);
        Assert.Equal("", vm.ReportSummary);   // nothing ran — no lead line
    }

    /// ROUND 13: this button says "Free up 2 KB", so it must actually free
    /// them — the same one-step recycle→purge the Depolama page runs. The
    /// report line is the freed figure, never "moved to Recycle Bin".
    [Fact]
    public async Task CleanSafe_AutoPurgesItsOwnItems_ReportsFreedTruth_ThenRescans()
    {
        var loc = EnglishLoc();
        var (vm, host, state, bin) = BuildWithBin();
        await state.ScanAsync();
        // Pins the ORDER structurally: the pre-clean snapshot must already
        // have happened by the time the engine is asked to recycle.
        var queriesWhenCleanRan = -1;
        host.OnClean = (scan, _) =>
        {
            queriesWhenCleanRan = bin.IdQueries.Count;
            return new CleanReport(scan.Items
                .Select(i => new CleanEntry(scan.Target.Id, i.Path, i.Bytes, "recycled"))
                .ToList());
        };

        await vm.CleanSafeAsync();

        Assert.Equal("user-temp", Assert.Single(host.Cleans).TargetId);
        Assert.Equal(1, queriesWhenCleanRan);
        // the purge touched EXACTLY this run's own recycled items
        Assert.Equal(new[] { @"C:\x\user-temp\item" }, Assert.Single(bin.Purged));
        var line = Assert.Single(vm.ReportLines);
        Assert.Equal(loc.F("clean.report.summary.freed", 1, "2 KB"), line.Text);
        Assert.True(line.IsDone);
        Assert.Equal(
            loc.F("overview.report.summary", loc.F("overview.report.part.freed", "2 KB")),
            vm.ReportSummary);
        Assert.Equal(2, host.ScanCalls);
    }

    /// ROUND 13 safety, carried over from round 12: a file the USER deleted
    /// earlier at the same original path is snapshotted before the clean and
    /// excluded from the purge — the overview can no more destroy it than
    /// the Depolama page can.
    [Fact]
    public async Task CleanSafe_ExcludesBinItemsThatPredateTheClean()
    {
        var (vm, _, state, bin) = BuildWithBin();
        bin.PreExistingIds.Add(@"C:\$Recycle.Bin\S-1-5-21\$RUSER01.tmp");
        await state.ScanAsync();

        await vm.CleanSafeAsync();

        // the snapshot asked about exactly the planned safe-default items…
        Assert.Equal(new[] { @"C:\x\user-temp\item" }, Assert.Single(bin.IdQueries));
        // …and the purge was handed those identities to skip
        var purge = Assert.Single(bin.PurgeCalls);
        Assert.Equal(new[] { @"C:\$Recycle.Bin\S-1-5-21\$RUSER01.tmp" }, purge.Exclude);
    }

    /// ROUND 13: when the purge falls short, the overview quotes what really
    /// left the disk (0 B here) — never the bytes-moved-to-bin figure — and
    /// says so, without the done dot.
    [Fact]
    public async Task CleanSafe_PartialPurge_ReportsFreedNotRecycled()
    {
        var loc = EnglishLoc();
        var (vm, _, state, bin) = BuildWithBin();
        bin.PurgeFails.Add(@"C:\x\user-temp\item");
        await state.ScanAsync();

        await vm.CleanSafeAsync();

        Assert.Equal(new[]
        {
            loc.F("clean.report.summary.freed", 1, "0 B"),
            loc.F("clean.report.binleft", "2 KB"),
        }, vm.ReportLines.Select(l => l.Text));
        Assert.Equal(new[] { true, false }, vm.ReportLines.Select(l => l.IsDone));
        Assert.Equal(
            loc.F("overview.report.summary", loc.F("overview.report.part.freed", "0 B")),
            vm.ReportSummary);
    }

    [Fact]
    public async Task CleanSafe_DryRun_BlocksWithFeedback_AndNeverTouchesTheBin()
    {
        var loc = EnglishLoc();
        var settings = new Settings { DryRun = true };
        var (vm, host, state, bin) = BuildWithBin(() => settings.DryRun,
            settings: settings);
        await state.ScanAsync();

        await vm.CleanSafeAsync();

        Assert.Equal(new[] { loc["dryrun.blocked"] },
            vm.ReportLines.Select(l => l.Text));
        Assert.All(host.Cleans, c => Assert.True(c.DryRun));
        Assert.Empty(bin.Purged);
    }

    [Fact]
    public async Task DoneReport_ListsUndoables_UndoCallsHost_ThenRescans()
    {
        var (vm, host, state) = Build();
        host.Undoable.Add(new UndoableFix("power-plan",
            new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc)));
        await state.ScanAsync();

        var row = Assert.Single(vm.DoneRows);
        Assert.Equal("power-plan", row.RuleId);
        // a completed fix reads as its outcome, never as the problem title
        Assert.Equal("Power plan switched to high performance", row.Title);

        await vm.UndoAsync(row);

        Assert.Equal(new[] { "power-plan" }, host.Undone);
        Assert.Equal(2, host.ScanCalls);
    }

    [Fact]
    public async Task DoneReport_ShowsOnlyWhileJournalHasRows_AndNoSessionReport()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build();

        // nothing ever fixed → no journal face (no empty frame)
        await state.ScanAsync();
        Assert.False(vm.ShowDoneReport);
        Assert.Equal("", vm.DoneLead);

        // journal rows → the journal face, with its lead sentence
        host.Undoable.Add(new UndoableFix("power-plan",
            new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc)));
        await state.ScanAsync();
        Assert.True(vm.ShowDoneReport);
        Assert.Equal(loc.F("overview.report.live", 1), vm.DoneLead);

        // a fix run puts the run-scoped story on screen → journal face yields
        await vm.FixAllAsync();
        Assert.NotEmpty(vm.ReportLines);
        Assert.False(vm.ShowDoneReport);

        // the next scan starts a new story → the journal face returns.
        // Deterministic wait: ScanCommand fires-and-forgets its scan, so we
        // await the state's own Changed signal instead of racing a bare
        // Task.Yield against the thread pool (this test's old flake).
        var rescanned = new TaskCompletionSource();
        state.Changed += () => rescanned.TrySetResult();
        vm.ScanCommand.Execute(null);
        await rescanned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(vm.ReportLines);
        Assert.True(vm.ShowDoneReport);
    }

    [Fact]
    public async Task Undo_DryRun_BlocksWithFeedback()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build(() => true);
        host.Undoable.Add(new UndoableFix("power-plan",
            new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc)));
        await state.ScanAsync();

        await vm.UndoAsync(Assert.Single(vm.DoneRows));

        Assert.Empty(host.Undone);
        Assert.Equal(new[] { loc["dryrun.blocked"] },
            vm.ReportLines.Select(l => l.Text));
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
    public async Task DoneReport_FlagsOnlyRowsAddedAfterTheFirstRefresh_AsNew()
    {
        var (vm, host, state) = Build();
        host.Undoable.Add(new UndoableFix("power-plan",
            new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc)));
        await state.ScanAsync();

        // startup population: nothing animates, however old the journal is
        Assert.False(Assert.Single(vm.DoneRows).IsNew);

        // a fix run adds an undoable → its row (and only its row) is new
        host.Undoable.Add(new UndoableFix("visual-effects",
            new DateTime(2026, 8, 15, 11, 0, 0, DateTimeKind.Utc)));
        await state.ScanAsync();
        Assert.True(vm.DoneRows.Single(r => r.RuleId == "visual-effects").IsNew);
        Assert.False(vm.DoneRows.Single(r => r.RuleId == "power-plan").IsNew);

        // the next refresh renders it as an ordinary row — one-shot entry
        await state.ScanAsync();
        Assert.False(vm.DoneRows.Single(r => r.RuleId == "visual-effects").IsNew);
    }

    [Fact]
    public async Task DoneReport_RuleWithoutDoneKey_FallsBackToFixedComposition()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build();
        host.Undoable.Add(new UndoableFix("custom-x",
            new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc)));
        await state.ScanAsync();

        var row = vm.DoneRows.Single(r => r.RuleId == "custom-x");
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
        Assert.Equal(37.4, vm.LiveCpuPercent);   // numeric twin drives the CPU ring
        Assert.Equal("62%", vm.LiveRamText);
        Assert.Equal("71°C", vm.LiveTempText);
        Assert.Equal("Temperature · GPU", vm.LiveTempCaption);
        Assert.Equal("GPU 71°C", vm.LiveTempBadgeText);   // the gauge's center readout
        Assert.Equal("122.0 GB", vm.LiveDiskText);
    }

    [Fact]
    public async Task LiveTick_MissingSensors_ShowDashPlaceholders()
    {
        var live = new FakeLive { Next = new LiveReading(null, null, null, null, 0) };
        var (vm, _, _) = Build(live: live);

        await vm.LiveTickAsync();

        Assert.Equal("—", vm.LiveCpuText);
        Assert.Equal(0.0, vm.LiveCpuPercent);   // CPU ring rests as an empty arc
        Assert.Equal("—", vm.LiveRamText);
        Assert.Equal("—", vm.LiveTempText);
        Assert.Equal("Temperature", vm.LiveTempCaption);   // no source suffix
        Assert.Equal("", vm.LiveTempBadgeText);   // gauge readout hides entirely
    }

    [Fact]
    public async Task LiveTick_TempWithoutSource_HidesTheGaugeReadout()
    {
        // Defensive: the tile still shows the degrees, but the gauge's
        // compact "GPU 78°C" line needs both halves to say anything.
        var live = new FakeLive { Next = new LiveReading(null, null, 55.0, null, 0) };
        var (vm, _, _) = Build(live: live);

        await vm.LiveTickAsync();

        Assert.Equal("55°C", vm.LiveTempText);
        Assert.Equal("", vm.LiveTempBadgeText);
    }

    /// ROUND 13 review (I1): one runner sits behind three buttons, so the
    /// per-view-model busy flags cannot stop the tray and the overview from
    /// purging at the same time — double-counted freed bytes, and a "still
    /// in the Recycle Bin" line for bytes already gone. The runner's lease
    /// makes the whole sequence single-flight app-wide; the surface that
    /// loses it does nothing at all, exactly like a re-press does.
    [Fact]
    public async Task CleanSafe_WhileAnotherSurfaceIsCleaning_IsNoOp()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(null,
            TestData.Target("user-temp", CleanupLevel.Safe, 2048));
        var state = new AppState(host);
        var bin = new FakeBin();
        var runner = new SafeCleanRunner(new CleanService(host, new Settings()), bin);
        var overview = new OverviewViewModel(state, host, new FixAllService(host),
            runner, new FakeLive(), EnglishLoc(), () => false);
        var flyout = new FlyoutViewModel(state, runner, new FixAllService(host),
            EnglishLoc(), () => false);
        await state.ScanAsync();

        // OnClean blocks on the background thread until the test releases it,
        // so the tray's clean is provably still holding the runner (not a
        // timing assumption) when the overview button is pressed.
        using var gate = new System.Threading.ManualResetEventSlim(false);
        host.OnClean = (scan, _) =>
        {
            gate.Wait();
            return new CleanReport(scan.Items
                .Select(i => new CleanEntry(scan.Target.Id, i.Path, i.Bytes, "recycled"))
                .ToList());
        };

        var tray = flyout.CleanSafeAsync();
        var press = overview.CleanSafeAsync();

        // The lease is taken and refused SYNCHRONOUSLY, before either method
        // reaches its first await, so this is a fact rather than a timing
        // assumption: the overview press comes back already completed while
        // the tray's clean is still blocked in the engine.
        Assert.True(press.IsCompleted);
        Assert.False(tray.IsCompleted);

        gate.Set();
        await tray;
        await press;

        // One clean, one purge: the bin was never handed two runs at once.
        Assert.Single(host.Cleans);
        Assert.Single(bin.PurgeCalls);
    }

    /// Fake rule ids on purpose: unlisted rules rank by severity, and their
    /// missing resx keys prove the English fallback carries the band.
    [Fact]
    public async Task Revelation_LeadsWithTheTopHeadline_AndCountsTheRest()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(
            new[]
            {
                TestData.Finding("aa-fake", cat: RuleCategory.Advise, canFix: false,
                    headline: new Headline("13", "programs start with Windows",
                        "rule.aa-fake.headline.value", new[] { "13" },
                        "rule.aa-fake.headline.caption", Array.Empty<string>())),
                TestData.Finding("zz-fake", sev: Severity.Critical,
                    cat: RuleCategory.Advise, canFix: false,
                    headline: new Headline("57 s", "boot time — the middle of the last 8 boots",
                        "rule.zz-fake.headline.value", new[] { "57" },
                        "rule.zz-fake.headline.caption", new[] { "8" })),
            });

        await state.ScanAsync();

        Assert.True(vm.HasRevelation);
        Assert.Equal("57 s", vm.RevelationValue);
        Assert.Equal("boot time — the middle of the last 8 boots", vm.RevelationCaption);
        Assert.Equal("Title zz-fake", vm.RevelationClaim);
        Assert.Equal("Evidence zz-fake", vm.RevelationEvidence);
        Assert.Equal("and 1 more", vm.RevelationMoreText);
    }

    [Fact]
    public async Task Revelation_NoHeadlines_ShowsTheHonestEmptyLine()
    {
        var (vm, host, state) = Build();   // default snapshot carries no headlines

        await state.ScanAsync();

        Assert.False(vm.HasRevelation);
        Assert.Equal(
            $"All {DiagnosticRuleRegistry.All.Count} rules looked — nothing on this machine leads with a number.",
            vm.RevelationEmptyText);
        Assert.Equal("", vm.RevelationMoreText);
    }

    [Fact]
    public void OpenHealth_RaisesTheNavigationEvent()
    {
        var (vm, _, _) = Build();
        var fired = false;
        vm.OpenHealthRequested += () => fired = true;
        vm.OpenHealthCommand.Execute(null);
        Assert.True(fired);
    }

    [Fact]
    public async Task SaveReport_RendersTheCardAndAnnouncesThePath()
    {
        var rendered = new List<(ReportCardModel Model, string Path)>();
        var (vm, host, state) = Build(
            renderReport: (m, p) => { rendered.Add((m, p)); return true; });
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("zz-fake", cat: RuleCategory.Advise, canFix: false,
                headline: new Headline("57 s", "cap",
                    "rule.zz-fake.headline.value", new[] { "57" },
                    "rule.zz-fake.headline.caption", Array.Empty<string>())),
        }, new SensorStatus(true, true, null));
        await state.ScanAsync();

        vm.SaveReportCommand.Execute(null);

        var (model, path) = Assert.Single(rendered);
        Assert.Equal("57 s", model.Findings[0].Lead);
        Assert.EndsWith(".png", path);
        Assert.Equal(EnglishLoc().F("overview.report.card.saved", path), vm.ReportSavedText);
    }

    [Fact]
    public void SaveReport_WithoutASnapshot_CannotExecute()
    {
        var (vm, _, _) = Build();
        Assert.False(vm.SaveReportCommand.CanExecute(null));
    }

    /// The saved line names a card built from ONE scan. The next scan
    /// replaces the snapshot underneath it, so a line still reading "Saved:
    /// …brisk-report-….png" would be pointing at a picture of the machine as
    /// it WAS — a stale claim on a page whose whole job is the current one.
    [Fact]
    public async Task SaveReport_ThenAnotherScan_ClearsTheSavedLine()
    {
        var (vm, _, state) = Build(renderReport: (_, _) => true);
        await state.ScanAsync();
        vm.SaveReportCommand.Execute(null);
        Assert.NotEqual("", vm.ReportSavedText);

        await state.ScanAsync();

        Assert.Equal("", vm.ReportSavedText);
    }

    /// The clipboard copy is best-effort by design — another process holding
    /// it must not turn a card that IS on disk into an error. But the line
    /// that says so used to promise "(copied to the clipboard)" in exactly
    /// the failure the catch exists to absorb. The surface now says which of
    /// the two happened.
    [Fact]
    public async Task SaveReport_WhenTheClipboardRefuses_ClaimsOnlyTheFile()
    {
        string? saved = null;
        var (vm, _, state) = Build(renderReport: (_, p) => { saved = p; return false; });
        await state.ScanAsync();

        vm.SaveReportCommand.Execute(null);

        Assert.Equal(EnglishLoc().F("overview.report.card.saved.fileonly", saved!),
            vm.ReportSavedText);
        Assert.DoesNotContain("clipboard", vm.ReportSavedText);
    }

    /// A read-only Pictures folder or a full disk is the console verb's
    /// "brisk: {message}". The button owes the same answer — not a generic
    /// unhandled-exception modal over a confirmation line saying nothing.
    [Fact]
    public async Task SaveReport_WhenTheRenderFails_SaysSoInsteadOfThrowing()
    {
        var (vm, _, state) = Build(renderReport: (_, _) =>
            throw new UnauthorizedAccessException("Access to the path is denied."));
        await state.ScanAsync();

        vm.SaveReportCommand.Execute(null);

        Assert.Equal(
            EnglishLoc().F("overview.report.card.failed", "Access to the path is denied."),
            vm.ReportSavedText);
    }

    /// The other half of the same button. Writing the PNG was inside the try;
    /// BUILDING the model was not, and building it reads the fix journal. A
    /// corrupt fix-journal.jsonl therefore threw straight past the catch and
    /// out of a RelayCommand — an unhandled-exception dialog on the one
    /// surface whose console twin answers the same failure with a sentence.
    ///
    /// The throw is armed after the scan on purpose: Refresh reads the journal
    /// too, and what is under test here is the button's own read.
    [Fact]
    public async Task SaveReport_WhenTheJournalReadFails_SaysSoInsteadOfThrowing()
    {
        var host = new UnreadableJournalHost();
        var state = new AppState(host);
        var vm = new OverviewViewModel(state, host, new FixAllService(host),
            new SafeCleanRunner(new CleanService(host, new Settings()), new FakeBin()),
            new FakeLive(), EnglishLoc(), () => false, (_, _) => true);
        await state.ScanAsync();
        host.Armed = true;

        vm.SaveReportCommand.Execute(null);

        Assert.Equal(
            EnglishLoc().F("overview.report.card.failed", "fix-journal.jsonl is corrupt"),
            vm.ReportSavedText);
    }

    /// Fakes.cs is locked, so a fix journal that cannot be read is simulated
    /// with a decorator whose ListUndoable throws once the test arms it.
    private sealed class UnreadableJournalHost : IEngineHost
    {
        public FakeEngineHost Inner { get; } = new();
        public bool Armed { get; set; }

        public System.Collections.Generic.IReadOnlyList<UndoableFix> ListUndoable() =>
            Armed
                ? throw new System.Text.Json.JsonException("fix-journal.jsonl is corrupt")
                : Inner.ListUndoable();

        public Task<ScanSnapshot> ScanAsync(IProgress<string>? progress = null,
            System.Threading.CancellationToken ct = default) => Inner.ScanAsync(progress, ct);
        public FixOutcome Fix(string ruleId) => Inner.Fix(ruleId);
        public FixOutcome Undo(string ruleId) => Inner.Undo(ruleId);
        public CleanReport Clean(TargetScanResult scan, bool dryRun,
                Action<CleanEntry>? onEntry = null) =>
            Inner.Clean(scan, dryRun, onEntry);
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
        public FixOutcome KeepDisplayFix() => Inner.KeepDisplayFix();
        public SessionIdentity Session() => Inner.Session();
        public bool IsElevated() => Inner.IsElevated();
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
        public CleanReport Clean(TargetScanResult scan, bool dryRun,
                Action<CleanEntry>? onEntry = null) =>
            Inner.Clean(scan, dryRun, onEntry);
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
        public FixOutcome KeepDisplayFix() => Inner.KeepDisplayFix();
        public SessionIdentity Session() => Inner.Session();
        public bool IsElevated() => Inner.IsElevated();
    }
}
