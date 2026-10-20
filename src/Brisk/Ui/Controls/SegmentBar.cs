using System;
using System.Windows;
using System.Windows.Media;

namespace Brisk.Ui.Controls;

/// The row of twelve slanted segments under the hero number (the mockup's
/// .segs): lit up to Fraction, green while Green.
public sealed class SegmentBar : FrameworkElement
{
    public const int Count = 12;
    private const double W = 12, H = 8, Gap = 3, Slant = 3;

    private static readonly Brush Dim = Frozen(Color.FromArgb(0x4D, 0x5F, 0xD4, 0xE8));
    private static readonly Brush Lit = Frozen(Color.FromRgb(0x5F, 0xD4, 0xE8));
    private static readonly Brush LitGreen = Frozen(Color.FromRgb(0x3E, 0xCF, 0x8E));

    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction),
        typeof(double), typeof(SegmentBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GreenProperty = DependencyProperty.Register(nameof(Green),
        typeof(bool), typeof(SegmentBar), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public bool Green { get => (bool)GetValue(GreenProperty); set => SetValue(GreenProperty, value); }

    public static int LitFor(double fraction) => (int)Math.Round(Math.Clamp(fraction, 0, 1) * Count);

    protected override Size MeasureOverride(Size availableSize) => new(Count * (W + Gap) - Gap + Slant, H);

    protected override void OnRender(DrawingContext dc)
    {
        var lit = LitFor(Fraction);
        for (var i = 0; i < Count; i++)
        {
            var x = i * (W + Gap);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x + Slant, 0), true, true);
                c.LineTo(new Point(x + W + Slant, 0), false, false);
                c.LineTo(new Point(x + W, H), false, false);
                c.LineTo(new Point(x, H), false, false);
            }
            g.Freeze();
            dc.DrawGeometry(i < lit ? (Green ? LitGreen : Lit) : Dim, null, g);
        }
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
