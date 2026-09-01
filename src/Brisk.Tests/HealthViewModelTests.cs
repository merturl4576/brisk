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
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Logging;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

public class HealthViewModelTests
{
    private static Loc EnglishLoc()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        return loc;
    }

    private static (HealthViewModel Vm, FakeEngineHost Host, AppState State) Build(
        Func<bool>? isDryRun = null)
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", Severity.Warning, RuleCategory.Auto,
                stars: 4, canFix: true),
            TestData.Finding("custom-x", Severity.Info, RuleCategory.Advise,
                stars: 2, canFix: false),
        });
        host.Undoable.Add(new UndoableFix("visual-effects", DateTime.UtcNow));
        var state = new AppState(host);
        var fixAll = new FixAllService(host);
        // Wired exactly as App.xaml.cs wires it: the confirmation is raised
        // as each rule is fixed, not from a loop over the finished batch.
        state.TrackFixes(fixAll);
        return (new HealthViewModel(state, host, EnglishLoc(), isDryRun ?? (() => false),
            fixAll, morphPause: () => Task.CompletedTask), host, state);
    }

    /// ROUND 11 page hero: the numeric score twin drives the gauge sweep,
    /// and the status sentence speaks over THIS page's slice of findings.
    [Fact]
    public async Task PageHero_ScoreValueAndStatusLine_FollowThePagesSlice()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build();
        Assert.Equal(0.0, vm.ScoreValue);   // empty track before the first scan
        await state.ScanAsync();

        Assert.Equal(72.0, vm.ScoreValue);
        Assert.Equal(loc["overview.status.attention"], vm.StatusLine);

        // fixables gone, one advise left → positive with the count
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("custom-x", Severity.Info, RuleCategory.Advise,
                stars: 2, canFix: false),
        });
        await state.ScanAsync();
        Assert.Equal(loc.F("overview.status.advise", 1), vm.StatusLine);

        // nothing at all → plain good news
        host.NextSnapshot = TestData.Snapshot();
        await state.ScanAsync();
        Assert.Equal(loc["overview.status.good"], vm.StatusLine);
    }

    /// v0.4 stopped a Notice lowering the score, because brisk cannot charge
    /// for what it can only report. The COLOUR kept charging: thermals is
    /// Severity.Warning, so the row wore warning amber under a sentence saying
    /// the machine is in good shape. Severity still describes how bad the
    /// thing is; Kind decides whether brisk is in a position to call it a
    /// problem, and the colour follows Kind first.
    [Fact]
    public async Task NoticeRows_DoNotWearTheWarningColour()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("thermals", Severity.Warning, RuleCategory.Advise,
                canFix: false, kind: FindingKind.Notice),
        });
        await state.ScanAsync();

        var notice = Assert.Single(vm.NoticeRows);
        Assert.Equal("SeverityNotice", notice.SeverityKey);
    }

    [Fact]
    public async Task Rows_MapFindings_TitlesLocalized_WithEngineFallback()
    {
        var (vm, _, state) = Build();
        await state.ScanAsync();

        var power = Assert.Single(vm.Rows);
        Assert.Equal("power-plan", power.RuleId);
        // resx has rule.power-plan.title -> localized, not the engine string
        Assert.Equal("Power plan is limiting speed", power.Title);
        Assert.Equal("SeverityWarning", power.SeverityKey);
        Assert.Equal("●●●●○", power.ImpactText);
        Assert.True(power.CanFix);

        // Advise findings live in their own "Recommendations" section
        var custom = Assert.Single(vm.AdviseRows);
        Assert.Equal("custom-x", custom.RuleId);
        // rule.custom-x.title is not in the resx -> engine English fallback
        Assert.Equal("Title custom-x", custom.Title);
        Assert.True(custom.IsAdvise);
        Assert.False(custom.CanFix);
    }

    [Fact]
    public async Task AdviseRow_SpeaksLocalizedAdvice_EvidenceBehindDetailsFold()
    {
        var loc = EnglishLoc();
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
        });
        var state = new AppState(host);
        var vm = new HealthViewModel(state, host, loc, () => false,
            new FixAllService(host));
        await state.ScanAsync();

        var row = Assert.Single(vm.AdviseRows);
        Assert.Equal(loc["rule.thermals.advice"], row.AdviceText);
        Assert.NotEqual(row.Evidence, row.AdviceText);   // no English body
        Assert.True(row.HasDetails);                     // evidence still reachable
        Assert.False(row.IsDetailsShown);                // …but folded by default
    }

    [Fact]
    public async Task AdviseRow_WithoutAdviceKey_FallsBackToEvidence_NoRedundantFold()
    {
        var (vm, _, state) = Build();   // custom-x has no rule.custom-x.advice
        await state.ScanAsync();

        var row = Assert.Single(vm.AdviseRows);
        Assert.Equal(row.Evidence, row.AdviceText);
        Assert.False(row.HasDetails);   // the fold would just repeat the body
    }

    [Fact]
    public async Task AdviseRow_StorageRules_GetTheOpenStorageAction()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("disk-breakdown", cat: RuleCategory.Advise, canFix: false),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
        });
        var state = new AppState(host);
        var vm = new HealthViewModel(state, host, EnglishLoc(), () => false,
            new FixAllService(host));
        var navigations = 0;
        vm.OpenStorageRequested += () => navigations++;
        await state.ScanAsync();

        var disk = vm.AdviseRows.Single(r => r.RuleId == "disk-breakdown");
        Assert.True(disk.HasStorageAction);
        Assert.True(disk.OpenStorageCommand.CanExecute(null));
        disk.OpenStorageCommand.Execute(null);
        Assert.Equal(1, navigations);

        // no in-app action exists for thermals — no fake button
        Assert.False(vm.AdviseRows.Single(r => r.RuleId == "thermals").HasStorageAction);
        // fixable rows keep their Fix/Undo rendering, never the storage action
        Assert.False(vm.Rows.Single(r => r.RuleId == "power-plan").HasStorageAction);
    }

    /// Kind decides placement: a Notice leaves the advise section for its
    /// own band. It is NEVER hidden — kind changes scoring and placement,
    /// and a fact brisk can only report still gets a card that says so.
    [Fact]
    public async Task NoticeFindings_LandInTheNoticeBand_NotTheAdviseSection()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("disk-breakdown", cat: RuleCategory.Advise, canFix: false),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false,
                kind: FindingKind.Notice),
        });

        await state.ScanAsync();

        Assert.Single(vm.AdviseRows);
        Assert.Equal("disk-breakdown", vm.AdviseRows[0].RuleId);
        Assert.Single(vm.NoticeRows);
        Assert.Equal("thermals", vm.NoticeRows[0].RuleId);
        Assert.Empty(vm.Rows.Where(r => r.RuleId == "thermals"));

        // The band is rebuilt by each scan, not stacked onto.
        await state.ScanAsync();
        Assert.Single(vm.NoticeRows);
    }

    /// Notices cost the score nothing, but they are still worth reading, so
    /// the hero sentence counts them among the öneri. A page whose only
    /// findings are notices must not announce good news directly above a
    /// band that is listing them — that would erase them from the count.
    [Fact]
    public async Task StatusLine_CountsNotices_AmongTheAdvice()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false,
                kind: FindingKind.Notice),
        });

        await state.ScanAsync();

        Assert.Empty(vm.Rows);
        Assert.Empty(vm.AdviseRows);          // the notice band holds it alone
        Assert.Equal(loc.F("overview.status.advise", 1), vm.StatusLine);
        Assert.NotEqual(loc["overview.status.good"], vm.StatusLine);

        // advice and notices together: one count spanning both bands
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("disk-breakdown", cat: RuleCategory.Advise, canFix: false),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false,
                kind: FindingKind.Notice),
        });
        await state.ScanAsync();
        Assert.Equal(loc.F("overview.status.advise", 2), vm.StatusLine);

        // A problem alongside a notice: the advisory count must never
        // outrank a fixable row. "Bilgisayarın iyi durumda — 1 öneri" over
        // a page listing something brisk can fix is the exact reassurance
        // this wave exists to stop.
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false,
                kind: FindingKind.Notice),
        });
        await state.ScanAsync();
        Assert.Equal(loc["overview.status.attention"], vm.StatusLine);
    }

    /// The overview band's "see the evidence" opens the named card wherever
    /// it sits — the problem list, the advice, or the notice band.
    ///
    /// All three bands are asserted positively because all three are real:
    /// RevelationPicker leads with startup-bloat or display-refresh (problem
    /// rows) and disk-breakdown (advise) as readily as with a notice, so
    /// whichever band the link reaches depends on the machine, not on the
    /// caller. A band lost from the lookup would be navigate-but-don't-expand
    /// — the defect this method exists to fix, returning silently for
    /// everyone whose top revelation is not a notice.
    [Fact]
    public async Task ExpandFinding_OpensTheNamedCard_WhereverItLives()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
            TestData.Finding("startup-bloat", cat: RuleCategory.Confirm, canFix: true),
            TestData.Finding("disk-breakdown", cat: RuleCategory.Advise, canFix: false),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false,
                kind: FindingKind.Notice),
        }, new SensorStatus(true, true, null));
        await state.ScanAsync();

        vm.ExpandFinding("startup-bloat");    // problem list
        vm.ExpandFinding("disk-breakdown");   // advice
        vm.ExpandFinding("thermals");         // notice band

        Assert.True(vm.Rows.Single(r => r.RuleId == "startup-bloat").IsExpanded);
        Assert.True(vm.AdviseRows.Single(r => r.RuleId == "disk-breakdown").IsExpanded);
        Assert.True(vm.NoticeRows.Single(r => r.RuleId == "thermals").IsExpanded);
        // A card nobody asked for stays shut: the link opens one, not the page.
        Assert.False(vm.Rows.Single(r => r.RuleId == "power-plan").IsExpanded);
    }

    [Fact]
    public async Task SectionFilter_SplitsFindingsAcrossPages()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
            TestData.Finding("ram-pressure", cat: RuleCategory.Advise, canFix: false),
            TestData.Finding("storage-sense", cat: RuleCategory.Confirm, canFix: true),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
            TestData.Finding("some-future-rule", cat: RuleCategory.Confirm, canFix: true),
        });
        var state = new AppState(host);
        var health = new HealthViewModel(state, host, EnglishLoc(), () => false,
            new FixAllService(host), FindingSections.IsHealth);
        var perf = new HealthViewModel(state, host, EnglishLoc(), () => false,
            new FixAllService(host), FindingSections.IsPerformance);
        await state.ScanAsync();

        // Performans: speed levers incl. the advise-level RAM finding
        Assert.Equal(new[] { "power-plan" }, perf.Rows.Select(r => r.RuleId));
        Assert.Equal(new[] { "ram-pressure" }, perf.AdviseRows.Select(r => r.RuleId));
        // Sağlık: machine/disk condition; unknown rules default here
        Assert.Equal(new[] { "storage-sense", "some-future-rule" },
            health.Rows.Select(r => r.RuleId));
        Assert.Equal(new[] { "thermals" }, health.AdviseRows.Select(r => r.RuleId));
    }

    /// The notice rules are the ones the revelation band leads with, and
    /// "see the evidence" routes off THIS predicate — so the id-to-page
    /// mapping is pinned directly, not just through a view model's filter.
    /// boot-degradation is the one live use caught: its card is on
    /// Performans and the link went to Sağlık. Moving an id between the two
    /// sets is a routing change, and it has to fail a test to happen.
    [Theory]
    [InlineData("boot-degradation", true)]
    [InlineData("memory-speed", true)]
    [InlineData("ram-pressure", true)]
    [InlineData("thermals", false)]     // machine condition, not a speed lever
    // Disk space, so Saglik — and beside disk-breakdown, which is the folder
    // total the same walk produced. A reader who follows one of the two to a
    // page must find the other one on it.
    [InlineData("disk-breakdown", false)]
    [InlineData("large-files", false)]
    public void NoticeRules_ClassifyToThePageThatHoldsTheirCard(
        string ruleId, bool onPerformance)
    {
        // IsHealth stopped being simply !IsPerformance when the privacy ids
        // arrived, so the either/or the second assertion encodes is no longer
        // general: it holds for these four because none of them is a privacy
        // id. That precondition is asserted rather than trusted to a comment,
        // so a privacy id added to this theory fails on a line that says why
        // rather than as a bare Expected/Actual on one of the two below.
        // Privacy routing has its own pins in PrivacyRedLineTests.
        Assert.False(FindingSections.IsPrivacy(ruleId),
            $"{ruleId} is a privacy rule — this theory covers only the "
            + "Performans/Sağlık either-or");
        Assert.Equal(onPerformance, FindingSections.IsPerformance(ruleId));
        Assert.Equal(!onPerformance, FindingSections.IsHealth(ruleId));
    }

    [Fact]
    public async Task DoneRows_ComeFromTheJournal_SlicedPerPage_NewestFirst()
    {
        var loc = EnglishLoc();
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot();
        // journal: one performance fix, one health fix, out of time order
        host.Undoable.Add(new UndoableFix("browser-gpu",
            new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc)));
        host.Undoable.Add(new UndoableFix("storage-sense",
            new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc)));
        host.Undoable.Add(new UndoableFix("power-plan",
            new DateTime(2026, 8, 16, 10, 0, 0, DateTimeKind.Utc)));
        var state = new AppState(host);
        var perf = new HealthViewModel(state, host, loc, () => false,
            new FixAllService(host), FindingSections.IsPerformance,
            doneFilter: FindingSections.IsPerformance);
        var health = new HealthViewModel(state, host, loc, () => false,
            new FixAllService(host), FindingSections.IsHealth,
            doneFilter: FindingSections.IsHealth);
        await state.ScanAsync();

        // each page reports its own slice, newest first, in past tense —
        // only what brisk actually did, never a static checklist
        Assert.Equal(new[] { "power-plan", "browser-gpu" },
            perf.DoneRows.Select(r => r.RuleId));
        Assert.Equal(loc["rule.power-plan.done"], perf.DoneRows[0].Title);
        Assert.Equal(new[] { "storage-sense" },
            health.DoneRows.Select(r => r.RuleId));
        Assert.True(perf.ShowDoneReport);
        Assert.Equal(loc.F("overview.report.live", 2), perf.DoneLead);
    }

    [Fact]
    public async Task DoneRows_EmptyJournal_NoReportFace()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot();
        var state = new AppState(host);
        var perf = new HealthViewModel(state, host, EnglishLoc(), () => false,
            new FixAllService(host), FindingSections.IsPerformance,
            doneFilter: FindingSections.IsPerformance);
        await state.ScanAsync();

        Assert.Empty(perf.DoneRows);          // nothing ever fixed
        Assert.False(perf.ShowDoneReport);    // → no empty frame
        Assert.Equal("", perf.DoneLead);
    }

    [Fact]
    public async Task DoneRow_Undo_CallsHost_ThenRescans()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot();
        host.Undoable.Add(new UndoableFix("power-plan",
            new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc)));
        var state = new AppState(host);
        var perf = new HealthViewModel(state, host, EnglishLoc(), () => false,
            new FixAllService(host), FindingSections.IsPerformance,
            doneFilter: FindingSections.IsPerformance,
            morphPause: () => Task.CompletedTask);
        await state.ScanAsync();

        await perf.UndoDoneAsync(Assert.Single(perf.DoneRows));

        Assert.Equal(new[] { "power-plan" }, host.Undone);
        Assert.Equal(2, host.ScanCalls);
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
            TestData.Finding("custom-x", cat: RuleCategory.Advise, canFix: false),
        });
        await state.ScanAsync();                           // only advice remains
        Assert.False(vm.FixAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task FixAllButton_IgnoresPageFilter_FixAllActsOnWholeSnapshot()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            // performance-section finding: hidden on the Sağlık page …
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
        });
        var state = new AppState(host);
        var vm = new HealthViewModel(state, host, EnglishLoc(), () => false,
            new FixAllService(host), FindingSections.IsHealth);
        await state.ScanAsync();

        Assert.Empty(vm.Rows);                          // … page shows nothing
        Assert.True(vm.FixAllCommand.CanExecute(null)); // but fix-all would act
    }

    [Fact]
    public async Task CanUndo_ComesFromJournal()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
            { TestData.Finding("visual-effects", cat: RuleCategory.Confirm) });
        await state.ScanAsync();
        Assert.True(vm.Rows.Single().CanUndo);
    }

    [Fact]
    public async Task FixRow_CallsHost_ThenRescans()
    {
        var (vm, host, state) = Build();
        await state.ScanAsync();
        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "power-plan"));

        Assert.Equal(new[] { "power-plan" }, host.Fixed);
        Assert.Equal(2, host.ScanCalls);
    }

    // A display change can blank the screen, so it is applied provisionally.
    // The confirmation lives on AppState (fix round 1), not on this page's
    // own view model: FixAllService acts unfiltered, so Fix all on EITHER
    // findings page — and the tray — can trigger display-refresh, and only
    // the shared state is guaranteed to be watched by whichever page (or
    // MainWindow's own overlay) is actually on screen.
    [Fact]
    public async Task FixingDisplayRefresh_RaisesAConfirmation()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("display-refresh", Severity.Critical, RuleCategory.Auto,
                stars: 5, canFix: true),
        });
        await state.ScanAsync();

        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "display-refresh"));

        // Structural, not incidental: ConfirmDisplayFix sets this on the
        // caller's own thread before it ever touches a background task, so
        // it is guaranteed set the moment FixAsync returns control here —
        // regardless of the (default, 15s) window still being open.
        Assert.NotNull(state.PendingConfirmation);
        // Resolve it via Keep (the same path a user answering "yes" takes)
        // instead of leaving the real 15-second window's timer running
        // after this test returns.
        state.KeepDisplayCommand.Execute(null);
        await state.PendingConfirmTask!;
    }

    [Fact]
    public async Task FixingAnotherRule_RaisesNoConfirmation()
    {
        var (vm, _, state) = Build();
        await state.ScanAsync();

        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "power-plan"));

        Assert.Null(state.PendingConfirmation);
    }

    [Fact]
    public async Task ConfirmationWindowElapsing_UndoesTheDisplayFix()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("display-refresh", Severity.Critical, RuleCategory.Auto,
                stars: 5, canFix: true),
        });
        await state.ScanAsync();

        // Zero-length window: the same path a user takes by not answering.
        state.ConfirmationWindow = TimeSpan.Zero;
        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "display-refresh"));
        // A real join point (fix round 1): the rollback runs on a background
        // task now, so waiting for it is what makes the assertions below
        // deterministic rather than trusting a zero delay to finish first.
        await state.PendingConfirmTask!;

        Assert.Equal(new[] { "display-refresh" }, host.Undone);
        Assert.Null(state.PendingConfirmation);
    }

    /// Fix round 2 (Critical, Finding A): the flyout — not MainWindow — is
    /// the app's default startup surface (App.xaml.cs shows it, not the
    /// main window, unless launched with "--tray"), and the overlay lives
    /// only in MainWindow. App.xaml.cs subscribes to this event and calls
    /// its existing ShowMain() so the window with the overlay actually
    /// comes on screen. App.xaml.cs itself is not unit-tested — this pins
    /// the AppState-level contract that layer depends on: raising a
    /// confirmation fires the event, and a non-matching rule id does not.
    [Fact]
    public async Task ConfirmDisplayFix_RaisesConfirmationRaised()
    {
        var state = new AppState(new FakeEngineHost());
        var raisedCount = 0;
        state.ConfirmationRaised += () => raisedCount++;

        state.ConfirmDisplayFix("power-plan");
        Assert.Equal(0, raisedCount);

        state.ConfirmDisplayFix("display-refresh");
        Assert.Equal(1, raisedCount);

        // Resolve rather than leave the real 15-second window's background
        // timer running past this test's return.
        state.KeepDisplayCommand.Execute(null);
        await state.PendingConfirmTask!;
    }

    /// Fix round 1 (Critical): FixAllService is unfiltered, so pressing Fix
    /// all on the HEALTH page — which never shows display-refresh as a row,
    /// since Task 2 routes it to Performance — can still fix it. Before the
    /// confirmation moved to AppState, that meant the rollback ran with no
    /// overlay watching anywhere: this proves the fix reaches the shared
    /// state even from a page whose own filter excludes the rule entirely.
    [Fact]
    public async Task FixAllOnHealthPage_StillRaisesTheDisplayConfirmation()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("display-refresh", Severity.Critical, RuleCategory.Auto,
                stars: 5, canFix: true),
        });
        var state = new AppState(host);
        var fixAll = new FixAllService(host);
        state.TrackFixes(fixAll);
        var health = new HealthViewModel(state, host, EnglishLoc(), () => false,
            fixAll, FindingSections.IsHealth,
            morphPause: () => Task.CompletedTask);
        await state.ScanAsync();
        Assert.Empty(health.Rows);   // display-refresh is not even a row here

        await health.FixAllAsync();

        Assert.NotNull(state.PendingConfirmation);
        // Same cleanup as above: resolve rather than leave the 15-second
        // window's background timer running past this test's return.
        state.KeepDisplayCommand.Execute(null);
        await state.PendingConfirmTask!;
    }

    /// FIX WAVE, Finding 1. The registry is the only thing a reboot reads, so
    /// writing the raised mode there before anyone confirms it is what turns
    /// "the screen went black and I held the power button" into a machine that
    /// boots black with no brisk running to undo it. Nothing may be persisted
    /// while the question is still open; the Keep is what persists it.
    [Fact]
    public async Task Keep_IsTheOnlyThingThatWritesTheModeToTheRegistry()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("display-refresh", Severity.Critical, RuleCategory.Auto,
                stars: 5, canFix: true),
        });
        await state.ScanAsync();

        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "display-refresh"));
        Assert.Equal(0, host.KeepDisplayCalls);   // still provisional

        state.KeepDisplayCommand.Execute(null);
        await state.PendingConfirmTask!;

        Assert.Equal(1, host.KeepDisplayCalls);
    }

    /// The other half of the same rule: nobody answered, so nothing was ever
    /// written — a restart would have undone it even if the rollback had not.
    [Fact]
    public async Task ConfirmationWindowElapsing_PersistsNothing()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("display-refresh", Severity.Critical, RuleCategory.Auto,
                stars: 5, canFix: true),
        });
        await state.ScanAsync();

        state.ConfirmationWindow = TimeSpan.Zero;
        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "display-refresh"));
        await state.PendingConfirmTask!;

        Assert.Equal(0, host.KeepDisplayCalls);
        Assert.Equal(new[] { "display-refresh" }, host.Undone);
    }

    /// FIX WAVE, Finding 3. The rollback ran on a background task that never
    /// rescanned, and only a scan repopulates the rows — so both pages went on
    /// showing "Displays raised to their highest refresh rate" as a live,
    /// undoable fix for a mode that had gone back minutes earlier. There is no
    /// periodic scan to save it: the claim stood until something unrelated
    /// happened to trigger one.
    [Fact]
    public async Task ConfirmationWindowElapsing_RescansSoNothingKeepsClaimingTheFix()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("display-refresh", Severity.Critical, RuleCategory.Auto,
                stars: 5, canFix: true),
        });
        await state.ScanAsync();
        // The mode going back is exactly what makes the next scan see the
        // display running slow again — here, a snapshot with nothing in it.
        host.OnUndo = _ => host.NextSnapshot = TestData.Snapshot();

        state.ConfirmationWindow = TimeSpan.Zero;
        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "display-refresh"));
        await state.PendingConfirmTask!;

        Assert.Empty(state.Snapshot!.Findings);
        Assert.Empty(vm.Rows);
    }

    /// FIX WAVE, Finding 3. A rollback delegate that THROWS left RolledBack
    /// false and surfaced nothing at all — the app silently forgot that it had
    /// changed the display and failed to change it back.
    [Fact]
    public async Task RollbackThatThrows_IsStillReported()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("display-refresh", Severity.Critical, RuleCategory.Auto,
                stars: 5, canFix: true),
        });
        await state.ScanAsync();
        host.OnUndo = _ => throw new InvalidOperationException("the journal is gone");

        state.ConfirmationWindow = TimeSpan.Zero;
        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "display-refresh"));
        await state.PendingConfirmTask!;   // must complete, not fault

        Assert.Contains("the journal is gone", state.DisplayNotice);
        Assert.Null(state.PendingConfirmation);
    }

    /// FIX WAVE, Finding 4 (spec gap). The spec requires: "When the countdown
    /// expires, brisk reports honestly what it tried and that it rolled back,
    /// naming the likely cause (cable or adapter)." Nothing said anything on
    /// the ordinary path — only a rollback that itself failed produced a
    /// message, which is the rarest outcome of the three.
    [Fact]
    public async Task OrdinaryRollback_SaysSo_AndNamesTheLikelyCause()
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
        var vm = new HealthViewModel(state, host, loc, () => false, fixAll,
            morphPause: () => Task.CompletedTask);
        await state.ScanAsync();

        state.ConfirmationWindow = TimeSpan.Zero;
        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "display-refresh"));
        await state.PendingConfirmTask!;

        Assert.Equal(loc["display-confirm.rolledback"], state.DisplayNotice);
        Assert.Contains("cable", state.DisplayNotice);
    }

    /// FIX WAVE, Finding 5. The confirmation used to be raised from a loop
    /// over the FINISHED batch, so a display raised first sat there — possibly
    /// black — through every remaining fix with no timer running at all. It
    /// now starts at the mode change, which means the rules fixed after it in
    /// the same batch already see a countdown in flight.
    [Fact]
    public async Task FixAll_StartsTheCountdownAtTheModeChange_NotAfterTheBatch()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("display-refresh", Severity.Critical, RuleCategory.Auto,
                stars: 5, canFix: true),
            TestData.Finding("power-plan", Severity.Warning, RuleCategory.Auto,
                stars: 4, canFix: true),
        });
        var state = new AppState(host, EnglishLoc());
        var fixAll = new FixAllService(host);
        state.TrackFixes(fixAll);
        var pendingWhenFixed = new List<(string Rule, bool Pending)>();
        // Subscribed AFTER TrackFixes, so this observes the state the batch
        // was in as each rule landed.
        fixAll.FixedRule += (finding, _) =>
            pendingWhenFixed.Add((finding.RuleId, state.PendingConfirmation is not null));
        var vm = new HealthViewModel(state, host, EnglishLoc(), () => false, fixAll,
            morphPause: () => Task.CompletedTask);
        await state.ScanAsync();

        await vm.FixAllAsync();

        Assert.True(pendingWhenFixed.Single(x => x.Rule == "power-plan").Pending);
        state.KeepDisplayCommand.Execute(null);
        await state.PendingConfirmTask!;
    }

    /// FIX WAVE, Finding 5, single-row half: the 400 ms "Fixed" morph used to
    /// run before the countdown started.
    [Fact]
    public async Task SingleFix_StartsTheCountdownBeforeTheMorphPause()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("display-refresh", Severity.Critical, RuleCategory.Auto,
                stars: 5, canFix: true),
        });
        var state = new AppState(host, EnglishLoc());
        var fixAll = new FixAllService(host);
        state.TrackFixes(fixAll);
        var pendingDuringPause = false;
        var vm = new HealthViewModel(state, host, EnglishLoc(), () => false, fixAll,
            morphPause: () =>
            {
                pendingDuringPause = state.PendingConfirmation is not null;
                return Task.CompletedTask;
            });
        await state.ScanAsync();

        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "display-refresh"));

        Assert.True(pendingDuringPause);
        state.KeepDisplayCommand.Execute(null);
        await state.PendingConfirmTask!;
    }

    /// FIX WAVE, Finding 6. Each view model's busy flag guards only its own
    /// button, so a flyout Fix-all and a page Fix-all can overlap. A second
    /// confirmation replacing the first meant the first run's exit pulled the
    /// overlay out from under a window still counting down — and the second
    /// fix, finding every display already raised, journals an EMPTY prior
    /// state, so the rollback meant to bring the picture back restores
    /// nothing. One screen, one rescue.
    [Fact]
    public async Task SecondConfirmation_DoesNotReplaceTheOneAlreadyRunning()
    {
        var state = new AppState(new FakeEngineHost(), EnglishLoc());

        state.ConfirmDisplayFix("display-refresh");
        var first = state.PendingConfirmation;
        var firstRun = state.PendingConfirmTask;

        state.ConfirmDisplayFix("display-refresh");

        Assert.Same(first, state.PendingConfirmation);
        Assert.Same(firstRun, state.PendingConfirmTask);

        state.KeepDisplayCommand.Execute(null);
        await state.PendingConfirmTask!;
        Assert.Null(state.PendingConfirmation);
    }

    /// FIX WAVE re-review, N1. The rescue resolves inside Task.Run, so there
    /// is no SynchronizationContext under it and Changed fired on a
    /// thread-pool thread. Every subscriber is UI-affine —
    /// FlyoutViewModel.Refresh ends in RaiseCanExecuteChanged (IsEnabled on a
    /// ButtonBase), HealthViewModel.Refresh clears an ObservableCollection
    /// behind a CollectionView — so the first one threw, the throw aborted the
    /// rest of the invocation list, and RescanAsync's blanket catch swallowed
    /// it: the rescan reached nobody. xUnit has no dispatcher, so no
    /// assertion about rows could ever have caught that.
    ///
    /// This pins the seam instead. The marshal here queues instead of running,
    /// which is only possible if the raise genuinely goes THROUGH it.
    [Fact]
    public async Task RollbackRescan_ReachesSubscribersThroughTheUiMarshal()
    {
        var queued = new List<Action>();
        var host = new FakeEngineHost();
        var state = new AppState(host, EnglishLoc(), toUiThread: queued.Add);
        var changed = 0;
        state.Changed += () => changed++;

        state.ConfirmationWindow = TimeSpan.Zero;
        state.ConfirmDisplayFix("display-refresh");
        await state.PendingConfirmTask!;

        // Nothing has landed yet: the "dispatcher" has not run.
        Assert.Equal(0, changed);
        Assert.Equal("", state.DisplayNotice);
        Assert.NotEmpty(queued);

        foreach (var action in queued) action();

        Assert.True(changed > 0, "the rollback's rescan never raised Changed");
        Assert.NotEqual("", state.DisplayNotice);
    }

    /// FIX WAVE re-review, N4(b). ConfirmationRaised reaches a window
    /// (App.xaml.cs calls ShowMain), and a dispatcher shutting down throws
    /// there. Raised between the gate closing and the rollback task existing,
    /// that left the gate latched with no rescue behind it at all: fix-all
    /// dead app-wide, the window topmost until restart, and the rest of the
    /// fix batch abandoned on the worker thread.
    [Fact]
    public async Task ConfirmationRaisedThatThrows_LeavesAWorkingRescueBehindIt()
    {
        var host = new FakeEngineHost();
        var state = new AppState(host, EnglishLoc());
        state.ConfirmationRaised += () => throw new InvalidOperationException("no dispatcher");
        state.ConfirmationWindow = TimeSpan.Zero;

        state.ConfirmDisplayFix("display-refresh");

        Assert.NotNull(state.PendingConfirmTask);
        await state.PendingConfirmTask!;
        Assert.Null(state.PendingConfirmation);          // the gate reopened
        Assert.Equal(new[] { "display-refresh" }, host.Undone);
    }

    /// WAVE B, B1. The banner is the one home for the rollback sentence, so it
    /// must not become furniture: the Dismiss button clears it, and the next
    /// display attempt supersedes it. A scan deliberately does NOT — the scan
    /// after a rollback re-detects the display running slow, and the sentence
    /// explaining why the raise did not stick belongs beside that finding.
    [Fact]
    public async Task DisplayNotice_IsDismissible_AndSupersededByTheNextAttempt()
    {
        var host = new FakeEngineHost();
        var state = new AppState(host, EnglishLoc());
        state.ConfirmationWindow = TimeSpan.Zero;

        state.ConfirmDisplayFix("display-refresh");
        await state.PendingConfirmTask!;
        Assert.True(state.HasDisplayNotice);

        state.DismissDisplayNoticeCommand.Execute(null);
        Assert.False(state.HasDisplayNotice);
        Assert.Equal("", state.DisplayNotice);

        state.ConfirmDisplayFix("display-refresh");
        await state.PendingConfirmTask!;
        Assert.True(state.HasDisplayNotice);

        await state.ScanAsync();
        Assert.True(state.HasDisplayNotice);   // the finding is back; so is the reason

        // A new attempt is a new story: the old sentence goes as it is raised.
        state.ConfirmationWindow = TimeSpan.FromSeconds(15);
        state.ConfirmDisplayFix("display-refresh");
        Assert.False(state.HasDisplayNotice);
        state.KeepDisplayCommand.Execute(null);
        await state.PendingConfirmTask!;
    }

    /// WAVE B, B2. requireAdministrator on a machine whose interactive user is
    /// a STANDARD account means over-the-shoulder elevation: the user types a
    /// different administrator's credentials and brisk's token belongs to that
    /// administrator. HKCU, %LOCALAPPDATA% and the Recycle Bin then all follow
    /// the token — so brisk would scan and recycle a profile nobody is sitting
    /// in front of while the report says "your temp files". brisk does not
    /// refuse; it says whose profile this actually is.
    [Fact]
    public void DifferentInteractiveUser_IsDisclosed_NamingBothAccounts()
    {
        var host = new FakeEngineHost
        {
            SessionIdentity = new SessionIdentity(@"PC\Admin", @"PC\alice", true),
        };

        var state = new AppState(host, EnglishLoc());

        Assert.True(state.HasIdentityWarning);
        Assert.Contains(@"PC\Admin", state.IdentityWarning);
        Assert.Contains(@"PC\alice", state.IdentityWarning);
    }

    /// The ordinary case — an administrator account, where UAC preserves the
    /// SID — says nothing at all. A banner on every machine is noise, and
    /// noise is what gets ignored on the machine that needs it.
    [Fact]
    public void SameUser_SaysNothing()
    {
        var state = new AppState(new FakeEngineHost(), EnglishLoc());
        Assert.False(state.HasIdentityWarning);
    }

    /// An unknown interactive user is NOT a mismatch. A probe that cannot
    /// answer must not be turned into a confident claim about whose files
    /// these are.
    [Fact]
    public void UnknownInteractiveUser_ClaimsNothing()
    {
        var host = new FakeEngineHost
        {
            SessionIdentity = SessionIdentity.Unknown(@"PC\Admin"),
        };

        Assert.False(new AppState(host, EnglishLoc()).HasIdentityWarning);
    }

    /// Fix round 1 (Important, Finding 3): a rollback that could not
    /// actually restore the previous mode must not look identical to one
    /// that did. FakeEngineHost.Undo always succeeds, so a thin override
    /// simulates the journal-has-nothing-to-undo case FixRunner.Undo really
    /// returns, and asserts the failure surfaces on the page's Message
    /// instead of vanishing silently.
    [Fact]
    public async Task FailedRollback_SurfacesOnTheBanner_InsteadOfLookingLikeSuccess()
    {
        var inner = new FakeEngineHost();
        inner.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("display-refresh", Severity.Critical, RuleCategory.Auto,
                stars: 5, canFix: true),
        });
        var host = new UndoFailsHost(inner, "display-refresh: nothing to undo");
        var state = new AppState(host);
        var vm = new HealthViewModel(state, host, EnglishLoc(), () => false,
            new FixAllService(host), morphPause: () => Task.CompletedTask);
        await state.ScanAsync();

        state.ConfirmationWindow = TimeSpan.Zero;
        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "display-refresh"));
        await state.PendingConfirmTask!;

        // Wave B (B1): the banner lives on the window, so it says this on
        // whichever of the five pages the user is looking at.
        Assert.Equal("display-refresh: nothing to undo", state.DisplayNotice);
    }

    [Fact]
    public async Task FixAll_DryRun_NeverCallsHost_ShowsMessage()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build(() => true);
        await state.ScanAsync();

        await vm.FixAllAsync();

        Assert.Empty(host.Fixed);
        Assert.Equal(0, host.RestorePointCalls);
        Assert.Equal(loc["dryrun.blocked"], vm.Message);
    }

    [Fact]
    public async Task FixRow_DryRun_NeverCallsHost_ShowsMessage()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build(() => true);
        await state.ScanAsync();

        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "power-plan"));

        Assert.Empty(host.Fixed);
        Assert.Equal(loc["dryrun.blocked"], vm.Message);
    }

    [Fact]
    public async Task UndoRow_DryRun_NeverCallsHost_ShowsMessage()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build(() => true);
        host.NextSnapshot = TestData.Snapshot(new[]
            { TestData.Finding("visual-effects", cat: RuleCategory.Confirm) });
        await state.ScanAsync();

        await vm.UndoAsync(vm.Rows.Single());

        Assert.Empty(host.Undone);
        Assert.Equal(loc["dryrun.blocked"], vm.Message);
    }

    [Fact]
    public async Task FixAll_WithRestorePointRefused_AbortsWithMessage()
    {
        var loc = EnglishLoc();
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", Severity.Warning, RuleCategory.Auto,
                stars: 4, canFix: true),
        });
        var state = new AppState(host);
        var vm = new HealthViewModel(state, host, loc, () => false,
            new FixAllService(host));

        await state.ScanAsync();
        vm.CreateRestorePointFirst = true;
        host.RestorePointResult = false;

        await vm.FixAllAsync();

        Assert.Equal(1, host.RestorePointCalls);
        Assert.Empty(host.Fixed);
        Assert.Equal(loc["health.restorepointfailed"], vm.Message);
    }

    [Fact]
    public async Task FixAll_WithRestorePointOk_RunsFixes()
    {
        var (vm, host, state) = Build();
        await state.ScanAsync();
        vm.CreateRestorePointFirst = true;

        await vm.FixAllAsync();

        Assert.Equal(1, host.RestorePointCalls);
        Assert.Equal(new[] { "power-plan" }, host.Fixed);
    }

    [Fact]
    public async Task FixAll_IncludesConfirmFixables_LikeStartupBloat()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
            TestData.Finding("startup-bloat", cat: RuleCategory.Confirm, canFix: true),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
        });
        var state = new AppState(host);
        var vm = new HealthViewModel(state, host, EnglishLoc(), () => false,
            new FixAllService(host), morphPause: () => Task.CompletedTask);
        await state.ScanAsync();

        await vm.FixAllAsync();

        var loc = EnglishLoc();
        Assert.Equal(new[] { "power-plan", "startup-bloat" }, host.Fixed);
        Assert.Equal("", vm.Message);   // success speaks through the report
        Assert.Equal(
            loc.F("overview.report.summary", loc.F("overview.report.part.fixes", 2)),
            vm.ReportSummary);
    }

    [Fact]
    public async Task FixAll_NothingFixable_SaysSoPlainly()
    {
        var loc = EnglishLoc();
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
            TestData.Finding("ram-pressure", cat: RuleCategory.Advise, canFix: false),
        });
        var state = new AppState(host);
        var vm = new HealthViewModel(state, host, loc, () => false,
            new FixAllService(host));
        await state.ScanAsync();

        await vm.FixAllAsync();

        Assert.Empty(host.Fixed);
        Assert.Equal(loc["health.nofixables"], vm.Message);
    }

    [Fact]
    public async Task FixSingleRow_PopulatesTheEmphasizedReport()
    {
        var loc = EnglishLoc();
        var (vm, _, state) = Build();
        await state.ScanAsync();

        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "power-plan"));

        Assert.Equal("", vm.Message);
        var line = Assert.Single(vm.ReportLines);
        Assert.Equal(loc["rule.power-plan.done"], line.Text);
        Assert.True(line.IsDone);
        Assert.Equal(
            loc.F("overview.report.summary", loc.F("overview.report.part.fixes", 1)),
            vm.ReportSummary);
    }

    [Fact]
    public async Task ManualScan_ClearsThePreviousReport()
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
    public async Task UndoRow_ClearsTheStaleReport()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
            { TestData.Finding("visual-effects", cat: RuleCategory.Confirm) });
        await state.ScanAsync();
        await vm.FixAsync(vm.Rows.Single());
        Assert.NotEmpty(vm.ReportLines);

        await vm.UndoAsync(vm.Rows.Single());

        // a celebration of the undone fix would be stale news
        Assert.Empty(vm.ReportLines);
        Assert.Equal("", vm.ReportSummary);
    }

    /// The maintainer's first live look at 0.6.0 (2026-08-26) found this:
    /// with Gizlilik in the split, "more findings in <sibling>" counted
    /// EVERYTHING not on this page — privacy included — so Sağlık and
    /// Performans each promised 8 where their named target held 2. The
    /// count must follow the label; a third page must not inflate it.
    [Fact]
    public async Task CrossLinks_CountThePageTheyName_NotEverythingElsewhere()
    {
        var loc = EnglishLoc();
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
            TestData.Finding("storage-sense", cat: RuleCategory.Confirm, canFix: true),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
            TestData.Finding("advertising-id", cat: RuleCategory.Auto, canFix: true),
            TestData.Finding("usb-history", cat: RuleCategory.Advise, canFix: false),
        });
        var state = new AppState(host);
        var health = new HealthViewModel(state, host, loc, () => false,
            new FixAllService(host), FindingSections.IsHealth,
            crossLinkKey: "health.crosslink",
            crossLinkFilter: FindingSections.IsPerformance);
        var perf = new HealthViewModel(state, host, loc, () => false,
            new FixAllService(host), FindingSections.IsPerformance,
            crossLinkKey: "performance.crosslink",
            crossLinkFilter: FindingSections.IsHealth);
        await state.ScanAsync();

        // Sağlık names Performans and counts ONLY it: the one performance
        // finding, not one plus the two privacy findings beside it.
        Assert.Equal(loc.F("health.crosslink", 1), health.CrossLinkText);
        // Performans names Sağlık: its two, not two plus two.
        Assert.Equal(loc.F("performance.crosslink", 2), perf.CrossLinkText);
    }

    [Fact]
    public async Task CrossLinks_CountTheSiblingPagesFindings_AndHideAtZero()
    {
        var loc = EnglishLoc();
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
            TestData.Finding("storage-sense", cat: RuleCategory.Confirm, canFix: true),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
        });
        var state = new AppState(host);
        var health = new HealthViewModel(state, host, loc, () => false,
            new FixAllService(host), FindingSections.IsHealth,
            crossLinkKey: "health.crosslink",
            crossLinkFilter: FindingSections.IsPerformance);
        var perf = new HealthViewModel(state, host, loc, () => false,
            new FixAllService(host), FindingSections.IsPerformance,
            doneFilter: FindingSections.IsPerformance,
            crossLinkKey: "performance.crosslink",
            crossLinkFilter: FindingSections.IsHealth);
        await state.ScanAsync();

        // Sağlık points at the 1 performance finding; Performans at the 2
        // health findings (advise included — they are findings too).
        Assert.True(health.HasCrossLink);
        Assert.Equal(loc.F("health.crosslink", 1), health.CrossLinkText);
        Assert.Equal("1 more findings in Performance →", health.CrossLinkText);
        Assert.True(perf.HasCrossLink);
        Assert.Equal(loc.F("performance.crosslink", 2), perf.CrossLinkText);

        // counts follow the snapshot; the row hides at zero
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("storage-sense", cat: RuleCategory.Confirm, canFix: true),
        });
        await state.ScanAsync();
        Assert.False(health.HasCrossLink);
        Assert.Equal("", health.CrossLinkText);
        Assert.True(perf.HasCrossLink);
        Assert.Equal(loc.F("performance.crosslink", 1), perf.CrossLinkText);

        // the link navigates via the page-switch event
        var navigations = 0;
        perf.CrossNavigateRequested += () => navigations++;
        perf.CrossNavigateCommand.Execute(null);
        Assert.Equal(1, navigations);
    }

    [Fact]
    public async Task CrossLinks_NeverAppearOnPagesBuiltWithoutAKey()
    {
        var (vm, _, state) = Build();   // no crossLinkKey, no filter
        await state.ScanAsync();

        Assert.False(vm.HasCrossLink);
        Assert.Equal("", vm.CrossLinkText);
    }

    [Fact]
    public async Task Score_Renders()
    {
        var (vm, _, state) = Build();
        await state.ScanAsync();
        Assert.Equal("72", vm.ScoreText);
    }

    [Fact]
    public async Task FixAll_AllSucceed_ShowsEmphasizedReport()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build();
        await state.ScanAsync();

        await vm.FixAllAsync();

        Assert.Equal(new[] { "power-plan" }, host.Fixed);
        Assert.Equal("", vm.Message);   // success speaks through the report
        // outcome first, then the when-it-shows note (dotless, 2026-08-30)
        Assert.Equal(new[] { loc["rule.power-plan.done"], loc["report.expectation"] },
            vm.ReportLines.Select(l => l.Text));
        Assert.True(vm.ReportLines[0].IsDone);
        Assert.False(vm.ReportLines[1].IsDone);
        Assert.Equal(
            loc.F("overview.report.summary", loc.F("overview.report.part.fixes", 1)),
            vm.ReportSummary);
    }

    [Fact]
    public async Task FixAll_SomeFixesFail_ShowsPartialSummary()
    {
        var loc = EnglishLoc();
        var inner = new FakeEngineHost();
        inner.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", canFix: true),
            TestData.Finding("visual-effects", canFix: true),
        });
        var host = new FailingFixHost(inner, "visual-effects");
        var state = new AppState(host);
        var vm = new HealthViewModel(state, host, loc, () => false,
            new FixAllService(host), morphPause: () => Task.CompletedTask);
        await state.ScanAsync();

        await vm.FixAllAsync();

        // the partial note is an info line in the report — dotless, honest —
        // and since 2026-08-30 the when-it-shows note stands beside it
        Assert.Equal("", vm.Message);
        var partial = vm.ReportLines.Single(
            l => !l.IsDone && l.Text != loc["report.expectation"]);
        Assert.Equal(loc.F("health.fixpartial", 1, 2), partial.Text);
        Assert.Contains(vm.ReportLines,
            l => l.IsDone && l.Text == loc["rule.power-plan.done"]);
        Assert.Equal(
            loc.F("overview.report.summary", loc.F("overview.report.part.fixes", 1)),
            vm.ReportSummary);
    }

    [Fact]
    public async Task FixAll_TogglesIsBusy_AndRaisesChange()
    {
        var (vm, _, state) = Build();
        await state.ScanAsync();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        await vm.FixAllAsync();

        Assert.False(vm.IsBusy);
        Assert.Contains("IsBusy", raised);
    }

    /// The performance read-back line: only the instance that opted in (the
    /// Performans page) prints it, and only off a snapshot that carries a
    /// trend — both figures in whole seconds with their counts, the same
    /// rounding the boot-degradation rule uses.
    [Fact]
    public async Task BootTrend_ShowsOnThePerfInstance_AndOnlyThere()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(null) with
        {
            BootTrend = new BootTrend(59_400, 4, 41_200, 2,
                new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc)),
        };
        var state = new AppState(host);
        var loc = EnglishLoc();
        var perf = new HealthViewModel(state, host, loc, () => false,
            new FixAllService(host), showBootTrend: true);
        var health = new HealthViewModel(state, host, loc, () => false,
            new FixAllService(host));
        await state.ScanAsync();

        Assert.Contains("59 s", perf.BootTrendText);
        Assert.Contains("41 s", perf.BootTrendText);
        Assert.Contains("4 boots", perf.BootTrendText);
        Assert.Equal("", health.BootTrendText);
    }

    /// One boot after the changes is its own sentence — never "1 boots",
    /// and "the first boot since" is the honest label for evidence that thin.
    [Fact]
    public async Task BootTrend_SingleBootAfter_GetsItsOwnSentence()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(null) with
        {
            BootTrend = new BootTrend(59_400, 4, 41_200, 1,
                new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc)),
        };
        var state = new AppState(host);
        var perf = new HealthViewModel(state, host, EnglishLoc(), () => false,
            new FixAllService(host), showBootTrend: true);
        await state.ScanAsync();

        Assert.Contains("first boot", perf.BootTrendText);
        Assert.DoesNotContain("1 boots", perf.BootTrendText);
    }

    [Fact]
    public async Task BootTrend_NoTrendInTheSnapshot_LineStaysEmpty()
    {
        var host = new FakeEngineHost();   // TestData.Snapshot carries no trend
        var state = new AppState(host);
        var perf = new HealthViewModel(state, host, EnglishLoc(), () => false,
            new FixAllService(host), showBootTrend: true);
        await state.ScanAsync();

        Assert.Equal("", perf.BootTrendText);
    }

    [Theory]
    [InlineData(95, "Good")]
    [InlineData(72, "SeverityNotice")]
    [InlineData(50, "SeverityCritical")]
    public async Task ScoreBrushKey_FollowsScore(int health, string expected)
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = new ScanSnapshot(Array.Empty<DiagnosticFinding>(),
            new ScanResult(Array.Empty<TargetScanResult>()), health,
            new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc),
            new SensorStatus(false, false, null), Array.Empty<ReadBackResult>(),
            Array.Empty<UsbDeviceRecord>(), null);
        var state = new AppState(host);
        var vm = new HealthViewModel(state, host, EnglishLoc(), () => false,
            new FixAllService(host));

        await state.ScanAsync();

        Assert.Equal(expected, vm.ScoreBrushKey);
    }

    /// Fakes.cs is a locked contract, so partial FixAll failure is simulated
    /// with a thin decorator that only overrides Fix.
    private sealed class FailingFixHost : IEngineHost
    {
        private readonly FakeEngineHost _inner;
        private readonly string _failingRuleId;

        public FailingFixHost(FakeEngineHost inner, string failingRuleId)
        {
            _inner = inner;
            _failingRuleId = failingRuleId;
        }

        public FixOutcome Fix(string ruleId) =>
            string.Equals(ruleId, _failingRuleId, StringComparison.OrdinalIgnoreCase)
                ? new FixOutcome(false, ruleId)
                : _inner.Fix(ruleId);

        public Task<ScanSnapshot> ScanAsync(IProgress<string>? progress = null,
            CancellationToken ct = default) => _inner.ScanAsync(progress, ct);
        public FixOutcome Undo(string ruleId) => _inner.Undo(ruleId);
        public CleanReport Clean(TargetScanResult scan, bool dryRun,
                Action<CleanEntry>? onEntry = null) =>
            _inner.Clean(scan, dryRun, onEntry);
        public IReadOnlyList<UndoableFix> ListUndoable() => _inner.ListUndoable();
        public IReadOnlyList<ActionLogEntry> ReadLog(int max = 200) => _inner.ReadLog(max);
        public IReadOnlyList<StartupEntry> ListStartup() => _inner.ListStartup();
        public bool SetStartupEnabled(string hive, string name, bool enabled) =>
            _inner.SetStartupEnabled(hive, name, enabled);
        public bool RunElevated(string cliArgs) => _inner.RunElevated(cliArgs);
        public bool CreateRestorePoint() => _inner.CreateRestorePoint();
        public long FreeDiskBytes() => _inner.FreeDiskBytes();
        public long LifetimeReclaimedBytes() => _inner.LifetimeReclaimedBytes();
        public FixOutcome KeepDisplayFix() => _inner.KeepDisplayFix();
        public SessionIdentity Session() => _inner.Session();
        public bool IsElevated() => _inner.IsElevated();
    }

    /// Fakes.cs is a locked contract, so a failed rollback (the journal
    /// already had no prior state — FixRunner.Undo's real "nothing to undo"
    /// case) is simulated with a thin decorator that only overrides Undo.
    private sealed class UndoFailsHost : IEngineHost
    {
        private readonly FakeEngineHost _inner;
        private readonly string _message;

        public UndoFailsHost(FakeEngineHost inner, string message)
        {
            _inner = inner;
            _message = message;
        }

        public FixOutcome Undo(string ruleId) => new(false, _message);

        public FixOutcome Fix(string ruleId) => _inner.Fix(ruleId);
        public Task<ScanSnapshot> ScanAsync(IProgress<string>? progress = null,
            CancellationToken ct = default) => _inner.ScanAsync(progress, ct);
        public CleanReport Clean(TargetScanResult scan, bool dryRun,
                Action<CleanEntry>? onEntry = null) =>
            _inner.Clean(scan, dryRun, onEntry);
        public IReadOnlyList<UndoableFix> ListUndoable() => _inner.ListUndoable();
        public IReadOnlyList<ActionLogEntry> ReadLog(int max = 200) => _inner.ReadLog(max);
        public IReadOnlyList<StartupEntry> ListStartup() => _inner.ListStartup();
        public bool SetStartupEnabled(string hive, string name, bool enabled) =>
            _inner.SetStartupEnabled(hive, name, enabled);
        public bool RunElevated(string cliArgs) => _inner.RunElevated(cliArgs);
        public bool CreateRestorePoint() => _inner.CreateRestorePoint();
        public long FreeDiskBytes() => _inner.FreeDiskBytes();
        public long LifetimeReclaimedBytes() => _inner.LifetimeReclaimedBytes();
        public FixOutcome KeepDisplayFix() => _inner.KeepDisplayFix();
        public SessionIdentity Session() => _inner.Session();
        public bool IsElevated() => _inner.IsElevated();
    }
}
