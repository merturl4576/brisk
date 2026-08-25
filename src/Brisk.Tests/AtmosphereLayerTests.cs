using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brisk.Views;
using Xunit;
// WinForms is on in this project, so bare Color, Point and Size are
// ambiguous.
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Brisk.Tests;

/// The atmosphere is judged by eye from .snapshots, but two claims about it
/// are not matters of taste and are asserted here instead: the light theme
/// really does collapse to one flat fill, and the palette the layer falls
/// back to really is Dark.xaml's.
public class AtmosphereLayerTests
{
    private static readonly Size Frame = new(400, 300);

    /// Settled decision 1: the cockpit's STRUCTURE is shared between themes,
    /// the atmosphere is a dark-theme thing. "Flat" has to mean flat — one
    /// colour, not a nearly-flat one with a rain column still in it — because
    /// a light page has no night for the texture to sit on.
    ///
    /// And the fill has to BE there. Counting colours alone cannot say so: a
    /// RenderTargetBitmap starts out transparent, and an untouched buffer is
    /// also "exactly one colour". Deleting the flat branch's DrawRectangle
    /// left every one of the suite's 845 tests green — this is the only
    /// automated statement of the light-theme contract, so it asserts the
    /// pixel's VALUE, not just that there is one of it.
    [Fact]
    public void Flat_RendersExactlyOneColour_AndItIsTheGroundFill()
    {
        Color ground = default;
        var pixels = RenderedPixels(() =>
        {
            var layer = new AtmosphereLayer { IsFlat = true };
            ground = ColorOf(layer.GroundBrush);
            return layer;
        });

        Assert.True(pixels.Count == 1,
            $"the flat atmosphere rendered {pixels.Count} distinct colours — " +
            "flat has to mean flat, with no rain column left in it");
        var only = pixels.Single();
        Assert.True(only == Pbgra32(ground),
            $"the flat atmosphere rendered 0x{only:X8}, not the opaque Bg fill " +
            $"0x{Pbgra32(ground):X8} — and 0x00000000, an empty buffer, is " +
            "'exactly one colour' too, so a layer that drew nothing at all " +
            "would satisfy the count on its own");
    }

    /// And the other half of the same claim: with the atmosphere on, there is
    /// genuinely something there. A gradient alone would already beat a flat
    /// fill, so the bar is set well above "a few bands".
    [Fact]
    public void NotFlat_RendersTheWholeAtmosphere()
    {
        var colors = RenderedPixels(() => new AtmosphereLayer { IsFlat = false }).Count;

        Assert.True(colors > 64,
            $"the atmosphere rendered {colors} distinct colours — gradient, " +
            "rain, grid and glow together cannot be that few");
    }

    /// The layer carries its own copy of the dark palette for the case where
    /// nothing has bound it — and ContrastTests measures that copy. A copy
    /// that drifted from Dark.xaml would leave the legibility guarantee
    /// describing a palette the app no longer wears, which is exactly the
    /// drifted-duplicate defect this repo has been bitten by before.
    [Fact]
    public void TheFallbackPalette_MatchesDarkXaml()
    {
        var painted = new Dictionary<string, Color>();
        OnStaThread(() =>
        {
            var layer = new AtmosphereLayer();
            painted["Bg0"] = ColorOf(layer.SkyBrush);
            painted["Bg"] = ColorOf(layer.GroundBrush);
            painted["AccentDim"] = ColorOf(layer.TextureBrush);
            painted["AccentGlow"] = ColorOf(layer.GlowBrush);
        });

        foreach (var (key, color) in painted)
            Assert.Equal(ThemeSource.ColorOf("Dark.xaml", key), color);
    }

    private static Color ColorOf(System.Windows.Media.Brush? brush) =>
        ((SolidColorBrush)brush!).Color;

    /// Renders the layer offscreen and returns the distinct pixel values it
    /// produced — the values, not their count, because what a flat render
    /// drew matters as much as how many of it there were. WPF objects demand
    /// an STA thread and the test runner does not have one — the same reason
    /// ReportCardRenderer.RenderOnStaThread exists. The layer is built on that
    /// thread too: a DispatcherObject belongs to whichever thread made it, and
    /// rendering one from another throws.
    private static HashSet<int> RenderedPixels(Func<AtmosphereLayer> build)
    {
        var distinct = new HashSet<int>();
        OnStaThread(() =>
        {
            var layer = build();
            layer.Measure(Frame);
            layer.Arrange(new Rect(new Point(0, 0), Frame));
            layer.UpdateLayout();

            var bitmap = new RenderTargetBitmap(
                (int)Frame.Width, (int)Frame.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(layer);
            var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            distinct = new HashSet<int>(pixels);
        });
        return distinct;
    }

    /// What CopyPixels hands back for one opaque colour: Pbgra32 is B,G,R,A in
    /// memory, so a little-endian int32 reads as 0xAARRGGBB. At alpha 255 the
    /// premultiplied channels are the straight ones.
    private static int Pbgra32(Color color) => unchecked((int)(
        0xFF000000u | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B));

    private static void OnStaThread(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { work(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
