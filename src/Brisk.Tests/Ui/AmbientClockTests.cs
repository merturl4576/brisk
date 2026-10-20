using Brisk.Ui;

namespace Brisk.Tests.Ui;

public sealed class AmbientClockTests
{
    [Fact]
    public void RegisteredMotion_RunsOnlyWhileVisibleAndAllowed()
    {
        var allowed = true;
        var running = 0;
        var clock = new AmbientClock(() => allowed);
        clock.Register("rain", () => running++, () => running--);
        Assert.Equal(0, running);
        clock.SetVisible(true);
        Assert.Equal(1, running);
        clock.SetVisible(true);
        Assert.Equal(1, running);
        clock.SetVisible(false);
        Assert.Equal(0, running);
        clock.SetVisible(true);
        allowed = false;
        clock.Reevaluate();
        Assert.Equal(0, running);
        Assert.False(clock.IsActive);
    }

    [Fact]
    public void Register_WhileActive_StartsAtOnce_AndUnregisterStops()
    {
        var running = 0;
        var clock = new AmbientClock(() => true);
        clock.SetVisible(true);
        clock.Register("arc", () => running++, () => running--);
        Assert.Equal(1, running);
        clock.Unregister("arc");
        Assert.Equal(0, running);
        clock.Unregister("arc");
        Assert.Equal(0, running);
    }

    [Fact]
    public void Register_SameOwnerTwice_ReplacesTheFirst()
    {
        var a = 0; var b = 0;
        var clock = new AmbientClock(() => true);
        clock.SetVisible(true);
        clock.Register("x", () => a++, () => a--);
        clock.Register("x", () => b++, () => b--);
        Assert.Equal(0, a);
        Assert.Equal(1, b);
    }
}

public sealed class ManualTimersTests
{
    [Fact]
    public void Every_FiresOnEachPeriod_AndStopsWhenDisposed()
    {
        var t = new ManualTimers();
        var n = 0;
        var sub = t.Every(TimeSpan.FromSeconds(10), () => n++);
        t.Advance(TimeSpan.FromSeconds(25));
        Assert.Equal(2, n);
        sub.Dispose();
        t.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(2, n);
    }

    [Fact]
    public void After_FiresOnce()
    {
        var t = new ManualTimers();
        var n = 0;
        t.After(TimeSpan.FromSeconds(5), () => n++);
        t.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(0, n);
        t.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, n);
    }
}
