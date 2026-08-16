using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Brisk.Views;

/// The ONE numeral-transition mechanism, shared by all four instrument pods
/// and the gauge's center temp badge: bind NumeralTick.Value instead of
/// Text, and every value change slides the new numeral in (5 px up + fade,
/// 170 ms ease-out) so each LiveMetrics tick visibly "ticks".
///
/// Data-driven by design: it only ever runs when a bound value actually
/// changes (the view models' Set() already swallows no-op updates), so it
/// needs no visibility or reduce-motion gating — a hidden window stops the
/// ticker, and reduce-motion keeps data-driven motion per the round-7 spec.
/// Both animations (Opacity + TranslateTransform.Y) are one-shot
/// render-thread transitions on a single TextBlock.
public static class NumeralTick
{
    public const double SlideUpPx = 5;
    public const double DurationMs = 170;

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.RegisterAttached("Value", typeof(string), typeof(NumeralTick),
            new PropertyMetadata(null, OnValueChanged));

    public static string? GetValue(DependencyObject d) =>
        (string?)d.GetValue(ValueProperty);

    public static void SetValue(DependencyObject d, string? value) =>
        d.SetValue(ValueProperty, value);

    private static void OnValueChanged(DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        var text = (string?)e.NewValue ?? "";
        tb.Text = text;
        // The initial binding push (old value null) fills quietly — the
        // startup page must not open with five simultaneous ticks — and a
        // value clearing to empty has nothing to slide in.
        if (e.OldValue is not string old || old.Length == 0 || text.Length == 0)
            return;
        Tick(tb);
    }

    private static void Tick(TextBlock tb)
    {
        if (tb.RenderTransform is not TranslateTransform slide)
            tb.RenderTransform = slide = new TranslateTransform();
        var duration = new Duration(TimeSpan.FromMilliseconds(DurationMs));
        tb.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(SlideUpPx, 0, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }
}
