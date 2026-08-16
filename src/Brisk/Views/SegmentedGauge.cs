using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Brisk.Views;

/// Which slice of the ring one instance draws. The cockpit hero stacks two
/// instances over each other — an Unlit track below and a Lit arc above
/// carrying the one permitted glow — because a DropShadowEffect wraps a
/// whole element, and only the lit segments may glow.
public enum GaugeLayer { All, Lit, Unlit }

/// The cockpit hero's segmented ring gauge: SegmentCount radial ticks over
/// a 270° arc with the opening at the bottom (replaces the round-4 plain
/// arc). Lit segments are the health-score proportion; colors come in via
/// LitBrush/UnlitBrush (hero-local brushes). Data-driven motion is the
/// one-shot ease-out sweep of LitCount whenever Score changes — segments
/// light up sequentially as the animated count passes them.
///
/// Round 7: the Lit layer also owns the ONE permitted glow. GlowColor
/// builds a DropShadowEffect in code — never a shared/frozen XAML resource,
/// because animating a frozen Freezable throws — so its Opacity stays
/// animatable and the ambient "breathing" (0.25↔0.45, ease-in-out,
/// auto-reverse, forever) can run on it. The breathing loop is gated by the
/// page's AmbientMotionController (window visibility + reduce-motion).
public sealed class SegmentedGauge : FrameworkElement
{
    /// One constant rules the ring's density (spec: 48–60 ticks).
    public const int SegmentCount = 54;
    private const double StartAngle = 135;   // bottom-left; the opening faces down
    private const double FullSweep = 270;
    private const double SegmentThickness = 5;
    private const double SegmentLength = 16;
    /// Full-scale sweep budget: 600 ms ≈ 11 ms per segment at score 100,
    /// one-shot, ease-out (fast ignition, settling at the tip).
    private const double SweepMs = 600;

    public static readonly DependencyProperty ScoreProperty =
        DependencyProperty.Register(nameof(Score), typeof(double), typeof(SegmentedGauge),
            new PropertyMetadata(0.0, OnScoreChanged));

