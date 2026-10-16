using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace Brisk.Ui.Controls;

/// The window's background: the navy-turquoise gradient, the digital rain,
/// and the perspective grid floor from the mockup. Everything is built once;
/// motion is a translate per rain column and one shift of the floor plane,
/// started and stopped by the ambient clock. Decorated=false (the detail
/// pages) fades rain and floor out and stops them; the gradient stays.
public sealed class Atmosphere : Grid
{
    public const int RainColumns = 38;
    private const int GlyphsPerColumn = 26;
    private const double LineHeight = 14;
    private const string Glyphs = "01アイウエオカキクケコサシスセソ7392";

    private sealed record Column(TextBlock Text, TranslateTransform Fall, double LeftFraction, double Seconds, double Phase);

    private readonly AmbientClock _clock;
    private readonly Canvas _rain = new() { ClipToBounds = true };
    private readonly Viewport3D _floor = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TranslateTransform3D _floorShift = new();
    private readonly List<Column> _columns = new();

    public static readonly DependencyProperty DecoratedProperty = DependencyProperty.Register(nameof(Decorated),
        typeof(bool), typeof(Atmosphere), new PropertyMetadata(true, (d, e) => ((Atmosphere)d).OnDecorated((bool)e.NewValue)));

    public bool Decorated { get => (bool)GetValue(DecoratedProperty); set => SetValue(DecoratedProperty, value); }

    public bool MotionRunning { get; private set; }
    public int RainColumnCount => _columns.Count;

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
        Children.Add(_floor);
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
        _floor.BeginAnimation(OpacityProperty, fade);
        if (on) Register(); else _clock.Unregister(this);
    }

    private void BuildRain()
    {
        var random = new Random(17);
        var brush = new SolidColorBrush(Color.FromArgb(0x1C, 0x5F, 0xD4, 0xE8));
        brush.Freeze();
        var font = new FontFamily("Consolas");
        for (var c = 0; c < RainColumns; c++)
        {
            var glyphs = new char[GlyphsPerColumn * 2 - 1];
            for (var i = 0; i < GlyphsPerColumn; i++)
            {
                glyphs[i * 2] = Glyphs[random.Next(Glyphs.Length)];
                if (i < GlyphsPerColumn - 1) glyphs[i * 2 + 1] = '\n';
            }
            var fall = new TranslateTransform();
            var text = new TextBlock
            {
                Text = new string(glyphs),
                FontFamily = font,
                FontSize = 12,
                LineHeight = LineHeight,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Foreground = brush,
                RenderTransform = fall,
                CacheMode = new BitmapCache(),
            };
            _rain.Children.Add(text);
            _columns.Add(new Column(text, fall, (c * 2.7 + random.NextDouble()) / 100,
                7 + random.NextDouble() * 9, random.NextDouble()));
        }
    }

    private void BuildFloor()
    {
        const double Width = 50, Near = 6, Far = -44;
        // One bitmap of the whole grid, drawn once: a tiny tiled brush is
        // realized too coarsely in 3D and its lines vanish.
        const int Pixels = 1024, Cells = 50;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var line = new SolidColorBrush(Color.FromArgb(0x70, 0x5F, 0xD4, 0xE8));
            const double Step = (double)Pixels / Cells;
            for (var i = 0; i <= Cells; i++)
            {
                dc.DrawRectangle(line, null, new Rect(i * Step, 0, 1.6, Pixels));
                dc.DrawRectangle(line, null, new Rect(0, i * Step, Pixels, 1.6));
            }
        }
        var grid = new System.Windows.Media.Imaging.RenderTargetBitmap(Pixels, Pixels, 96, 96, PixelFormats.Pbgra32);
        grid.Render(visual);
        grid.Freeze();
        var brush = new ImageBrush(grid);
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
        _floor.Camera = new PerspectiveCamera(new Point3D(0, 1.4, 3), new Vector3D(0, -0.5, -1),
            new Vector3D(0, 1, 0), 75);
        _floor.OpacityMask = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(0, 0),
            GradientStops = { new GradientStop(Colors.Black, 0.3), new GradientStop(Colors.Transparent, 0.95) },
        };
    }

    private double ColumnHeight => GlyphsPerColumn * LineHeight;

    private void Arrange()
    {
        _floor.Height = Math.Max(1, ActualHeight * 0.48);
        foreach (var c in _columns)
        {
            Canvas.SetLeft(c.Text, c.LeftFraction * ActualWidth);
            // The still frame: each column where its fall would be.
            if (!MotionRunning) c.Fall.Y = -ColumnHeight + c.Phase * (ActualHeight + ColumnHeight);
        }
        if (MotionRunning)
        {
            Stop();
            Start();
        }
    }

    private void Start()
    {
        MotionRunning = true;
        var height = Math.Max(ActualHeight, 1);
        foreach (var c in _columns)
        {
            var fall = new DoubleAnimation(-ColumnHeight, height, TimeSpan.FromSeconds(c.Seconds))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(-c.Phase * c.Seconds),
            };
            c.Fall.BeginAnimation(TranslateTransform.YProperty, (AnimationTimeline)Ambient.Paced(fall));
        }
        _floorShift.BeginAnimation(TranslateTransform3D.OffsetZProperty, (AnimationTimeline)Ambient.Paced(
            new DoubleAnimation(0, 1, TimeSpan.FromSeconds(3.5)) { RepeatBehavior = RepeatBehavior.Forever }));
    }

    private void Stop()
    {
        MotionRunning = false;
        foreach (var c in _columns) c.Fall.BeginAnimation(TranslateTransform.YProperty, null);
        _floorShift.BeginAnimation(TranslateTransform3D.OffsetZProperty, null);
        foreach (var c in _columns) c.Fall.Y = -ColumnHeight + c.Phase * (Math.Max(ActualHeight, 1) + ColumnHeight);
    }
}
