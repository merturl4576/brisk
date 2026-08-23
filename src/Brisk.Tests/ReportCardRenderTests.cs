using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Diagnostics;
using Xunit;
// WinForms is on in this project, so bare Color is ambiguous.
using Color = System.Windows.Media.Color;

namespace Brisk.Tests;

/// The pixel side gets a smoke test, not a pixel test: the PNG exists, is a
/// PNG, and is card-sized. Everything about the card's CONTENT is pinned on
/// the model in ReportCardModelTests.
///
/// One thing beyond existence IS worth reading off the pixels. The ring is
/// the card's whole subject and it is the part with no text to assert on:
/// the first card this renderer produced came out with a full grey track and
/// no lit arc at all — the gauge's ignition animation never advances without
/// a dispatcher pumping frames — and it was a perfectly valid 312 KB PNG. So
/// the arc is counted, in the colour the score's band calls for.
public class ReportCardRenderTests
{
    /// The three lit-arc colours, straight from Theming/Shared.xaml.
    private static readonly Color Good = Color.FromRgb(0x4A, 0xDE, 0x80);
    private static readonly Color Crit = Color.FromRgb(0xF8, 0x71, 0x71);

    private static ReportCardModel Card(int health)
    {
        var loc = new Brisk.Localization.Loc();
        loc.SetLanguage("en");
        return ReportCardModel.Build(
            TestData.Snapshot(null, new SensorStatus(true, true, null))
                with { Health = health },
            Array.Empty<UndoableFix>(), loc);
    }

    private static string Render(ReportCardModel model)
    {
        var path = Path.Combine(
            Directory.CreateTempSubdirectory("brisk-card-").FullName, "card.png");
        ReportCardRenderer.RenderOnStaThread(model, path);
        return path;
    }

    /// How many pixels of the written PNG sit within a per-channel tolerance
    /// of one colour. Reads the file rather than the RenderTargetBitmap, so
    /// what is counted is what a reader would actually see.
    private static int PixelsNear(string path, Color target, int tolerance = 12)
    {
        BitmapSource frame;
        using (var stream = File.OpenRead(path))
            frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);

        var hits = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (Math.Abs(pixels[i + 2] - target.R) <= tolerance
                && Math.Abs(pixels[i + 1] - target.G) <= tolerance
                && Math.Abs(pixels[i] - target.B) <= tolerance)
                hits++;
        return hits;
    }

    [Fact]
    public void Render_WritesAValidPng()
    {
        var bytes = File.ReadAllBytes(Render(Card(95)));

        Assert.True(bytes.Length > 10_000, $"suspiciously small: {bytes.Length} bytes");
        // The eight-byte PNG signature.
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            bytes[..8]);
    }

    [Fact]
    public void Render_LightsTheRing()
    {
        var green = PixelsNear(Render(Card(95)), Good);

        // The lit arc at a healthy score is ~51 of 54 ticks, each a 5x16
        // stroke drawn at 2x — tens of thousands of pixels. The floor is set
        // an order of magnitude below that: it is here to catch a ring that
        // did not light at all, which is worth exactly zero green pixels.
        Assert.True(green > 3_000, $"the ring is not lit: {green} green pixels");
    }

    [Fact]
    public void Render_PaintsTheRingInTheScoresBand()
    {
        var healthy = Render(Card(95));
        var failing = Render(Card(35));

        // Same arc, different claim: a machine at 35 must not be posted in
        // the healthy green, and one at 95 must not be posted in the alarm
        // red. Each card carries its own colour and almost none of the other.
        Assert.True(PixelsNear(healthy, Good) > 3_000);
        Assert.True(PixelsNear(failing, Crit) > 3_000);
        Assert.True(PixelsNear(failing, Good) < 100,
            $"a failing card is wearing green: {PixelsNear(failing, Good)} pixels");
        Assert.True(PixelsNear(healthy, Crit) < 100,
            $"a healthy card is wearing red: {PixelsNear(healthy, Crit)} pixels");
    }
}
