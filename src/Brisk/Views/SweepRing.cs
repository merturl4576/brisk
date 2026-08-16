using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Brisk.Views;

/// The hero's live CPU ring: a thin round-capped arc just inside the
/// segmented gauge's ticks, sweeping the same 270° span. Pure data-driven
/// motion — every LiveMetrics tick slews the arc to the new CPU% with a
/// short ease-out (the same Value→animated-render-DP pattern as the gauge's
/// LitCount sweep), so it stays alive even under reduce-motion, where only
/// the perpetual ambient layer is skipped.
public sealed class SweepRing : FrameworkElement
{
    /// Spec band 3–4 px, round caps.
    public const double Thickness = 3.5;
    /// Per-tick slew budget (spec band 400–600 ms), ease-out.
    public const double SlewMs = 480;
    // Mirrors SegmentedGauge's dial: 135° start, opening at the bottom.
    private const double StartAngle = 135;
    private const double FullSweep = 270;
    /// Edge → arc-centerline distance, derived from the gauge's geometry:
    /// its ticks end at side/2 − 18.5 (half the 5 px tick thickness + 16 px
    /// length); a 6 px gap puts this ring at side/2 − 24.5.
    private const double Inset = 24.5;

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(SweepRing),
            new PropertyMetadata(0.0, OnValueChanged));

    /// What actually renders; Value changes animate this toward the new
    /// percentage so the arc glides instead of jumping.
    public static readonly DependencyProperty ShownProperty =
        DependencyProperty.Register(nameof(Shown), typeof(double), typeof(SweepRing),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RingBrushProperty =
        DependencyProperty.Register(nameof(RingBrush), typeof(Brush), typeof(SweepRing),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// Live CPU percentage (0–100), bound by the page.
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Shown
    {
        get => (double)GetValue(ShownProperty);
        set => SetValue(ShownProperty, value);
    }

    public Brush? RingBrush
    {
        get => (Brush?)GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    private static void OnValueChanged(DependencyObject d,
        DependencyPropertyChangedEventArgs e) =>
        ((SweepRing)d).BeginAnimation(ShownProperty,
            new DoubleAnimation(Math.Clamp((double)e.NewValue, 0, 100),
                new Duration(TimeSpan.FromMilliseconds(SlewMs)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });

    /// 0..100 % → 0..270° of arc, clamped.
    public static double SweepFor(double percent) =>
        FullSweep * Math.Clamp(percent, 0, 100) / 100;

    protected override void OnRender(DrawingContext dc)
    {
        if (RingBrush is null) return;
        var side = Math.Min(ActualWidth, ActualHeight);
        var radius = side / 2 - Inset;
        var sweep = SweepFor(Shown);
        if (radius <= 0 || sweep <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(SegmentedGauge.PointOn(center, radius, StartAngle),
                isFilled: false, isClosed: false);
            ctx.ArcTo(SegmentedGauge.PointOn(center, radius, StartAngle + sweep),
                new Size(radius, radius), 0, sweep > 180,
                SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        var pen = new Pen(RingBrush, Thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        if (pen.CanFreeze) pen.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
