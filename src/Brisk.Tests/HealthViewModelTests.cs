using System;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Diagnostics;
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
        return (new HealthViewModel(state, host, EnglishLoc(), isDryRun ?? (() => false)),
            host, state);
    }

    [Fact]
    public async Task Rows_MapFindings_TitlesLocalized_WithEngineFallback()
    {
        var (vm, _, state) = Build();
        await state.ScanAsync();

        Assert.Equal(2, vm.Rows.Count);
        var power = vm.Rows.Single(r => r.RuleId == "power-plan");
        // resx has rule.power-plan.title -> localized, not the engine string
        Assert.Equal("Power plan is limiting speed", power.Title);
        Assert.Equal("SeverityWarning", power.SeverityKey);
        Assert.Equal("●●●●○", power.ImpactText);
        Assert.True(power.CanFix);

        var custom = vm.Rows.Single(r => r.RuleId == "custom-x");
        // rule.custom-x.title is not in the resx -> engine English fallback
        Assert.Equal("Title custom-x", custom.Title);
        Assert.True(custom.IsAdvise);
        Assert.False(custom.CanFix);
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
        var vm = new HealthViewModel(state, host, loc, () => false);

        await state.ScanAsync();
        vm.CreateRestorePointFirst = true;
        host.RestorePointResult = false;

        await vm.FixAllAsync();

        Assert.Equal(1, host.RestorePointCalls);
        Assert.Empty(host.Fixed);
        Assert.Equal(loc["health.restorepointfailed"], vm.Message);
    }

    [Fact]
    public async Task FixAll_WithRestorePointOk_FixesAutoRules()
    {
        var (vm, host, state) = Build();
        await state.ScanAsync();
        vm.CreateRestorePointFirst = true;

        await vm.FixAllAsync();

        Assert.Equal(1, host.RestorePointCalls);
        Assert.Equal(new[] { "power-plan" }, host.Fixed);
    }

    [Fact]
    public async Task Score_Renders()
    {
        var (vm, _, state) = Build();
        await state.ScanAsync();
        Assert.Equal("72", vm.ScoreText);
    }
}
