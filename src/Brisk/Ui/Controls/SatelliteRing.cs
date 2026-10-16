using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Brisk.Ui.Controls;

/// One of the three floor satellites: a value, a caption, and a glowing ring
/// lying on the grid floor. Done turns it green and stops the pulse.
public sealed class SatelliteRing : StackPanel
{
    private static readonly SolidColorBrush Neutral = Frozen(Color.FromRgb(0xE6, 0xF4, 0xF8));
    private static readonly SolidColorBrush Green = Frozen(Color.FromRgb(0x3E, 0xCF, 0x8E));
    private static readonly SolidColorBrush Ring = Frozen(Color.FromArgb(0xB3, 0x5F, 0xD4, 0xE8));
    private static readonly SolidColorBrush Muted = Frozen(Color.FromRgb(0x7F, 0xA3, 0xB1));

    private readonly TextBlock _value = new() { FontSize = 18, FontWeight = FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _caption = new() { FontSize = 12, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Ellipse _ring = new() { StrokeThickness = 2, Stroke = Ring };
    private readonly Ellipse _glow = new() { StrokeThickness = 6, Stroke = Ring, Effect = new BlurEffect { Radius = 10 }, Opacity = 0.35 };

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value),
        typeof(string), typeof(SatelliteRing), new PropertyMetadata("", (d, e) => ((SatelliteRing)d)._value.Text = (string)e.NewValue));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(nameof(Caption),
        typeof(string), typeof(SatelliteRing), new PropertyMetadata("", (d, e) => ((SatelliteRing)d)._caption.Text = (string)e.NewValue));

    public static readonly DependencyProperty DoneProperty = DependencyProperty.Register(nameof(Done),
        typeof(bool), typeof(SatelliteRing), new PropertyMetadata(false, (d, _) => ((SatelliteRing)d).Paint()));

    public static readonly DependencyProperty RingWidthProperty = DependencyProperty.Register(nameof(RingWidth),
        typeof(double), typeof(SatelliteRing), new PropertyMetadata(96.0, (d, _) => ((SatelliteRing)d).Size()));

    public static readonly DependencyProperty RingHeightProperty = DependencyProperty.Register(nameof(RingHeight),
        typeof(double), typeof(SatelliteRing), new PropertyMetadata(28.0, (d, _) => ((SatelliteRing)d).Size()));

    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public bool Done { get => (bool)GetValue(DoneProperty); set => SetValue(DoneProperty, value); }
    public double RingWidth { get => (double)GetValue(RingWidthProperty); set => SetValue(RingWidthProperty, value); }
    public double RingHeight { get => (double)GetValue(RingHeightProperty); set => SetValue(RingHeightProperty, value); }

    public Brush ValueBrush => _value.Foreground;

    public SatelliteRing()
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        var rings = new Grid { Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        rings.Children.Add(_glow);
        rings.Children.Add(_ring);
        Children.Add(_value);
        Children.Add(_caption);
        Children.Add(rings);
        Ambient.SetMotion(_glow, AmbientMotion.Pulse);
        Size();
        Paint();
    }

    private void Size()
    {
        foreach (var e in new[] { _ring, _glow })
        {
            e.Width = RingWidth;
            e.Height = RingHeight;
        }
    }

    private void Paint()
    {
        _value.Foreground = Done ? Green : Neutral;
        _ring.Stroke = Done ? Green : Ring;
        _glow.Stroke = Done ? Green : Ring;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
