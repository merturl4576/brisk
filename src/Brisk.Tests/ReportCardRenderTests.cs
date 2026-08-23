using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Xunit;
// WinForms is on in this project, so bare Color is ambiguous.
using Color = System.Windows.Media.Color;

namespace Brisk.Tests;

/// The pixel side gets a smoke test, not a pixel test: the PNG exists, is a
/// PNG, and is card-sized. Everything about the card's CONTENT is pinned on
/// the model in ReportCardModelTests.
///
/// Two things beyond existence ARE worth reading off the pixels, because they
/// are the parts with no text to assert on. The ring is one: the first card
/// this renderer produced came out with a full grey track and no lit arc at
/// all — the gauge's ignition animation never advances without a dispatcher
/// pumping frames — and it was a perfectly valid 312 KB PNG. The finding rows
/// are the other: they are a DataTemplate, and a template that silently
/// renders nothing looks exactly like a machine that had nothing to report.
public class ReportCardRenderTests
{
    /// Straight from Theming/Shared.xaml: the three lit-arc colours, and the
    /// ink the card writes its text in.
    private static readonly Color Good = Color.FromRgb(0x4A, 0xDE, 0x80);
    private static readonly Color Warn = Color.FromRgb(0xFB, 0xBF, 0x24);
    private static readonly Color Crit = Color.FromRgb(0xF8, 0x71, 0x71);
    private static readonly Color Ink = Color.FromRgb(0xF2, 0xF4, 0xF8);

    /// The headline lead column, in bitmap pixels: 48 padding + 300 gauge
    /// column + 36 margin puts it at card x 384, 200 wide, and the card is
    /// rendered at 2x. The quiet card writes almost no ink this far left.
    private const int LeadFromX = 384 * 2;
    private const int LeadToX = (384 + 200) * 2;

    private static Brisk.Localization.Loc English()
    {
        var loc = new Brisk.Localization.Loc();
        loc.SetLanguage("en");
        return loc;
    }

    private static Headline H(string value) => new(value, "cap",
        "rule.fake.headline.value", new[] { value },
        "rule.fake.headline.caption", Array.Empty<string>());

    /// A quiet machine: no findings, no fixes, both sensors answered.
    private static ReportCardModel Card(int health) => ReportCardModel.Build(
        TestData.Snapshot(null, new SensorStatus(true, true, null))
            with { Health = health },
        Array.Empty<UndoableFix>(), English());

