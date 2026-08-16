using System;
using System.Windows;
using System.Windows.Media;

namespace Brisk.Views;

/// The orbit's comet: one brighter arc with an opacity-falloff tail,
/// revolving slowly outside the segmented gauge (a quiet radar glint, not a
/// spinner). Technique: the tail is approximated with four stacked short
/// arcs of decreasing opacity — a gradient-stroked path was rejected
/// because WPF maps a LinearGradientBrush to the stroke's bounding box, not
/// along the path, so the falloff would distort as the comet rotates.
///
/// The drawing itself is static: OnRender runs once and the revolution is a
/// RotateTransform animation on the element (render-thread, no re-render
/// per frame). The head sits at 0° (3 o'clock) in local coordinates; the
/// tail trails behind it against the clockwise spin.
public sealed class CometTail : FrameworkElement
{
    /// Each arc's span; four of them make the 24° comet (spec band 20–30°).
    public const double SegmentDegrees = 6;
    /// Head → tail falloff. The 0.60 peak is the spec's "~60% opacity".
    public static readonly double[] SegmentOpacities = { 0.60, 0.38, 0.20, 0.08 };
    private const double Thickness = 2.5;

    public static readonly DependencyProperty TailColorProperty =
        DependencyProperty.Register(nameof(TailColor), typeof(Color), typeof(CometTail),
            new FrameworkPropertyMetadata(Colors.Transparent,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public Color TailColor
    {
        get => (Color)GetValue(TailColorProperty);
        set => SetValue(TailColorProperty, value);
    }

    /// Start angle of arc i, counted back from the head's leading edge at
    /// 0°: arc 0 (the head) spans [-6°, 0°], arc 1 [-12°, -6°], and so on —
    /// contiguous, dimming toward the tail.
    public static double StartDegrees(int index) => -SegmentDegrees * (index + 1);

    protected override void OnRender(DrawingContext dc)
    {
        var side = Math.Min(ActualWidth, ActualHeight);
        var radius = (side - Thickness) / 2;
        if (radius <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        for (var i = 0; i < SegmentOpacities.Length; i++)
        {
            var from = StartDegrees(i);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(SegmentedGauge.PointOn(center, radius, from),
                    isFilled: false, isClosed: false);
                ctx.ArcTo(SegmentedGauge.PointOn(center, radius, from + SegmentDegrees),
                    new Size(radius, radius), 0, false,
                    SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze();
            var brush = new SolidColorBrush(TailColor)
            {
                Opacity = SegmentOpacities[i],
            };
            brush.Freeze();
            // Flat caps: round caps would overlap at the joints and stack
            // alpha into visible beads on a 2.5 px hairline.
            var pen = new Pen(brush, Thickness);
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }
}
