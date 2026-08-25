using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Brisk.ViewModels;
using Brisk.Views;
using Xunit;
using Color = System.Windows.Media.Color;   // WinForms usings make bare
using Point = System.Windows.Point;         // Point/Color ambiguous

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
    [InlineData(89, "SeverityNotice")]
    [InlineData(70, "SeverityNotice")]
    [InlineData(69, "SeverityCritical")]
    [InlineData(0, "SeverityCritical")]
    public void GaugeColor_FollowsTheSharedHealthMapping(int health, string expected)
    {
        Assert.Equal(expected, HealthBrush.KeyFor(health));
    }

    /// FrameworkElement construction needs an STA thread (InputManager);
    /// xunit runs MTA, so the one instance-level test hops threads.
    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception e) { failure = ExceptionDispatchInfo.Capture(e); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    /// Round 7's animatability guarantee: the glow is built in code, so it
    /// is never a frozen shared resource — a frozen Freezable cannot be
    /// animated, and the breathing storyboard writes to this instance.
    [Fact]
    public void GlowEffect_IsCodeBuilt_Unfrozen_AndReusedAcrossColorSwaps() => RunSta(() =>
    {
        var gauge = new SegmentedGauge();
        Assert.Null(gauge.Effect);   // no glow until a color arrives

        gauge.GlowColor = Color.FromRgb(0x4A, 0xDE, 0x80);
        var glow = Assert.IsType<DropShadowEffect>(gauge.Effect);
        Assert.False(glow.IsFrozen);
        Assert.Equal(SegmentedGauge.GlowRestOpacity, glow.Opacity);
        Assert.Equal(16.0, glow.BlurRadius);
        Assert.Equal(0.0, glow.ShadowDepth);
        Assert.Equal(Color.FromRgb(0x4A, 0xDE, 0x80), glow.Color);

        // a score-color swap recolors the SAME instance (no frozen-resource
        // exchange that would detach a running breathing animation)
        gauge.GlowColor = Color.FromRgb(0xFB, 0xBF, 0x24);
        Assert.Same(glow, gauge.Effect);
        Assert.Equal(Color.FromRgb(0xFB, 0xBF, 0x24), glow.Color);
    });

    [Fact]
    public void BreathingAnimation_MatchesTheSpecBands()
    {
        var breath = SegmentedGauge.BreathingAnimation();

        Assert.Equal(0.25, breath.From);
        Assert.Equal(0.45, breath.To);
        Assert.True(breath.AutoReverse);
        Assert.Equal(RepeatBehavior.Forever, breath.RepeatBehavior);
        Assert.InRange(breath.Duration.TimeSpan.TotalSeconds, 3.5, 4.0);
        var ease = Assert.IsType<SineEase>(breath.EasingFunction);
        Assert.Equal(EasingMode.EaseInOut, ease.EasingMode);
    }
}
