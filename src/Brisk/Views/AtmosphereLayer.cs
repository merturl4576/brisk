using System;
using System.Windows;
using System.Windows.Media;
using Brisk.Theming;

namespace Brisk.Views;

/// The ground the cockpit stands on: a vertical gradient, a faint field of
/// digital rain, a perspective grid floor and the glow at its horizon.
///
/// It is drawn ONCE PER WINDOW, not per page. That is the whole point of it
/// being its own element at the bottom of MainWindow's grid: navigating from
/// Sağlık to Performans never restarts or jumps the atmosphere, and a page
/// that wants depth under it pays nothing to get it.
///
/// It is also completely static. A full-window animated brush invalidates the
/// visual tree every frame, and the people who install a diagnostics tool are
/// the people whose machine is already slow — an animated background on that
/// machine argues against the product. The living motion in brisk stays where
/// it earns its cost: the small instrument arcs under AmbientMotionController.
public sealed class AtmosphereLayer : FrameworkElement
{
    // ------------------------------------------------------------------
    // The tuning surface, and it is SETTLED. These three opacities were
    // chosen from rendered images at the variant gate, not argued about —
    // the middle weight, with the horizon glow raised until it could
    // actually be seen.
    //
    // They are also a legibility budget, and THE BUDGET IS NOW SPENT.
    // BrightestComposite() composites exactly these, in exactly this order,
    // and ContrastTests refuses anything that pushes TextMuted under 4.5:1.
    // What they compose to is (20,44,61), which is 4.51:1 — seven
    // thousandths above the floor.
    //
    // So the rule the rest of the cockpit has to obey: NOTHING ELSE MAY ADD
    // LIGHT to anything BrightestComposite() accounts for. A lit panel edge,
    // a floor ellipse, any new decoration on the bare ground — each carries
    // its OWN contrast check rather than leaning on this one, because there
    // is no room left underneath it.
    //
    // And if headroom is ever genuinely needed, it is bought by lightening
    // TextMuted one step. NEVER by dimming the atmosphere back down: the
    // weight below is what the design was chosen at, and quietly returning
    // it would undo the decision instead of paying for the new thing.
    // ------------------------------------------------------------------

    /// How much of the texture colour a rain column adds to the sky.
    public const double RainOpacity = 0.04;

    /// How much of the sky the rain thins out over as it nears the
    /// horizon, as a fraction of the sky's height.
    ///
    /// Rain used to cross the horizon and fall down the floor, which
    /// contradicted the one thing the floor is there to establish — weather
    /// BEHIND a plane means it is not a plane. Cutting it off at the line
    /// fixed that and bought a straight edge of column-ends in exchange,
    /// which is precisely the defect the grid above had just been cured of;
    /// at four percent it is invisible until you raise the gain, and then
    /// it is a ruled line across the window. So the rain thins into the
    /// distance instead and reaches nothing exactly AT the horizon, which
    /// is both the cure and what distance does to weather.
    private const double RainFadeHeight = 0.25;

    /// The grid floor's line opacity. The spec's band was 8-12%, and the top
    /// of that band turns out not to be affordable — anything past 0.107
    /// fails the floor even with rain and glow left alone.
    public const double GridOpacity = 0.08;

    /// The horizon glow's opacity AT ITS CENTRE; it falls to nothing at the
    /// edge of the ellipse below.
    ///
    /// This is the most expensive number in the file: AccentGlow is a bright
    /// turquoise, so each 1% here costs about 0.11 of contrast ratio against
    /// the 0.05 that rain or grid costs. It opened at 0.03 and was RAISED,
    /// which is the opposite of what a budget argument would suggest, for a
    /// reason that only a render of the bare layer could show: at 0.03 the
    /// entire horizon was six levels of green across 1100 pixels — invisible,
    /// while still charging 0.28 of ratio for the privilege. A light at the
    /// end of the floor is the most cockpit-like thing in the concept, so it
    /// was made real rather than deleted. 0.045 is the ceiling that leaves
    /// rain and grid where they are.
    public const double GlowOpacity = 0.045;

    /// The glow ellipse's radii, as fractions of the window. Wide and flat —
    /// a horizon is a long light, not a lamp.
    public const double GlowRadiusX = 0.55;
    public const double GlowRadiusY = 0.20;

