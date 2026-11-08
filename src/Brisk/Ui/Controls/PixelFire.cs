using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Brisk.Ui.Controls;

/// The MAX tile's fire: the old Doom fire on a 42×15 grid of cells, drawn
/// into a tiny bitmap that WPF stretches without smoothing. The bottom row is
/// the source; each frame every cell passes its heat one row up, a little
/// cooler and maybe a column aside. At rest it burns low at 0.3; hot
/// (selected, focused or hovered) it burns taller at 0.85.
///
/// It ticks at most 20 times a second and only while the ambient clock runs
/// (window on screen, brisk's "Hareketli arka plan" on). Otherwise it is one
/// still frame, redrawn once when its heat changes (ruling R30).
public sealed class PixelFire : Image
{
    public const int Columns = 42, Rows = 15;
    public const int FrameRate = 20;
    public static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(1000.0 / FrameRate);
    public const double RestOpacity = 0.3, HotOpacity = 0.85;

    /// Mean heat lost per row: higher is shorter flames.
    private const double RestSpread = 4.6, HotSpread = 3.6;
    private const int WarmSteps = 30;
    private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(450);

    /// The mockup's palette, coolest first; heat 0 is no flame.
    private static readonly int[] Palette = new[] { 0 }.Concat(
        "1c0604 2e0806 450b07 5c0e08 75120a 8f170c a81d0e c2240f d93011 e84313 f15a16 f7721a fb8a1f fea226 ffb92f ffcf3d ffe15a"
            .Split(' ').Select(h => unchecked((int)0xFF000000) | Convert.ToInt32(h, 16))).ToArray();

    public static readonly DependencyProperty HotProperty = DependencyProperty.Register(nameof(Hot),
        typeof(bool), typeof(PixelFire), new PropertyMetadata(false, (d, _) => ((PixelFire)d).OnHot()));

    private readonly AmbientClock _clock;
    private readonly IUiTimers _timers;
    private readonly Random _random;
    private readonly byte[] _heat = new byte[Columns * Rows];
    private readonly int[] _pixels = new int[Columns * Rows];
    private readonly WriteableBitmap _bitmap = new(Columns, Rows, 96, 96, PixelFormats.Bgra32, null);
    private double _spread = RestSpread;
    private IDisposable? _tick;

    public PixelFire() : this(AmbientClock.Instance, new DispatcherUiTimers(), new Random()) { }

    public PixelFire(AmbientClock clock, IUiTimers timers, Random random)
    {
        _clock = clock;
        _timers = timers;
        _random = random;
        Source = _bitmap;
        Stretch = Stretch.Fill;
        IsHitTestVisible = false;
        Opacity = RestOpacity;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        for (var x = 0; x < Columns; x++) _heat[(Rows - 1) * Columns + x] = (byte)(Palette.Length - 1);
        // A warm start: the first frame already burns.
        for (var i = 0; i < WarmSteps; i++) Step(RestSpread);
        Draw();
        _clock.Register(this, Start, Stop);
        Loaded += (_, _) => _clock.Register(this, Start, Stop);
        Unloaded += (_, _) => _clock.Unregister(this);
    }

    public bool Hot { get => (bool)GetValue(HotProperty); set => SetValue(HotProperty, value); }

    public bool Running => _tick is not null;

    /// Frames drawn so far (the warm start is the first).
    public int Frames { get; private set; }

    /// Cells holding some flame.
    public int LitCells => _heat.Count(h => h > 0);

    private double Target => Hot ? HotSpread : RestSpread;

    private void Start() => _tick ??= _timers.Every(FrameInterval, Frame);

    private void Stop()
    {
        _tick?.Dispose();
        _tick = null;
        Still();
    }

    private void Frame()
    {
        Step(Target);
        Draw();
    }

    /// One still frame at the current heat, settled as the mockup's static frame is.
    private void Still()
    {
        _spread = Target;
        for (var i = 0; i < WarmSteps; i++) Step(_spread);
        Draw();
    }

    private void OnHot()
    {
        var to = Hot ? HotOpacity : RestOpacity;
        if (_clock.IsActive)
            BeginAnimation(OpacityProperty, (AnimationTimeline)Ambient.Paced(new DoubleAnimation(to, Fade)));
        else
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = to;
        }
        if (!Running) Still();
    }

    private void Step(double target)
    {
        _spread += (target - _spread) * .15;
        for (var x = 0; x < Columns; x++)
        for (var y = 1; y < Rows; y++)
        {
            var v = _heat[y * Columns + x];
            if (v == 0)
            {
                _heat[(y - 1) * Columns + x] = 0;
                continue;
            }
            var dx = Math.Clamp(x + (_random.Next(4) switch { 0 => -1, 3 => 1, _ => 0 }), 0, Columns - 1);
            _heat[(y - 1) * Columns + dx] = (byte)Math.Max(0, v - (int)(_random.NextDouble() * _spread));
        }
    }

    private void Draw()
    {
        for (var i = 0; i < _heat.Length; i++) _pixels[i] = Palette[_heat[i]];
        _bitmap.WritePixels(new Int32Rect(0, 0, Columns, Rows), _pixels, Columns * 4, 0);
        Frames++;
    }
}
