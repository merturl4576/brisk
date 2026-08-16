using Brisk.Views;
using Xunit;

namespace Brisk.Tests;

/// The gate in front of the hero's perpetual storyboards: window visibility
/// (the same signal that drives LiveMetrics) AND Windows' reduce-motion
/// setting. Start/Stop are plain delegates here — the logic is what round 7
/// must not get wrong, the storyboards themselves are eyeballed.
public class AmbientMotionControllerTests
{
    [Fact]
    public void Show_StartsOnce_RepeatedShowsAreIdempotent()
    {
        int started = 0, stopped = 0;
        var gate = new AmbientMotionController(() => true,
            () => started++, () => stopped++);

        gate.SetActive(true);
        gate.SetActive(true);   // IsVisibleChanged + StateChanged both fire

        Assert.True(gate.IsRunning);
        Assert.Equal(1, started);
        Assert.Equal(0, stopped);
    }

    [Fact]
    public void Hide_StopsTheClocks_AndOnlyOnce()
    {
        int started = 0, stopped = 0;
        var gate = new AmbientMotionController(() => true,
            () => started++, () => stopped++);

        gate.SetActive(true);
        gate.SetActive(false);
        gate.SetActive(false);   // hide + minimize both report invisible

        Assert.False(gate.IsRunning);
        Assert.Equal(1, started);
        Assert.Equal(1, stopped);
    }

    [Fact]
    public void Hide_WhenNothingEverStarted_DoesNotStop()
    {
        var stopped = 0;
        var gate = new AmbientMotionController(() => true,
            () => { }, () => stopped++);

        gate.SetActive(false);

        Assert.Equal(0, stopped);
    }

    [Fact]
    public void ReduceMotion_KeepsThePerpetualLayerOff()
    {
        var started = 0;
        var gate = new AmbientMotionController(() => false,
            () => started++, () => { });

        gate.SetActive(true);

        Assert.False(gate.IsRunning);
        Assert.Equal(0, started);
    }

    [Fact]
    public void ReduceMotion_IsReReadOnEveryActivation()
    {
        var animations = false;
        int started = 0, stopped = 0;
        var gate = new AmbientMotionController(() => animations,
            () => started++, () => stopped++);

        gate.SetActive(true);            // reduce-motion on: stays still
        Assert.Equal(0, started);

        animations = true;               // user re-enables system animations
        gate.SetActive(true);            // next visibility signal picks it up
        Assert.True(gate.IsRunning);
        Assert.Equal(1, started);

        animations = false;              // reduce-motion re-enabled mid-run:
        gate.SetActive(true);            // the same signal now stops it
        Assert.False(gate.IsRunning);
        Assert.Equal(1, stopped);
    }
}