    /// Where the horizon sits, top-down. In the lower third, so the floor
    /// reads as floor rather than as a second sky.
    public const double HorizonFraction = 0.68;

    /// The rain tile, in device-independent units. Large and awkwardly
    /// proportioned on purpose: a small tile announces itself as a repeat
    /// long before the eye reads it as weather.
    private const double RainTileWidth = 240;
    private const double RainTileHeight = 420;
    private const double RainColumnWidth = 2;

    private const double GridLineThickness = 1;

    /// Lines parallel to the horizon, crowding together as they recede —
    /// which is a projection, not a look: a ground line at distance d lands
    /// at 1/d up the floor. GridDepthSpread is how far apart the lines are on
    /// the ground compared with how far the nearest one is from the viewer,
    /// and it is the only thing that decides how steeply they compress.
    ///
    /// 26, not the 16 it opened at — and the count was never the fix. At 16
    /// the farthest line landed 29px below the horizon on a 700px window
    /// with a bare band above it, which read as a hard edge across the
    /// floor at y≈504. No tuning ever moved it and none could have: tuning
    /// changes how LOUD a line is, and this was about where the lines stop.
    /// Raising the count does not close it either, it only moves it — the
    /// remaining gap shrinks as 1/k, so it would take some 248 lines to get
    /// under two pixels, and by then the far ones are packed several to a
    /// pixel and the edge has become a bright bar instead. Measured at 26
    /// with the pen fade alone, the edge was still there at a quarter of
    /// its old strength, in the same place it always was.
    ///
    /// What ends a series is the series ending at nothing, so DepthFadeAt
    /// below takes line k to zero at k = GridDepthLines. The farthest line
    /// is then invisible wherever it lands, whatever the count is, and the
    /// count goes back to being what it always claimed to be: how far the
    /// floor is drawn. 26 is where the lines are still individually
    /// resolvable for most of their run.
    private const int GridDepthLines = 26;
    private const double GridDepthSpread = 0.45;

    /// How far down the floor the pen reaches full strength, as a fraction
    /// of the floor height. It is atmospheric distance rather than the seam
    /// fix — DepthFadeAt is what ends the depth series — and it is the only
    /// thing that reaches the FAN lines, which used to arrive at the
    /// vanishing point at full weight and meet there in a star.
    ///
    /// It costs nothing against the legibility budget and cannot: fading
    /// is subtraction. See BrightestComposite.
    private const double GridFadeDepth = 0.35;

    /// Lines running away from the viewer, counted out from the centre. The
    /// outer ones leave through the side edges rather than the bottom, which
    /// is what makes the fan read as a plane instead of a star.
    private const int GridFanLines = 7;

    // The dark palette, repeated here as the defaults a layer paints with
    // when nothing has bound it — an AtmosphereLayer built in a test with no
    // theme in scope still has to draw, and still has to be measurable.
    // MainWindow binds the live theme keys over all four; AtmosphereLayerTests
    // pins these copies against Dark.xaml so the two cannot drift apart.
    private static readonly SolidColorBrush DefaultSky = Frozen(0x05, 0x0B, 0x16);      // Bg0
    private static readonly SolidColorBrush DefaultGround = Frozen(0x0A, 0x16, 0x26);   // Bg
    private static readonly SolidColorBrush DefaultTexture = Frozen(0x3A, 0x8F, 0xA3);  // AccentDim
    private static readonly SolidColorBrush DefaultGlow = Frozen(0x5F, 0xD4, 0xE8);     // AccentGlow

