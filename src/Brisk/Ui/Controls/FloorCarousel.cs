using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Brisk.Ui.Controls;

/// Home's floor (the mockup's .plat): the satellites stand on one ellipse
/// lying on the grid floor, the one at angle 0 in front, the others behind
/// and smaller. Each child sits at its BaseAngle plus the carousel's Angle;
/// a new Angle turns the floor there over one second at 20 frames a second,
/// or at once with reduced motion. The platform rings, the beam and the
/// links to each satellite are drawn here and move with them.
///
/// Laid out in a 1100×236 design box (the stage's width, the mockup's floor
/// height) scaled to the panel. A child's anchor — the centre of its ring —
/// is AnchorInset above its bottom edge, centred.
public sealed class FloorCarousel : Panel
{
    public const double DesignWidth = 1100, DesignHeight = 236;
    public const double CenterY = 96, RadiusX = 310, RadiusY = 58;
    public static readonly TimeSpan Turn = TimeSpan.FromSeconds(1);

    private static readonly Pen Orbit = Frozen(new Pen(Accent(0x73), 1.2));
    private static readonly Pen OrbitOuter = Frozen(new Pen(Accent(0x2E), 0.8));
    private static readonly Pen Platform = Frozen(new Pen(Accent(0x99), 1.6));
    private static readonly Pen PlatformThin = Frozen(new Pen(Accent(0x7A), 1));
    private static readonly Brush PlatformFill = FrozenBrush(Accent(0x1A));
    private static readonly Pen Link = Frozen(new Pen(Accent(0x8C), 1.2) { DashStyle = new DashStyle(new[] { 5 / 1.2, 9 / 1.2 }, 0) });
    private static readonly Brush Beam = FrozenBrush(new LinearGradientBrush(
        Color.FromArgb(0x00, 0x5F, 0xD4, 0xE8), Color.FromArgb(0x24, 0x5F, 0xD4, 0xE8), 90));

