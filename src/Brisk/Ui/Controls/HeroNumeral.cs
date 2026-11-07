using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Animation;
using Brisk.Session;

namespace Brisk.Ui.Controls;

/// What the hero number counts: a size ("26" GB) or a load ("58" %).
public enum HeroKind { Bytes, Percent }

/// The huge thin number in the gauge ("26" GB, or "58" % on the live
/// monitor). A new value is counted up to (or down to) over 600 ms at 20
/// frames a second, or lands at once with reduced motion or Glide off; null
/// shows a dash.
public sealed class HeroNumeral : TextBlock
{
    private static readonly TimeSpan Count = TimeSpan.FromMilliseconds(600);

    public static readonly DependencyProperty BytesProperty = DependencyProperty.Register(nameof(Bytes),
        typeof(long?), typeof(HeroNumeral), new PropertyMetadata(null, OnBytesChanged));

    public static readonly DependencyProperty ShownBytesProperty = DependencyProperty.Register(nameof(ShownBytes),
        typeof(double), typeof(HeroNumeral), new PropertyMetadata(0.0, (d, _) => ((HeroNumeral)d).Redraw()));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind),
        typeof(HeroKind), typeof(HeroNumeral), new PropertyMetadata(HeroKind.Bytes, (d, _) => ((HeroNumeral)d).Redraw()));

    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(nameof(Percent),
        typeof(double?), typeof(HeroNumeral), new PropertyMetadata(null, OnPercentChanged));

    public static readonly DependencyProperty ShownPercentProperty = DependencyProperty.Register(nameof(ShownPercent),
        typeof(double), typeof(HeroNumeral), new PropertyMetadata(0.0, (d, _) => ((HeroNumeral)d).Redraw()));

    /// false: a new value lands at once (a live reading every second);
    /// true: it is counted to.
    public static readonly DependencyProperty GlideProperty = DependencyProperty.Register(nameof(Glide),
        typeof(bool), typeof(HeroNumeral), new PropertyMetadata(true));

    public static readonly DependencyProperty CultureProperty = DependencyProperty.Register(nameof(Culture),
        typeof(CultureInfo), typeof(HeroNumeral),
        new PropertyMetadata(CultureInfo.GetCultureInfo("en"), (d, _) => ((HeroNumeral)d).Redraw()));

    public static readonly DependencyProperty UnitFontSizeProperty = DependencyProperty.Register(nameof(UnitFontSize),
        typeof(double), typeof(HeroNumeral), new PropertyMetadata(26.0, (d, _) => ((HeroNumeral)d).Redraw()));

    public long? Bytes { get => (long?)GetValue(BytesProperty); set => SetValue(BytesProperty, value); }
    public double ShownBytes { get => (double)GetValue(ShownBytesProperty); set => SetValue(ShownBytesProperty, value); }
    public HeroKind Kind { get => (HeroKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public double? Percent { get => (double?)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public double ShownPercent { get => (double)GetValue(ShownPercentProperty); set => SetValue(ShownPercentProperty, value); }
    public bool Glide { get => (bool)GetValue(GlideProperty); set => SetValue(GlideProperty, value); }
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

    /// A whole percent; a dash when unread.
    public static (string Number, string Unit) PercentFor(double? shown, CultureInfo c) =>
        shown is { } s ? (Math.Round(Math.Clamp(s, 0, 100), MidpointRounding.AwayFromZero).ToString("0", c), "%") : ("—", "");

    private static void OnBytesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var n = (HeroNumeral)d;
        if (e.NewValue is long target) n.CountTo(ShownBytesProperty, target);
        n.Redraw();
    }

    private static void OnPercentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var n = (HeroNumeral)d;
        if (e.NewValue is double target) n.CountTo(ShownPercentProperty, target);
        n.Redraw();
    }

    private void CountTo(DependencyProperty shown, double target)
    {
        var from = (double)GetValue(shown);
        BeginAnimation(shown, null);
        if (!Ambient.Moving || !Glide)
        {
            SetValue(shown, target);
            return;
        }
        BeginAnimation(shown, (AnimationTimeline)Ambient.Paced(new DoubleAnimation(from, target, Count)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        }));
    }

    private void Redraw()
    {
        (Number, Unit) = Kind == HeroKind.Percent
            ? PercentFor(Percent is null ? null : ShownPercent, Culture)
            : TextFor(Bytes is null ? null : ShownBytes, Culture);
        Inlines.Clear();
        Inlines.Add(new Run(Number));
        if (Unit.Length > 0) Inlines.Add(new Run(" " + Unit) { FontSize = UnitFontSize, FontWeight = FontWeights.Normal });
    }
}
