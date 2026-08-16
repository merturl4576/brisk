using Brisk.Views;
using Xunit;

namespace Brisk.Tests;

/// The live CPU ring's pure math and spec-band constants — the arc itself
/// is one StreamGeometry swept over these numbers.
public class SweepRingTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 135)]      // half load = half the 270° dial
    [InlineData(100, 270)]     // full load = the full dial, never more
    [InlineData(-5, 0)]        // clamped: a sensor glitch can't unwind it
    [InlineData(150, 270)]
    public void SweepFor_MapsCpuPercentOntoTheDial(double percent, double expected)
    {
        Assert.Equal(expected, SweepRing.SweepFor(percent));
    }

    [Fact]
    public void RingGeometry_StaysInsideTheSpecBands()
    {
        Assert.InRange(SweepRing.Thickness, 3, 4);       // thin, round-capped
        Assert.InRange(SweepRing.SlewMs, 400, 600);      // per-tick ease-out
    }
}
