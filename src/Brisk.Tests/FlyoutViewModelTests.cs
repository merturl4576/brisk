using System;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

public class FlyoutViewModelTests
{
    private static Loc EnglishLoc()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        return loc;
    }

    private static FakeEngineHost HostWithSnapshot()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(
            new[]
            {
                TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
                TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
            },
            TestData.Target("user-temp", CleanupLevel.Safe, 2048),
            TestData.Target("chrome-cache", CleanupLevel.Safe, 0, skipped: "chrome is running"),
            TestData.Target("old-installers", CleanupLevel.Deep, 4096, pick: true),
            TestData.Target("npm-cache", CleanupLevel.Developer, 1024));
        return host;
    }

    private static FlyoutViewModel Vm(FakeEngineHost host, Func<bool>? isDryRun = null)
    {
        var state = new AppState(host);
        return new FlyoutViewModel(state,
            new CleanService(host, new Settings()), new FixAllService(host),
            EnglishLoc(), isDryRun ?? (() => false));
    }

    [Fact]
    public async Task Scan_PopulatesSummaryLines()
    {
        var host = HostWithSnapshot();
        var vm = Vm(host);
        await vm.ScanNowAsync();

        Assert.True(vm.HasSnapshot);
        Assert.Equal("72", vm.HealthText);
        Assert.Equal("SeverityWarning", vm.HealthBrushKey);
        Assert.Equal("2 findings · 1 one-click fixable", vm.FindingsLine);
        Assert.Contains("7 KB", vm.ReclaimLine);   // 2048+4096+1024 = 7168
        Assert.Contains("Last scan:", vm.LastScanLine);
    }

    [Fact]
    public async Task FixAll_FixesFixables_SkipsAdvise_ThenRescans()
    {
        var host = HostWithSnapshot();
        var vm = Vm(host);
        await vm.ScanNowAsync();
        await vm.FixAllAsync();

        Assert.Equal(new[] { "power-plan" }, host.Fixed);
        Assert.Equal(2, host.ScanCalls);
    }

    [Fact]
    public async Task FixAll_IncludesConfirmFixables()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("startup-bloat", cat: RuleCategory.Confirm, canFix: true),
        });
        var vm = Vm(host);
        await vm.ScanNowAsync();
        await vm.FixAllAsync();

        Assert.Equal(new[] { "startup-bloat" }, host.Fixed);
    }

    [Fact]
    public async Task FixAll_DryRun_NeverCallsHostFix()
    {
        var host = HostWithSnapshot();
        var vm = Vm(host, isDryRun: () => true);
        await vm.ScanNowAsync();
        await vm.FixAllAsync();

        Assert.Empty(host.Fixed);
        Assert.Equal(1, host.ScanCalls);   // only the initial scan, no rescan
    }

    [Fact]
    public async Task CleanSafe_CleansOnlyEligibleSafeTargets_ThenRescans()
    {
        var host = HostWithSnapshot();
        var vm = Vm(host);
        await vm.ScanNowAsync();
        await vm.CleanSafeAsync();

        Assert.Equal("user-temp", Assert.Single(host.Cleans).TargetId);
        Assert.Equal(2048, vm.LastCleanOutcome!.RecycledBytes);
        Assert.Equal(2, host.ScanCalls);
    }

    [Fact]
    public async Task ScanState_GuardsReentry()
    {
        var host = HostWithSnapshot();
        var state = new AppState(host);
        await Task.WhenAll(state.ScanAsync(), state.ScanAsync());
        Assert.Equal(1, host.ScanCalls);
    }

    [Fact]
    public async Task CleanSafe_ReentrantCallWhileBusy_IsNoOp()
    {
        var host = HostWithSnapshot();
        var vm = Vm(host);
        await vm.ScanNowAsync();

        // OnClean blocks on the background thread until the test releases it,
        // so CleanSafeAsync's first call is provably still in flight (not a
        // timing assumption) when the second call is made.
        using var gate = new System.Threading.ManualResetEventSlim(false);
        host.OnClean = (scan, dryRun) =>
        {
            gate.Wait();
            return new BriskEngine.Cleaning.CleanReport(scan.Items
                .Select(i => new BriskEngine.Cleaning.CleanEntry(
                    scan.Target.Id, i.Path, i.Bytes, "recycled")).ToList());
        };

        var first = vm.CleanSafeAsync();
        var second = vm.CleanSafeAsync();

        // The busy flag was set synchronously before the first await, so the
        // re-entrant call returns a synchronously-completed task without ever
        // reaching the engine.
        Assert.True(second.IsCompleted);
        Assert.False(first.IsCompleted);

        gate.Set();
        await first;

        Assert.Single(host.Cleans);
    }
}
