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

        Assert.True(await pending);
        Assert.False(rolledBack);
        Assert.False(confirmation.RolledBack);
    }

    [Fact]
    public void DefaultWindow_IsFifteenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(15),
            new RefreshConfirmation(() => { }).Window);
    }
}
