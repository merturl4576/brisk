using Brisk.Views;
using Xunit;
using Point = System.Windows.Point;   // WinForms usings make bare Point ambiguous

namespace Brisk.Tests;

/// The gauge's pure geometry (no WPF visual tree needed): score → sweep and
/// angle → screen point. The rendered arcs are just these two functions.
public class GaugeArcTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 135)]
    [InlineData(100, 270)]      // full track is 270°, opening at the bottom
    [InlineData(-10, 0)]        // clamped — a score can't unwind the arc
    [InlineData(150, 270)]
    public void SweepFor_MapsScoreOntoTheThreeQuarterArc(double value, double expected)
    {
        Assert.Equal(expected, GaugeArc.SweepFor(value));
    }

    [Fact]
    public void PointOn_WalksClockwiseFromBottomLeft_OverTheTop()
    {
        var center = new Point(100, 100);

        // 135° = the gauge's start: left of center AND below it (screen y down)
        var start = GaugeArc.PointOn(center, 50, 135);
        Assert.True(start.X < center.X);
        Assert.True(start.Y > center.Y);

        // 270° = top of the dial
        var top = GaugeArc.PointOn(center, 50, 270);
        Assert.Equal(100, top.X, 6);
        Assert.Equal(50, top.Y, 6);

        // 135° + 270° = 405° ≡ 45°: ends at the bottom-right, mirroring the start
        var end = GaugeArc.PointOn(center, 50, 135 + 270);
        Assert.Equal(start.Y, end.Y, 6);
        Assert.Equal(center.X - start.X, end.X - center.X, 6);
    }
}
