using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using Brisk.Localization;
using Brisk.Tests.Snapshots;
using Brisk.Theming;
using Brisk.ViewModels;
using Brisk.Views;
using Xunit;
// WinForms is on in this project, so these three bare names are ambiguous.
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Size = System.Windows.Size;

namespace Brisk.Tests;

/// The panel language, asserted from SOURCE — because a style is not a
/// behaviour. Most of this file renders nothing: a `Style` in a resource
/// dictionary makes no claim a running test can watch, and every defect below
/// ships green. The exceptions are the two tests that have to drive something
/// — the card's triggers and the read-back dot's colour across a theme switch
/// — and each says in its own comment why reading the markup could not have
/// answered it.
///
///   * a header that TRIMS instead of wrapping loses the end of a Turkish
///     sentence, and Turkish is where it happens — "brisk'in yalnızca
///     bildirebildikleri" is nearly twice its English original, and the
///     header carries the widest text in the panel;
///   * a page whose headings quietly point back at the old micro-label
///     stops inheriting the panel, and the page still lays out, still
///     photographs, still passes every view-model test;
///   * a corner bracket is a LIT panel edge, and the atmosphere's legibility
///     budget was spent at the tuning gate — so anything lit that arrived
///     after it carries its own contrast check rather than leaning on that
///     one;
///   * and the card's own triggers reach five children by name, which is the
///     one thing the reskin could actually have broken. That one has to be
///     driven rather than read.
public sealed class PanelSourceTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// "{StaticResource Foo}" -> "Foo".
    private static readonly Regex ResourceKey =
        new(@"\{(?:Dynamic|Static)Resource\s+([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    // ------------------------------------------------------------------
    // The wrap guard.
    // ------------------------------------------------------------------

    /// The header strip wraps and never trims — and "never" has to reach
    /// through BasedOn, which is the whole reason this is not one line.
    /// Body sets TextTrimming="CharacterEllipsis", so a header built the
    /// obvious way (BasedOn Body, plus a background) inherits an ellipsis
    /// and clips the Turkish heading with nothing in the file saying so.
    /// The chain is walked and the absence is asserted over all of it.
    [Fact]
    public void ThePanelHeader_Wraps_AndNeverTrims()
    {
        var setters = SetterChain("PanelHeader");

        Assert.True(setters.TryGetValue("TextWrapping", out var wrapping),
            "PanelHeader sets no TextWrapping — the header carries the widest " +
            "text in the panel, and a TextBlock that does not wrap puts it on " +
            "one line and lets the panel decide what to do with the rest");
        Assert.Equal("Wrap", wrapping);

        Assert.False(setters.ContainsKey("TextTrimming"),
            "PanelHeader ends up setting TextTrimming " +
            $"(\"{setters.GetValueOrDefault("TextTrimming")}\") somewhere in its " +
            "BasedOn chain. Trimming and wrapping are not alternatives that " +
            "both work — a trimming header drops the end of the Turkish " +
            "heading, which is the longest string on the page");
    }

    // ------------------------------------------------------------------
    // The bet: the pages inherit.
    // ------------------------------------------------------------------

    /// Every section heading on the findings pages resolves to PanelHeader.
    ///
    /// This is the claim the whole wave rests on, and it is exactly the kind
    /// that rots silently: repoint one heading back at SectionLabel and the
    /// page still builds, still lays out, still photographs — it just stops
    /// being a cockpit in one place, and no other test in the suite looks.
    /// The wrap claim is re-asserted through the page's OWN style too, and
    /// through it FIRST: a page-local `BasedOn` style is free to add setters
    /// of its own, and the nearest style wins. That sentence was here before
    /// the code was, which is the defect a reviewer caught — see
    /// InlineStyleOf. A local ATTRIBUTE beats both, and missing it was the
    /// second half of the same omission; it is checked below.
    ///
    /// Only direct <Setter> children of a style count, here as in the cards
    /// guard: a setter inside <Style.Triggers> is a state rather than a
    /// dress, which is how the finding card keeps its hover fill. A page that
    /// trimmed its heading from a trigger would pass. That is a knowing gap,
    /// not an oversight.
    [Theory]
    [InlineData("HealthPage.xaml", "health.advise.section")]
    [InlineData("HealthPage.xaml", "health.notice.section")]
    [InlineData("PerfPage.xaml", "health.advise.section")]
    [InlineData("PerfPage.xaml", "health.notice.section")]
    [InlineData("PerfPage.xaml", "startup.title")]
    public void TheSectionHeadings_AreDressedAsPanelHeaders(string page, string key)
    {
        var heading = XDocument.Load(Path.Combine(BriskDir(), "Views", page)).Root!
            .Descendants()
            .Where(e => e.Name.LocalName == "TextBlock")
            .SingleOrDefault(e => ((string?)e.Attribute("Text"))?.Contains(
                $"[{key}]", StringComparison.Ordinal) == true);
        Assert.True(heading is not null,
            $"{page} has no TextBlock showing [{key}]");

        var chain = StyleChain(StyleKeyOf(heading!));
        Assert.Contains("PanelHeader", chain.Keys);

        var setters = CascadeOf(heading!, chain.Styles);
        Assert.Equal("Wrap", setters.GetValueOrDefault("TextWrapping"));
        Assert.False(setters.ContainsKey("TextTrimming"),
            $"{page}'s [{key}] heading trims after all — a style on the page " +
            "put the ellipsis back that PanelHeader was written to keep out");

        // The same two claims on the ELEMENT, because a local attribute beats
        // every style setter in WPF — which left this guard green under a
        // one-attribute mutation while the Turkish heading trimmed on screen.
        // The cards guard below had been checking both surfaces all along.
        Assert.True(heading!.Attribute("TextTrimming") is null,
            $"{page}'s [{key}] heading sets TextTrimming as a local attribute, " +
            "which beats every setter PanelHeader has");
        var wrapping = (string?)heading!.Attribute("TextWrapping");
        Assert.True(wrapping is null or "Wrap",
            $"{page}'s [{key}] heading sets TextWrapping to {wrapping} as a " +
            "local attribute, which beats the Wrap that PanelHeader sets");
    }

    /// The cards on Sağlık, Performans and Depolama are not written on those
    /// pages: FindingCard and CompletionReport live in Shared.xaml, which is
    /// what makes "three pages change look without changing" true at all. So
    /// the two templates are pinned to the panel style rather than to a
    /// hand-copied set of brush attributes — copies are how the app ends up
    /// with four panels wearing three different edges.
    [Theory]
    [InlineData("FindingCard")]
    [InlineData("CompletionReport")]
    public void TheSharedCards_WearThePanel(string template)
    {
        var root = SharedResource("DataTemplate", template).Elements()
            .Single(e => !e.Name.LocalName.Contains('.'));

        Assert.Contains("CockpitPanel", StyleChain(StyleKeyOf(root)).Keys);

        // The panel's own fill and edge, not a private copy of them. A local
        // attribute beats a style setter in WPF, and an inline setter beats
        // the BasedOn one, so a leftover Background in EITHER place would
        // silently win over CockpitPanel and the card would go on wearing
        // whatever it wore before while claiming to have joined. Only direct
        // <Setter> children count: a setter inside <Style.Triggers> is a
        // state, which is how the card's hover fill is allowed to live here.
        var inline = Collapse(InlineStyleOf(root) is { } style
            ? new[] { style }
            : Array.Empty<XElement>());
        foreach (var overridden in new[]
                 { "Background", "BorderBrush", "BorderThickness", "CornerRadius" })
        {
            Assert.True(root.Attribute(overridden) is null,
                $"{template}'s panel sets {overridden} as a local attribute, " +
                "which beats the CockpitPanel setter it is supposed to be taking");
            Assert.False(inline.ContainsKey(overridden),
                $"{template}'s panel sets {overridden} in its own inline style, " +
                "which beats the CockpitPanel setter it is BasedOn");
        }
    }

    /// The panel carries its brackets itself.
    ///
    /// It is a templated ContentControl and not a Style on Border for exactly
    /// this: a Style cannot give an element children, so a Border-styled
    /// panel would need the four corners added by hand at every use site —
    /// and a panel that arrives without its brackets somewhere is how a
    /// language stops being one. Six panels adopt this today across three
    /// pages; none of them mentions a bracket.
    [Fact]
    public void ThePanel_CarriesItsOwnBrackets()
    {
        var panel = SharedResource("Style", "CockpitPanel").Descendants()
            .Single(e => e.Name.LocalName == "ControlTemplate");

        Assert.Contains(panel.Descendants(),
            e => (string?)e.Attribute("Template") == "{StaticResource PanelBrackets}");
        Assert.Contains(panel.Descendants(), e => e.Name.LocalName == "ContentPresenter");
    }

    // ------------------------------------------------------------------
    // The brackets.
    // ------------------------------------------------------------------

    /// Four corners, drawn. "Drawn" is the load-bearing word: the reference
    /// this design came from is a picture, and copying a corner out of it
    /// would put a raster asset in a theme dictionary that cannot follow a
    /// palette, cannot follow a DPI, and cannot be recoloured by a theme.
    [Fact]
    public void ThePanelBrackets_AreFourDrawnCorners()
    {
        var template = SharedResource("ControlTemplate", "PanelBrackets");
        var paths = template.Descendants()
            .Where(e => e.Name.LocalName == "Path")
            .ToArray();

        Assert.Equal(4, paths.Length);
        var corners = paths
            .Select(p => ((string?)p.Attribute("HorizontalAlignment"),
                (string?)p.Attribute("VerticalAlignment")))
            .ToHashSet();
        Assert.Equal(4, corners.Count);

        Assert.DoesNotContain(template.Descendants(),
            e => e.Name.LocalName is "Image" or "ImageBrush");
        var data = SetterChain("PanelBracket").GetValueOrDefault("Data");
        Assert.True(data?.StartsWith("M", StringComparison.Ordinal) == true,
            $"the bracket's Data is \"{data}\" — the geometry is meant to be " +
            "path mini-language, so the corner is drawn from numbers this " +
            "repo owns rather than lifted out of the reference image");
    }

    /// The brackets' own contrast check, and the reason it exists rather
    /// than being waved through.
    ///
    /// The variant gate raised the horizon glow until the bare atmosphere
    /// measured 4.5068:1 against a 4.5 floor — a margin of seven
    /// thousandths. The rule recorded in AtmosphereLayer.cs is that nothing
    /// arriving after it may add light to what BrightestComposite() sees,
    /// and that any LIT panel edge carries its own stated check.
    ///
    /// The brackets obey the first half by construction: they are drawn
    /// inside the panel, on an opaque Surface fill, so no bracket pixel is
    /// ever composited over bare sky, rain, grid or glow. What is left to
    /// prove is the second half — that a turquoise mark on that fill is
    /// actually visible — and 3:1 is the floor WCAG 1.4.11 sets for a
    /// graphical object, which is what a bracket is. Both ends are READ from
    /// the dictionaries: a hex copied in here would certify the palette it
    /// was copied from forever.
    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void TheCornerBrackets_StandOutAgainstThePanelTheySitOn(string theme)
    {
        var stroke = ResourceKey.Match(
            SetterChain("PanelBracket").GetValueOrDefault("Stroke") ?? "").Groups[1].Value;
        var fill = ResourceKey.Match(
            SetterChain("CockpitPanel").GetValueOrDefault("Background") ?? "").Groups[1].Value;
        Assert.NotEqual("", stroke);
        Assert.NotEqual("", fill);

        var ratio = Contrast.Ratio(ThemeSource.ColorOf(theme, stroke),
            ThemeSource.ColorOf(theme, fill));
        Assert.True(ratio >= 3.0,
            $"{theme}: the corner bracket ({stroke}) on the panel it sits on " +
            $"({fill}) is {ratio:F2}:1 — under the 3:1 floor a graphical " +
            "object has to clear, so the panel's lit edge is decoration " +
            "nobody can see");
    }

    /// Where the keyboard goes, which is the bill for making CockpitPanel a
    /// templated ContentControl rather than a Style on Border.
    ///
    /// Control overrides Focusable to true and IsTabStop defaults true, so
    /// the six panels that used to be plain Borders — every finding card,
    /// the completion report and Depolama's three cards — became keyboard
    /// tab stops the moment they adopted the panel, each one wearing the
    /// dotted marching-ants rectangle that round 11 wrote Win11FocusVisual to
    /// remove. A keyboard user reached six inert plates before reaching a
    /// control that did anything. Nothing in the suite noticed: a style makes
    /// no claim a running test watches, and every page still laid out and
    /// photographed exactly as before. It is the class of change the brief
    /// said to stop for, and I shipped it as "appearance".
    ///
    /// Asserted on a BUILT card rather than on two setters, and in both
    /// directions: the panel is out of the tab order, and it did not take the
    /// card with it. Reading the setters alone would be satisfied by an
    /// over-broad fix that locked the keyboard out of the card's own
    /// controls, which is a worse defect than the one being fixed.
    ///
    /// The second direction takes THREE properties, and picking the wrong one
    /// is a mistake this test already shipped once: it asserted only
    /// expander.IsTabStop, which NO edit to CockpitPanel can flip, because
    /// Focusable and IsTabStop do not inherit. The guard named an adversary
    /// it could not see. The two ways a panel really can drag its contents
    /// out of the tab order both travel DOWN the tree:
    ///
    ///   * TabNavigation=None on the panel makes Tab skip the whole subtree
    ///     while every IsTabStop under it stays true. It is one plausible
    ///     hardening line away, and it would leave every finding row
    ///     impossible to open from the keyboard.
    ///   * IsEnabled=False inherits, so it disables the subtree outright.
    ///
    /// The expander then speaks for itself, and that takes three properties
    /// too. Tab does not stop on an element unless IsTabStop, Focusable and
    /// IsEnabled ALL hold, and this guard asserted the first of the three
    /// only — from the round that wrote it (7b56434), through the round that
    /// widened the two panel-side directions and kept "the expander as the
    /// third" (1134196), until the whole-branch review ruled it
    /// must-fix-before-merge. A lone Focusable="False" on the ToggleButton is
    /// the way in, and it is not hypothetical shape: this app's XAML carries
    /// Focusable="False" on six elements today and exactly one of them says
    /// IsTabStop="False" beside it, so the lone attribute is the common form
    /// and this ToggleButton is one paste from wearing it — Tab unable to
    /// open a single finding row, and the assertion below it green.
    ///
    /// Both new lines were watched red on that ToggleButton in Shared.xaml,
    /// one plant each. Focusable="False" reported "the expander has
    /// Focusable=False", with the IsTabStop assertion above it still passing:
    /// the defect in one picture. IsEnabled="False" reported "the expander is
    /// disabled", which the PANEL's IsEnabled line above does not see — that
    /// one catches the value only when it arrives by inheritance.
    ///
    /// Visibility is a fourth condition and is deliberately not asserted: a
    /// Collapsed expander would pass all three lines. Reasoned from WPF's
    /// rules, not planted.
    [Fact]
    public void ThePanel_IsNoKeyboardStop_ButItsExpanderStillIs()
    {
        var row = ARow();

        SnapshotRenderer.OnUiThread(() =>
        {
            var card = BuildCard(row);
            var panel = ThePanelIn(card);

            Assert.False(panel.IsTabStop,
                "the card root is a keyboard tab stop " + Dash +
                "the panel is an inert plate, and Tab walks the user onto it " +
                "before it reaches anything that does something");
            Assert.False(panel.Focusable,
                "the card root is focusable " + Dash +
                "which is also what puts WPF's default dotted focus " +
                "rectangle on it, the one artefact Win11FocusVisual exists " +
                "to keep off this app");

            // Tab has to keep DESCENDING into the panel, and this is the one
            // the expander cannot answer for: TabNavigation=None here would
            // skip the whole subtree with every IsTabStop under it left true.
            Assert.Equal(KeyboardNavigationMode.Continue,
                KeyboardNavigation.GetTabNavigation(panel));
            // IsEnabled, unlike the two above, DOES inherit — so a panel
            // that took this one down would take the whole card down with it.
            Assert.True(panel.IsEnabled,
                "the panel is disabled " + Dash + "IsEnabled inherits, so the " +
                "expander and both buttons under it went with it");

            // Three assertions rather than one &&, each naming the attribute
            // it reads, so the one that fails is the one the message names
            // and the other two are still reported on the next run.
            var expander = Descendants(card).OfType<ToggleButton>().First();
            Assert.True(expander.IsTabStop,
                "the expander has IsTabStop=False " + Dash + "the row can no " +
                "longer be opened from the keyboard at all");
            Assert.True(expander.Focusable,
                "the expander has Focusable=False " + Dash + "Tab does not " +
                "stop on what cannot take focus, so IsTabStop above being " +
                "true buys nothing and the row can no longer be opened from " +
                "the keyboard at all");
            Assert.True(expander.IsEnabled,
                "the expander is disabled " + Dash + "a disabled control is " +
                "not a tab stop either, and every finding's evidence sits " +
                "behind this one control");
        });
    }

    /// The one test here that builds anything, and it is the one thing the
    /// reskin could genuinely have broken.
    ///
    /// Adopting the panel changed the card's ROOT ELEMENT, from a Border to a
    /// templated ContentControl, and the template's triggers reach five of
    /// its children by TargetName. TargetName is resolved when a trigger
    /// FIRES — not at parse, not at layout — and no trigger on this card
    /// fires in its resting state. So a name that stopped resolving would sit
    /// green through every unit test, every page render and every snapshot in
    /// this repo, and announce itself as an unhandled exception the first
    /// time a user pressed Fix and watched it succeed.
    ///
    /// The Fixed morph is the trigger that names all five, so the row is
    /// driven into it. The Working pulse is deliberately NOT fired: it is the
    /// app's one Forever animation and it names nothing the morph does not.
    [Fact]
    public void TheFixedMorph_StillReachesTheCardsChildren()
    {
        var row = ARow();

        SnapshotRenderer.OnUiThread(() =>
        {
            var card = BuildCard(row);

            // Raises IsFixed, which applies the morph's setters and begins its
            // storyboards — all of which have to find their targets first.
            row.CompleteFix(true);
            card.UpdateLayout();

            // And the setter's own effect, so this is not merely "it did not
            // throw": a card that has been fixed stops offering to fix it.
            var fix = (Button)CardTemplate().FindName("FixButton", card);
            Assert.False(fix.IsEnabled);
        });
    }

    /// An em dash, as a constant, because these messages are built by
    /// concatenation and a bare one inside a string literal reads as a
    /// hyphen at this width.
    private const string Dash = "— ";

    /// One finding row, ready to be dressed as a card.
    private static FindingRow ARow()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        return new FindingRow(TestData.Finding("power-plan"), loc,
            canUndo: false, _ => { }, _ => { });
    }

    private static DataTemplate CardTemplate() =>
        (DataTemplate)Application.Current.Resources["FindingCard"];

    /// A bare ContentPresenter is what an ItemsControl would put the row in
    /// anyway, and it is the FrameworkElement the template's namescope hangs
    /// off — so a FindName through it is the same lookup a trigger performs,
    /// rather than a lookalike. Runs on the harness UI thread.
    private static ContentPresenter BuildCard(FindingRow row)
    {
        var card = new ContentPresenter
        {
            Content = row,
            ContentTemplate = CardTemplate(),
        };
        OffscreenLayout.LayOut(card, new Size(720, 200));
        return card;
    }

    /// The one element in the built card that wears CockpitPanel. Found by
    /// walking the style's BasedOn chain rather than by type: ButtonBase is a
    /// ContentControl too, so "the ContentControl in there" would have been
    /// the expander and both of its buttons as well as the panel.
    private static ContentControl ThePanelIn(DependencyObject card)
    {
        var panel = (Style)Application.Current.Resources["CockpitPanel"];
        return Descendants(card).OfType<ContentControl>()
            .Single(c => Wears(c.Style, panel));
    }

    private static bool Wears(Style? worn, Style panel)
    {
        for (var style = worn; style is not null; style = style.BasedOn)
            if (ReferenceEquals(style, panel)) return true;
        return false;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var found in Descendants(VisualTreeHelper.GetChild(root, i)))
                yield return found;
    }

    // ------------------------------------------------------------------

    /// The style keyed `key` in Shared.xaml and everything it is BasedOn,
    /// nearest first.
    private static (string[] Keys, XElement[] Styles) StyleChain(string? key)
    {
        var keys = new List<string>();
        var styles = new List<XElement>();
        for (var at = string.IsNullOrEmpty(key) ? null : key; at is not null;)
        {
            keys.Add(at);
            var style = SharedResource("Style", at);
            styles.Add(style);
            at = ResourceKey.Match((string?)style.Attribute("BasedOn") ?? "")
                .Groups[1].Value is { Length: > 0 } parent ? parent : null;
        }
        return (keys.ToArray(), styles.ToArray());
    }

    /// Every setter a style ends up applying, nearest-wins.
    private static Dictionary<string, string> SetterChain(string key) =>
        Collapse(StyleChain(key).Styles);

    private static Dictionary<string, string> Collapse(IEnumerable<XElement> styles)
    {
        var setters = new Dictionary<string, string>(StringComparer.Ordinal);
        // Reversed so the nearest style in the chain is written last and wins,
        // which is the order WPF resolves BasedOn in.
        foreach (var style in styles.Reverse())
        foreach (var setter in style.Elements().Where(e => e.Name.LocalName == "Setter"))
            setters[(string)setter.Attribute("Property")!] =
                (string?)setter.Attribute("Value") ?? "";
        return setters;
    }

    /// The style an element wears, whether it names one on the Style
    /// attribute or declares an inline one that is BasedOn a shared key.
    private static string? StyleKeyOf(XElement element)
    {
        var named = ResourceKey.Match((string?)element.Attribute("Style") ?? "")
            .Groups[1].Value;
        if (named.Length > 0) return named;

        var inline = InlineStyleOf(element);
        return inline is null
            ? null
            : ResourceKey.Match((string?)inline.Attribute("BasedOn") ?? "").Groups[1].Value;
    }

    /// The <X.Style> a use site declares inline, if it declares one.
    ///
    /// It has to be handed to Collapse alongside the Shared.xaml chain, and
    /// forgetting to was a real hole rather than a hypothetical one: the
    /// heading guard's comment claimed the wrap was re-asserted through the
    /// page style while only the shared chain was ever read, so a
    /// TextTrimming setter planted in a page-local style trimmed the Turkish
    /// heading with the guard still green — and the failure message the
    /// guard would never print says "a style on the page put the ellipsis
    /// back". A guard that asserts less than its comment claims is worse
    /// than no comment; this wave has been burned by one already.
    private static XElement? InlineStyleOf(XElement element) =>
        element.Elements()
            .SingleOrDefault(e => e.Name.LocalName.EndsWith(".Style", StringComparison.Ordinal))
            ?.Elements().SingleOrDefault(e => e.Name.LocalName == "Style");

    /// The whole cascade an element actually ends up wearing: its inline
    /// style first, because that is the nearest one, then everything the
    /// inline style is BasedOn.
    private static Dictionary<string, string> CascadeOf(
        XElement element, IEnumerable<XElement> chain)
    {
        var inline = InlineStyleOf(element);
        return Collapse(inline is null ? chain : chain.Prepend(inline));
    }

    /// The half of the impact-meter suppression that lives in markup, and the
    /// half nothing else can see.
    ///
    /// FindingRow.ShowsImpact is only worth anything if the card actually
    /// binds the meter's visibility to it. A binding path that still names
    /// IsAdvise, or names nothing, fails SILENTLY in WPF: the trigger never
    /// matches, the Setter never runs, and ●○○○○ goes on rendering over every
    /// privacy row — a meter claiming a measurement nobody made, on the one
    /// page whose subject is brisk not claiming what it did not read.
    ///
    /// Named as a property, not merely "some trigger": the reflection line is
    /// what survives the day the view-model tests stop holding the name for
    /// the compiler. Watched red by pointing the trigger back at IsAdvise:
    /// `the card collapses its impact meter on "IsAdvise", not on ShowsImpact`.
    [Fact]
    public void TheImpactMeter_BindsItsVisibilityToThePropertyThatDecidesIt()
    {
        const string property = "ShowsImpact";

        Assert.True(typeof(FindingRow).GetProperty(property) is { } p
                    && p.PropertyType == typeof(bool),
            $"FindingRow exposes no bool {property} for the card to bind");

        var dots = SharedResource("DataTemplate", "FindingCard").Descendants()
            .Single(e => (string?)e.Attribute(X + "Name") == "ImpactDots");
        var collapses = dots.Descendants()
            .Where(e => e.Name.LocalName == "DataTrigger")
            .Where(t => t.Elements().Any(s => s.Name.LocalName == "Setter"
                && (string?)s.Attribute("Property") == "Visibility"
                && (string?)s.Attribute("Value") == "Collapsed"))
            .ToArray();

        // Counted rather than Single()d: "none" and "two" are different
        // mistakes, and Single throws where it should report.
        Assert.True(collapses.Length == 1,
            $"the impact meter has {collapses.Length} triggers that collapse " +
            "it, and exactly one property decides whether a row has an impact " +
            "reading to show");
        var binding = (string?)collapses[0].Attribute("Binding") ?? "";
        Assert.True(binding.Contains(property, StringComparison.Ordinal),
            $"the card collapses its impact meter on \"{binding}\", not on " +
            $"{property} — a binding path WPF cannot resolve never matches, " +
            "so the meter keeps rendering and nothing fails");
    }

    // ------------------------------------------------------------------
    // The read-back dot, across a theme switch.
    // ------------------------------------------------------------------

    /// A colour a view model NAMES and the theme RESOLVES has to be resolved
    /// again when the theme changes, and the read-back dot was not.
    ///
    /// It painted its Fill through a value converter, which read the
    /// application's resources once per binding evaluation. ThemeManager
    /// .Apply clears and re-adds the merged dictionaries, and nothing
    /// re-evaluates a converter binding when a dictionary changes — so every
    /// dot kept the previous theme's brush until the next scan happened to
    /// rebuild the rows. The two palettes genuinely disagree here (Good is
    /// #4ADE80 dark and #16A34A light; SeverityWarning #FBBF24 and #B45309),
    /// so what stood on screen after a switch was the other theme's colour on
    /// a page of verdicts. App.xaml.cs makes this same argument about the tray
    /// icon in as many words: a theme switch has to reach every mark brisk
    /// draws, not only the ones inside the dictionary.
    ///
    /// EveryReadBackColour_IsAKeyBothThemesCarry cannot see it. That one asks
    /// whether the KEY is in each dictionary, and both dictionaries held it
    /// the whole time.
    ///
    /// The swap is performed on a dictionary in the page's own scope — the
    /// same clear-and-re-add ThemeManager performs, without pulling the theme
    /// out from under every other test sharing this process. EVERY dot is
    /// read, in BOTH directions, because a mechanism that happens to be right
    /// about the one green dot says nothing about the amber ones.
    [Fact]
    public void EveryReadBackDot_WearsTheThemeThatIsInstalledNow()
    {
        SnapshotRenderer.OnUiThread(() =>
        {
            var window = SnapshotTests.CockpitWindow();
            var vm = (PrivacyViewModel)
                ((FrameworkElement)window.FindName("PrivacyView")!).DataContext;
            var page = new PrivacyPage();
            page.Bind(vm);
            var host = new Border { Child = page, Resources = new ResourceDictionary() };

            // Light first, so the very first resolution is already something
            // other than the dictionary the Application is holding — a dot
            // that reads the app's resources instead of its own scope is
            // wrong before anything has been switched at all.
            foreach (var theme in new[] { "Light.xaml", "Dark.xaml", "Light.xaml" })
            {
                Wear(host, theme);
                OffscreenLayout.LayOut(host, new Size(1000, 1400));

                // Selected by the ROW behind each one, not by shape: every
                // finding card on this page draws a severity dot of its own,
                // and "the Ellipses in the tree" is eighteen of them.
                var dots = Descendants(host).OfType<System.Windows.Shapes.Ellipse>()
                    .Where(e => e.DataContext is ReadBackRow)
                    .ToList();
                Assert.True(dots.Count == vm.ReadBackRows.Count,
                    $"{dots.Count} dots in the read-back block for " +
                    $"{vm.ReadBackRows.Count} lines — this test reads them " +
                    "pairwise and cannot say anything if they do not pair");

                for (var i = 0; i < dots.Count; i++)
                {
                    var row = vm.ReadBackRows[i];
                    var expected = ThemeSource.ColorOf(theme, row.StateBrushKey);
                    Assert.True(dots[i].Fill is SolidColorBrush,
                        $"the {row.State} dot paints nothing at all under " +
                        $"{theme}: its Fill is {dots[i].Fill?.ToString() ?? "null"}");
                    Assert.True(((SolidColorBrush)dots[i].Fill).Color == expected,
                        $"the {row.State} dot is wearing " +
                        $"{((SolidColorBrush)dots[i].Fill).Color} under {theme}, " +
                        $"and {theme}'s \"{row.StateBrushKey}\" is {expected} — " +
                        "the dot resolved its key against a palette that is no " +
                        "longer installed, which is a verdict rendered in the " +
                        "other theme's colour");
                }
            }
        });
    }

    /// One theme dictionary in an element's own scope, replacing whatever was
    /// there — the same clear-and-re-add ThemeManager.Apply performs on the
    /// application's, which is the operation whose effect is under test. The
    /// ";component" URI names brisk-app for the reason SnapshotRenderer gives:
    /// the short form resolves against the entry assembly, which under the
    /// test host is the runner.
    private static void Wear(FrameworkElement element, string themeFile)
    {
        element.Resources.MergedDictionaries.Clear();
        element.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/brisk-app;component/Theming/{themeFile}"),
        });
    }

    /// The markup half of the Recall link, and the half nothing else can
    /// see. The view model can be as right as it likes about
    /// HasWindowsSettingAction: if no Button in the card binds
    /// OpenWindowsSettingCommand, the link does not exist on screen and every
    /// view-model test still passes — which is precisely the state the page
    /// shipped in, with the spec's sentence about Recall half-built and
    /// nothing red.
    ///
    /// Both directions are asserted for the same reason the impact meter's
    /// guard above asserts both: a Button with no Visibility binding renders
    /// on every card in the app, and a Visibility bound to a path WPF cannot
    /// resolve never becomes Collapsed either.
    [Fact]
    public void TheWindowsSettingLink_BindsItsVisibilityToThePropertyThatDecidesIt()
    {
        const string property = "HasWindowsSettingAction";

        Assert.True(typeof(FindingRow).GetProperty(property) is { } p
                    && p.PropertyType == typeof(bool),
            $"FindingRow exposes no bool {property} for the card to bind");

        var links = SharedResource("DataTemplate", "FindingCard").Descendants()
            .Where(e => e.Name.LocalName == "Button"
                && ((string?)e.Attribute("Command") ?? "")
                    .Contains("OpenWindowsSettingCommand", StringComparison.Ordinal))
            .ToArray();

        // Counted rather than Single()d: "none" is the link never having been
        // built and "two" is two controls over one command, and those are
        // different mistakes to have made.
        Assert.True(links.Length == 1,
            $"{links.Length} Buttons in the finding card bind " +
            "OpenWindowsSettingCommand; the Recall row's one link to Windows' " +
            "own setting is what this test is about");
        var visibility = (string?)links[0].Attribute("Visibility") ?? "";
        Assert.True(visibility.Contains(property, StringComparison.Ordinal),
            $"the link binds its Visibility to \"{visibility}\", not to " +
            $"{property} — a binding path WPF cannot resolve never matches, so " +
            "the link renders on every card in the app, pointing at a Windows " +
            "setting the row never named");
    }

    private static XElement SharedResource(string kind, string key) =>
        SharedXaml().Elements().Single(e =>
            e.Name.LocalName == kind && (string?)e.Attribute(X + "Key") == key);

    private static XElement SharedXaml() =>
        XDocument.Load(Path.Combine(BriskDir(), "Theming", "Shared.xaml")).Root!;

    private static string BriskDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return Path.Combine(dir.FullName, "src", "Brisk");
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }
}
