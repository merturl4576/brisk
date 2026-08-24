using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
/// behaviour. Almost nothing here renders: a `Style` in a resource dictionary
/// makes no claim a running test can watch, and every defect below ships
/// green. (The one exception is the last test, which has to build a card;
/// its comment says why.)
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
///     driven rather than read, and it is the last test in the file.
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
    /// The wrap claim is re-asserted through the page's OWN style too, since
    /// a page-local `BasedOn` style is free to add setters of its own.
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

        var setters = Collapse(chain.Styles);
        Assert.Equal("Wrap", setters.GetValueOrDefault("TextWrapping"));
        Assert.False(setters.ContainsKey("TextTrimming"),
            $"{page}'s [{key}] heading trims after all — a style on the page " +
            "put the ellipsis back that PanelHeader was written to keep out");
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
        // attribute beats a style setter in WPF, so a leftover Background=
        // here would silently win over CockpitPanel and the card would go on
        // wearing whatever it wore before while claiming to have joined.
        foreach (var overridden in new[]
                 { "Background", "BorderBrush", "BorderThickness", "CornerRadius" })
            Assert.True(root.Attribute(overridden) is null,
                $"{template}'s panel sets {overridden} locally, which beats the " +
                "CockpitPanel setter it is supposed to be taking");
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
    /// directions: the panel is out of the tab order AND the expander inside
    /// it is still in it. Reading the setters alone would be satisfied by a
    /// panel that had taken the card's own controls out of the tab order with
    /// it, which is a worse defect than the one being fixed.
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

            var expander = Descendants(card).OfType<ToggleButton>().First();
            Assert.True(expander.IsTabStop,
                "the panel took the card's own expander out of the tab order " +
                "with it " + Dash + "the row can no longer be opened from the " +
                "keyboard at all");
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

        var inline = element.Elements()
            .SingleOrDefault(e => e.Name.LocalName.EndsWith(".Style", StringComparison.Ordinal))
            ?.Elements().SingleOrDefault(e => e.Name.LocalName == "Style");
        return inline is null
            ? null
            : ResourceKey.Match((string?)inline.Attribute("BasedOn") ?? "").Groups[1].Value;
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
