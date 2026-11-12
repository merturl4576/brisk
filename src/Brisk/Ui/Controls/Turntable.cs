using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Brisk.Ui.Controls;

/// The gauge's rings: everything inside turns about its centre to Angle,
/// over one second at 20 frames a second (the mockup's 120° turn when a
/// satellite comes to the front), or at once with reduced motion.
public sealed class Turntable : Grid
{
    public static readonly TimeSpan Turn = TimeSpan.FromSeconds(1);

    private readonly RotateTransform _rotate = new();

    public static readonly DependencyProperty AngleProperty = DependencyProperty.Register(nameof(Angle),
        typeof(double), typeof(Turntable), new PropertyMetadata(0.0, (d, e) => ((Turntable)d).TurnTo((double)e.NewValue)));

    public double Angle { get => (double)GetValue(AngleProperty); set => SetValue(AngleProperty, value); }

    /// Where the rings stand now, mid-turn included.
    public double ShownAngle => _rotate.Angle;

    public Turntable()
    {
        RenderTransform = _rotate;
        RenderTransformOrigin = new Point(0.5, 0.5);
    }

    private void TurnTo(double angle)
    {
        var from = _rotate.Angle;
        _rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        if (!Ambient.Moving)
        {
            _rotate.Angle = angle;
            return;
        }
        _rotate.BeginAnimation(RotateTransform.AngleProperty, (AnimationTimeline)Ambient.Paced(
            new DoubleAnimation(from, angle, Turn) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } }));
    }

    /// A still frame shows where the turn lands.
    public void Settle()
    {
        _rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        _rotate.Angle = Angle;
    }
}
