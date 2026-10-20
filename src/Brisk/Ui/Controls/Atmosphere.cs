using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace Brisk.Ui.Controls;

/// The window's background: the navy-turquoise gradient, the digital rain,
/// and the perspective grid floor from the mockup. Everything is built once.
/// Motion is four translates (the rain falls in four lanes of different
/// speed, each lane a loop of its columns) and one shift of the floor plane,
/// started and stopped by the ambient clock — measured, one animation per
/// column cost a quarter of a CPU core. Decorated=false (the detail pages)
/// fades rain and floor out and stops them; the gradient stays.
public sealed class Atmosphere : Grid
{
    public const int RainColumns = 38;
    private const int GlyphsPerColumn = 26;
    private const double LineHeight = 14;
    private const string Glyphs = "01アイウエオカキクケコサシスセソ7392";

    public const int RainLanes = 4;
    private static readonly double[] LaneSeconds = { 8, 10.5, 13, 15.5 };
    private const int RainFrameRate = 20, FloorFrameRate = 15;

    /// A column sits in its lane twice, one period apart: when the lane has
    /// fallen one period and jumps back, the copy is exactly where the column
    /// was, so the loop has no seam.
    private sealed record Column(TextBlock Text, TextBlock Copy, double LeftFraction, double Phase);

    private sealed class Lane
    {
        public readonly Canvas Panel = new();
        public readonly TranslateTransform Fall = new();
        public readonly List<Column> Columns = new();
        public double Seconds;
    }

    private readonly AmbientClock _clock;
    private readonly Canvas _rain = new() { ClipToBounds = true };
    private readonly Viewport3D _floor = new() { IsHitTestVisible = false };
    // The fade to the horizon is on this host: an opacity mask set on the
    // Viewport3D itself blanks the whole scene.
    private readonly Grid _floorHost = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TranslateTransform3D _floorShift = new();
    private readonly List<Lane> _lanes = new();

    public static readonly DependencyProperty DecoratedProperty = DependencyProperty.Register(nameof(Decorated),
        typeof(bool), typeof(Atmosphere), new PropertyMetadata(true, (d, e) => ((Atmosphere)d).OnDecorated((bool)e.NewValue)));

    public bool Decorated { get => (bool)GetValue(DecoratedProperty); set => SetValue(DecoratedProperty, value); }

    public bool MotionRunning { get; private set; }
    public int RainColumnCount => _lanes.Sum(l => l.Columns.Count);

    public Atmosphere() : this(AmbientClock.Instance) { }

    public Atmosphere(AmbientClock clock)
    {
        _clock = clock;
        IsHitTestVisible = false;
        var background = new Rectangle();
        background.SetResourceReference(Shape.FillProperty, "Ui.Background");
        Children.Add(background);
        BuildRain();
        Children.Add(_rain);
        BuildFloor();
        _floorHost.Children.Add(_floor);
        Children.Add(_floorHost);
        SizeChanged += (_, _) => Arrange();
        Loaded += (_, _) => { if (Decorated) Register(); };
        Unloaded += (_, _) => _clock.Unregister(this);
        Register();
    }

    private void Register() => _clock.Register(this, Start, Stop);

