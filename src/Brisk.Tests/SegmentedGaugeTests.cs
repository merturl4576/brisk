using System.Linq;
using Brisk.ViewModels;
using Brisk.Views;
using Xunit;
using Point = System.Windows.Point;   // WinForms usings make bare Point ambiguous

namespace Brisk.Tests;

/// The cockpit gauge's pure math (no WPF visual tree needed): score → lit
/// segments, index → tick angle, angle → screen point. The rendered ring is
/// just these functions swept over one segment-count constant.
public class SegmentedGaugeTests
{
    [Fact]
    public void SegmentCount_StaysInsideTheSpecBand()
    {
        Assert.InRange(SegmentedGauge.SegmentCount, 48, 60);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 27)]        // half the score lights half the ring
    [InlineData(100, 54)]       // full score lights every segment
    [InlineData(-10, 0)]        // clamped — a score can't unwind the ring
    [InlineData(150, 54)]
    public void LitCountFor_MapsScoreOntoTheSegments(double score, double expected)
    {
        Assert.Equal(expected, SegmentedGauge.LitCountFor(score));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 27)]
    [InlineData(100, 54)]
    public void LitSegments_CountMatchesTheScoreProportion(double score, int expectedLit)
    {
        var litCount = SegmentedGauge.LitCountFor(score);
        var lit = Enumerable.Range(0, SegmentedGauge.SegmentCount)
            .Count(i => SegmentedGauge.IsLit(i, litCount));
        Assert.Equal(expectedLit, lit);
    }

    [Fact]
    public void AngleFor_SpreadsTicksEvenlyOverTheThreeQuarterArc()
    {
        // first tick sits centered just past the 135° bottom-left start
        Assert.Equal(135 + 135.0 / SegmentedGauge.SegmentCount,
            SegmentedGauge.AngleFor(0), 6);

        // last tick mirrors it just before the 405° bottom-right end
        Assert.Equal(405 - 135.0 / SegmentedGauge.SegmentCount,
            SegmentedGauge.AngleFor(SegmentedGauge.SegmentCount - 1), 6);

        // even pitch: 270° divided by the segment count
        var pitch = 270.0 / SegmentedGauge.SegmentCount;
        Assert.Equal(pitch,
            SegmentedGauge.AngleFor(1) - SegmentedGauge.AngleFor(0), 6);

        // every tick stays inside the arc — the bottom gap is never invaded
        for (var i = 0; i < SegmentedGauge.SegmentCount; i++)
            Assert.InRange(SegmentedGauge.AngleFor(i), 135, 405);
    }

    [Fact]
    public void PointOn_WalksClockwiseFromBottomLeft_OverTheTop()
    {
        var center = new Point(100, 100);

        // 135° = the ring's start: left of center AND below it (screen y down)
        var start = SegmentedGauge.PointOn(center, 50, 135);
        Assert.True(start.X < center.X);
        Assert.True(start.Y > center.Y);

        // 270° = top of the dial
        var top = SegmentedGauge.PointOn(center, 50, 270);
        Assert.Equal(100, top.X, 6);
        Assert.Equal(50, top.Y, 6);

        // 135° + 270° = 405° ≡ 45°: ends at the bottom-right, mirroring the start
        var end = SegmentedGauge.PointOn(center, 50, 135 + 270);
        Assert.Equal(start.Y, end.Y, 6);
        Assert.Equal(center.X - start.X, end.X - center.X, 6);
    }

    /// The ring's color comes from the same score → brush-key mapping the
    /// whole app uses; the hero merely resolves the key to its bright
    /// dark-surface variants. Pin the boundaries the gauge lives on.
    [Theory]
    [InlineData(100, "Good")]
    [InlineData(90, "Good")]
    [InlineData(89, "SeverityWarning")]
    [InlineData(70, "SeverityWarning")]
    [InlineData(69, "SeverityCritical")]
    [InlineData(0, "SeverityCritical")]
    public void GaugeColor_FollowsTheSharedHealthMapping(int health, string expected)
    {
        Assert.Equal(expected, HealthBrush.KeyFor(health));
    }
}
