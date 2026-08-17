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

    /// ROUND 11: the owner saw the top banner AND the in-card report repeat
    /// each other after a simple clean. One result surface now — the report
    /// carries the actions; the banner belongs to advanced level cleans.
    [Fact]
    public async Task SimpleClean_RunsExactlyTodaysSafeDefaults_OneResultSurface_Rescans()
    {
        var (vm, host, _, state) = Build(SimpleHost());
        await state.ScanAsync();

        await vm.CleanSimpleAsync();

        // exactly the safe-default set — never the admin/deep/opt-in targets
        Assert.Equal(new[] { "user-temp", "discord-cache" },
            host.Cleans.Select(c => c.TargetId));
        Assert.Empty(host.ElevatedRuns);
        Assert.False(vm.HasBanner);           // no duplicated banner
        Assert.True(vm.HasReport);
        Assert.True(vm.HasReportActions);     // Undo + Reclaim live in the report
        Assert.True(vm.UndoCommand.CanExecute(null));
        Assert.True(vm.ReclaimCommand.CanExecute(null));
        Assert.Equal(2, host.ScanCalls);
    }

    /// REGRESSION PIN — the 2026-08-17 trust bug in GUI terms: the card
    /// promised ~450 MB and delivered 22 MB because a running app's cache
    /// (WhatsApp, 310 MB) and delete-locked temp files sat in the headline.
    /// The headline now counts ONLY what Temizle can take right now; the
    /// held bytes get their own actionable lines instead.
    [Fact]
    public async Task SimpleView_HonestTotal_ExcludesAppHeldAndLockedBytes()
    {
        var loc = EnglishLoc();
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(null,
            TestData.Target("user-temp", CleanupLevel.Safe, 2048,
                category: "System", lockedBytes: 120L << 20),
            TestData.Target("whatsapp-cache", CleanupLevel.Safe, 310L << 20,
                skipped: "WhatsApp is running — close it to include this target",
                app: "WhatsApp|WhatsApp.Root", category: "App"));
        var (vm, _, _, state) = Build(host);
        await state.ScanAsync();

        // headline + groups: the 2 KB that can actually go — never the
        // 310 MB app-held cache or the 120 MB locked temp content
        Assert.Equal("2 KB", vm.SimpleTotalText);
        var group = Assert.Single(vm.SimpleGroups);
        Assert.Equal((loc["clean.group.system"], "2 KB"), (group.Name, group.SizeText));
        // the held bytes are surfaced, actionably, in the user's words
        Assert.True(vm.HasLockedNotes);
        Assert.Equal(new[]
        {
            loc.F("clean.simple.locked.app", "WhatsApp", "310 MB"),
            loc.F("clean.simple.locked.inuse", "120 MB"),
        }, vm.LockedNotes);
    }

    /// The post-clean report explains the app-held survivor with the action
    /// attached ("close WhatsApp and clean again") — no more silently
    /// untouched 310 MB groups.
    [Fact]
    public async Task SimpleClean_Report_ExplainsAppHeldSurvivors()
    {
        var loc = EnglishLoc();
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(null,
            TestData.Target("user-temp", CleanupLevel.Safe, 2048, category: "System"),
            TestData.Target("whatsapp-cache", CleanupLevel.Safe, 310L << 20,
                skipped: "WhatsApp is running — close it to include this target",
                app: "WhatsApp|WhatsApp.Root", category: "App"));
        var (vm, _, _, state) = Build(host);
        await state.ScanAsync();

        await vm.CleanSimpleAsync();

        // the skipped target never reached the engine…
        Assert.Equal(new[] { "user-temp" }, host.Cleans.Select(c => c.TargetId));
        // …and the report says why its bytes are still there, actionably
        Assert.True(vm.HasReportReasons);
        Assert.Equal(
            loc.F("clean.report.skipped.appheld", "WhatsApp", "310 MB"),
            vm.ReportReasonsText);
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

    /// UX ROUND 10: the owner's live press cleaned 1.39 GB over six minutes
    /// with zero feedback and read as "does nothing". The simple card must
    /// now visibly work: busy state, real progress, and a re-press no-op.
    [Fact]
    public async Task SimpleClean_ShowsBusyState_AndBlocksThePress_WhileRunning()
    {
        var (vm, host, _, state) = Build(SimpleHost());
        await state.ScanAsync();
        using var gate = new System.Threading.ManualResetEventSlim(false);
        host.OnClean = (scan, _) =>
        {
            gate.Wait();
            return new BriskEngine.Cleaning.CleanReport(scan.Items
                .Select(i => new BriskEngine.Cleaning.CleanEntry(
                    scan.Target.Id, i.Path, i.Bytes, "recycled")).ToList());
        };

        var run = vm.CleanSimpleAsync();

        Assert.True(vm.IsBusy);
        Assert.False(vm.SimpleCleanCommand.CanExecute(null));
        var second = vm.CleanSimpleAsync();
        Assert.True(second.IsCompleted);      // re-press is a no-op

        gate.Set();
        await run;

        Assert.False(vm.IsBusy);
        Assert.True(vm.SimpleCleanCommand.CanExecute(null));
        Assert.Equal(2, host.Cleans.Count);   // both safe targets, once each
    }

    /// The big number ticks DOWN through the engine's real per-entry stream
    /// (2 KB gone → 1 KB left → 0 B), then the closing rescan restores the
    /// measured truth — never fake progress theater.
    [Fact]
    public async Task SimpleClean_CountsTheTotalDown_OnRealEntries()
    {
        var (vm, _, _, state) = Build(SimpleHost());
        await state.ScanAsync();
        var totals = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.SimpleTotalText))
                totals.Add(vm.SimpleTotalText);
        };

        await vm.CleanSimpleAsync();

        Assert.Equal(new[] { "1 KB", "0 B", "3 KB" }, totals);
        Assert.Equal(1.0, vm.ProgressFraction);
        Assert.Equal(EnglishLoc().F("clean.progress", 2, 2), vm.ProgressText);
    }

    /// UX ROUND 10 completion report: junk count, bytes moved to the bin,
    /// and the measured free-disk story — before → after.
    [Fact]
    public async Task SimpleClean_Report_TellsCountBytes_AndHonestDiskLine()
    {
        var loc = EnglishLoc();
        var (vm, host, _, state) = Build(SimpleHost());
        host.FreeDisk = 100L << 30;
        await state.ScanAsync();

        await vm.CleanSimpleAsync();

        Assert.True(vm.HasReport);
        Assert.Equal(loc.F("clean.report.summary", 2, "3 KB"), vm.ReportSummary);
        // ROUND 11: recycling moved bytes to the bin on the same volume, so
        // free space did not move — the line says where the bytes ARE
        // instead of the meaningless "100.0 GB → 100.0 GB"
        Assert.Equal(loc.F("clean.report.disk.binned", "3 KB"), vm.ReportDiskText);
        Assert.False(vm.HasReportReasons);
        Assert.Equal("", vm.ProblemsText);
    }

    [Fact]
    public async Task SimpleClean_MeasuredDiskGain_ShowsTheRegainedPhrase()
    {
        var loc = EnglishLoc();
        var (vm, host, _, state) = Build(SimpleHost());
        host.FreeDisk = 100L << 30;
        await state.ScanAsync();
        host.OnClean = (scan, _) =>
        {
            host.FreeDisk = 102L << 30;   // the clean visibly freed space
            return new BriskEngine.Cleaning.CleanReport(scan.Items
                .Select(i => new BriskEngine.Cleaning.CleanEntry(
                    scan.Target.Id, i.Path, i.Bytes, "recycled")).ToList());
        };

        await vm.CleanSimpleAsync();

        Assert.Equal(
            loc.F("clean.report.disk.gained", "100.0 GB", "102.0 GB", "2.0 GB"),
            vm.ReportDiskText);
    }

    /// When nothing could go, the report stays calm and says WHY in human
    /// words (round-9 GUI-edge localization) — never silence, never alarm,
    /// never a raw English path dump.
    [Fact]
    public async Task SimpleClean_NothingRecycled_ReportsCalmly_WithHumanReasons()
    {
        var loc = EnglishLoc();
        var (vm, host, _, state) = Build(SimpleHost());
        await state.ScanAsync();
        host.OnClean = (scan, _) => new BriskEngine.Cleaning.CleanReport(
            scan.Items.Select(i => new BriskEngine.Cleaning.CleanEntry(
                scan.Target.Id, i.Path, 0, "error",
                scan.Target.Id == "user-temp"
                    ? $"SHFileOperation failed (32) for '{i.Path}'"
                    : $"SHFileOperation failed (5) for '{i.Path}'")).ToList());

        await vm.CleanSimpleAsync();

        Assert.False(vm.HasBanner);
        Assert.True(vm.HasReport);
        Assert.False(vm.HasReportActions);    // nothing to purge or put back
        Assert.Equal(loc["clean.report.none"], vm.ReportSummary);
        // nothing recycled, nothing measured — no disk line at all (round 11)
        Assert.Equal("", vm.ReportDiskText);
        Assert.True(vm.HasReportReasons);
        Assert.Equal(
            loc.F("clean.report.skipped.inuse", 1) + "\n"
            + loc.F("clean.report.skipped.other", 1),
            vm.ReportReasonsText);
        Assert.Equal("", vm.ProblemsText);    // the report replaced the dump
    }

    /// "Reclaim space now" purges the bin — the report's disk line
    /// re-measures at that moment, when free space actually rises, and the
    /// actions retire (nothing left to purge or put back).
    [Fact]
    public async Task Reclaim_RemeasuresTheReportDiskLine()
    {
        var loc = EnglishLoc();
        var (vm, host, bin, state) = Build(SimpleHost());
        host.FreeDisk = 100L << 30;
        await state.ScanAsync();
        await vm.CleanSimpleAsync();
        Assert.True(vm.HasReportActions);

        host.FreeDisk = 103L << 30;           // the purge freed real bytes
        vm.ReclaimCommand.Execute(null);

        Assert.Single(bin.Purged);
        Assert.True(vm.HasReport);            // the story stays up
        Assert.False(vm.HasReportActions);
        Assert.Equal(
            loc.F("clean.report.disk.gained", "100.0 GB", "103.0 GB", "3.0 GB"),
            vm.ReportDiskText);
    }

    /// A purge whose measured delta is below the noise floor still gets a
    /// truthful line: the bin was emptied, the recycled bytes are gone for
    /// good — never a "100.0 GB → 100.0 GB".
    [Fact]
    public async Task Reclaim_SmallDelta_SaysTheBinWasEmptied()
    {
        var loc = EnglishLoc();
        var (vm, host, _, state) = Build(SimpleHost());
        host.FreeDisk = 100L << 30;
        await state.ScanAsync();
        await vm.CleanSimpleAsync();

        vm.ReclaimCommand.Execute(null);      // free space barely moved

        Assert.Equal(loc.F("clean.report.disk.purged", "3 KB"), vm.ReportDiskText);
        Assert.False(vm.HasReportActions);
    }

    /// Undo from the report puts everything back: the story flips to the
    /// undone line, the actions retire, and a rescan refills the promise.
    [Fact]
    public async Task Undo_AfterSimpleClean_TellsTheUndoneStory_AndRescans()
    {
        var loc = EnglishLoc();
        var (vm, host, bin, state) = Build(SimpleHost());
        await state.ScanAsync();
        await vm.CleanSimpleAsync();
        var scansBefore = host.ScanCalls;

        vm.UndoCommand.Execute(null);

        Assert.Single(bin.Restored);
        Assert.True(vm.HasReport);
        Assert.False(vm.HasReportActions);
        Assert.Equal(loc["clean.report.undone"], vm.ReportSummary);
        Assert.Equal("", vm.ReportDiskText);
        Assert.Equal(scansBefore + 1, host.ScanCalls);   // the shelf refills
    }

    [Fact]
    public async Task SimpleClean_DryRun_ShowsNoReport_AndNeverMovesTheTotal()
    {
        var host = SimpleHost();
        var state = new AppState(host);
        var settings = new Settings { DryRun = true };
        var vm = new CleanViewModel(state, host,
            new CleanService(host, settings), new FakeBin(), EnglishLoc(),
            () => settings.DryRun);
        await state.ScanAsync();

        await vm.CleanSimpleAsync();

        Assert.False(vm.HasReport);
        Assert.False(vm.IsBusy);
        Assert.Equal(EnglishLoc()["dryrun.blocked"], vm.ProblemsText);
        // round-10 review: a dry run reclaims NOTHING — the big number must
        // still promise the full amount, not a counted-down "0 B"
        Assert.Equal("3 KB", vm.SimpleTotalText);
    }

    /// Round-10 review: the simple card's hairline and "Temizleniyor…"
    /// gate on IsSimpleCleanBusy — a Gelişmiş level clean raises IsBusy
    /// but must never light the always-visible card with the previous
    /// simple clean's stale 100% progress.
    [Fact]
    public async Task CleanLevel_NeverLightsTheSimpleCardsProgress()
    {
        var (vm, host, _, state) = Build(SimpleHost());
        await state.ScanAsync();
        await vm.CleanSimpleAsync();          // leaves stale progress behind
        Assert.Equal(1.0, vm.ProgressFraction);

        using var gate = new System.Threading.ManualResetEventSlim(false);
        host.OnClean = (scan, _) =>
        {
            gate.Wait();
            return new BriskEngine.Cleaning.CleanReport(scan.Items
                .Select(i => new BriskEngine.Cleaning.CleanEntry(
                    scan.Target.Id, i.Path, i.Bytes, "recycled")).ToList());
        };
        var safe = vm.Levels.Single(l => l.Level == CleanupLevel.Safe);
        var run = vm.CleanLevelAsync(safe);

        Assert.True(vm.IsBusy);               // the app IS busy…
        Assert.False(vm.IsSimpleCleanBusy);   // …but the simple card stays dark

        gate.Set();
        await run;
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsSimpleCleanBusy);
    }

    /// Round-10 review: the big total's push cadence must outlast
    /// NumeralTick's slide, or every push restarts the animation mid-flight
    /// and the numeral strobes instead of ticking.
    [Fact]
    public void TotalPushCadence_OutlastsTheNumeralTickAnimation()
    {
        Assert.True(CleanViewModel.TotalPushMs > Brisk.Views.NumeralTick.DurationMs);
        Assert.True(CleanViewModel.TotalPushMs >= CleanViewModel.ProgressPushMs);
    }
}
