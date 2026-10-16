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
    public async Task Ticks_SurviveARescan_InTheSameSession()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        s.SetTicked("space.caches", false);
        s.SetTicked("space.shaders", true);
        await s.LookAsync();
        Assert.False(s.IsTicked("space.caches"));
        Assert.True(s.IsTicked("space.shaders"));
    }

    [Fact]
    public async Task NeedsConfirmation_ListsDownloadsWithFilesAndRecycleBin()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        Assert.Equal(new[] { "space.downloads", "space.recycle-bin" }, s.NeedsConfirmation(null).Select(i => i.Id));
        Assert.Empty(s.NeedsConfirmation(ActionGroupKind.Privacy));
    }

    [Fact]
    public async Task NeedsConfirmation_DownloadsRowWithNoFilesTicked_IsNotListed()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        s.SetFileTicked(@"C:\D\a.exe", false);
        s.SetFileTicked(@"C:\D\b.msi", false);
        Assert.DoesNotContain(s.NeedsConfirmation(null), i => i.Id == "space.downloads");
        await s.RunAsync(null);
        Assert.DoesNotContain("space.downloads", b.Runs.Single().RowIds);
    }

    [Fact]
    public async Task RunAsync_SkipUnrecoverable_UnticksThemAndRunsTheRest()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        await s.RunAsync(null, skipUnrecoverable: true);
        var sel = b.Runs.Single();
        Assert.DoesNotContain("space.recycle-bin", sel.RowIds);
        Assert.DoesNotContain("space.downloads", sel.RowIds);
        Assert.Contains("space.caches", sel.RowIds);
        Assert.False(s.IsTicked("space.recycle-bin"));
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
        await s.RunAsync(null);
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
        await s.RunAsync(null);
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
        await s.RunAsync(null);
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
    public async Task Run_EveryRowFails_EndsDoneWithResults()
    {
        var b = FakeBackend.Typical();
        b.Outcome = i => new RowResult(i.Id, RowOutcome.Failed, "boom", null, 0, 0);
        var s = NewSession(b);
        await s.LookAsync();
        await s.RunAsync(null);
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
        var first = s.RunAsync(null);
        Assert.True(SpinWait.SpinUntil(() => s.State == SessionState.Running, 2000));
        await s.RunAsync(null);
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
        await s.RunAsync(null);
        Assert.Empty(b.Runs);
    }

    [Fact]
    public async Task UndoAsync_UndoesTheLastRun_ThenLooksAgain()
    {
        var b = FakeBackend.Typical(); var s = NewSession(b);
        await s.LookAsync();
        await s.RunAsync(null);
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
        await s.RunAsync(ActionGroupKind.Space, skipUnrecoverable: true);
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
        await s.RunAsync(null);
        await s.RescanAsync();
        Assert.Equal(SessionState.Ready, s.State);
        Assert.True(s.RanThisSession);
        Assert.Equal("run-1", s.LastRun!.RunId);
    }
}
