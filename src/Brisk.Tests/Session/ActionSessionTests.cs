using System.IO;
using BriskEngine.Actions;
using BriskEngine.Cleaning;
using BriskEngine.Models;
using Brisk.Session;

namespace Brisk.Tests.Session;

public sealed class ActionSessionTests
{
    private static readonly object Gate = new();
    private static ActionSession NewSession(FakeBackend b) => new(b, a => { lock (Gate) a(); });

    [Fact]
    public async Task LookAsync_EndsReady_WithBothHalvesAndDefaultTicks()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        Assert.Equal(SessionState.Ready, s.State);
        Assert.NotNull(s.Plan);
        Assert.True(s.IsTicked("space.caches"));
        Assert.False(s.IsTicked("space.shaders"));
        Assert.True(s.IsFileTicked(@"C:\D\a.exe"));
        Assert.Equal(new[] { ActionGroupKind.Space, ActionGroupKind.Speed, ActionGroupKind.Privacy },
            s.Plan!.Groups.Select(g => g.Kind));
    }

    [Fact]
    public async Task LookAsync_ReportsSizedTargetsAsTheyArrive()
    {
        var b = FakeBackend.Typical();
        b.Sized.Add(new TargetScanResult(CleanupTargetRegistry.All.First(t => t.Id == "user-temp"),
            new[] { new ResolvedItem("user-temp", @"C:\t\x", 10, null) }, null));
        var s = NewSession(b);
        var sizedCounts = new List<int>();
        s.Changed += () => sizedCounts.Add(s.SizedSoFar.Count);
        await s.LookAsync();
        Assert.Contains(1, sizedCounts);
    }

    [Fact]
    public async Task LookAsync_FastHalfFails_EndsFailedWithTheMessage()
    {
        var b = FakeBackend.Typical(); b.FailFast = new InvalidOperationException("wmi down");
        var s = NewSession(b);
        await s.LookAsync();
        Assert.Equal(SessionState.Failed, s.State);
        Assert.Equal("wmi down", s.Error);
    }

    [Fact]
    public async Task Skips_AndTheLevel_SurviveARescan_InTheSameSession()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        s.ToggleSkip("space.caches");
        s.ToggleSkip("startup:HKCU:OneDrive");      // off by default: a tap adds it
        s.SetLevel(SpaceLevel.Mid);
        await s.LookAsync();
        Assert.True(s.IsSkipped("space.caches"));
        Assert.False(s.IsSkipped("startup:HKCU:OneDrive"));
        Assert.Equal(SpaceLevel.Mid, s.Level);
    }

    [Fact]
    public async Task DownloadsRowWithNoFilesTicked_DoesNotRun()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        s.SetLevel(SpaceLevel.Mid);                     // the Downloads row is MID
        s.SetFileTicked(@"C:\D\a.exe", false);
        s.SetFileTicked(@"C:\D\b.msi", false);
        Assert.Equal(16_000_000_000, s.LevelBytes(SpaceLevel.Mid));
        await s.RunAsync(ActionGroupKind.Space);
        var ran = b.Runs.Single().RowIds;
        Assert.Contains("space.recycle-bin", ran);
        Assert.DoesNotContain("space.downloads", ran);
    }

    /// No confirmation before deleting: a row that cannot come back runs as
    /// soon as its level is chosen (MAX warns on the page, not in a dialog).
    [Fact]
    public async Task RowsThatCannotComeBack_RunWithoutAConfirmation()
    {
        var b = Levels(); var s = NewSession(b);
        await s.LookAsync();
        s.SetLevel(SpaceLevel.Max);
        await s.RunAsync(ActionGroupKind.Space);
        Assert.Contains("space.windows-old", b.Runs.Single().RowIds);
        Assert.Equal(SessionState.Done, s.State);
    }

    [Fact]
    public async Task RunAsync_Group_RunsOnlyThatGroup_AndTheDeferredQueueOnlyWithSpace()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        await s.RunAsync(ActionGroupKind.Privacy);
        Assert.Equal(new[] { "privacy.ads" }, b.Runs.Single().RowIds.ToArray());
        Assert.Equal(0, b.DeferredProcessed);
        await s.RescanAsync();
        await s.RunAllAsync();
        Assert.Equal(1, b.DeferredProcessed);
    }

    [Fact]
    public async Task RunAsync_EndsDone_WithResultsLastRunAndReceipt()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        b.ReceiptToReturn = TestReceipts.Freed("run-1", 14_000_000_000);
        await s.LookAsync();
        var started = new List<string?>();
        s.Changed += () => started.Add(s.CurrentRowId);
        await s.RunAllAsync();
        Assert.Equal(SessionState.Done, s.State);
        Assert.Contains("space.caches", started);
        Assert.Equal(RowOutcome.Done, s.Results["space.caches"].Outcome);
        Assert.Equal("run-1", s.LastRun!.RunId);
        Assert.True(s.RanThisSession);
        Assert.Equal(14_000_000_000, s.Receipt!.FreedBytes);
        Assert.Null(s.CurrentRowId);
    }

    [Fact]
    public async Task TheChangeThatSaysDone_AlreadyAllowsUndo()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        var seen = new List<(SessionState State, bool CanUndo, bool Busy)>();
        s.Changed += () => seen.Add((s.State, s.CanUndo, s.Busy));
        await s.RunAllAsync();
        var done = seen.First(x => x.State == SessionState.Done);
        Assert.False(done.Busy);
        Assert.True(done.CanUndo);
    }

    [Fact]
    public async Task TheChangeThatSaysReady_IsNotBusy()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        var seen = new List<(SessionState State, bool Busy)>();
        s.Changed += () => seen.Add((s.State, s.Busy));
        await s.LookAsync();
        Assert.False(seen.First(x => x.State == SessionState.Ready).Busy);
    }

    [Fact]
    public async Task RefreshReceipt_AnOlderRefreshFinishingLate_DoesNotWin()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        await s.RunAllAsync();
        var release = new ManualResetEventSlim(false);
        var firstCall = b.ReceiptRefreshes + 1;
        b.ReceiptByCall = n =>
        {
            if (n != firstCall) return TestReceipts.Freed("run-1", 2);
            release.Wait(TimeSpan.FromSeconds(5));
            return TestReceipts.Freed("run-1", 1);
        };
        var older = s.RefreshReceiptAsync();
        Assert.True(SpinWait.SpinUntil(() => b.ReceiptRefreshes >= firstCall, 2000));
        await s.RefreshReceiptAsync();
        release.Set();
        await older;
        Assert.Equal(2, s.Receipt!.FreedBytes);
    }

    private static RunRecord OldRun() => new("old", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1),
        Array.Empty<RowResult>(), new[] { "privacy.ads" });

    [Fact]
    public async Task RunAsync_ClearsThePreviousReceipt_WhileRunning()
    {
        var b = FakeBackend.Typical();
        b.Latest = OldRun();
        b.ReceiptToReturn = TestReceipts.Freed("old", 5, startupAfter: 2, privacyOff: 9);
        var s = NewSession(b);
        await s.LookAsync();
        Assert.NotNull(s.Receipt);
        b.HoldRun = new ManualResetEventSlim(false);
        var run = s.RunAllAsync();
        Assert.True(SpinWait.SpinUntil(() => s.State == SessionState.Running, 2000));
        Assert.Null(s.Receipt);
        b.HoldRun.Set();
        await run;
    }

    [Fact]
    public async Task RunAll_SkippingLeavesNothing_ReturnsToReadyWithoutARun()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        foreach (var i in s.Items.ToList())
            if (!s.IsSkipped(i.Id)) s.ToggleSkip(i.Id);
        await s.RunAllAsync();
        Assert.Empty(b.Runs);
        Assert.Equal(SessionState.Ready, s.State);
        Assert.False(s.Busy);
    }

    [Fact]
    public async Task UndoAsync_Throws_KeepsTheErrorThroughTheRescan()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        await s.RunAllAsync();
        b.FailUndo = new InvalidOperationException("access denied");
        await s.UndoAsync();
        Assert.Equal(SessionState.Ready, s.State);
        Assert.Equal("access denied", s.UndoError);
        Assert.True(s.CanUndo);
    }

    [Fact]
    public async Task Run_ReceiptRefreshThrows_TheRunStillEndsDone()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        b.ReceiptByCall = _ => throw new IOException("locked");
        await s.RunAllAsync();
        Assert.Equal(SessionState.Done, s.State);
        Assert.Null(s.Receipt);
    }

    [Fact]
    public async Task Look_PreviousReceiptThrows_ThePlanIsStillReady()
    {
        var b = FakeBackend.Typical();
        b.Latest = OldRun();
        b.ReceiptByCall = _ => throw new IOException("locked");
        var s = NewSession(b);
        await s.LookAsync();
        Assert.Equal(SessionState.Ready, s.State);
        Assert.Equal("old", s.LastRun!.RunId);
    }

    [Fact]
    public async Task RefreshReceipt_ForAnOlderRun_DoesNotOverwriteTheNewRunsReceipt()
    {
        var b = FakeBackend.Typical();
        b.Latest = OldRun();
        var release = new ManualResetEventSlim(false);
        b.ReceiptByCall = n => n switch
        {
            1 => TestReceipts.Freed("old", 1),
            2 => Wait(release, TestReceipts.Freed("old", 1)),
            _ => TestReceipts.Freed("run-1", 2),
        };
        var s = NewSession(b);
        await s.LookAsync();
        var older = s.RefreshReceiptAsync();
        Assert.True(SpinWait.SpinUntil(() => b.ReceiptRefreshes >= 2, 2000));
        await s.RunAllAsync();
        release.Set();
        await older;
        Assert.Equal("run-1", s.Receipt!.RunId);
    }

    private static T Wait<T>(ManualResetEventSlim gate, T value)
    {
        gate.Wait(TimeSpan.FromSeconds(5));
        return value;
    }

    [Fact]
    public async Task Run_EveryRowFails_EndsDoneWithResults()
    {
        var b = FakeBackend.Typical();
        b.Outcome = i => new RowResult(i.Id, RowOutcome.Failed, "boom", null, 0, 0);
        var s = NewSession(b);
        await s.LookAsync();
        await s.RunAllAsync();
        Assert.Equal(SessionState.Done, s.State);
        Assert.NotEmpty(s.Results);
        Assert.All(s.Results.Values, r => Assert.Equal(RowOutcome.Failed, r.Outcome));
    }

    [Fact]
    public async Task RunAsync_WhileRunning_IsRefused()
    {
        var b = FakeBackend.Typical(); b.HoldRun = new ManualResetEventSlim(false);
        var s = NewSession(b);
        await s.LookAsync();
        var first = s.RunAllAsync();
        Assert.True(SpinWait.SpinUntil(() => s.State == SessionState.Running, 2000));
        await s.RunAllAsync();
        await s.RunAsync(ActionGroupKind.Speed);
        await s.RescanAsync();
        await s.UndoAsync();
        b.HoldRun.Set();
        await first;
        Assert.Single(b.Runs);
        Assert.Empty(b.Undone);
        Assert.Equal(SessionState.Done, s.State);
    }

    [Fact]
    public async Task RunAsync_BeforeReady_IsRefused()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.RunAllAsync();
        await s.RunAsync(ActionGroupKind.Space);
        Assert.Empty(b.Runs);
    }

    [Fact]
    public async Task UndoAsync_UndoesTheLastRun_ThenLooksAgain()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        await s.RunAllAsync();
        Assert.True(s.CanUndo);
        await s.UndoAsync();
        Assert.Equal(new[] { "run-1" }, b.Undone);
        Assert.Equal(SessionState.Ready, s.State);
        Assert.NotNull(s.UndoResults);
        Assert.False(s.CanUndo);
    }

    [Fact]
    public async Task CanUndo_IsFalse_WhenTheRunHasNoUndoableRules()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        await s.RunAsync(ActionGroupKind.Space);
        Assert.False(s.CanUndo);
    }

    [Fact]
    public async Task LookAsync_LoadsThePreviousRunFromDisk()
    {
        var b = FakeBackend.Typical();
        b.Latest = new RunRecord("old", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1),
            Array.Empty<RowResult>(), new[] { "privacy.ads" });
        b.ReceiptToReturn = TestReceipts.Freed("old", 20_000_000_000);
        var s = NewSession(b);
        await s.LookAsync();
        Assert.Equal("old", s.LastRun!.RunId);
        Assert.False(s.RanThisSession);
        Assert.True(s.CanUndo);
        Assert.Equal(20_000_000_000, s.Receipt!.FreedBytes);
    }

    [Fact]
    public async Task Rescan_AfterARun_KeepsThisSessionsRunAndReceipt()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        b.ReceiptToReturn = TestReceipts.Freed("run-1", 5_000);
        await s.LookAsync();
        await s.RunAllAsync();
        await s.RescanAsync();
        Assert.Equal(SessionState.Ready, s.State);
        Assert.True(s.RanThisSession);
        Assert.Equal("run-1", s.LastRun!.RunId);
    }

    // ---- Yer aç levels, tap to skip, Hepsini yap, undo scope

    private static readonly StringComparer Ordinal = StringComparer.Ordinal;

    /// Typical's Speed and Privacy rows, and one Space row per level: caches
    /// 1 GB (MIN), shaders 2 GB (MID), Windows.old 4 GB (MAX), and restore
    /// points at MAX with no size, as the builder makes it.
    private static FakeBackend Levels()
    {
        var b = FakeBackend.Typical();
        b.Space = new ActionGroup(ActionGroupKind.Space, new[]
        {
            FakeBackend.Row("space.caches", ActionGroupKind.Space, 1L << 30, reversible: false, level: SpaceLevel.Min),
            FakeBackend.Row("space.shaders", ActionGroupKind.Space, 2L << 30, on: false, reversible: false,
                level: SpaceLevel.Mid),
            FakeBackend.Row("space.windows-old", ActionGroupKind.Space, 4L << 30, on: false, reversible: false,
                level: SpaceLevel.Max, lost: true),
            FakeBackend.Row("space.restore-points", ActionGroupKind.Space, null, on: false, reversible: false,
                level: SpaceLevel.Max, lost: true),
        });
        return b;
    }

    private static ActionItem ItemOf(ActionSession s, string id) => s.Items.Single(i => i.Id == id);

    [Fact]
    public async Task Level_selection_is_cumulative()
    {
        var b = Levels(); var s = NewSession(b);
        await s.LookAsync();
        Assert.Equal(SpaceLevel.Min, s.Level);                  // the default selection
        Assert.Equal(1L << 30, s.LevelBytes(SpaceLevel.Min));
        Assert.Equal(3L << 30, s.LevelBytes(SpaceLevel.Mid));
        Assert.Equal(7L << 30, s.LevelBytes(SpaceLevel.Max));
        Assert.False(s.WillRun(ItemOf(s, "space.shaders")));

        s.SetLevel(SpaceLevel.Mid);
        Assert.True(s.WillRun(ItemOf(s, "space.shaders")));
        await s.RunAsync(ActionGroupKind.Space);
        Assert.Equal(new[] { "space.caches", "space.shaders" }, b.Runs[0].RowIds.OrderBy(i => i, Ordinal));

        await s.RescanAsync();
        s.SetLevel(SpaceLevel.Max);
        await s.RunAsync(ActionGroupKind.Space);
        Assert.Equal(new[] { "space.caches", "space.restore-points", "space.shaders", "space.windows-old" },
            b.Runs[1].RowIds.OrderBy(i => i, Ordinal));
    }

    [Fact]
    public async Task Locked_row_above_level_cannot_be_toggled()
    {
        var b = Levels(); var s = NewSession(b);
        await s.LookAsync();
        var changes = 0;
        s.Changed += () => changes++;

        s.ToggleSkip("space.shaders");                          // MID, above MIN: locked
        Assert.False(s.IsSkipped("space.shaders"));
        Assert.Equal(3L << 30, s.LevelBytes(SpaceLevel.Mid));
        Assert.Equal(0, changes);

        s.SetLevel(SpaceLevel.Mid);
        Assert.Equal(1, changes);
        s.ToggleSkip("space.shaders");                          // included now: a tap skips it
        Assert.True(s.IsSkipped("space.shaders"));
        Assert.Equal(1L << 30, s.LevelBytes(SpaceLevel.Mid));
        Assert.False(s.WillRun(ItemOf(s, "space.shaders")));

        s.SetLevel(SpaceLevel.Min);
        s.ToggleSkip("space.shaders");                          // locked again: the skip stays
        Assert.True(s.IsSkipped("space.shaders"));
        s.ToggleSkip("space.caches");                           // a MIN row is open at every level
        Assert.True(s.IsSkipped("space.caches"));
        Assert.Equal(0, s.LevelBytes(SpaceLevel.Min));
    }

    [Fact]
    public async Task Run_uses_selection_at_press_time()
    {
        var b = Levels(); b.HoldRun = new ManualResetEventSlim(false);
        var s = NewSession(b);
        await s.LookAsync();
        var run = s.RunAsync(ActionGroupKind.Space);             // pressed at MIN
        Assert.True(SpinWait.SpinUntil(() => s.State == SessionState.Running, 2000));

        s.SetLevel(SpaceLevel.Max);                             // refused while a run is going
        s.ToggleSkip("space.caches");                           // refused while running
        Assert.Equal(SpaceLevel.Min, s.Level);
        Assert.False(s.IsSkipped("space.caches"));

        b.HoldRun.Set();
        await run;
        Assert.Equal(new[] { "space.caches" }, b.Runs.Single().RowIds);
        Assert.Equal(new[] { "space.caches" }, s.Results.Keys);
        Assert.Equal(SessionState.Done, s.State);
        s.SetLevel(SpaceLevel.Max);                             // not busy any more
        Assert.Equal(SpaceLevel.Max, s.Level);
    }

    [Fact]
    public async Task Run_all_uses_min_and_defaults()
    {
        var b = Levels(); var s = NewSession(b);
        await s.LookAsync();
        s.SetLevel(SpaceLevel.Max);                             // Yer aç's level does not ride along
        s.ToggleSkip("speed.animations");                       // a skip on any page holds
        s.ToggleSkip("privacy.location");                       // added on its page; not a default
        var takes = s.Items.Where(s.InRunAll).Select(i => i.Id).OrderBy(i => i, Ordinal).ToList();

        await s.RunAllAsync();

        var ran = b.Runs.Single().RowIds.OrderBy(i => i, Ordinal).ToList();
        Assert.Equal(new[] { "privacy.ads", "space.caches", "startup:HKCU:Spotify" }, ran);
        Assert.Equal(ActionSelection.ForLevel(s.Plan!, SpaceLevel.Min, new HashSet<string> { "speed.animations" })
            .RowIds.OrderBy(i => i, Ordinal), ran);
        Assert.Equal(takes, ran);
        Assert.Null(s.RunGroup);
        Assert.Equal(1, b.DeferredProcessed);                   // space was in the run
    }

    [Fact]
    public async Task A_row_off_by_default_is_added_by_a_tap_to_its_page_run_only()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        Assert.True(s.IsSkipped("privacy.location"));           // not in any run yet
        s.ToggleSkip("privacy.location");
        Assert.False(s.IsSkipped("privacy.location"));
        Assert.False(s.InRunAll(ItemOf(s, "privacy.location")));
        await s.RunAsync(ActionGroupKind.Privacy);
        Assert.Equal(new[] { "privacy.ads", "privacy.location" }, b.Runs.Single().RowIds.OrderBy(i => i, Ordinal));
    }

    /// Ruling R22: the level is free while a look goes (the run still takes
    /// its selection at the press) and refused while an undo is in flight.
    [Fact]
    public async Task Level_is_free_during_the_look_and_refused_during_an_undo()
    {
        var b = Levels(); var s = NewSession(b);
        await s.LookAsync();
        b.HoldScan = new ManualResetEventSlim(false);
        var look = s.RescanAsync();
        Assert.Equal(SessionState.Looking, s.State);
        Assert.True(s.Busy);
        s.SetLevel(SpaceLevel.Mid);
        Assert.Equal(SpaceLevel.Mid, s.Level);
        b.HoldScan.Set();
        await look;
        await s.RunAsync(ActionGroupKind.Space);
        Assert.Equal(new[] { "space.caches", "space.shaders" }, b.Runs[0].RowIds.OrderBy(i => i, Ordinal));

        await s.RescanAsync();
        await s.RunAsync(ActionGroupKind.Privacy);
        Assert.True(s.CanUndo);
        b.HoldUndo = new ManualResetEventSlim(false);
        var undo = s.UndoAsync();
        s.SetLevel(SpaceLevel.Max);                             // refused: the undo is in flight
        s.ToggleSkip("space.caches");
        Assert.Equal(SpaceLevel.Mid, s.Level);
        Assert.False(s.IsSkipped("space.caches"));
        b.HoldUndo.Set();
        await undo;
        s.SetLevel(SpaceLevel.Max);
        Assert.Equal(SpaceLevel.Max, s.Level);
    }

    /// Ruling R21: Geri al reverses the Speed and Privacy rules of the run
    /// alone. Hepsini yap ran Space and both pages in one run; a Space rule
    /// in it (0.11.0's hibernation, then on by default) stays as it is.
    [Fact]
    public async Task Undo_reverses_only_the_speed_and_privacy_rules_of_the_run()
    {
        var b = FakeBackend.Typical();
        var hibernation = ActionPlanBuilder.ItemFrom(ActionCatalog.Find("space.hibernation"), 1L << 30)
            with { Reversible = true, Level = SpaceLevel.Min };
        b.Space = new ActionGroup(ActionGroupKind.Space, b.Space!.Items.Append(hibernation).ToList());
        var s = NewSession(b);
        await s.LookAsync();
        await s.RunAllAsync();
        Assert.Contains("hibernation", s.LastRun!.UndoableRuleIds);
        Assert.True(s.CanUndo);

        await s.UndoAsync();

        Assert.Equal(new[] { "privacy.ads", "speed.animations", "startup:HKCU:Spotify" },
            b.UndoneRules.OrderBy(i => i, Ordinal));
        Assert.DoesNotContain(s.UndoResults!, r => r.RowId == "hibernation");
        Assert.NotNull(b.Latest!.UndoneUtc);                    // the run counts as undone
        Assert.False(s.CanUndo);
    }

    [Fact]
    public async Task Undo_of_a_previous_run_leaves_its_space_rules_alone()
    {
        var b = Levels();
        b.Latest = new RunRecord("old", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1),
            Array.Empty<RowResult>(), new[] { "hibernation", "advertising-id", "startup:HKCU:Spotify" });
        var s = NewSession(b);
        await s.LookAsync();
        await s.UndoAsync();
        Assert.Equal(new[] { "startup:HKCU:Spotify", "advertising-id" }, b.UndoneRules);
    }

    [Fact]
    public async Task Space_only_run_offers_no_undo()
    {
        // 0.11.0 kept hibernation's rule as undoable; Yer aç has no Geri al,
        // so a record holding it alone offers none.
        var b = Levels();
        var hibernation = ActionPlanBuilder.ItemFrom(ActionCatalog.Find("space.hibernation"), 12L << 30)
            with { Reversible = true };
        b.Space = new ActionGroup(ActionGroupKind.Space, b.Space!.Items.Append(hibernation).ToList());
        var s = NewSession(b);
        await s.LookAsync();
        s.SetLevel(SpaceLevel.Mid);
        await s.RunAsync(ActionGroupKind.Space);
        Assert.Contains("hibernation", s.LastRun!.UndoableRuleIds);
        Assert.False(s.CanUndo);

        await s.RescanAsync();
        await s.RunAsync(ActionGroupKind.Privacy);
        Assert.True(s.CanUndo);
    }

    [Fact]
    public async Task A_previous_run_that_holds_only_space_offers_no_undo()
    {
        var b = Levels();
        b.Latest = new RunRecord("old", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1),
            Array.Empty<RowResult>(), new[] { "hibernation" });
        var s = NewSession(b);
        await s.LookAsync();
        Assert.Equal("old", s.LastRun!.RunId);
        Assert.False(s.CanUndo);
    }

    [Fact]
    public async Task Unknown_size_rows_add_nothing_to_level_bytes()
    {
        var b = Levels(); var s = NewSession(b);
        await s.LookAsync();
        var restore = ItemOf(s, "space.restore-points");
        Assert.Null(restore.Bytes);
        Assert.Equal(7L << 30, s.LevelBytes(SpaceLevel.Max));   // 1 + 2 + 4; no guess for the row without a size
        s.SetLevel(SpaceLevel.Max);
        s.ToggleSkip("space.windows-old");
        Assert.Equal(3L << 30, s.LevelBytes(SpaceLevel.Max));
        Assert.True(s.WillRun(restore));                        // it still runs at MAX
    }

    /// Rulings R7, R10, R12: these targets are opt-in for the console only;
    /// Yer aç takes their rows when the level includes them.
    [Fact]
    public async Task The_console_opt_in_rows_run_at_their_level()
    {
        var b = FakeBackend.Typical();
        ActionItem Catalog(string id, long? bytes) => ActionPlanBuilder.ItemFrom(ActionCatalog.Find(id), bytes);
        b.Space = new ActionGroup(ActionGroupKind.Space, new[]
        {
            Catalog("space.caches", 100), Catalog("space.project-deps", 200), Catalog("space.profile-git", 300),
            Catalog("space.archives", 400), Catalog("space.restore-points", null),
        });
        foreach (var target in new[] { "stale-project-deps", "profile-git", "archive-beside-extracted", "restore-points" })
            Assert.True(CleanupTargetRegistry.All.Single(t => t.Id == target).RequiresExplicitOptIn);
        var s = NewSession(b);
        await s.LookAsync();
        s.SetLevel(SpaceLevel.Max);
        await s.RunAsync(ActionGroupKind.Space);
        Assert.Equal(new[] { "space.archives", "space.caches", "space.profile-git", "space.project-deps",
            "space.restore-points" }, b.Runs.Single().RowIds.OrderBy(i => i, Ordinal));
    }

    [Fact]
    public async Task Personal_files_arrive_from_the_look_without_holding_it_up()
    {
        var b = Levels();
        b.Personal.Add(new PersonalFile("kali.vdi", @"C:\VMs\kali.vdi", 24L << 30));
        b.HoldPersonal = new ManualResetEventSlim(false);
        var s = NewSession(b);
        await s.LookAsync();
        Assert.Equal(SessionState.Ready, s.State);              // the walk does not hold up the plan
        Assert.False(s.PersonalFilesReady);
        Assert.Empty(s.PersonalFiles);
        Assert.True(SpinWait.SpinUntil(() => b.PersonalCalls == 1, 2000));
        Assert.True(b.PersonalTokens.Single().CanBeCanceled);

        b.HoldPersonal.Set();
        Assert.True(SpinWait.SpinUntil(() => s.PersonalFilesReady, 3000));
        Assert.Equal("kali.vdi", s.PersonalFiles.Single().Name);
        Assert.Equal(1L << 30, s.LevelBytes(SpaceLevel.Min));   // never counted
        Assert.Equal(7L << 30, s.LevelBytes(SpaceLevel.Max));
    }

    [Fact]
    public async Task Personal_files_that_cannot_be_read_are_an_empty_list()
    {
        var b = Levels(); b.FailPersonal = new UnauthorizedAccessException("denied");
        var s = NewSession(b);
        await s.LookAsync();
        Assert.True(SpinWait.SpinUntil(() => s.PersonalFilesReady, 3000));
        Assert.Empty(s.PersonalFiles);
        Assert.Equal(SessionState.Ready, s.State);
    }

    [Fact]
    public async Task A_new_look_cancels_the_last_walk_and_its_answer_never_lands()
    {
        var b = Levels();
        b.HoldPersonal = new ManualResetEventSlim(false);
        b.PersonalIgnoresCancel = true;                         // a walk that finishes anyway
        b.PersonalByCall = n => new[] { new PersonalFile(n == 1 ? "old.iso" : "new.iso", @"C:\x.iso", 2L << 30) };
        var s = NewSession(b);
        var seen = new List<string>();
        s.Changed += () => seen.AddRange(s.PersonalFiles.Select(f => f.Name));

        await s.LookAsync();
        Assert.True(SpinWait.SpinUntil(() => b.PersonalCalls == 1, 2000));
        await s.RescanAsync();
        Assert.True(SpinWait.SpinUntil(() => b.PersonalCalls == 2, 2000));
        Assert.True(b.PersonalTokens[0].IsCancellationRequested);
        Assert.False(b.PersonalTokens[1].IsCancellationRequested);

        b.HoldPersonal.Set();
        Assert.True(SpinWait.SpinUntil(() => s.PersonalFilesReady, 3000));
        Thread.Sleep(100);                                      // room for a late first answer
        Assert.Equal("new.iso", s.PersonalFiles.Single().Name);
        lock (Gate) Assert.DoesNotContain("old.iso", seen);
    }
}
