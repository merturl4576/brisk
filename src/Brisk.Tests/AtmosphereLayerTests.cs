using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Brisk.Views;
using Xunit;
// WinForms is on in this project, so bare Color, ColorConverter, Point and
// Size are ambiguous.
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
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
    [Fact]
    public void Flat_RendersExactlyOneColour()
    {
        var colors = DistinctColors(() => new AtmosphereLayer { IsFlat = true });

        Assert.Equal(1, colors);
    }

    /// And the other half of the same claim: with the atmosphere on, there is
    /// genuinely something there. A gradient alone would already beat a flat
    /// fill, so the bar is set well above "a few bands".
    [Fact]
    public void NotFlat_RendersTheWholeAtmosphere()
    {
        var colors = DistinctColors(() => new AtmosphereLayer { IsFlat = false });

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
            Assert.Equal(DarkThemeColor(key), color);
    }

    private static Color ColorOf(System.Windows.Media.Brush? brush) =>
        ((SolidColorBrush)brush!).Color;

    /// Renders the layer offscreen and counts what came out. WPF objects
    /// demand an STA thread and the test runner does not have one — the same
    /// reason ReportCardRenderer.RenderOnStaThread exists. The layer is built
    /// on that thread too: a DispatcherObject belongs to whichever thread
    /// made it, and rendering one from another throws.
    private static int DistinctColors(Func<AtmosphereLayer> build)
    {
        var count = 0;
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
            count = new HashSet<int>(pixels).Count;
        });
        return count;
    }

    /// Reads the value straight out of the .xaml source, the way
    /// ThemeDictionaryTests does — no Application or pack-URI plumbing.
    private static Color DarkThemeColor(string key)
    {
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "brisk.sln"))) continue;
            var hex = XDocument.Load(
                    Path.Combine(dir.FullName, "src", "Brisk", "Theming", "Dark.xaml")).Root!
                .Elements().Single(e => (string?)e.Attribute(x + "Key") == key)
                .Attribute("Color")!.Value;
            return (Color)ColorConverter.ConvertFromString(hex)!;
        }
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }

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