    private static readonly UndoableFix[] ThreeFixes =
    {
        new("display-refresh", new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Utc)),
        new("startup-bloat", new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc)),
        new("power-plan", new DateTime(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc)),
    };

    private static List<DiagnosticFinding> FiveHeadlines() => new()
    {
        TestData.Finding("aa-fake", canFix: false, headline: H("57 s")),
        TestData.Finding("bb-fake", canFix: false, headline: H("60 Hz")),
        TestData.Finding("cc-fake", canFix: false, headline: H("13")),
        TestData.Finding("dd-fake", canFix: false, headline: H("12.4 GB")),
        TestData.Finding("ee-fake", canFix: false, headline: H("2133 MT/s")),
    };

    /// A busy machine, with and without the findings. Everything else about
    /// the two is identical — the same fixes, the same silent sensors — so
    /// the ink between them differs by the finding rows and nothing else.
    private static ReportCardModel FullCard(int health) =>
        CardWith(health, FiveHeadlines());

    private static ReportCardModel FixesOnlyCard(int health) =>
        CardWith(health, null);

    private static ReportCardModel CardWith(
        int health, IReadOnlyList<DiagnosticFinding>? findings) =>
        ReportCardModel.Build(
            TestData.Snapshot(findings, new SensorStatus(false, false, true))
                with { Health = health },
            ThreeFixes, English());

    private static string Render(ReportCardModel model)
    {
        var path = Path.Combine(
            Directory.CreateTempSubdirectory("brisk-card-").FullName, "card.png");
        ReportCardRenderer.RenderOnStaThread(model, path);
        return path;
    }

    /// How many pixels of the written PNG sit within a per-channel tolerance
    /// of one colour, optionally only inside a vertical band of it. Reads the
    /// file rather than the RenderTargetBitmap, so what is counted is what a
    /// reader would actually see.
    private static int PixelsNear(string path, Color target, int tolerance = 12,
        int fromX = 0, int toX = int.MaxValue)
    {
        BitmapSource frame;
        using (var stream = File.OpenRead(path))
            frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var width = bgra.PixelWidth;
        var stride = width * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);

        var hits = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var x = i / 4 % width;
            if (x < fromX || x >= toX) continue;
            if (Math.Abs(pixels[i + 2] - target.R) <= tolerance
                && Math.Abs(pixels[i + 1] - target.G) <= tolerance
                && Math.Abs(pixels[i] - target.B) <= tolerance)
                hits++;
        }
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
        var middling = Render(Card(72));
        var failing = Render(Card(35));

        var healthyGreen = PixelsNear(healthy, Good);
        var healthyRed = PixelsNear(healthy, Crit);
        var middlingAmber = PixelsNear(middling, Warn);
        var middlingGreen = PixelsNear(middling, Good);
        var failingRed = PixelsNear(failing, Crit);
        var failingGreen = PixelsNear(failing, Good);

        // Same arc, three claims. A machine at 35 must not be posted in the
        // healthy green, and neither must one at 72 — that band is the whole
        // reason the ring stopped being hardcoded.
        Assert.True(healthyGreen > 3_000, $"95 is not green: {healthyGreen}");
        Assert.True(middlingAmber > 3_000, $"72 is not amber: {middlingAmber}");
        Assert.True(failingRed > 3_000, $"35 is not red: {failingRed}");
        Assert.True(middlingGreen < 100, $"72 is wearing green: {middlingGreen}");
        Assert.True(failingGreen < 100, $"35 is wearing green: {failingGreen}");
        Assert.True(healthyRed < 100, $"95 is wearing red: {healthyRed}");
    }

    /// Everything else here renders the EMPTY card, where the findings
    /// template and the fixes block never run — a typo in {Binding Lead}
    /// would render blank leads and leave the whole suite green. This one
    /// renders the full card and weighs the ink in the lead column against
    /// the SAME card with the findings taken away. The unread sentence and
    /// the fix lines cross that column too, which is why the comparison is
    /// against that card and not against the quiet one: what is left of the
    /// difference is the five leads.
    [Fact]
    public void Render_DrawsTheFindingRows()
    {
        var full = Render(FullCard(72));
        var without = Render(FixesOnlyCard(72));

        var withLeads = PixelsNear(full, Ink, fromX: LeadFromX, toX: LeadToX);
        var withoutLeads = PixelsNear(without, Ink, fromX: LeadFromX, toX: LeadToX);
        var amber = PixelsNear(full, Warn);

        Assert.True(withLeads > withoutLeads + 3_000,
            $"the finding leads are missing: {withLeads} vs {withoutLeads} lit "
            + "pixels in the lead column");
        // The tall card is also the one where the centred column could clip,
        // so prove the ring beside it still rendered.
        Assert.True(amber > 3_000, $"the full card's ring is not lit: {amber}");
    }

    /// `brisk-app.exe report --out card.png`. Path.GetDirectoryName returns an
    /// empty string for a bare name and Directory.CreateDirectory("") throws
    /// on it, so this is both the likeliest thing a user types and the thing
    /// that used to fail — after the render had done all its work.
    [Fact]
    public void Render_AcceptsAPlainFilename()
    {
        var name = $"brisk-card-{Guid.NewGuid():N}.png";
        try
        {
            ReportCardRenderer.RenderOnStaThread(Card(95), name);

            Assert.True(File.Exists(name), $"nothing landed at {Path.GetFullPath(name)}");
            Assert.True(new FileInfo(name).Length > 10_000);
        }
        finally
        {
            File.Delete(name);
        }
    }
}
