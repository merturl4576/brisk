using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brisk.Services;
using Brisk.ViewModels;
using Brisk.Views;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Xunit;
// WinForms is on in this project, so bare Color is ambiguous.
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;

namespace Brisk.Tests;

/// The pixel side gets a smoke test, not a pixel test: the PNG exists, is a
/// PNG, and is card-sized. What the card SAYS is pinned on the model in
/// ReportCardModelTests, and everything here is about what the model cannot
/// answer for.
///
/// THREE things are worth reading off the pixels, because they are the parts
/// with no text to assert on. The ring is the first: the first card this
/// renderer produced came out with a full grey track and no lit arc at all —
/// the gauge's ignition animation never advances without a dispatcher pumping
/// frames — and it was a perfectly valid 312 KB PNG. The finding rows are the
/// second: they are a DataTemplate, and a template that silently renders
/// nothing looks exactly like a machine that had nothing to report. The
/// numeral's ink is the third, added by bbc28ea: HeroScore's triggers paint
/// the score in its band colour, which is right in the cockpit and wrong
/// inside a ring that already carries the band, and the only place that shows
/// is the pixels.
///
/// TWO more are read off the laid-out CONTROL rather than off the pixels, and
/// neither has a model test that could see it. What the column asks for
/// against what the Grid gives it is one — a clipped card and a card that fits
/// look equally tidy, so no pixel count can tell them apart. The findings'
/// overflow line is the other, and the only one of the five this wave added:
/// its text and its collapse trigger bind the same model property, and a
/// misspelled path there renders an empty row rather than failing, which is a
/// defect the model is incapable of having.
///
/// Five jobs, then, and the count is written down because the last two
/// versions of this paragraph each enumerated a set that had already grown.
public class ReportCardRenderTests
{
    /// Straight from Theming/Shared.xaml: the three lit-arc colours, and the
    /// ink the card writes its text in.
    private static readonly Color Good = Color.FromRgb(0x4A, 0xDE, 0x80);
    private static readonly Color Notice = Color.FromRgb(0x22, 0xD3, 0xEE);
    private static readonly Color Warn = Color.FromRgb(0xFB, 0xBF, 0x24);
    private static readonly Color Crit = Color.FromRgb(0xF8, 0x71, 0x71);
    private static readonly Color Ink = Color.FromRgb(0xF2, 0xF4, 0xF8);

    /// The headline lead column, in bitmap pixels: 48 padding + 300 gauge
    /// column + 36 margin puts it at card x 384, 200 wide, and the card is
    /// rendered at 2x. The quiet card writes almost no ink this far left.
    private const int LeadFromX = 384 * 2;
    private const int LeadToX = (384 + 200) * 2;

