using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Brisk.Tests.Snapshots;
using Brisk.Ui;
using Brisk.Ui.Controls;
using Brisk.Views;
using Size = System.Windows.Size;

namespace Brisk.Tests.Ui;

public sealed class ControlsTests
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr"), En = CultureInfo.GetCultureInfo("en");

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.64, 41)]
    [InlineData(1.0, 64)]
    [InlineData(1.7, 64)]
    [InlineData(-1, 0)]
    public void TickGauge_LitFor(double fraction, int lit) => Assert.Equal(lit, TickGauge.LitFor(fraction));

    [Fact]
    public void HeroNumeral_TextFor_NullIsADash() => Assert.Equal(("—", ""), HeroNumeral.TextFor(null, Tr));

    [Fact]
    public void HeroNumeral_TextFor_UsesSizes() => Assert.Equal(("5,1", "GB"), HeroNumeral.TextFor(5_476_083_302, Tr));

    [Fact]
    public void Settle_ParksTheGaugeAndTheNumeral()
    {
        SnapshotRenderer.OnUiThread(() =>
        {
            var gauge = new TickGauge { Width = 400, Height = 400, Fraction = 0.5 };
            var numeral = new HeroNumeral { Culture = En, Bytes = 2L << 30 };
            var panel = new StackPanel { Children = { gauge, numeral } };
            OffscreenLayout.LayOut(panel, new Size(400, 600));
            Assert.Equal(0.5, gauge.ShownFraction);
            Assert.Equal("2.0", numeral.Number);
            Assert.Equal("GB", numeral.Unit);
        });
    }

    [Fact]
    public void HeroNumeral_NullBytes_ShowsADashAtOnce()
    {
        SnapshotRenderer.OnUiThread(() =>
        {
            var numeral = new HeroNumeral { Culture = Tr, Bytes = 3L << 30 };
            numeral.Bytes = null;
            Assert.Equal("—", numeral.Number);
            Assert.Equal("", numeral.Unit);
        });
    }

    [Fact]
    public void SatelliteRing_Done_TurnsTheValueGreen()
    {
        SnapshotRenderer.OnUiThread(() =>
        {
            var ring = new SatelliteRing { Value = "14 → 3", Caption = "Hızlandır" };
            var neutral = ring.ValueBrush;
            ring.Done = true;
            Assert.NotEqual(neutral, ring.ValueBrush);
        });
    }

    [Fact]
    public void Ambient_Spin_StartsAndStopsWithTheClock()
    {
        SnapshotRenderer.OnUiThread(() =>
        {
            var clock = new AmbientClock(() => true);
            var arc = new Ellipse { Width = 100, Height = 100 };
            Ambient.SetMotion(arc, AmbientMotion.Spin);
            Ambient.Attach(arc, clock);
            clock.SetVisible(true);
            var rotate = Assert.IsType<RotateTransform>(arc.RenderTransform);
            Assert.True(rotate.HasAnimatedProperties);
            clock.SetVisible(false);
            Assert.False(rotate.HasAnimatedProperties);
        });
    }

    [Fact]
    public void Atmosphere_HasTheMockupsRainColumns_AndStopsWhenUndecorated()
    {
        SnapshotRenderer.OnUiThread(() =>
        {
            var clock = new AmbientClock(() => true);
            var atmosphere = new Atmosphere(clock);
            OffscreenLayout.LayOut(atmosphere, new Size(1100, 680));
            Assert.Equal(Atmosphere.RainColumns, atmosphere.RainColumnCount);
            clock.SetVisible(true);
            Assert.True(atmosphere.MotionRunning);
            atmosphere.Decorated = false;
            Assert.False(atmosphere.MotionRunning);
        });
    }
}