    /// Light theme: one flat fill and nothing else. Rain, grid and glow are
    /// light added to a dark room, and a light page has no dark room — so the
    /// same control renders flat rather than a second control existing.
    public static readonly DependencyProperty IsFlatProperty =
        DependencyProperty.Register(nameof(IsFlat), typeof(bool), typeof(AtmosphereLayer),
            new FrameworkPropertyMetadata(false,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// The gradient's top — Bg0, the deep the window falls away to.
    public static readonly DependencyProperty SkyBrushProperty =
        DependencyProperty.Register(nameof(SkyBrush), typeof(Brush), typeof(AtmosphereLayer),
            new FrameworkPropertyMetadata(DefaultSky,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// The gradient's bottom — Bg, the page ground; and the whole fill when
    /// the layer is flat.
    public static readonly DependencyProperty GroundBrushProperty =
        DependencyProperty.Register(nameof(GroundBrush), typeof(Brush), typeof(AtmosphereLayer),
            new FrameworkPropertyMetadata(DefaultGround,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// Rain and grid both — AccentDim, the accent's quiet voice, which is
    /// what decoration is allowed to speak in.
    public static readonly DependencyProperty TextureBrushProperty =
        DependencyProperty.Register(nameof(TextureBrush), typeof(Brush), typeof(AtmosphereLayer),
            new FrameworkPropertyMetadata(DefaultTexture,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// The horizon glow — AccentGlow, the one key that exists so a glow can
    /// be retuned, or switched off, without dragging the accent with it.
    public static readonly DependencyProperty GlowBrushProperty =
        DependencyProperty.Register(nameof(GlowBrush), typeof(Brush), typeof(AtmosphereLayer),
            new FrameworkPropertyMetadata(DefaultGlow,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public bool IsFlat
    {
        get => (bool)GetValue(IsFlatProperty);
        set => SetValue(IsFlatProperty, value);
    }

    public Brush? SkyBrush
    {
        get => (Brush?)GetValue(SkyBrushProperty);
        set => SetValue(SkyBrushProperty, value);
    }

    public Brush? GroundBrush
    {
        get => (Brush?)GetValue(GroundBrushProperty);
        set => SetValue(GroundBrushProperty, value);
    }

    public Brush? TextureBrush
    {
        get => (Brush?)GetValue(TextureBrushProperty);
        set => SetValue(TextureBrushProperty, value);
    }

    public Brush? GlowBrush
    {
        get => (Brush?)GetValue(GlowBrushProperty);
        set => SetValue(GlowBrushProperty, value);
    }

    /// The four colours OnRender paints with. Read through these, never off
    /// a brush directly, so that BrightestComposite() below is answering a
    /// question about the same numbers the render used.
    private Color SkyColor => ColorOf(SkyBrush);
    private Color GroundColor => ColorOf(GroundBrush);
    private Color TextureColor => ColorOf(TextureBrush);
    private Color GlowColor => ColorOf(GlowBrush);

    /// The brightest colour this layer can put behind a glyph — COMPUTED from
    /// the same constants and the same colours OnRender draws with, never
    /// sampled off a bitmap. Sampling would let the two drift the moment
    /// someone edits OnRender, and the contrast guarantee would quietly stop
    /// describing what is on screen.
    ///
    /// The spec names the worst case as the brightest rain texel over the
    /// brightest gradient stop. This goes one step further and stacks the
    /// grid line and the glow on top of that. It is an upper bound, not a
    /// pixel anyone can point at — no single pixel carries every maximum
    /// at once — which makes the test stricter than the drawing and never
    /// looser, and that is the direction a legibility guard should err.
    ///
    /// The bound got LOOSER when the seams were fixed, and the arithmetic
    /// was deliberately left alone. Rain now fades out before the horizon
    /// and the grid fades in after it, so rain and grid never share
    /// a pixel at all and the grid no longer reaches full strength where
    /// the glow is brightest. Both changes only SUBTRACT light.
    /// Re-deriving the stack against the new geometry would buy back a few
    /// thousandths of headroom nobody has asked for, and would trade a
    /// bound that is obviously safe for one that has to be argued.
    ///
    /// Always opaque — alpha means nothing to a contrast ratio.
    public Color BrightestComposite()
    {
        if (IsFlat) return Opaque(GroundColor);

        var stop = Contrast.RelativeLuminance(SkyColor) > Contrast.RelativeLuminance(GroundColor)
            ? SkyColor
            : GroundColor;
        var rain = Over(stop, TextureColor, RainOpacity);
        var grid = Over(rain, TextureColor, GridOpacity);
        return Over(grid, GlowColor, GlowOpacity);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        if (IsFlat)
        {
            dc.DrawRectangle(Solid(GroundColor), null, bounds);
            return;
        }

        DrawGradient(dc, bounds);
        DrawRain(dc, bounds);
        DrawGridFloor(dc, bounds);
        DrawHorizonGlow(dc, bounds);
    }

    private void DrawGradient(DrawingContext dc, Rect bounds)
    {
        var gradient = new LinearGradientBrush(
            SkyColor, GroundColor, new Point(0.5, 0), new Point(0.5, 1));
        gradient.Freeze();
        dc.DrawRectangle(gradient, null, bounds);
    }

    /// The rain falls in the SKY only, and arrives there rather than being
    /// cut off — see RainFadeHeight. One mask does both halves: it reaches
    /// zero at the horizon, and Pad keeps it at zero for the whole floor
    /// below, so there is nothing left to clip.
    private void DrawRain(DrawingContext dc, Rect bounds)
    {
        var horizon = HorizonOf(bounds);
        var mask = new LinearGradientBrush
        {
            StartPoint = new Point(0, horizon * (1 - RainFadeHeight)),
            EndPoint = new Point(0, horizon),
            MappingMode = BrushMappingMode.Absolute,
            GradientStops =
            {
                new GradientStop(Colors.White, 0),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1),
            },
        };
        mask.Freeze();

        dc.PushOpacityMask(mask);
        dc.PushOpacity(RainOpacity);
        dc.DrawRectangle(RainBrush(TextureColor), null, bounds);
        dc.Pop();
        dc.Pop();
    }

    /// The grid floor: a fan of lines running away from a vanishing point on
    /// the horizon, crossed by lines that crowd together as they recede.
    /// Clipped to the floor so nothing of it strays into the sky.
    private void DrawGridFloor(DrawingContext dc, Rect bounds)
    {
        var horizon = HorizonOf(bounds);
        var depth = bounds.Height - horizon;
        if (depth <= 0) return;

        var vanishing = new Point(bounds.Width / 2, horizon);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var i = -GridFanLines; i <= GridFanLines; i++)
            {
                ctx.BeginFigure(vanishing, isFilled: false, isClosed: false);
                ctx.LineTo(
                    new Point(vanishing.X + i * bounds.Width / GridFanLines, bounds.Height),
                    isStroked: true, isSmoothJoin: false);
            }
        }
        geometry.Freeze();

        var pen = new Pen(DepthFade(TextureColor, horizon, depth),
            GridLineThickness);
        pen.Freeze();
        dc.PushOpacity(GridOpacity);
        dc.PushClip(new RectangleGeometry(
            new Rect(0, horizon, bounds.Width, depth)));
        dc.DrawGeometry(null, pen, geometry);
        // The depth lines are drawn one at a time rather than into the fan's
        // geometry, because each carries its own place in the series and
        // the last one has to be drawn at nothing. k = 1 is the near edge
        // of the floor.
        for (var k = 1; k <= GridDepthLines; k++)
        {
            var y = horizon + depth / (1 + (k - 1) * GridDepthSpread);
            dc.PushOpacity(DepthFadeAt(k));
            dc.DrawLine(pen, new Point(0, y), new Point(bounds.Width, y));
            dc.Pop();
        }
        dc.Pop();
        dc.Pop();
    }

    /// How much of the pen the k-th depth line gets: all of it at the near
    /// edge of the floor, none of it at the far end. Squared rather than
    /// straight so the near half of the floor keeps the weight it was
    /// tuned at and the fade is spent where it is needed, on the far lines
    /// that pack together into a band.
    ///
    /// It never exceeds 1, so it cannot brighten anything, which is what
    /// keeps BrightestComposite an upper bound.
    private static double DepthFadeAt(int k)
    {
        var t = (k - 1) / (double)(GridDepthLines - 1);
        return 1 - t * t;
    }

    private void DrawHorizonGlow(DrawingContext dc, Rect bounds)
    {
        var horizon = HorizonOf(bounds);
        var radiusX = bounds.Width * GlowRadiusX;
        var radiusY = bounds.Height * GlowRadiusY;
        var glow = GlowColor;
        // The outer stop keeps the glow's OWN rgb and drops only its alpha.
        // Fading to Colors.Transparent instead would interpolate through
        // black and ring the glow with a dark halo.
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(glow, 0),
                new GradientStop(Color.FromArgb(0x60, glow.R, glow.G, glow.B), 0.45),
                new GradientStop(Color.FromArgb(0x00, glow.R, glow.G, glow.B), 1),
            },
        };
        brush.Freeze();

        dc.PushOpacity(GlowOpacity);
        dc.DrawRectangle(brush, null, new Rect(
            bounds.Width / 2 - radiusX, horizon - radiusY, radiusX * 2, radiusY * 2));
        dc.Pop();
    }

    /// The grid pen: the texture colour at the near edge of the floor,
    /// faded to nothing at the horizon. Absolute mapping, so the fade is
    /// anchored to the FLOOR and not to each line's own bounding box — a
    /// relative gradient on a one-pixel-tall horizontal line would paint
    /// every line one flat colour, which is no fade at all. The default
    /// Pad spread is the other half of it: everything below the fade band
    /// keeps the texture at full strength.
    private static Brush DepthFade(Color color, double horizon, double depth)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, horizon),
            EndPoint = new Point(0, horizon + depth * GridFadeDepth),
            MappingMode = BrushMappingMode.Absolute,
            GradientStops =
            {
                // The far stop keeps the texture's own rgb and drops only
                // its alpha, for the reason the glow does the same: fading
                // to Colors.Transparent interpolates through black.
                new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 0),
                new GradientStop(color, 1),
            },
        };
        brush.Freeze();
        return brush;
    }

    /// One expression, two callers: the glow has to sit on the SAME line the
    /// grid vanishes into, or the light is coming from somewhere the floor
    /// does not agree with.
    private static double HorizonOf(Rect bounds) => bounds.Height * HorizonFraction;

    /// The rain: short vertical columns on one tile, repeated. The table is
    /// written out rather than generated so that what ships is exactly what
    /// was looked at — (x, y, length), in tile units.
    private static readonly (double X, double Y, double Length)[] RainColumns =
    {
        (7, 26, 74), (7, 268, 40),
        (23, 150, 118),
        (38, 0, 58), (38, 322, 66),
        (56, 84, 34),
        (71, 12, 26), (71, 210, 96),
        (88, 118, 62),
        (103, 40, 44), (103, 288, 88),
        (120, 176, 30),
        (136, 60, 108), (136, 344, 52),
        (152, 232, 56),
        (169, 130, 78), (169, 380, 28),
        (184, 4, 36), (184, 254, 92),
        (201, 164, 46),
        (217, 92, 24), (217, 300, 70),
        (232, 202, 104), (232, 366, 38),
    };

    /// Frozen, so WPF realizes the tile once and reuses it for every repeat
    /// instead of re-rasterizing per composition pass.
    private static DrawingBrush RainBrush(Color color)
    {
        var columns = new GeometryGroup();
        foreach (var (x, y, length) in RainColumns)
            columns.Children.Add(new RectangleGeometry(
                new Rect(x, y, RainColumnWidth, length)));
        var tile = new Rect(0, 0, RainTileWidth, RainTileHeight);
        var brush = new DrawingBrush(new GeometryDrawing(Solid(color), null, columns))
        {
            TileMode = TileMode.Tile,
            // Absolute on both sides: the tile keeps its size whatever the
            // window does, and stays put when the table above is edited.
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = tile,
        };
        brush.Freeze();
        return brush;
    }

    /// A brush that is not a flat colour cannot be reasoned about here, and a
    /// null one paints nothing — both answer "adds no light", which is what
    /// Transparent means to Over() below.
    private static Color ColorOf(Brush? brush) =>
        brush is SolidColorBrush solid ? solid.Color : Colors.Transparent;

    private static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b) =>
        Solid(Color.FromRgb(r, g, b));

    private static Color Opaque(Color color) =>
        Color.FromRgb(color.R, color.G, color.B);

    /// Source-over, the way the compositor does it: the layer's own alpha
    /// times the opacity it was pushed at.
    private static Color Over(Color under, Color over, double opacity)
    {
        var alpha = Math.Clamp(opacity, 0, 1) * (over.A / 255.0);
        return Color.FromRgb(
            Mix(under.R, over.R, alpha),
            Mix(under.G, over.G, alpha),
            Mix(under.B, over.B, alpha));
    }

    private static byte Mix(byte under, byte over, double alpha) =>
        (byte)Math.Round(alpha * over + (1 - alpha) * under);
}
