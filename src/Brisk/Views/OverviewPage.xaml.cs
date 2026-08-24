using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Brisk.Views;

public partial class OverviewPage : UserControl
{
    // Ambient budgets (spec bands): orbit 24–36 s, comet 8–12 s, sheen
    // pass 1.5 s inside an ~9 s period.
    private const double OrbitSeconds = 30;
    private const double CometSeconds = 10;
    private const double SheenPeriodSeconds = 9;
    private const double SheenTravelSeconds = 1.5;
    /// Band parked fully left of the panel / slid fully past its right edge
    /// (640 px content column; the 20°-tilted 120 px band spans ±142 px
    /// around its center at X+60).
    private const double SheenRestX = -240;
    private const double SheenEndX = 700;

    private readonly AmbientMotionController _ambient;

    public OverviewPage()
    {
        InitializeComponent();
        _ambient = new AmbientMotionController(
            // SPI_GETCLIENTAREAANIMATION — Windows' "show animations"
            // accessibility switch; false = reduce motion, ambient stays off.
            () => SystemParameters.ClientAreaAnimation,
            StartAmbient, StopAmbient);
        // The hero Border does not clip children to its rounded corners, so
        // the traveling sheen band gets a matching rounded clip in code
        // (updates only on resize — no per-frame work).
        HeroPanel.SizeChanged += (_, e) =>
            HeroPanel.Clip = new RectangleGeometry(new Rect(e.NewSize), 12, 12);
    }

    /// MainWindow calls this from the SAME IsVisibleChanged/StateChanged
    /// path that gates LiveMetrics — one visibility signal, so the ambient
    /// clocks and the data ticker can never drift apart. Data-driven motion
    /// (the CPU and RAM arcs, the numeral ticks) needs no handling here: it
    /// only moves when the ticker delivers a value, and the ticker already
    /// stops.
    public void SetMotionActive(bool active) => _ambient.SetActive(active);

    private void StartAmbient()
    {
        CometLayer.Opacity = 1;
        OrbitSpin.BeginAnimation(RotateTransform.AngleProperty, Revolution(OrbitSeconds));
        CometSpin.BeginAnimation(RotateTransform.AngleProperty, Revolution(CometSeconds));
        SheenSlide.BeginAnimation(TranslateTransform.XProperty, SheenSweep());
        LitGauge.SetGlowBreathing(true);
    }

    /// BeginAnimation(…, null) unloads the clocks entirely — a hidden
    /// window is not "an invisible window still compositing" — and every
    /// property falls back to its parked base value.
    private void StopAmbient()
    {
        OrbitSpin.BeginAnimation(RotateTransform.AngleProperty, null);
        CometSpin.BeginAnimation(RotateTransform.AngleProperty, null);
        SheenSlide.BeginAnimation(TranslateTransform.XProperty, null);
        LitGauge.SetGlowBreathing(false);
        CometLayer.Opacity = 0;
    }

    /// One slow linear revolution, forever, on a RotateTransform — an
    /// independent (render-thread) animation; the dashes/comet themselves
    /// are drawn once and never re-rendered.
    private static DoubleAnimation Revolution(double seconds) =>
        new(0, 360, new Duration(TimeSpan.FromSeconds(seconds)))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };

    /// The sheen's cycle: snap to the parked position, glide across in
    /// 1.5 s, then rest off-panel for the remainder of the ~9 s period.
    private static DoubleAnimationUsingKeyFrames SheenSweep()
    {
        var sweep = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromSeconds(SheenPeriodSeconds)),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(SheenRestX,
            KeyTime.FromTimeSpan(TimeSpan.Zero)));
        sweep.KeyFrames.Add(new LinearDoubleKeyFrame(SheenEndX,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(SheenTravelSeconds))));
        return sweep;
    }
}
