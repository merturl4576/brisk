using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Brisk.Ui.Controls;

/// The hero: 64 ticks on a 270° arc open at the bottom (the Monster gauge),
/// with a minor tick every fourth. Drawn in a 400×400 design box scaled to
/// the element. Fraction is where the ticks should be lit up to;
/// ShownFraction sweeps toward it, so a new value fills the arc in front of
/// the user instead of jumping.
public sealed class TickGauge : FrameworkElement
{
    public const int TickCount = 64;
    private const double Box = 400, Center = 200;
    private static readonly TimeSpan Sweep = TimeSpan.FromMilliseconds(900);

    private static readonly Pen Unlit = Frozen(new Pen(Brush(0x1A, 0x3A, 0x48), 6));
    private static readonly Pen LitBlue = Frozen(new Pen(Brush(0xE8, 0xF6, 0xFB), 6));
    private static readonly Pen LitGreen = Frozen(new Pen(Brush(0x3E, 0xCF, 0x8E), 6));
    private static readonly Pen Minor = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x8C, 0xDF, 0xEE, 0xF4)), 1.5));

    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction),
        typeof(double), typeof(TickGauge), new PropertyMetadata(0.0, OnFractionChanged));

    public static readonly DependencyProperty ShownFractionProperty = DependencyProperty.Register(nameof(ShownFraction),
        typeof(double), typeof(TickGauge), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GreenProperty = DependencyProperty.Register(nameof(Green),
        typeof(bool), typeof(TickGauge), new FrameworkPropertyMetadata(false, OnGreenChanged));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public double ShownFraction { get => (double)GetValue(ShownFractionProperty); set => SetValue(ShownFractionProperty, value); }
    public bool Green { get => (bool)GetValue(GreenProperty); set => SetValue(GreenProperty, value); }

    public static int LitFor(double fraction) => (int)Math.Round(Math.Clamp(fraction, 0, 1) * TickCount);

    private static void OnFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (TickGauge)d;
        g.BeginAnimation(ShownFractionProperty, new DoubleAnimation(g.ShownFraction, (double)e.NewValue, Sweep)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// Turning green starts the fill again from empty: the run refills the arc.
    private static void OnGreenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (TickGauge)d;
        g.BeginAnimation(ShownFractionProperty, null);
        g.ShownFraction = 0;
        g.BeginAnimation(ShownFractionProperty, new DoubleAnimation(0, g.Fraction, Sweep)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    protected override void OnRender(DrawingContext dc)
    {
        var scale = Math.Min(ActualWidth, ActualHeight) / Box;
        if (scale <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - Box * scale) / 2, (ActualHeight - Box * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        var lit = LitFor(ShownFraction);
        var on = Green ? LitGreen : LitBlue;
        for (var i = 0; i < TickCount; i++)
        {
            var a = (135 + i * (270.0 / (TickCount - 1))) * Math.PI / 180;
            var (cos, sin) = (Math.Cos(a), Math.Sin(a));
            dc.DrawLine(i < lit ? on : Unlit, new Point(Center + 150 * cos, Center + 150 * sin),
                new Point(Center + 172 * cos, Center + 172 * sin));
            if (i % 4 == 0)
                dc.DrawLine(Minor, new Point(Center + 128 * cos, Center + 128 * sin),
                    new Point(Center + 136 * cos, Center + 136 * sin));
        }
        dc.Pop();
        dc.Pop();
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? Box : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? Box : availableSize.Height);

    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    private static Pen Frozen(Pen p)
    {
        p.Freeze();
        return p;
    }
}