    public static readonly DependencyProperty BaseAngleProperty = DependencyProperty.RegisterAttached("BaseAngle",
        typeof(double), typeof(FloorCarousel), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static double GetBaseAngle(DependencyObject d) => (double)d.GetValue(BaseAngleProperty);
    public static void SetBaseAngle(DependencyObject d, double value) => d.SetValue(BaseAngleProperty, value);

    public static readonly DependencyProperty AngleProperty = DependencyProperty.Register(nameof(Angle),
        typeof(double), typeof(FloorCarousel), new PropertyMetadata(0.0, (d, e) => ((FloorCarousel)d).TurnTo((double)e.NewValue)));

    public static readonly DependencyProperty ShownAngleProperty = DependencyProperty.Register(nameof(ShownAngle),
        typeof(double), typeof(FloorCarousel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AnchorInsetProperty = DependencyProperty.Register(nameof(AnchorInset),
        typeof(double), typeof(FloorCarousel), new FrameworkPropertyMetadata(11.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    /// Where the floor is turning to, in degrees; the satellite whose
    /// BaseAngle is -Angle (mod 360) ends in front.
    public double Angle { get => (double)GetValue(AngleProperty); set => SetValue(AngleProperty, value); }

    /// Where the floor stands now.
    public double ShownAngle { get => (double)GetValue(ShownAngleProperty); set => SetValue(ShownAngleProperty, value); }

    public double AnchorInset { get => (double)GetValue(AnchorInsetProperty); set => SetValue(AnchorInsetProperty, value); }

    /// A satellite's spot on the floor at `degrees` (0 = front): its anchor
    /// in the design box, and its depth (1 front, 0 back).
    public static (double X, double Y, double Depth) Spot(double degrees)
    {
        var th = degrees * Math.PI / 180;
        return (DesignWidth / 2 + RadiusX * Math.Sin(th), CenterY + RadiusY * Math.Cos(th), (Math.Cos(th) + 1) / 2);
    }

    /// The mockup's depth cues: 72 % size and 55 % opacity at the back.
    public static double ScaleAt(double depth) => 0.72 + 0.38 * depth;
    public static double OpacityAt(double depth) => 0.55 + 0.45 * depth;

    private void TurnTo(double angle)
    {
        var from = ShownAngle;
        BeginAnimation(ShownAngleProperty, null);
        if (!Ambient.Moving)
        {
            ShownAngle = angle;
            return;
        }
        BeginAnimation(ShownAngleProperty, (AnimationTimeline)Ambient.Paced(
            new DoubleAnimation(from, angle, Turn) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } }));
    }

    /// A still frame shows where the turn lands.
    public void Settle()
    {
        BeginAnimation(ShownAngleProperty, null);
        ShownAngle = Angle;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren) child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(double.IsInfinity(availableSize.Width) ? DesignWidth : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? DesignHeight : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (sx, sy) = (finalSize.Width / DesignWidth, finalSize.Height / DesignHeight);
        foreach (UIElement child in InternalChildren)
        {
            var (x, y, depth) = Spot(GetBaseAngle(child) + ShownAngle);
            var size = child.DesiredSize;
            var anchorY = Math.Max(0, size.Height - AnchorInset);
            child.Arrange(new Rect(new Point(x * sx - size.Width / 2, y * sy - anchorY), size));

            var scale = ScaleAt(depth);
            if (child.RenderTransform is not ScaleTransform s || s.IsFrozen)
                child.RenderTransform = s = new ScaleTransform();
            s.ScaleX = s.ScaleY = scale;
            s.CenterX = size.Width / 2;
            s.CenterY = anchorY;
            child.Opacity = OpacityAt(depth);
            SetZIndex(child, (int)Math.Round(depth * 1000));
        }
        return finalSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var (sx, sy) = (ActualWidth / DesignWidth, ActualHeight / DesignHeight);
        if (sx <= 0 || sy <= 0) return;
        dc.PushTransform(new ScaleTransform(sx, sy));
        var centre = new Point(DesignWidth / 2, CenterY);
        dc.DrawEllipse(null, Orbit, centre, RadiusX, RadiusY);
        dc.DrawEllipse(null, OrbitOuter, centre, RadiusX + 14, RadiusY + 5);
        dc.DrawEllipse(null, Platform, centre, 150, 30);
        dc.DrawEllipse(null, PlatformThin, centre, 112, 22.4);
        dc.DrawEllipse(PlatformFill, PlatformThin, centre, 74, 14.8);

        var beam = new StreamGeometry();
        using (var g = beam.Open())
        {
            g.BeginFigure(new Point(centre.X - 58, centre.Y - 6), true, true);
            g.LineTo(new Point(centre.X + 58, centre.Y - 6), false, false);
            g.LineTo(new Point(centre.X + 30, centre.Y + RadiusY), false, false);
            g.LineTo(new Point(centre.X - 30, centre.Y + RadiusY), false, false);
        }
        beam.Freeze();
        dc.DrawGeometry(Beam, null, beam);

        foreach (UIElement child in InternalChildren)
        {
            var (x, y, _) = Spot(GetBaseAngle(child) + ShownAngle);
            var link = new StreamGeometry();
            using (var g = link.Open())
            {
                g.BeginFigure(centre, false, false);
                g.QuadraticBezierTo(new Point((centre.X + x) / 2, Math.Max(centre.Y, y) + 18), new Point(x, y), true, false);
            }
            link.Freeze();
            dc.DrawGeometry(null, Link, link);
        }
        dc.Pop();
    }

    private static SolidColorBrush Accent(byte alpha) => new(Color.FromArgb(alpha, 0x5F, 0xD4, 0xE8));

    private static Pen Frozen(Pen p)
    {
        p.Freeze();
        return p;
    }

    private static Brush FrozenBrush(Brush b)
    {
        b.Freeze();
        return b;
    }
}