    /// A box around the numeral in the middle of the gauge, well inside the
    /// ring's arc and clear of the "brisk" wordmark up in the top strip.
    private const int NumeralFromX = 140 * 2;
    private const int NumeralToX = 260 * 2;
    private const int NumeralFromY = 400 * 2;
    private const int NumeralToY = 520 * 2;

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
        int fromX = 0, int toX = int.MaxValue, int fromY = 0, int toY = int.MaxValue)
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
            var y = i / 4 / width;
            if (x < fromX || x >= toX || y < fromY || y >= toY) continue;
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
        var middlingNotice = PixelsNear(middling, Notice);
        var middlingAmber = PixelsNear(middling, Warn);
        var middlingGreen = PixelsNear(middling, Good);
        var failingRed = PixelsNear(failing, Crit);
        var failingGreen = PixelsNear(failing, Good);

        // Same arc, three claims. A machine at 35 must not be posted in the
        // healthy green, and neither must one at 72 — that band is the whole
        // reason the ring stopped being hardcoded.
        Assert.True(healthyGreen > 3_000, $"95 is not green: {healthyGreen}");
        Assert.True(middlingNotice > 3_000,
            $"72 is not the notice colour: {middlingNotice}");
        Assert.True(failingRed > 3_000, $"35 is not red: {failingRed}");
        Assert.True(middlingGreen < 100, $"72 is wearing green: {middlingGreen}");
        Assert.True(failingGreen < 100, $"35 is wearing green: {failingGreen}");
        Assert.True(healthyRed < 100, $"95 is wearing red: {healthyRed}");
        // The band MOVED, and this is the half that proves it: before the
        // notice colour, 72 was amber here. Without this line the ring could
        // wear both and the count above would still pass.
        Assert.True(middlingAmber < 100, $"72 is still wearing amber: {middlingAmber}");
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
        var notice = PixelsNear(full, Notice);

        Assert.True(withLeads > withoutLeads + 3_000,
            $"the finding leads are missing: {withLeads} vs {withoutLeads} lit "
            + "pixels in the lead column");
        // The tall card is also the one where the centred column could clip,
        // so prove the ring beside it still rendered.
        Assert.True(notice > 3_000, $"the full card's ring is not lit: {notice}");
    }

    /// The numeral in the middle of the ring stays the card's own ink at every
    /// band. It wears HeroScore, whose triggers paint the score in its band
    /// colour — right in the app's cockpit, wrong here, where the ring around
    /// it already carries the band and a red numeral inside a red ring says
    /// the same thing twice. Those triggers bound a property the card's model
    /// did not expose until this round, so the numeral rendered white by
    /// accident; it is white on purpose now, and this is what says so.
    [Theory]
    [InlineData(95)]
    [InlineData(72)]
    [InlineData(35)]
    public void Render_KeepsTheNumeralInTheCardsOwnInk(int health)
    {
        var path = Render(Card(health));

        var white = PixelsNear(path, Ink, fromX: NumeralFromX, toX: NumeralToX,
            fromY: NumeralFromY, toY: NumeralToY);
        var banded = PixelsNear(path, Good, fromX: NumeralFromX, toX: NumeralToX,
                fromY: NumeralFromY, toY: NumeralToY)
            + PixelsNear(path, Warn, fromX: NumeralFromX, toX: NumeralToX,
                fromY: NumeralFromY, toY: NumeralToY)
            + PixelsNear(path, Crit, fromX: NumeralFromX, toX: NumeralToX,
                fromY: NumeralFromY, toY: NumeralToY);

        Assert.True(white > 500, $"the numeral is not written in ink: {white} pixels");
        Assert.True(banded < 100, $"the numeral is wearing its band: {banded} pixels");
    }

    /// The worst card the model will build: the findings section over its cap
    /// so the overflow line is on the card, every disclosure reporting that it
    /// read nothing, both sensors silent with no measured reason (the longest
    /// unread sentence in both languages — the measured variants say less,
    /// because brisk knows more), and far more fixes than the frame holds.
    ///
    /// The card is a fixed 1600x900 with nothing in it that scrolls, wraps or
    /// shrinks. What actually happens to a body column taller than its Grid is
    /// WPF's layout clip: the column is cut at the Grid's edge and the rows
    /// past it are simply not drawn. No exception, no warning, and nothing on
    /// the picture to say a row is missing — a shareable PNG quietly short of
    /// the truth, and the failure mode no pixel count can see, because a
    /// clipped card and a card that fits look equally tidy.
    ///
    /// So this weighs the column's own appetite against the height it is
    /// given, on the real control with the real dictionaries. The children of
    /// a StackPanel are measured with unbounded height in the stacking
    /// direction, so their DesiredSize is what they WANT — unclamped, unlike
    /// the panel's own, which layout has already trimmed to the slot.
    /// Both languages, because the card is rendered in the one the install is
    /// set to and a Turkish sentence that wrapped onto a third line would be a
    /// row of fixes off the bottom of a card nobody had tested.
    ///
    /// This fixture used to be five findings, called "the picker's maximum".
    /// The picker has no maximum — it takes every finding that carries a
    /// headline, and five was only how many shipped rules carried one. The
    /// model has a maximum now, and this is measured against that.
    ///
    /// WHAT THE tr PASS DOES NOT COVER: the three section headings. They bind
    /// Loc.Instance in the markup rather than the Loc the model was built
    /// with, and Loc.Instance is a process-lifetime singleton this harness
    /// leaves in English — putting it in Turkish would hand Turkish strings to
    /// every test class running beside this one. So the tr card is measured
    /// with Turkish body text under English headings. Each heading is one
    /// short line in both languages and the photographed card clears the frame
    /// by a wide margin, which is why this is recorded rather than worked
    /// around.
    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    public void WorstCaseCard_FitsInsideTheFrameItIsDrawnInto(string language)
    {
        var model = WorstCaseModel(language);

        var (wanted, given) = MeasureBody(model);

        Assert.True(wanted <= given,
            $"the body column wants {wanted:F0}px and the frame gives it {given:F0}px "
            + $"— {model.Findings.Count} finding rows, {model.Unread.Count} unread "
            + $"lines and {model.Fixes.Count} fix rows do not fit, and the ones past "
            + "the edge are clipped away without a word");
        // The fixture IS the worst case, asserted after the measurement so a
        // budget regression reports the pixels rather than the shape.
        Assert.NotEqual("", model.FindingsMoreText);
        Assert.Equal(ReportCardModel.MaxFindingRows, model.Findings.Count);
        Assert.Equal(1 + UnreadableDisclosureIds.Length, model.Unread.Count);
        // What the sections above left it: one row for each unread line past
        // the sensor's, and one for the overflow line.
        Assert.Equal(
            ReportCardModel.MaxFixRows - UnreadableDisclosureIds.Length - 1,
            model.Fixes.Count);
    }

    /// The quiet card at the other end, so the frame check is not passing on
    /// a layout that happens to be empty.
    [Fact]
    public void QuietCard_AlsoFitsInsideTheFrame()
    {
        var (wanted, given) = MeasureBody(Card(95));

        Assert.True(wanted > 0, "the body column measured nothing at all");
        Assert.True(wanted <= given,
            $"the body column wants {wanted:F0}px, the frame gives {given:F0}px");
    }

    /// The overflow line is a TextBlock whose Text and whose collapse trigger
    /// both bind the same model property, and a misspelled path there fails
    /// the way this card has already been bitten once: silently, rendering an
    /// empty row that eats the budget and says nothing. So this reads the
    /// rendered string off the control and holds it to the model's, in both
    /// directions — visible and carrying the count when findings were dropped,
    /// collapsed when none were.
    [Fact]
    public void TheOverflowLine_CarriesTheModelsCount_AndGoesAwayWhenNothingWasDropped()
    {
        var loc = new Brisk.Localization.Loc();
        loc.SetLanguage("en");
        var over = ReportCardModel.Build(
            TestData.Snapshot(WorstCaseFindings(loc), new SensorStatus(true, true, null)),
            Array.Empty<UndoableFix>(), loc);
        var exactly = ReportCardModel.Build(
            TestData.Snapshot(
                WorstCaseFindings(loc).Where(f => f.Headline is not null)
                    .Take(ReportCardModel.MaxFindingRows).ToList(),
                new SensorStatus(true, true, null)),
            Array.Empty<UndoableFix>(), loc);

        var (overText, overVisible) = ReadOverflowLine(over);
        var (exactlyText, exactlyVisible) = ReadOverflowLine(exactly);

        Assert.True(overVisible, "the overflow line is not on a card that dropped rows");
        Assert.Equal(over.FindingsMoreText, overText);
        Assert.False(exactlyVisible,
            $"the overflow line is on a card that dropped nothing, reading '{exactlyText}'");
    }

    /// THE NUMBERS FixBudget's DOC STANDS ON, measured on the real control
    /// rather than asserted in prose.
    ///
    /// The budget trades the fix list one row per line the sections above it
    /// take, and that is only sound while those rows are the heights the doc
    /// claims. It said all three were the same 29px. Two are; the findings'
    /// overflow line is six pixels taller, because it wears the finding rows'
    /// 12px bottom margin rather than the 6px the small rows use — so that
    /// term of the trade under-charges, which is the whole of why the worst
    /// card clears the frame by under one row instead of by nine pixels.
    ///
    /// A comment carrying a measured figure with nothing checking it is the
    /// exact shape that hid this card's clipping for a whole wave. This is
    /// that comment's guard, and it is deliberately the one test that fails
    /// when somebody changes that margin: the change is correct and the doc
    /// has to move with it.
    [Fact]
    public void TheRowHeightsTheBudgetTrades_AreTheOnesFixBudgetsDocClaims()
    {
        var findingRow = HeightOf(HeadlineFindings(5)) - HeightOf(HeadlineFindings(4));
        var smallRow = HeightOf(WithUnreadable(2)) - HeightOf(WithUnreadable(1));
        var overflowLine = HeightOf(HeadlineFindings(ReportCardModel.MaxFindingRows + 1))
            - HeightOf(HeadlineFindings(ReportCardModel.MaxFindingRows));

        Assert.Equal(51.90, findingRow, 2);
        Assert.Equal(28.61, smallRow, 2);
        Assert.Equal(34.61, overflowLine, 2);
        // The term that is NOT an even trade, as a number so it cannot grow
        // quietly: the overflow line is charged one small row and costs more.
        Assert.Equal(6.00, overflowLine - smallRow, 2);

        // And what that costs the frame, which is the reason any of this is
        // written down. Bounded rather than pinned: the claim is "less than
        // one row of room left, and more than none", which stays true under a
        // harmless layout tweak and goes false the moment the under-charge
        // grows into a clipped card.
        var (wanted, given) = MeasureBody(WorstCaseModel("en"));
        Assert.InRange(given - wanted, 0.0, smallRow);
    }

    /// n findings that lead with a number, nothing else — short titles, so the
    /// difference between two of these is one row and never a wrap.
    private static ScanSnapshot HeadlineFindings(int count) => TestData.Snapshot(
        Enumerable.Range(0, count)
            .Select(i => TestData.Finding($"rule-{i:00}", cat: RuleCategory.Advise,
                canFix: false, headline: H($"{i}")))
            .ToList(),
        new SensorStatus(true, true, null));

    /// The sensor line plus n disclosures that read nothing. The sensor
    /// variant is the SHORT one — everything answered — so the difference
    /// between two of these is one unread row and never a wrap.
    private static ScanSnapshot WithUnreadable(int count) => TestData.Snapshot(
        UnreadableDisclosureIds.Take(count).Select(id => new DiagnosticFinding(
            id, $"rule.{id}.title.unread", $"unread {id}", $"Evidence {id}",
            Severity.Info, RuleCategory.Advise, 1, CanFix: false,
            FixDescription: null, Headline: null, Kind: FindingKind.Notice)).ToList(),
        new SensorStatus(true, true, null));

    private static double HeightOf(ScanSnapshot snapshot)
    {
        var loc = new Brisk.Localization.Loc();
        loc.SetLanguage("en");
        return MeasureBody(ReportCardModel.Build(
            snapshot, Array.Empty<UndoableFix>(), loc)).Wanted;
    }

    /// The worst card the model will build, in one place because two tests
    /// weigh it: the frame check, and the row-height guard that says how much
    /// room the under-charged overflow line leaves it.
    private static ReportCardModel WorstCaseModel(string language)
    {
        var loc = new Brisk.Localization.Loc();
        loc.SetLanguage(language);
        var manyFixes = Enumerable.Range(0, 16)
            .Select(i => new UndoableFix($"rule-{i:00}",
                new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Utc).AddMinutes(-i)))
            .ToArray();
        return ReportCardModel.Build(
            TestData.Snapshot(WorstCaseFindings(loc), new SensorStatus(false, false, null))
                with { Health = 35 },
            manyFixes, loc);
    }

    /// The four report-only disclosures, which are the ids that can report
    /// having read nothing. Hardcoded here and held to four elsewhere:
    /// PrivacyRedLineTests.EveryPrivacyDisclosureRule_ShipsAnIdThePrivacyList
    /// Carries asserts the shipped count, so a fifth arrives as a failure
    /// there rather than as a worst case this file quietly stopped covering.
    private static readonly string[] UnreadableDisclosureIds =
    {
        "usb-history", "run-history", "recall-status", "delivery-optimization",
    };

    /// One row over the findings cap AND every disclosure unreadable at once.
    /// No machine produces both today: six findings that lead with a number
    /// needs at least one disclosure to have read something, and this fixture
    /// has all four reporting that they read nothing. The model accepts the
    /// combination, though, and the frame is measured against what the model
    /// accepts rather than against what today's registry happens to allow.
    private static List<DiagnosticFinding> WorstCaseFindings(Brisk.Localization.Loc loc)
    {
        var findings = LongestTitledRuleIds(loc, ReportCardModel.MaxFindingRows + 1)
            .Select((id, i) => TestData.Finding(id, cat: RuleCategory.Advise,
                canFix: false, headline: H($"{i}00 ms")))
            .ToList();
        findings.AddRange(UnreadableDisclosureIds.Select(id => new DiagnosticFinding(
            id, $"rule.{id}.title.unread", $"unread {id}", $"Evidence {id}",
            Severity.Info, RuleCategory.Advise, 1, CanFix: false,
            FixDescription: null, Headline: null, Kind: FindingKind.Notice)));
        return findings;
    }

    /// The longest titles the shipped registry actually holds, in the language
    /// under test — asked of the resx rather than guessed at, so a title
    /// rewritten longer than the frame allows fails here instead of shipping.
    /// The old fixture wrote its own sentences, which measured whatever the
    /// author imagined and left the Turkish pass measuring English, because
    /// "rule.long-0.title" is a key neither resx defines.
    private static IReadOnlyList<string> LongestTitledRuleIds(
        Brisk.Localization.Loc loc, int count) =>
        DiagnosticRuleRegistry.All.Select(r => r.Id)
            .OrderByDescending(id => loc.Title($"rule.{id}.title", id).Length)
            .ThenBy(id => id, StringComparer.Ordinal)
            .Take(count).ToList();

    /// Lays the card out and answers what its overflow line actually rendered.
    private static (string Text, bool Visible) ReadOverflowLine(ReportCardModel model)
    {
        var text = "";
        var visible = false;
        OnStaThread(() =>
        {
            var card = new ReportCard { DataContext = model };
            card.Measure(new Size(ReportCardRenderer.Width, ReportCardRenderer.Height));
            card.Arrange(new Rect(0, 0, ReportCardRenderer.Width, ReportCardRenderer.Height));
            card.UpdateLayout();
            text = card.FindingsMore.Text;
            visible = card.FindingsMore.Visibility == Visibility.Visible;
        });
        return (text, visible);
    }

    /// Lays the real ReportCard out at its real size on an STA thread and
    /// answers what its body column asked for and what the Grid handed it.
    private static (double Wanted, double Given) MeasureBody(ReportCardModel model)
    {
        var wanted = 0.0;
        var given = 0.0;
        OnStaThread(() =>
        {
            var card = new ReportCard { DataContext = model };
            card.Measure(new Size(ReportCardRenderer.Width, ReportCardRenderer.Height));
            card.Arrange(new Rect(0, 0, ReportCardRenderer.Width, ReportCardRenderer.Height));
            card.UpdateLayout();
            foreach (UIElement child in card.Body.Children)
                wanted += child.DesiredSize.Height;
            given = ((FrameworkElement)VisualTreeHelper.GetParent(card.Body)).ActualHeight;
        });
        return (wanted, given);
    }

    /// WPF objects demand an STA thread and the test runner does not have one
    /// — the same reason ReportCardRenderer.RenderOnStaThread exists.
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
