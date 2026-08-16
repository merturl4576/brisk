using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Brisk.Views;

/// The overview hero's circular health gauge: a 270° track arc with a value
/// arc on top, both stroked with round caps. Purely geometric — colors come
/// in through TrackBrush/ValueBrush (existing theme brushes), and the only
/// motion is a single ease-out sweep whenever Score changes. Never loops.
public sealed class GaugeArc : FrameworkElement
{
    private const double StartAngle = 135;   // bottom-left; the opening faces down
    private const double FullSweep = 270;

    public static readonly DependencyProperty ScoreProperty =
        DependencyProperty.Register(nameof(Score), typeof(double), typeof(GaugeArc),
            new PropertyMetadata(0.0, OnScoreChanged));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(GaugeArc),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(GaugeArc),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueBrushProperty =
        DependencyProperty.Register(nameof(ValueBrush), typeof(Brush), typeof(GaugeArc),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double),
            typeof(GaugeArc), new FrameworkPropertyMetadata(6.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// What the page binds (the health score, 0–100).
    public double Score
    {
        get => (double)GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    /// What actually gets drawn; Score changes animate this toward Score.
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush? ValueBrush
    {
        get => (Brush?)GetValue(ValueBrushProperty);
        set => SetValue(ValueBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// One tasteful motion: sweep from wherever the arc is to the new score,
    /// 350 ms ease-out. Covers both first load (0 → score) and rescans.
    private static void OnScoreChanged(DependencyObject d,
        DependencyPropertyChangedEventArgs e) =>
        ((GaugeArc)d).BeginAnimation(ValueProperty,
            new DoubleAnimation((double)e.NewValue,
                new Duration(TimeSpan.FromMilliseconds(350)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });

    protected override void OnRender(DrawingContext dc)
    {
        var side = Math.Min(ActualWidth, ActualHeight);
        if (side <= StrokeThickness) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (side - StrokeThickness) / 2;
        DrawArc(dc, TrackBrush, center, radius, FullSweep);
        var sweep = SweepFor(Value);
        if (sweep > 0) DrawArc(dc, ValueBrush, center, radius, sweep);
    }

    private void DrawArc(DrawingContext dc, Brush? brush, Point center,
        double radius, double sweep)
    {
        if (brush is null) return;
        var pen = new Pen(brush, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        if (pen.CanFreeze) pen.Freeze();
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(PointOn(center, radius, StartAngle), false, false);
            g.ArcTo(PointOn(center, radius, StartAngle + sweep),
                new Size(radius, radius), 0, sweep > 180,
                SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    /// 0..100 score → 0..270 degrees of value arc.
    public static double SweepFor(double value) =>
        FullSweep * Math.Clamp(value, 0, 100) / 100;

    /// Screen-space point on the circle. Angles run clockwise from 3 o'clock
    /// because screen y grows downward, so 135° is the bottom-left start and
    /// the sweep travels up over the top to the bottom-right.
    public static Point PointOn(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians),
                         center.Y + radius * Math.Sin(radians));
    }
}
