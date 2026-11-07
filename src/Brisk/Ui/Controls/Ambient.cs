using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Brisk.Ui.Controls;

/// Busy: a small spinner's quick turn (a nav badge waiting for the look);
/// with motion off it stands still as an open arc.
public enum AmbientMotion { None, Spin, SpinReverse, Flow, Pulse, Busy }

/// Decorative motion as an attached property: `Ambient.Motion="Spin"` on an
/// element registers it with the ambient clock while it is loaded. A frame
/// costs one property update on a cached layer — nothing is laid out or
/// redrawn.
public static class Ambient
{
    public const int FrameRate = 20;

    public static readonly DependencyProperty MotionProperty = DependencyProperty.RegisterAttached(
        "Motion", typeof(AmbientMotion), typeof(Ambient), new PropertyMetadata(AmbientMotion.None, OnMotionChanged));

    public static AmbientMotion GetMotion(DependencyObject d) => (AmbientMotion)d.GetValue(MotionProperty);
    public static void SetMotion(DependencyObject d, AmbientMotion value) => d.SetValue(MotionProperty, value);

    private static void OnMotionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.OldValue is not AmbientMotion.None) return;
        element.Loaded += (_, _) => Attach(element, AmbientClock.Instance);
        element.Unloaded += (_, _) => AmbientClock.Instance.Unregister(element);
    }

    /// Registers the element's motion with `clock` now (the Loaded handler
    /// does this with the app's clock).
    public static void Attach(FrameworkElement element, AmbientClock clock) =>
        clock.Register(element, () => Start(element), () => Stop(element));

    public static Timeline Paced(Timeline t)
    {
        Timeline.SetDesiredFrameRate(t, FrameRate);
        return t;
    }

    private static void Start(FrameworkElement element)
    {
        switch (GetMotion(element))
        {
            case AmbientMotion.Spin:
            case AmbientMotion.SpinReverse:
            case AmbientMotion.Busy:
            {
                var motion = GetMotion(element);
                var reverse = motion == AmbientMotion.SpinReverse;
                var turn = motion == AmbientMotion.Busy ? 1.2 : reverse ? 17 : 11;
                if (element.RenderTransform is not RotateTransform rotate)
                {
                    rotate = new RotateTransform();
                    element.RenderTransform = rotate;
                    element.RenderTransformOrigin = new Point(0.5, 0.5);
                }
                element.CacheMode ??= new BitmapCache();
                rotate.BeginAnimation(RotateTransform.AngleProperty, (AnimationTimeline)Paced(new DoubleAnimation(
                    reverse ? 360 : 0, reverse ? 0 : 360, TimeSpan.FromSeconds(turn))
                    { RepeatBehavior = RepeatBehavior.Forever }));
                break;
            }
            case AmbientMotion.Flow when element is Shape shape:
                // Dash lengths are in multiples of the stroke: 32 px a pass.
                shape.BeginAnimation(Shape.StrokeDashOffsetProperty, (AnimationTimeline)Paced(
                    new DoubleAnimation(0, -32 / Math.Max(1, shape.StrokeThickness), TimeSpan.FromSeconds(1.6))
                    { RepeatBehavior = RepeatBehavior.Forever }));
                break;
            case AmbientMotion.Pulse:
            {
                element.CacheMode ??= new BitmapCache();
                // A slow breath: ten frames a second is plenty.
                var pulse = new DoubleAnimation(0.35, 0.6, TimeSpan.FromSeconds(1.3))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(pulse, 10);
                element.BeginAnimation(UIElement.OpacityProperty, pulse);
                break;
            }
        }
    }

    private static void Stop(FrameworkElement element)
    {
        switch (GetMotion(element))
        {
            case AmbientMotion.Spin:
            case AmbientMotion.SpinReverse:
            case AmbientMotion.Busy:
                (element.RenderTransform as RotateTransform)?.BeginAnimation(RotateTransform.AngleProperty, null);
                break;
            case AmbientMotion.Flow when element is Shape shape:
                shape.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
                break;
            case AmbientMotion.Pulse:
                element.BeginAnimation(UIElement.OpacityProperty, null);
                break;
        }
    }
}
