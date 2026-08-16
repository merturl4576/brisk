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
            new FixAllService(host)), host, state);
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
            new FixAllService(host));
        await state.ScanAsync();

        await vm.FixAllAsync();

        Assert.Equal(new[] { "power-plan", "startup-bloat" }, host.Fixed);
        Assert.Equal(EnglishLoc().F("health.fixdone", 2), vm.Message);
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
    public async Task Score_Renders()
    {
        var (vm, _, state) = Build();
        await state.ScanAsync();
        Assert.Equal("72", vm.ScoreText);
    }

    [Fact]
    public async Task FixAll_AllSucceed_ShowsLocalizedSummary()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build();
        await state.ScanAsync();

        await vm.FixAllAsync();

        Assert.Equal(new[] { "power-plan" }, host.Fixed);
        Assert.Equal(loc.F("health.fixdone", 1), vm.Message);
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
            new FixAllService(host));
        await state.ScanAsync();

        await vm.FixAllAsync();

        Assert.Equal(loc.F("health.fixpartial", 1, 2), vm.Message);
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
