using System;
using System.Threading;
using System.Threading.Tasks;
using Brisk.Services;
using Xunit;

namespace Brisk.Tests;

public class RefreshConfirmationTests
{
    // The window elapsing means the user never answered — which is exactly
    // what a black screen looks like from here.
    [Fact]
    public async Task WindowElapses_RollsBack()
    {
        var rolledBack = false;
        var confirmation = new RefreshConfirmation(
            () => rolledBack = true, (_, _) => Task.CompletedTask);

        Assert.False(await confirmation.AwaitConfirmationAsync());
        Assert.True(rolledBack);
        Assert.True(confirmation.RolledBack);
    }

    [Fact]
    public async Task Kept_DoesNotRollBack()
    {
        var rolledBack = false;
        var confirmation = new RefreshConfirmation(
            () => rolledBack = true,
            (_, ct) => Task.Delay(Timeout.Infinite, ct));

        var pending = confirmation.AwaitConfirmationAsync();
        confirmation.Keep();

        // Race the pending task against a timeout instead of awaiting it
        // outright: if Keep() ever regresses to a no-op, the infinite delay
        // never completes, and without this race the test would hang forever
        // rather than reporting a failure.
        var winner = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(pending, winner);

        Assert.True(await pending);
        Assert.False(rolledBack);
        Assert.False(confirmation.RolledBack);
    }

    // The class exists to guard a fix that can blank the screen, so the
    // rollback must never fire twice off the same instance — not from a
    // second call, and not from a race between concurrent calls.
    [Fact]
    public async Task CalledTwice_RollsBackOnlyOnce()
    {
        var rollbackCount = 0;
        var confirmation = new RefreshConfirmation(
            () => rollbackCount++, (_, _) => Task.CompletedTask);

        await confirmation.AwaitConfirmationAsync();
        await confirmation.AwaitConfirmationAsync();

        Assert.Equal(1, rollbackCount);
    }

    // A second call that arrives while the first is still inside its window
    // (a duplicate event handler, a double-click) must not invent an answer
    // from a still-false RolledBack. Both callers should see the same real
    // outcome once the window actually elapses, and the rollback should
    // still run exactly once.
    [Fact]
    public async Task ConcurrentCall_WhileFirstStillPending_ObservesTheRealOutcome()
    {
        var rollbackCount = 0;
        var gate = new TaskCompletionSource<object?>();
        var confirmation = new RefreshConfirmation(
            () => rollbackCount++, (_, _) => gate.Task);

        var first = confirmation.AwaitConfirmationAsync();
        var second = confirmation.AwaitConfirmationAsync();

        // Neither call has an answer yet — the window has not elapsed.
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        // The window elapses.
        gate.SetResult(null);

        Assert.False(await first);
        Assert.False(await second);
        Assert.Equal(1, rollbackCount);
    }

    [Fact]
    public void DefaultWindow_IsFifteenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(15),
            new RefreshConfirmation(() => { }).Window);
    }
}
