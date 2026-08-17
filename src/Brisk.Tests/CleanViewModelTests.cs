using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

sealed class FakeBin : IRecycleBinSession
{
    public List<IReadOnlyList<string>> Restored { get; } = new();
    public List<IReadOnlyList<string>> Purged { get; } = new();
    public bool RestoreResult { get; set; } = true;
    public bool Restore(IReadOnlyList<string> originalPaths)
    { Restored.Add(originalPaths); return RestoreResult; }
    public bool Purge(IReadOnlyList<string> originalPaths)
    { Purged.Add(originalPaths); return true; }
    public void OpenRecycleBinUi() { }
}

public class CleanViewModelTests
{
    private static Loc EnglishLoc()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        return loc;
    }

    private static FakeEngineHost Host()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(null,
            TestData.Target("user-temp", CleanupLevel.Safe, 2048),
            TestData.Target("chrome-cache", CleanupLevel.Safe, 0, skipped: "chrome is running"),
            TestData.Target("docker-prune", CleanupLevel.Developer, 0, optIn: true),
            TestData.Target("old-installers", CleanupLevel.Deep, 4096, pick: true),
            TestData.Target("windows-temp", CleanupLevel.Deep, 1024, admin: true));
        return host;
    }

    private static (CleanViewModel, FakeEngineHost, FakeBin, AppState)
        Build(FakeEngineHost host, Func<bool>? isDryRun = null)
    {
        var state = new AppState(host);
        var bin = new FakeBin();
        var vm = new CleanViewModel(state, host,
            new CleanService(host, new Settings()), bin, EnglishLoc(),
            isDryRun ?? (() => false));
        return (vm, host, bin, state);
    }

    [Fact]
    public async Task Levels_BuildWithDefaultSelection()
    {
        var host0 = Host();
        host0.Lifetime = 5L << 30;
        var (vm, _, _, state) = Build(host0);
        await state.ScanAsync();

        Assert.Equal(3, vm.Levels.Count);
        Assert.Contains("5.0 GB", vm.LifetimeText);
        var safe = vm.Levels.Single(l => l.Level == CleanupLevel.Safe);
        Assert.True(safe.Targets.Single(t => t.Id == "user-temp").IsSelected);
        var skippedRow = safe.Targets.Single(t => t.Id == "chrome-cache");
        Assert.False(skippedRow.IsSelectable);
        Assert.False(skippedRow.IsSelected);

        var dev = vm.Levels.Single(l => l.Level == CleanupLevel.Developer);
        Assert.False(dev.Targets.Single(t => t.Id == "docker-prune").IsSelected);

        var deep = vm.Levels.Single(l => l.Level == CleanupLevel.Deep);
        var pick = deep.Targets.Single(t => t.Id == "old-installers");
        Assert.True(pick.IsPerItem);
        Assert.False(pick.IsSelected);
        Assert.Single(pick.Items);
        Assert.False(pick.Items[0].IsSelected);
        Assert.True(deep.Targets.Single(t => t.Id == "windows-temp").NeedsElevation);
    }

    /// The simple face: human-language groups over EXACTLY the safe-default
    /// predicate — skipped, opt-in, per-item and non-safe targets never
    /// count toward the number the one-button Temizle promises.
    private static FakeEngineHost SimpleHost()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(null,
            TestData.Target("user-temp", CleanupLevel.Safe, 2048, category: "System"),
            TestData.Target("discord-cache", CleanupLevel.Safe, 1024, category: "App"),
            TestData.Target("chrome-cache", CleanupLevel.Safe, 0,
                skipped: "chrome is running", app: "chrome", category: "Browser"),
            TestData.Target("npm-cache", CleanupLevel.Developer, 4096,
                category: "Package Manager"),
            TestData.Target("old-installers", CleanupLevel.Deep, 4096, pick: true),
            TestData.Target("windows-temp", CleanupLevel.Deep, 1024, admin: true));
        return host;
    }

    [Fact]
    public async Task SimpleView_AggregatesSafeDefaults_IntoHumanGroups()
    {
        var loc = EnglishLoc();
        var (vm, _, _, state) = Build(SimpleHost());
        await state.ScanAsync();

        // total = safe defaults only (2 KB + 1 KB); dev/deep/skipped excluded
        Assert.Equal("3 KB", vm.SimpleTotalText);
        // groups in size order, in human words; the empty Browser group
        // (chrome skipped) never renders
        Assert.Equal(new[]
        {
            (loc["clean.group.system"], "2 KB"),
            (loc["clean.group.app"], "1 KB"),
        }, vm.SimpleGroups.Select(g => (g.Name, g.SizeText)));
        Assert.True(vm.SimpleCleanCommand.CanExecute(null));
        // the technical list is folded away by default
        Assert.False(vm.IsAdvancedShown);
    }

    [Fact]
    public async Task SimpleClean_RunsExactlyTodaysSafeDefaults_ShowsBanner_Rescans()
    {
        var (vm, host, _, state) = Build(SimpleHost());
        await state.ScanAsync();

        await vm.CleanSimpleAsync();

        // exactly the safe-default set — never the admin/deep/opt-in targets
        Assert.Equal(new[] { "user-temp", "discord-cache" },
            host.Cleans.Select(c => c.TargetId));
        Assert.Empty(host.ElevatedRuns);
        Assert.True(vm.HasBanner);
        Assert.Contains("3 KB", vm.BannerText);
        Assert.Equal(2, host.ScanCalls);
    }

    [Fact]
    public async Task SimpleClean_DryRun_BlocksWithFeedback()
    {
        var host = SimpleHost();
        var state = new AppState(host);
        var settings = new Settings { DryRun = true };
        var vm = new CleanViewModel(state, host,
            new CleanService(host, settings), new FakeBin(), EnglishLoc(),
            () => settings.DryRun);
        await state.ScanAsync();

        await vm.CleanSimpleAsync();

        Assert.Equal(EnglishLoc()["dryrun.blocked"], vm.ProblemsText);
        Assert.False(vm.HasBanner);
        Assert.All(host.Cleans, c => Assert.True(c.DryRun));
    }

    [Fact]
    public async Task SimpleView_NothingToClean_DisablesTheButton()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(null,
            TestData.Target("chrome-cache", CleanupLevel.Safe, 0,
                skipped: "chrome is running", app: "chrome", category: "Browser"));
        var (vm, _, _, state) = Build(host);
        await state.ScanAsync();

        Assert.Empty(vm.SimpleGroups);
        Assert.False(vm.SimpleCleanCommand.CanExecute(null));
    }

    [Fact]
    public async Task CleanLevel_CleansSelected_ShowsBanner_Rescans()
    {
        var (vm, host, _, state) = Build(Host());
        await state.ScanAsync();
        var safe = vm.Levels.Single(l => l.Level == CleanupLevel.Safe);

        await vm.CleanLevelAsync(safe);

        Assert.Equal("user-temp", Assert.Single(host.Cleans).TargetId);
        Assert.True(vm.HasBanner);
        Assert.Contains("2 KB", vm.BannerText);
        Assert.Equal(2, host.ScanCalls);
    }

    [Fact]
    public async Task CleanLevel_PerItemTarget_CleansOnlyCheckedItems()
    {
        var (vm, host, _, state) = Build(Host());
        await state.ScanAsync();
        var deep = vm.Levels.Single(l => l.Level == CleanupLevel.Deep);
        var pick = deep.Targets.Single(t => t.Id == "old-installers");
        pick.IsSelected = true;
        pick.Items[0].IsSelected = true;
        deep.Targets.Single(t => t.Id == "windows-temp").IsSelected = false;

        TargetScanResult? seen = null;
        host.OnClean = (scan, _) =>
        {
            if (scan.Target.Id == "old-installers") seen = scan;
            return new BriskEngine.Cleaning.CleanReport(scan.Items
                .Select(i => new BriskEngine.Cleaning.CleanEntry(
                    scan.Target.Id, i.Path, i.Bytes, "recycled")).ToList());
        };
        await vm.CleanLevelAsync(deep);

        Assert.NotNull(seen);
        Assert.Single(seen!.Items);
    }

    [Fact]
    public async Task CleanLevel_ElevationTarget_GoesThroughRunElevated()
    {
        var (vm, host, _, state) = Build(Host());
        await state.ScanAsync();
        var deep = vm.Levels.Single(l => l.Level == CleanupLevel.Deep);
        deep.Targets.Single(t => t.Id == "windows-temp").IsSelected = true;

        await vm.CleanLevelAsync(deep);

        Assert.Equal("clean --target windows-temp --yes", Assert.Single(host.ElevatedRuns));
        Assert.DoesNotContain(host.Cleans, c => c.TargetId == "windows-temp");
    }

    [Fact]
    public async Task CleanLevel_ElevationTarget_DryRun_NeverElevates()
    {
        var (vm, host, _, state) = Build(Host(), isDryRun: () => true);
        await state.ScanAsync();
        var deep = vm.Levels.Single(l => l.Level == CleanupLevel.Deep);
        deep.Targets.Single(t => t.Id == "windows-temp").IsSelected = true;

        await vm.CleanLevelAsync(deep);

        Assert.Empty(host.ElevatedRuns);
        Assert.DoesNotContain(host.Cleans, c => c.TargetId == "windows-temp");
        Assert.Contains(EnglishLoc()["dryrun.blocked"], vm.ProblemsText);
    }

    [Fact]
    public async Task Undo_RestoresRecycledPaths_FailureFlagged()
    {
        var (vm, host, bin, state) = Build(Host());
        await state.ScanAsync();
        await vm.CleanLevelAsync(vm.Levels.Single(l => l.Level == CleanupLevel.Safe));

        bin.RestoreResult = false;
        vm.UndoCommand.Execute(null);
        Assert.Single(bin.Restored);
        Assert.True(vm.RestoreFailed);

        bin.RestoreResult = true;
        vm.UndoCommand.Execute(null);
        Assert.False(vm.RestoreFailed);
        Assert.False(vm.HasBanner);
    }

    [Fact]
    public async Task Reclaim_PurgesAndDismisses()
    {
        var (vm, host, bin, state) = Build(Host());
        await state.ScanAsync();
        await vm.CleanLevelAsync(vm.Levels.Single(l => l.Level == CleanupLevel.Safe));

        vm.ReclaimCommand.Execute(null);
        Assert.Single(bin.Purged);
        Assert.False(vm.HasBanner);
    }

    [Fact]
    public async Task CleanLevel_ReentrantCallWhileBusy_IsNoOp()
    {
        var (vm, host, _, state) = Build(Host());
        await state.ScanAsync();
        var safe = vm.Levels.Single(l => l.Level == CleanupLevel.Safe);

        // OnClean blocks on the background thread until the test releases it,
        // so CleanLevelAsync's first call is provably still in flight (not a
        // timing assumption) when the second call is made.
        using var gate = new System.Threading.ManualResetEventSlim(false);
        host.OnClean = (scan, dryRun) =>
        {
            gate.Wait();
            return new BriskEngine.Cleaning.CleanReport(scan.Items
                .Select(i => new BriskEngine.Cleaning.CleanEntry(
                    scan.Target.Id, i.Path, i.Bytes, "recycled")).ToList());
        };

        var first = vm.CleanLevelAsync(safe);
        var second = vm.CleanLevelAsync(safe);

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
