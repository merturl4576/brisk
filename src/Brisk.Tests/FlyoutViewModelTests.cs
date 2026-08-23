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
        => VmWithBin(host, isDryRun).Vm;

    /// Round 13: the tray Clean runs the same ONE-STEP recycle→purge flow as
    /// the Depolama page, so its seam needs the bin in view.
    private static (FlyoutViewModel Vm, FakeBin Bin) VmWithBin(FakeEngineHost host,
        Func<bool>? isDryRun = null, Settings? settings = null)
    {
        var state = new AppState(host);
        var bin = new FakeBin();
        var fixAll = new FixAllService(host);
        // Wired exactly as App.xaml.cs wires it: the confirmation is raised
        // as each rule is fixed, not from a loop over the finished batch.
        state.TrackFixes(fixAll);
        var vm = new FlyoutViewModel(state,
            new SafeCleanRunner(new CleanService(host, settings ?? new Settings()), bin),
            fixAll, EnglishLoc(), isDryRun ?? (() => false));
        return (vm, bin);
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
        // ROUND 11 honesty: the flyout's Clean runs the safe defaults, so
        // its line promises exactly that — 2 KB (user-temp), never the
        // deep/dev shelves (old-installers, npm) it would not touch.
        Assert.Contains("2 KB", vm.ReclaimLine);
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

    /// Fix round 1 (Critical): FixAllService is unfiltered, and the tray's
    /// Fix all is one of the four surfaces that can fix display-refresh — a
    /// display mode change that can blank the screen. The confirmation must
    /// reach the shared AppState from here too, not just from the findings
    /// pages, or the tray would be the one place the rescue never runs.
    [Fact]
    public async Task FixAll_RaisesTheDisplayConfirmation_WhenItFixesDisplayRefresh()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("display-refresh", Severity.Critical, RuleCategory.Auto,
                stars: 5, canFix: true),
        });
        var vm = Vm(host);
        await vm.ScanNowAsync();

        await vm.FixAllAsync();

        Assert.NotNull(vm.State.PendingConfirmation);
        // Resolve rather than leave the real 15-second window's background
        // timer running past this test's return.
        vm.State.KeepDisplayCommand.Execute(null);
        await vm.State.PendingConfirmTask!;
    }

    /// WAVE C, C1. The flyout is the app's DEFAULT surface — App.xaml.cs shows
    /// it, not the main window, unless launched with "--tray" — and it carries
    /// its own Clean and Fix all. So a standard-account user who lives in the
    /// tray could recycle another account's browser caches and temp files
    /// without ever meeting the main window's disclosure bar. The strip binds
    /// through State, which is what this pins; the markup itself is XAML and
    /// unreachable from here.
    [Fact]
    public void IdentityWarning_IsReadableFromTheFlyoutsOwnState()
    {
        var host = new FakeEngineHost
        {
            SessionIdentity = new SessionIdentity(@"PC\Admin", @"PC\alice", true),
        };
        var vm = Vm(host);

        Assert.True(vm.State.HasIdentityWarning);
        Assert.Contains(@"PC\Admin", vm.State.IdentityWarningShort);
        Assert.Contains(@"PC\alice", vm.State.IdentityWarningShort);
    }

    /// FIX WAVE, Finding 6. The flyout is the one fix surface the main
    /// window's overlay does not cover, so it is exactly the button a user can
    /// press while brisk is still asking whether the picture came back. A
    /// second batch there would re-fix display-refresh, find every display
    /// already raised, and journal an empty prior state over the real one —
    /// leaving the rollback with nothing to restore.
    [Fact]
    public async Task FixAll_IsRefused_WhileADisplayChangeIsStillUnconfirmed()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("display-refresh", Severity.Critical, RuleCategory.Auto,
                stars: 5, canFix: true),
        });
        var vm = Vm(host);
        await vm.ScanNowAsync();
        vm.State.ConfirmDisplayFix("display-refresh");   // another surface got there first
        host.Fixed.Clear();

        await vm.FixAllAsync();

        Assert.Empty(host.Fixed);
        vm.State.KeepDisplayCommand.Execute(null);
        await vm.State.PendingConfirmTask!;
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

    /// ROUND 13: the tray Clean frees the space for real — recycle, then
    /// purge exactly this run's own items — and its brief line quotes the
    /// POST-purge figure, not the bytes it moved to the bin.
    [Fact]
    public async Task CleanSafe_AutoPurgesItsOwnItems_ReportsFreedTruth_ThenRescans()
    {
        var loc = EnglishLoc();
        var host = HostWithSnapshot();
        var (vm, bin) = VmWithBin(host);
        await vm.ScanNowAsync();
        // Pins the ORDER structurally: the pre-clean snapshot must already
        // have happened by the time the engine is asked to recycle.
        var queriesWhenCleanRan = -1;
        host.OnClean = (scan, _) =>
        {
            queriesWhenCleanRan = bin.IdQueries.Count;
            return new BriskEngine.Cleaning.CleanReport(scan.Items
                .Select(i => new BriskEngine.Cleaning.CleanEntry(
                    scan.Target.Id, i.Path, i.Bytes, "recycled")).ToList());
        };

        await vm.CleanSafeAsync();

        Assert.Equal("user-temp", Assert.Single(host.Cleans).TargetId);
        Assert.Equal(1, queriesWhenCleanRan);
        // the purge touched EXACTLY this run's own recycled items
        Assert.Equal(new[] { @"C:\x\user-temp\item" }, Assert.Single(bin.Purged));
        Assert.Equal(2048, vm.LastCleanResult!.Outcome.RecycledBytes);
        Assert.Equal(2048, vm.LastCleanResult!.FreedBytes);
        Assert.True(vm.HasLastClean);
        Assert.Equal(loc.F("clean.report.summary.freed", 1, "2 KB"), vm.LastCleanLine);
        Assert.Equal(2, host.ScanCalls);
    }

    /// ROUND 13 safety, carried over from round 12: a file the USER deleted
    /// earlier at the same original path is snapshotted before the clean and
    /// excluded from the purge — the tray flyout can no more destroy it than
    /// the Depolama page can.
    [Fact]
    public async Task CleanSafe_ExcludesBinItemsThatPredateTheClean()
    {
        var host = HostWithSnapshot();
        var (vm, bin) = VmWithBin(host);
        bin.PreExistingIds.Add(@"C:\$Recycle.Bin\S-1-5-21\$RUSER01.tmp");
        await vm.ScanNowAsync();

        await vm.CleanSafeAsync();

        // the snapshot asked about exactly the planned safe-default items…
        Assert.Equal(new[] { @"C:\x\user-temp\item" }, Assert.Single(bin.IdQueries));
        // …and the purge was handed those identities to skip
        var purge = Assert.Single(bin.PurgeCalls);
        Assert.Equal(new[] { @"C:\$Recycle.Bin\S-1-5-21\$RUSER01.tmp" }, purge.Exclude);
    }

    /// ROUND 13: a purge that falls short never inflates the brief line —
    /// freed is what actually left the disk (0 B here), not the 2 KB moved.
    /// Round-13 review (I2): and the tray NAMES what stayed behind, like the
    /// other two surfaces do — "0 B freed" with no reason is a dead end.
    [Fact]
    public async Task CleanSafe_PartialPurge_ReportsFreedNotRecycled()
    {
        var loc = EnglishLoc();
        var host = HostWithSnapshot();
        var (vm, bin) = VmWithBin(host);
        bin.PurgeFails.Add(@"C:\x\user-temp\item");
        await vm.ScanNowAsync();

        await vm.CleanSafeAsync();

        Assert.Equal(0, vm.LastCleanResult!.FreedBytes);
        Assert.Equal(2048, vm.LastCleanResult!.LeftInBinBytes);
        Assert.Equal(
            loc.F("clean.report.summary.freed", 1, "0 B") + "\n"
                + loc.F("clean.report.binleft", "2 KB"),
            vm.LastCleanLine);
    }

    [Fact]
    public async Task CleanSafe_DryRun_NeverTouchesTheBin()
    {
        var host = HostWithSnapshot();
        var settings = new Settings { DryRun = true };
        var (vm, bin) = VmWithBin(host, () => settings.DryRun, settings);
        await vm.ScanNowAsync();

        await vm.CleanSafeAsync();

        Assert.All(host.Cleans, c => Assert.True(c.DryRun));
        Assert.Empty(bin.Purged);
        Assert.Equal(EnglishLoc()["dryrun.blocked"], vm.LastCleanLine);
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
