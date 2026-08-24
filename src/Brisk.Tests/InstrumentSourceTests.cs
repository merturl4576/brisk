using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Brisk.Theming;
using Xunit;
// WinForms is on in this project, so these two bare names are ambiguous.
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Brisk.Tests;

/// The instrument, asserted from SOURCE — for the reason PanelSourceTests
/// gives about styles: a brush on an arc makes no claim a running test
/// watches, so every defect below ships green.
///
/// Three concerns, and none of them is cosmetic:
///
///   * the satellites' floor pools are the one LIT element this round adds,
///     and the tuning gate spent the atmosphere's legibility budget down to
///     seven thousandths. AtmosphereLayer's rule names a floor ellipse by
///     name: anything lit that arrives after the gate carries its own
///     contrast check instead of leaning on that one. Two tests, because
///     the rule has two halves — where the pool is drawn, and what it does
///     to the text standing on it.
///   * an arc nobody can see is an instrument drawing a reading it does not
///     deliver. The passive arc is meant to be quieter than the primary,
///     and "quieter" has a floor underneath it.
///   * and in brisk a colour carries a claim. The outer health ring is the
///     instrument's only claim-carrying element; an inner arc that turned
///     amber would be a threshold judgment no rule in this app has made.
public sealed class InstrumentSourceTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// "{StaticResource Foo}" -> "Foo".
    private static readonly Regex ResourceKey =
        new(@"\{(?:Dynamic|Static)Resource\s+([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    // ------------------------------------------------------------------
    // The satellites' floor.
    // ------------------------------------------------------------------

    /// The floor pools obey the first half of the atmosphere's rule by
    /// construction — they are drawn inside the instrument panel, on its own
    /// opaque fill, so no pool pixel is ever composited over bare sky, rain,
    /// grid or glow, and BrightestComposite() is left exactly where the
    /// tuning gate left it.
    ///
    /// "By construction" is a claim about the page's shape, so it is read
    /// off the page rather than asserted about in prose. Move a satellite
    /// out from under the panel and the pool starts adding light to ground
    /// that has none to spare, with every contrast test in the repo still
    /// green.
    [Fact]
    public void TheSatelliteFloors_AreDrawnInsideThePanel_NotOnBareAtmosphere()
    {
        // ONE parse of the page, because both halves of this comparison are
        // XElement identity: a second XDocument.Load would hand back a
        // different object for the same markup and the containment check
        // would fail for a reason that has nothing to do with the layout.
        var page = Page();
        var inside = page.Descendants()
            .Single(e => (string?)e.Attribute(X + "Name") == "HeroPanel")
            .Descendants().ToHashSet();

        // NotEmpty first, because Assert.All over an empty collection passes:
        // delete both pools and everything below would stay green while
        // saying nothing. The count itself is deliberately not pinned — a
        // third satellite is a design decision, not a legibility one.
        var floors = SatelliteFloors(page);
        Assert.NotEmpty(floors);
        Assert.All(floors, floor => Assert.Contains(floor, inside));
    }

    /// The floor pools' own contrast check, and the second half of the rule
    /// the brackets answered a round ago: a lit thing has to leave the text
    /// standing on it legible.
    ///
    /// Everything here is READ. The pool's colour key and its peak opacity
    /// come out of the style, the panel underneath comes out of the page's
    /// own Background attribute, and the text colours come out of the styles
    /// the satellites wear. A hex copied in would certify the palette it was
    /// copied from forever — and the colour that decides this test, the
    /// caption's, could not be copied anyway: HeroMuted is 55% white, so
    /// what a reader sees is whatever the pool underneath makes of it.
    ///
    /// A Theory over both dictionaries because AccentGlow is a theme
    /// decision: the light dictionary holds it fully transparent, so the
    /// same page renders with no pool at all and the caption has to survive
    /// the bare panel instead. Both are real states of the app, and 4.5:1 is
    /// the floor for text in both.
    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void TheSatelliteReadouts_StayLegibleOnTheirOwnFloorPool(string theme)
    {
        var lit = Composite(PanelGround(), ThemeSource.ColorOf(theme, FloorGlowKey()),
            PeakFloorOpacity());

        foreach (var style in new[] { "SatelliteValue", "SatelliteCaption" })
        {
            var key = ResourceKey.Match(SetterChain(style)["Foreground"]).Groups[1].Value;
            Assert.NotEqual("", key);
            // Composited, not compared raw: HeroMuted is 55% white, so the
            // colour a reader actually sees is the pool showing through it.
            var ink = Composite(lit, SharedColor(key), 1);

            var ratio = Contrast.Ratio(ink, lit);
            Assert.True(ratio >= 4.5,
                $"{theme}: the satellite's {style} ({key}) on its own floor pool " +
                $"is {ratio:F2}:1 — under the 4.5:1 floor, so the light this " +
                "round added to the instrument is light a reader pays for");
        }
    }

    // ------------------------------------------------------------------
    // The two inner arcs.
    // ------------------------------------------------------------------

    /// Both arcs have to be visible as GRAPHICAL objects, which is the 3:1
    /// floor of WCAG 1.4.11 — the same one the corner brackets clear. A
    /// passive arc that is quieter than the primary is the design; one that
    /// cannot be seen is decoration nobody can see.
    ///
    /// A Fact and not a Theory, unlike the satellites above, because the
    /// Hero* family is always-dark in BOTH themes by design: the instrument
    /// panel does not follow the theme dictionaries, so there is exactly one
    /// state to measure.
    [Fact]
    public void TheInnerArcs_StandOutAgainstThePanelTheySweepOver()
    {
        var ground = PanelGround();

        foreach (var arc in new[] { "CpuArc", "RamArc" })
        {
            var key = RingBrushKeyOf(arc);
            var ratio = Contrast.Ratio(Composite(ground, SharedColor(key), 1), ground);
            Assert.True(ratio >= 3.0,
                $"the {arc} ({key}) on the panel it sweeps over is {ratio:F2}:1 " +
                "— under the 3:1 floor a graphical object has to clear, so the " +
                "instrument is drawing a reading nobody can read");
        }
    }

    /// In brisk a colour carries a claim, and the outer health ring is the
    /// instrument's ONLY claim-carrying element. A RAM arc that turned amber
    /// at 80% would be a threshold judgment no rule in this app has made.
    ///
    /// Asserted by VALUE and not by key name, because the way this actually
    /// goes wrong is a paste: the ring's own brushes are two lines away in
    /// the same file, and a copied hex would sail past any list of forbidden
    /// key names. The second assertion covers the other route — the ring
    /// picks its colour with a DataTrigger on ScoreBrushKey, and an arc that
    /// grew a Style of its own is one paste away from picking it the same
    /// way, at which point what the arc wears is no longer readable here at
    /// all.
    [Fact]
    public void TheInnerArcs_NeverWearTheHealthRingsColours()
    {
        var claims = new[] { "HeroGood", "HeroWarn", "HeroCrit" }
            .Select(SharedColor).ToArray();

        foreach (var arc in new[] { "CpuArc", "RamArc" })
        {
            var worn = SharedColor(RingBrushKeyOf(arc));
            Assert.DoesNotContain(claims,
                claim => claim.R == worn.R && claim.G == worn.G && claim.B == worn.B);

            Assert.Empty(ArcElement(arc).Elements()
                .Where(e => e.Name.LocalName.EndsWith(".Style", StringComparison.Ordinal)));
        }
    }

    // ------------------------------------------------------------------
    // The instrument's own palette.
    // ------------------------------------------------------------------

    /// The readout in the middle of the dial, against the fill the hero
    /// family paints behind it. 4.5:1 and not the arcs' 3:1 because these are
    /// glyphs: the score is 56 px and would clear WCAG's large-text allowance
    /// on its own, but the captions sharing its panel are 10.5 and 11 px
    /// small-caps, and they are read as one readout with it.
    ///
    /// EVERY colour the style can apply is measured, not the one setter that
    /// happens to be written first. HeroScore paints the score in HeroText
    /// only until a scan lands, and in one of the three brights ever after —
    /// so a check that read the default alone would certify the one colour a
    /// reader mostly does not see.
    ///
    /// A Theory over styles rather than over dictionaries, unlike the
    /// satellites above: the Hero* family is always-dark in BOTH themes by
    /// design, so there is exactly one state to measure.
    ///
    /// HeroStripCaption is the page heroes' caption — Sağlık, Performans and
    /// Depolama each open with a strip wearing this same fill — and it holds
    /// no colour of its own, so it is also what makes the BasedOn walk below
    /// load-bearing rather than merely written.
    [Theory]
    [InlineData("HeroScore")]
    [InlineData("HeroPodCaption")]
    [InlineData("HeroStripCaption")]
    public void TheScoreAndItsCaption_StayLegibleOnTheInstrumentsOwnFill(string style)
    {
        var ground = PanelGround();

        foreach (var key in StyleBrushKeys(style, "Foreground"))
        {
            // Composited, not compared raw, for HeroMuted's sake: it is 55%
            // white, so the colour a reader actually sees is the panel
            // showing through it.
            var ink = Composite(ground, SharedColor(key), 1);

            var ratio = Contrast.Ratio(ink, ground);
            Assert.True(ratio >= 4.5,
                $"{style}'s {key} on the instrument's own fill is {ratio:F2}:1 " +
                "— under the 4.5:1 floor text has to clear, so the panel was " +
                "retuned out from under the readout standing on it");
        }
    }

    /// The health ring's lit segments, at the 3:1 floor a graphical object
    /// has to clear — the arcs' floor, for the arcs' reason. All three bands,
    /// because a ring that is legible only while the machine is healthy goes
    /// quiet exactly when it has something to say.
    ///
    /// The UNLIT track is deliberately not in here. It is the ABSENCE of a
    /// segment rather than a segment — 12% white, nowhere near 3:1 — and
    /// pinning it would pin a floor the instrument does not want.
    [Fact]
    public void TheLitRingSegments_StandOutAgainstThePanelBehindThem()
    {
        var ground = PanelGround();

        foreach (var key in ElementBrushKeys("LitGauge", "LitBrush"))
        {
            var ratio = Contrast.Ratio(Composite(ground, SharedColor(key), 1), ground);
            Assert.True(ratio >= 3.0,
                $"the lit ring's {key} on the panel behind it is {ratio:F2}:1 — " +
                "under the 3:1 floor a graphical object has to clear, so the one " +
                "element in this instrument that carries a claim cannot be read");
        }
    }

    /// Local beats style, in WPF and in this file's blind spot: every ratio
    /// above is read off a Style, so a Foreground or an Opacity written on
    /// the INSTANCE would override the value being measured, and every check
    /// here would go on certifying a colour the page had stopped drawing.
    ///
    /// Only the properties that are measured, and only on the page this file
    /// reads. A local FontSize or TextAlignment is how these styles are meant
    /// to be specialised — the overview sets both — so a check that forbade
    /// every local value would be a check against using styles at all.
    [Fact]
    public void TheStyledElements_CarryNoLocalOverrideOfWhatIsMeasured()
    {
        var page = Page();
        var measured = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["SatelliteFloor"] = new[] { "Fill", "Opacity", "OpacityMask" },
            ["SatelliteValue"] = new[] { "Foreground" },
            ["SatelliteCaption"] = new[] { "Foreground" },
            ["HeroScore"] = new[] { "Foreground" },
            ["HeroPodCaption"] = new[] { "Foreground" },
        };

        foreach (var (style, properties) in measured)
        {
            var wearers = page.Descendants()
                .Where(e => ResourceKey.Match((string?)e.Attribute("Style") ?? "")
                    .Groups[1].Value == style)
                .ToArray();
            // NotEmpty first, for Assert.All's reason above: rename a style
            // and both loops below run zero times, leaving a guard that
            // passes by finding nothing to look at.
            Assert.NotEmpty(wearers);

            foreach (var element in wearers)
            foreach (var property in properties)
                // Two ways to write one override — an attribute, and the
                // property element <Ellipse.OpacityMask> that carries no
                // attribute for an attribute check to find.
                Assert.True(
                    element.Attribute(property) is null
                    && !element.Elements().Any(child => child.Name.LocalName
                        == element.Name.LocalName + "." + property),
                    $"{element.Name.LocalName} wearing {style} sets {property} on " +
                    "itself — local beats style in WPF, so the ratio measured off " +
                    $"{style} above is not the one this element draws");
        }
    }

    // ------------------------------------------------------------------
    // Reading the page and the dictionaries.
    // ------------------------------------------------------------------

    /// Source-over, the way the compositor does it: the top layer's own
    /// alpha times the opacity it is drawn at.
    ///
    /// Restated here rather than shared with AtmosphereLayer's copy, which
    /// is private to the layer and belongs to its own budget arithmetic —
    /// the one number in this repo that must not move for an unrelated
    /// reason. ContrastTests sits seven thousandths above its floor; lifting
    /// that method out from under it to save eight lines here would put a
    /// refactor in the blast radius of a legibility guarantee.
    private static Color Composite(Color under, Color over, double opacity)
    {
        var alpha = Math.Clamp(opacity, 0, 1) * (over.A / 255.0);
        return Color.FromRgb(
            Mix(under.R, over.R, alpha), Mix(under.G, over.G, alpha),
            Mix(under.B, over.B, alpha));
    }

    private static byte Mix(byte under, byte over, double alpha) =>
        (byte)Math.Round(alpha * over + (1 - alpha) * under);

    /// The brightest ground a satellite or an arc can be drawn on: the
    /// lightest stop of whatever brush the instrument panel actually names
    /// as its Background. Worst case, the way ContrastTests takes the
    /// atmosphere's — an average passes while one glyph sits on the bright
    /// side of the vignette. A flat fill answers the same question with one
    /// colour, so the panel is free to stop being a gradient without this
    /// turning into a lookup that throws.
    private static Color PanelGround()
    {
        var key = ResourceKey.Match((string?)Page().Descendants()
            .Single(e => (string?)e.Attribute(X + "Name") == "HeroPanel")
            .Attribute("Background") ?? "").Groups[1].Value;
        Assert.NotEqual("", key);

        var stops = SharedXaml().Elements()
            .Single(e => (string?)e.Attribute(X + "Key") == key)
            .Descendants().Where(e => e.Name.LocalName == "GradientStop")
            .Select(e => ColorOf((string)e.Attribute("Color")!))
            .ToArray();
        var ground = stops.Length == 0
            ? SharedColor(key)
            : stops.OrderByDescending(Contrast.RelativeLuminance).First();
        // The "no pool pixel ever reaches bare atmosphere" argument rests
        // entirely on this fill being opaque, and a contrast ratio cannot
        // see alpha: a translucent panel would be measured here exactly as
        // if it were solid, with the pool quietly showing through onto
        // ground that has no light to spare.
        Assert.Equal(255, ground.A);
        return ground;
    }

    /// The pool's peak light: the element's own Opacity times its mask's
    /// most opaque stop. Both are read, because turning either one up turns
    /// the pool up.
    private static double PeakFloorOpacity()
    {
        var opacity = double.Parse(SetterChain("SatelliteFloor")["Opacity"],
            CultureInfo.InvariantCulture);
        var mask = SharedResource("Style", "SatelliteFloor").Descendants()
            .Where(e => e.Name.LocalName == "GradientStop")
            .Max(e => ColorOf((string)e.Attribute("Color")!).A) / 255.0;
        return opacity * mask;
    }

    private static string FloorGlowKey()
    {
        var key = ResourceKey.Match(SetterChain("SatelliteFloor")["Fill"]).Groups[1].Value;
        Assert.NotEqual("", key);
        return key;
    }

    private static XElement[] SatelliteFloors(XElement page) =>
        page.Descendants()
            .Where(e => e.Name.LocalName == "Ellipse"
                && ResourceKey.Match((string?)e.Attribute("Style") ?? "").Groups[1].Value
                    == "SatelliteFloor")
            .ToArray();

    private static XElement ArcElement(string name) =>
        Page().Descendants()
            .Single(e => e.Name.LocalName == "SweepRing"
                && (string?)e.Attribute(X + "Name") == name);

    private static string RingBrushKeyOf(string arc)
    {
        var key = ResourceKey.Match((string?)ArcElement(arc).Attribute("RingBrush") ?? "")
            .Groups[1].Value;
        Assert.NotEqual("", key);
        return key;
    }

    /// One colour out of Shared.xaml, following the single level of
    /// indirection the Hero family uses: a brush may name a Color key rather
    /// than carry a literal, which is how the gauge shares one value between
    /// its brush and the DropShadowEffect that cannot be handed a brush.
    private static Color SharedColor(string key)
    {
        var element = SharedXaml().Elements()
            .Single(e => (string?)e.Attribute(X + "Key") == key);
        var written = (string?)element.Attribute("Color") ?? element.Value.Trim();
        var named = ResourceKey.Match(written).Groups[1].Value;
        return named.Length > 0 ? SharedColor(named) : ColorOf(written);
    }

    private static Color ColorOf(string hex) =>
        (Color)ColorConverter.ConvertFromString(hex)!;

    /// Every setter a style ends up applying, nearest-wins — the same walk
    /// PanelSourceTests does, because the satellites' colours live one
    /// BasedOn away in HeroPodValue and HeroPodCaption.
    private static Dictionary<string, string> SetterChain(string key)
    {
        var setters = new Dictionary<string, string>(StringComparer.Ordinal);
        var chain = new List<XElement>();
        for (var at = key; at is not null;)
        {
            var style = SharedResource("Style", at);
            chain.Add(style);
            at = ResourceKey.Match((string?)style.Attribute("BasedOn") ?? "")
                .Groups[1].Value is { Length: > 0 } parent ? parent : null;
        }
        // Reversed so the nearest style is written last and wins, which is
        // the order WPF resolves BasedOn in.
        chain.Reverse();
        foreach (var style in chain)
        foreach (var setter in style.Elements().Where(e => e.Name.LocalName == "Setter"))
            setters[(string)setter.Attribute("Property")!] =
                (string?)setter.Attribute("Value") ?? "";
        return setters;
    }

    /// Every brush key a style can apply to `property`: the whole BasedOn
    /// chain, and the setters INSIDE its triggers as well as the ones beside
    /// them. SetterChain above answers "what does this style apply by
    /// default", which is the wrong question for a colour that only shows in
    /// a state — and the hero's score is exactly that colour.
    private static string[] StyleBrushKeys(string styleKey, string property)
    {
        var keys = new List<string>();
        for (var at = styleKey; at is not null;)
        {
            var style = SharedResource("Style", at);
            keys.AddRange(BrushKeysIn(style, property));
            at = ResourceKey.Match((string?)style.Attribute("BasedOn") ?? "")
                .Groups[1].Value is { Length: > 0 } parent ? parent : null;
        }
        return Pinned(keys);
    }

    /// The same question asked of an ELEMENT on the page, which can answer it
    /// either way round: as an attribute on itself, or as setters in the
    /// style it carries inline. The lit gauge uses the second — a default and
    /// two DataTriggers — and reading only one of the two routes would
    /// measure the ring in one of its three states and call it the ring.
    private static string[] ElementBrushKeys(string name, string property)
    {
        var element = Page().Descendants()
            .Single(e => (string?)e.Attribute(X + "Name") == name);
        return Pinned(BrushKeysIn(element, property)
            .Prepend(ResourceKey.Match((string?)element.Attribute(property) ?? "")
                .Groups[1].Value)
            .ToList());
    }

    private static IEnumerable<string> BrushKeysIn(XElement scope, string property) =>
        scope.Descendants()
            .Where(e => e.Name.LocalName == "Setter"
                && (string?)e.Attribute("Property") == property)
            .Select(e => ResourceKey.Match((string?)e.Attribute("Value") ?? "")
                .Groups[1].Value);

    /// Empty is the failure mode both readers share: mistype a property name
    /// and the foreach measures nothing while the test stays green.
    private static string[] Pinned(List<string> keys)
    {
        var found = keys.Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(found);
        return found;
    }

    private static XElement SharedResource(string kind, string key) =>
        SharedXaml().Elements().Single(e =>
            e.Name.LocalName == kind && (string?)e.Attribute(X + "Key") == key);

    private static XElement SharedXaml() =>
        XDocument.Load(Path.Combine(BriskDir(), "Theming", "Shared.xaml")).Root!;

    private static XElement Page() =>
        XDocument.Load(Path.Combine(BriskDir(), "Views", "OverviewPage.xaml")).Root!;

    private static string BriskDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return Path.Combine(dir.FullName, "src", "Brisk");
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }
}
