using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Animation;
using Brisk.Session;

namespace Brisk.Ui.Controls;

/// The huge thin number in the gauge ("26" GB). A new Bytes value is counted
/// up to (or down to) over 600 ms; null shows a dash.
public sealed class HeroNumeral : TextBlock
{
    private static readonly TimeSpan Count = TimeSpan.FromMilliseconds(600);

    public static readonly DependencyProperty BytesProperty = DependencyProperty.Register(nameof(Bytes),
        typeof(long?), typeof(HeroNumeral), new PropertyMetadata(null, OnBytesChanged));

    public static readonly DependencyProperty ShownBytesProperty = DependencyProperty.Register(nameof(ShownBytes),
        typeof(double), typeof(HeroNumeral), new PropertyMetadata(0.0, (d, _) => ((HeroNumeral)d).Redraw()));

    public static readonly DependencyProperty CultureProperty = DependencyProperty.Register(nameof(Culture),
        typeof(CultureInfo), typeof(HeroNumeral),
        new PropertyMetadata(CultureInfo.GetCultureInfo("en"), (d, _) => ((HeroNumeral)d).Redraw()));

    public static readonly DependencyProperty UnitFontSizeProperty = DependencyProperty.Register(nameof(UnitFontSize),
        typeof(double), typeof(HeroNumeral), new PropertyMetadata(26.0, (d, _) => ((HeroNumeral)d).Redraw()));

    public long? Bytes { get => (long?)GetValue(BytesProperty); set => SetValue(BytesProperty, value); }
    public double ShownBytes { get => (double)GetValue(ShownBytesProperty); set => SetValue(ShownBytesProperty, value); }
    public CultureInfo Culture { get => (CultureInfo)GetValue(CultureProperty); set => SetValue(CultureProperty, value); }
    public double UnitFontSize { get => (double)GetValue(UnitFontSizeProperty); set => SetValue(UnitFontSizeProperty, value); }

    public string Number { get; private set; } = "0";
    public string Unit { get; private set; } = "GB";

    public HeroNumeral()
    {
        FontSize = 86;
        FontWeight = FontWeights.Light;
        Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular);
        Redraw();
    }

    public static (string Number, string Unit) TextFor(double? shown, CultureInfo c) =>
        shown is { } s ? Sizes.Split((long)Math.Round(s), c) : ("—", "");

    private static void OnBytesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var n = (HeroNumeral)d;
        if (e.NewValue is long target)
            n.BeginAnimation(ShownBytesProperty, new DoubleAnimation(n.ShownBytes, target, Count)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        n.Redraw();
    }

    private void Redraw()
    {
        (Number, Unit) = TextFor(Bytes is null ? null : ShownBytes, Culture);
        Inlines.Clear();
        Inlines.Add(new Run(Number));
        if (Unit.Length > 0) Inlines.Add(new Run(" " + Unit) { FontSize = UnitFontSize, FontWeight = FontWeights.Normal });
    }
}