    private void OnDecorated(bool on)
    {
        var fade = new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(300));
        _rain.BeginAnimation(OpacityProperty, fade);
        _floorHost.BeginAnimation(OpacityProperty, fade);
        if (on) Register(); else _clock.Unregister(this);
    }

    /// A still frame: the fade is where it lands.
    public void Settle()
    {
        foreach (var layer in new UIElement[] { _rain, _floorHost })
        {
            layer.BeginAnimation(OpacityProperty, null);
            layer.Opacity = Decorated ? 1 : 0;
        }
    }

    private void BuildRain()
    {
        var random = new Random(17);
        var brush = new SolidColorBrush(Color.FromArgb(0x1C, 0x5F, 0xD4, 0xE8));
        brush.Freeze();
        var font = new FontFamily("Consolas");
        for (var l = 0; l < RainLanes; l++)
        {
            var lane = new Lane { Seconds = LaneSeconds[l] };
            lane.Panel.RenderTransform = lane.Fall;
            _lanes.Add(lane);
            _rain.Children.Add(lane.Panel);
        }
        TextBlock MakeText(string text) => new()
        {
            Text = text,
            FontFamily = font,
            FontSize = 12,
            LineHeight = LineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = brush,
            CacheMode = new BitmapCache(),
        };
        for (var c = 0; c < RainColumns; c++)
        {
            var glyphs = new char[GlyphsPerColumn * 2 - 1];
            for (var i = 0; i < GlyphsPerColumn; i++)
            {
                glyphs[i * 2] = Glyphs[random.Next(Glyphs.Length)];
                if (i < GlyphsPerColumn - 1) glyphs[i * 2 + 1] = '\n';
            }
            var text = new string(glyphs);
            var lane = _lanes[random.Next(RainLanes)];
            var column = new Column(MakeText(text), MakeText(text), (c * 2.7 + random.NextDouble()) / 100, random.NextDouble());
            lane.Panel.Children.Add(column.Text);
            lane.Panel.Children.Add(column.Copy);
            lane.Columns.Add(column);
        }
    }

    private void BuildFloor()
    {
        // One unit = one grid cell. The plane is far larger than the view so
        // its edges never show; the shift below moves it one cell and loops.
        const double Width = 160, Near = 8, Far = -150;
        // One cell drawn once as a small bitmap, tiled across the plane.
        const int Cell = 64;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var line = new SolidColorBrush(Color.FromArgb(0x48, 0x5F, 0xD4, 0xE8));
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Cell, Cell));
            dc.DrawRectangle(line, null, new Rect(0, 0, Cell, 2));
            dc.DrawRectangle(line, null, new Rect(0, 0, 2, Cell));
        }
        var tile = new System.Windows.Media.Imaging.RenderTargetBitmap(Cell, Cell, 96, 96, PixelFormats.Pbgra32);
        tile.Render(visual);
        tile.Freeze();
        var brush = new ImageBrush(tile)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 1 / Width, 1 / (Near - Far)),
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.Freeze();

        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                new(-Width / 2, 0, Near), new(Width / 2, 0, Near), new(Width / 2, 0, Far), new(-Width / 2, 0, Far),
            },
            TextureCoordinates = new PointCollection { new(0, 1), new(1, 1), new(1, 0), new(0, 0) },
            TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 },
        };
        var material = new DiffuseMaterial(brush);
        var plane = new GeometryModel3D(mesh, material) { BackMaterial = material, Transform = _floorShift };
        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Colors.White));
        group.Children.Add(plane);
        _floor.Children.Add(new ModelVisual3D { Content = group });
        // Looking down the floor at the mockup's 62° tilt (28° below the horizon).
        _floor.Camera = new PerspectiveCamera(new Point3D(0, 9, 6), new Vector3D(0, -0.53, -1),
            new Vector3D(0, 1, 0), 70);
        _floorHost.OpacityMask = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(0, 0),
            GradientStops = { new GradientStop(Colors.Black, 0.3), new GradientStop(Colors.Transparent, 0.95) },
        };
    }

    private double ColumnHeight => GlyphsPerColumn * LineHeight;

    /// One loop of a lane: the window's height plus a column's.
    private double Period => Math.Max(ActualHeight, 1) + ColumnHeight;

    private void Arrange()
    {
        _floorHost.Height = Math.Max(1, ActualHeight * 0.48);
        foreach (var lane in _lanes)
        foreach (var c in lane.Columns)
        {
            Canvas.SetLeft(c.Text, c.LeftFraction * ActualWidth);
            Canvas.SetLeft(c.Copy, c.LeftFraction * ActualWidth);
            Canvas.SetTop(c.Text, c.Phase * Period - ColumnHeight);
            Canvas.SetTop(c.Copy, c.Phase * Period - ColumnHeight - Period);
        }
        if (MotionRunning)
        {
            Stop();
            Start();
        }
        else Rest();
    }

    /// The still frame: every lane halfway through its loop.
    private void Rest()
    {
        foreach (var lane in _lanes) lane.Fall.Y = Period / 2;
    }

    private void Start()
    {
        MotionRunning = true;
        foreach (var lane in _lanes)
        {
            var fall = new DoubleAnimation(0, Period, TimeSpan.FromSeconds(lane.Seconds)) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(fall, RainFrameRate);
            lane.Fall.BeginAnimation(TranslateTransform.YProperty, fall);
        }
        var shift = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(3.5)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(shift, FloorFrameRate);
        _floorShift.BeginAnimation(TranslateTransform3D.OffsetZProperty, shift);
    }

    private void Stop()
    {
        MotionRunning = false;
        foreach (var lane in _lanes) lane.Fall.BeginAnimation(TranslateTransform.YProperty, null);
        _floorShift.BeginAnimation(TranslateTransform3D.OffsetZProperty, null);
        Rest();
    }
}