    public static readonly DependencyProperty LitCountProperty =
        DependencyProperty.Register(nameof(LitCount), typeof(double), typeof(SegmentedGauge),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LitBrushProperty =
        DependencyProperty.Register(nameof(LitBrush), typeof(Brush), typeof(SegmentedGauge),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnlitBrushProperty =
        DependencyProperty.Register(nameof(UnlitBrush), typeof(Brush), typeof(SegmentedGauge),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LayerProperty =
        DependencyProperty.Register(nameof(Layer), typeof(GaugeLayer), typeof(SegmentedGauge),
            new FrameworkPropertyMetadata(GaugeLayer.All,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlowColorProperty =
        DependencyProperty.Register(nameof(GlowColor), typeof(Color), typeof(SegmentedGauge),
            new PropertyMetadata(default(Color), OnGlowColorChanged));

    /// The glow's resting opacity (round 6's constant); breathing swings
    /// ±0.10 around it and stopping the breath returns exactly here.
    public const double GlowRestOpacity = 0.35;

    private DropShadowEffect? _glow;
    private bool _breathing;

    /// What the page binds (the health score, 0–100).
    public double Score
    {
        get => (double)GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    /// What actually renders; Score changes animate this toward the score's
    /// segment count, and each tick lights the moment the count passes it.
    public double LitCount
    {
        get => (double)GetValue(LitCountProperty);
        set => SetValue(LitCountProperty, value);
    }

    public Brush? LitBrush
    {
        get => (Brush?)GetValue(LitBrushProperty);
        set => SetValue(LitBrushProperty, value);
    }

    public Brush? UnlitBrush
    {
        get => (Brush?)GetValue(UnlitBrushProperty);
        set => SetValue(UnlitBrushProperty, value);
    }

    public GaugeLayer Layer
    {
        get => (GaugeLayer)GetValue(LayerProperty);
        set => SetValue(LayerProperty, value);
    }

    /// Halo color for this layer (the hero sets it on the Lit layer only,
    /// via the same score triggers that pick LitBrush). Default (transparent
    /// black) means no glow at all.
    public Color GlowColor
    {
        get => (Color)GetValue(GlowColorProperty);
        set => SetValue(GlowColorProperty, value);
    }

    /// Score-color swaps mutate the ONE code-built effect instance instead
    /// of exchanging frozen resources — that is what keeps it animatable.
    private static void OnGlowColorChanged(DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        var gauge = (SegmentedGauge)d;
        var color = (Color)e.NewValue;
        if (color == default)
        {
            gauge._glow = null;
            gauge.Effect = null;
            return;
        }
        if (gauge._glow is null)
        {
            gauge._glow = new DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 0,
                Opacity = GlowRestOpacity,
            };
            gauge.Effect = gauge._glow;
            // Breathing may be requested before styles deliver the first
            // color (and thus the effect) — honor it now.
            if (gauge._breathing) gauge.BeginBreathing();
        }
        gauge._glow.Color = color;
    }

    /// The ambient breathing on the glow. On = the perpetual auto-reverse
    /// loop; off = unload the clock entirely and settle at the rest opacity
    /// (called when the window leaves the screen or reduce-motion is set).
    public void SetGlowBreathing(bool on)
    {
        _breathing = on;
        if (_glow is null) return;
        if (on)
        {
            BeginBreathing();
            return;
        }
        _glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        _glow.Opacity = GlowRestOpacity;
    }

    private void BeginBreathing() =>
        _glow!.BeginAnimation(DropShadowEffect.OpacityProperty, BreathingAnimation());

    /// 0.25 ↔ 0.45 over 3.8 s per leg, sine ease-in-out, auto-reverse,
    /// forever — the hero's slow "alive" pulse (spec: 3.5–4 s).
    public static DoubleAnimation BreathingAnimation() =>
        new(0.25, 0.45, new Duration(TimeSpan.FromSeconds(3.8)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };

    /// The one-shot sweep: from wherever the lit count is to the new score's
    /// count, ease-out. Covers first load (0 → score) and rescans alike; a
    /// rescan sweeps the delta instead of collapsing to zero first.
    private static void OnScoreChanged(DependencyObject d,
        DependencyPropertyChangedEventArgs e) =>
        ((SegmentedGauge)d).BeginAnimation(LitCountProperty,
            new DoubleAnimation(LitCountFor((double)e.NewValue),
                new Duration(TimeSpan.FromMilliseconds(SweepMs)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });

    protected override void OnRender(DrawingContext dc)
    {
        var side = Math.Min(ActualWidth, ActualHeight);
        var outer = (side - SegmentThickness) / 2;
        var inner = outer - SegmentLength;
        if (inner <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var litPen = PenFor(LitBrush);
        var unlitPen = PenFor(UnlitBrush);
        for (var i = 0; i < SegmentCount; i++)
        {
            var lit = IsLit(i, LitCount);
            if (Layer == GaugeLayer.Lit && !lit) continue;
            if (Layer == GaugeLayer.Unlit && lit) continue;
            var pen = lit ? litPen : unlitPen;
            if (pen is null) continue;
            var angle = AngleFor(i);
            dc.DrawLine(pen, PointOn(center, inner, angle),
                PointOn(center, outer, angle));
        }
    }

    private static Pen? PenFor(Brush? brush)
    {
        if (brush is null) return null;
        var pen = new Pen(brush, SegmentThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }

    /// 0..100 score → 0..SegmentCount lit segments, proportionally (kept as
    /// a double: the animation sweeps through it and any fraction lights the
    /// segment it is passing).
    public static double LitCountFor(double score) =>
        SegmentCount * Math.Clamp(score, 0, 100) / 100;

    /// A segment lights the moment the animated count passes its index, so
    /// at rest exactly round-up-of-proportion segments are lit.
    public static bool IsLit(int index, double litCount) => index < litCount;

    /// Center angle of segment i: the ticks split the 270° arc evenly, each
    /// sitting in the middle of its slice.
    public static double AngleFor(int index) =>
        StartAngle + (index + 0.5) * FullSweep / SegmentCount;

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
