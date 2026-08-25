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

    /// Park a ticking numeral at the value it is travelling towards.
    ///
    /// A still frame wants the resting state, not a frame of the journey —
    /// the same rule SegmentedGauge's ignition sweep follows, and for the
    /// same reason: offscreen there is no frame loop, so whatever moment the
    /// shutter catches is arbitrary. Here it is arbitrary AND dim, because a
    /// 170 ms fade photographed at 16 ms is a numeral at a quarter opacity.
    ///
    /// Dropping the clocks is all it takes. Both animations run TO the
    /// property's base value — opacity 0 to 1 where the base is 1, slide 5 to
    /// 0 where a fresh TranslateTransform is already 0 — so removing them
    /// reverts to exactly where the tick was going to land. Nothing has to be
    /// assigned, which also means nothing here can disagree with Tick below.
    internal static void Settle(TextBlock tb)
    {
        tb.BeginAnimation(UIElement.OpacityProperty, null);
        if (tb.RenderTransform is TranslateTransform slide)
            slide.BeginAnimation(TranslateTransform.YProperty, null);
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
