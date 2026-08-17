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
        return (new HealthViewModel(state, host, EnglishLoc(), isDryRun ?? (() => false),
            new FixAllService(host), morphPause: () => Task.CompletedTask), host, state);
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
            crossLinkKey: "health.crosslink");
        var perf = new HealthViewModel(state, host, loc, () => false,
            new FixAllService(host), FindingSections.IsPerformance,
            doneFilter: FindingSections.IsPerformance,
            crossLinkKey: "performance.crosslink");
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
        var line = Assert.Single(vm.ReportLines);
        Assert.Equal(loc["rule.power-plan.done"], line.Text);
        Assert.True(line.IsDone);
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

        // the partial note is an info line in the report — dotless, honest
        Assert.Equal("", vm.Message);
        var partial = vm.ReportLines.Single(l => !l.IsDone);
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

    [Theory]
    [InlineData(95, "Good")]
    [InlineData(72, "SeverityWarning")]
    [InlineData(50, "SeverityCritical")]
    public async Task ScoreBrushKey_FollowsScore(int health, string expected)
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = new ScanSnapshot(Array.Empty<DiagnosticFinding>(),
            new ScanResult(Array.Empty<TargetScanResult>()), health,
            new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc));
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
        public CleanReport Clean(TargetScanResult scan, bool dryRun) =>
            _inner.Clean(scan, dryRun);
        public IReadOnlyList<UndoableFix> ListUndoable() => _inner.ListUndoable();
        public IReadOnlyList<ActionLogEntry> ReadLog(int max = 200) => _inner.ReadLog(max);
        public IReadOnlyList<StartupEntry> ListStartup() => _inner.ListStartup();
        public bool SetStartupEnabled(string hive, string name, bool enabled) =>
            _inner.SetStartupEnabled(hive, name, enabled);
        public bool RunElevated(string cliArgs) => _inner.RunElevated(cliArgs);
        public bool CreateRestorePoint() => _inner.CreateRestorePoint();
        public long FreeDiskBytes() => _inner.FreeDiskBytes();
        public long LifetimeReclaimedBytes() => _inner.LifetimeReclaimedBytes();
        public bool IsElevated() => _inner.IsElevated();
    }
}
