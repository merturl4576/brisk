using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Brisk.Tests;

/// The cockpit shell is the one part of brisk a unit test cannot watch work.
/// WindowChrome's two classic mistakes are not behaviours, though — they are
/// CONFIGURATION, and configuration is readable. Both fail silently in a test
/// run and loudly in a user's hands:
///
///   * a title-bar control without IsHitTestVisibleInChrome is dead to
///     clicks, because the caption area swallows the input before WPF ever
///     sees it — the close button simply does nothing;
///   * a maximized WindowChrome window is extended past every screen edge by
///     the resize border, so without a WindowState-driven content margin the
///     window's own edges sit off-screen.
///
/// And one thing that is not a WindowChrome trap at all but died the same
/// quiet death: for two commits the nav's selected pill and the rail behind
/// it were both painted Surface, so "which page am I on" was unanswerable
/// from the screen. That is asserted here too, against the same source.
public sealed class ShellSourceTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// "{DynamicResource Foo}" -> "Foo".
    private static readonly Regex ResourceKey =
        new(@"\{(?:Dynamic|Static)Resource\s+([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    /// A maximized WindowChrome window is extended ~7px past each screen edge,
    /// so content needs a WindowState-driven margin; and a title-bar control
    /// without IsHitTestVisibleInChrome is dead to clicks. Both are silent
    /// failures in a unit test and obvious ones in a user's hands.
    [Fact]
    public void TitleBarInteractives_AreHitTestVisibleInChrome()
    {
        var window = MainWindowXaml();

        var titleBar = window.Descendants()
            .SingleOrDefault(e => (string?)e.Attribute(X + "Name") == "TitleBar");
        Assert.True(titleBar is not null,
            "MainWindow.xaml has no element named TitleBar — with WindowChrome " +
            "in charge of the caption, the window draws its own title bar or " +
            "it has none at all");

        var buttons = titleBar!.Descendants()
            .Where(e => e.Name.LocalName == "Button")
            .ToArray();
        Assert.NotEmpty(buttons);
        foreach (var button in buttons)
            Assert.True(IsHitTestVisibleInChrome(button),
                "a Button in the title bar carries no " +
                "WindowChrome.IsHitTestVisibleInChrome=\"True\" — the caption " +
                "area swallows its clicks and the control is dead");

        var chrome = window.Descendants()
            .SingleOrDefault(e => e.Name.LocalName == "WindowChrome");
        Assert.True(chrome is not null,
            "no WindowChrome declared — WindowStyle=None + AllowsTransparency " +
            "is the route that has to reimplement snap, resize and the system " +
            "menu by hand, and is not the one taken here");
        var caption = double.Parse((string)chrome!.Attribute("CaptionHeight")!,
            CultureInfo.InvariantCulture);
        Assert.True(caption > 0,
            "CaptionHeight is zero — nothing in the title bar would drag the " +
            "window, and double-click-to-maximize would be gone with it");
    }

    /// The maximize overhang. The root content's Margin has to READ the
    /// window state; a constant margin is the same bug wearing a number.
    [Fact]
    public void RootContentMargin_FollowsWindowState()
    {
        var root = MainWindowXaml().Elements()
            .Single(e => !e.Name.LocalName.Contains('.'));
        var margin = (string?)root.Attribute("Margin");

        Assert.True(margin is not null,
            "the root content element has no Margin — a maximized " +
            "WindowChrome window is extended past every screen edge, so its " +
            "content has to be pushed back in by that much");
        Assert.Contains("WindowState", margin!, StringComparison.Ordinal);
        // Named, not merely present. "reads WindowState through SOME converter"
        // is satisfied by every converter in the app, and MaximizedMarginTests
        // only proves what MaximizedMargin answers when something asks it — so
        // between the two of them a window wired to the wrong converter passed
        // both. This is the only piece of the shell whose correctness rests
        // entirely on its own arithmetic, since nobody has yet maximized a
        // real brisk window and looked at it.
        Assert.Contains("MaximizedMargin", margin!, StringComparison.Ordinal);
    }

    /// The rail is gone, so a tile floats directly on the atmosphere and has
    /// to answer "which page am I on" by itself. Both answers — hover and
    /// selection — must differ from the tile's resting fill AND from the
    /// ground behind it, in BOTH themes: the regression this refuses shipped
    /// as two keys that had quietly become the same colour.
    ///
    /// "The ground" is two keys, not one. The atmosphere is a gradient from
    /// Bg0 at the window's edges down to Bg, and the tiles sit high on the
    /// left where the SKY is — so a fill that drifted toward Bg0 would dim
    /// the tiles into their own background while a Bg-only check waved it
    /// through. Both ends of the gradient are checked.
    [Fact]
    public void NavTile_HoverAndSelection_AreVisibleAgainstTheGround()
    {
        var template = NavRadioTemplate();
        var tile = template.Descendants()
            .Single(e => (string?)e.Attribute(X + "Name") == "Tile");
        var rest = (string)tile.Attribute("Background")!;

        foreach (var state in new[] { "IsMouseOver", "IsChecked" })
        {
            var fill = TileSetters(template, state)
                .Where(s => (string?)s.Attribute("Property") == "Background")
                .Select(s => (string)s.Attribute("Value")!)
                .Single();

            Assert.NotEqual(rest, fill);
            var key = ResourceKey.Match(fill).Groups[1].Value;
            Assert.NotEqual("", key);
            foreach (var theme in new[] { "Dark.xaml", "Light.xaml" })
            foreach (var ground in new[] { "Bg", "Bg0" })
                Assert.NotEqual(ThemeSource.ColorOf(theme, ground),
                    ThemeSource.ColorOf(theme, key));
        }

        // Hover and selection must not be the same picture either: a user
        // sweeping the pointer down the nav would otherwise see every tile
        // look selected and never learn which page is actually showing.
        var hovered = TileSetters(template, "IsMouseOver")
            .Select(s => (string?)s.Attribute("Property"))
            .ToHashSet(StringComparer.Ordinal);
        var selected = TileSetters(template, "IsChecked")
            .Select(s => (string?)s.Attribute("Property"))
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(selected.Except(hovered, StringComparer.Ordinal));
    }

    /// The tile's glow is a DropShadowEffect, and an effect glows by Color —
    /// it cannot be handed a Brush. So each theme carries the accent glow
    /// twice: once as the AccentGlow brush the atmosphere binds, and once as
    /// the AccentGlowColor that Dark.xaml's effects are built from. Two
    /// copies of one value is a drift waiting to happen, and a drifted glow
    /// would be a second turquoise nobody chose — so they are pinned to each
    /// other. Light keeps its copy of the pair even though nothing there
    /// builds an effect from it any more (see the test below): the pin is
    /// what stops the two from drifting apart while nobody is looking.
    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void AccentGlow_BrushAndColor_AgreeInEachTheme(string theme)
    {
        Assert.Equal(ThemeValue(theme, "AccentGlow"),
            ThemeValue(theme, "AccentGlowColor"));
    }

    /// "Whether there is a glow at all is a theme decision, and the light
    /// dictionary answers no" — Dark.xaml has said that since the round that
    /// set the nav tiles floating. For that whole round the light dictionary
    /// could not actually say it, and this is the check that it now can.
    ///
    /// A DropShadowEffect CANNOT be switched off by its Color. WPF reads the
    /// RGB of that Color and ignores its alpha, so the #00000000 that stood
    /// in those keys was pure BLACK at the effect's own Opacity: the light
    /// theme wore a soft black halo on the title-bar mark and on the selected
    /// nav tile, and every render in the round was dark, so nothing here
    /// could see it. A photograph caught it. This is what stops it coming
    /// back, since restoring the effect is a two-line edit that breaks
    /// nothing and looks like a tidy-up.
    ///
    /// What is asserted is the DECLARATION and nothing past it: in Light.xaml
    /// both keys are x:Null, in Dark.xaml both are still DropShadowEffects.
    /// It makes no claim about rendered pixels — that a null resource leaves
    /// the Effect property null is WPF's business, and it was settled once,
    /// by re-rendering the light cockpit and measuring the halo gone rather
    /// than by asserting it here.
    ///
    /// The Dark half is not padding. Nulling BOTH dictionaries would satisfy
    /// a light-only assertion perfectly while deleting the glow from the
    /// theme whose whole atmosphere is built on having one.
    [Fact]
    public void TheGlowKeys_AreNullInLight_AndStillEffectsInDark()
    {
        foreach (var key in new[] { "GlowSoft", "GlowStrong" })
        {
            Assert.Equal(X + "Null", ThemeDeclaration("Light.xaml", key).Name);
            Assert.Equal("DropShadowEffect",
                ThemeDeclaration("Dark.xaml", key).Name.LocalName);
        }
    }

    /// The signature accent is the PALETTE's, not the desktop's. ThemeManager
    /// used to finish Apply() by writing the Windows accent colour over
    /// AccentBrush and AccentTextBrush, which meant the value in Dark.xaml or
    /// Light.xaml was a fallback that nobody with a configured desktop ever
    /// saw — and the variant gate's renders were pictures of a colour no user
    /// had.
    ///
    /// It is asserted from SOURCE because there is no behaviour to watch:
    /// re-adding two dictionary writes is a three-line change that breaks
    /// nothing, fails no existing test, and quietly hands the choice of
    /// brisk's decorative colour back to whoever configured Windows.
    [Fact]
    public void ThemeManager_DoesNotHandTheSignatureBackToTheDesktop()
    {
        // Comments explain why the injection is gone, so only the CODE lines
        // are searched — otherwise the explanation would fail the test.
        var code = File.ReadAllLines(
                Path.Combine(BriskDir(), "Theming", "ThemeManager.cs"))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .ToArray();

        foreach (var key in new[] { "AccentBrush", "AccentTextBrush" })
            Assert.False(code.Any(line =>
                    line.Contains($"Resources[\"{key}\"]", StringComparison.Ordinal)),
                $"ThemeManager assigns {key} again. The signature accent is the " +
                "theme dictionary's on purpose: a colour carries a claim in this " +
                "product, and a desktop accent brisk did not choose can land on " +
                "top of a severity colour — see the comment in Apply()");
    }

    /// The product rule, finally fenced: the decorative signature may never
    /// wear a claim colour. It could not be asserted while Windows chose the
    /// accent, because the value under test arrived at runtime from a machine
    /// the test does not run on. Pinning the accent is what made this
    /// checkable, and this is the check.
    ///
    /// The distance is crude Euclidean RGB on purpose. It is a fence, not a
    /// colour-science claim: exact inequality alone would have let a near-miss
    /// through, and a near-miss is the same defect with better manners. The
    /// tightest real pair today is light AccentBrush #0F6E7E against
    /// SeverityInfo #0067C0, at 68 — so 60 leaves room to move a value
    /// without leaving room to recreate the collision.
    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void TheSignatureAccent_NeverWearsAClaimColour(string theme)
    {
        var accent = ThemeValue(theme, "AccentBrush");

        foreach (var claim in new[]
                 { "SeverityInfo", "SeverityWarning", "SeverityCritical", "Good" })
        {
            var value = ThemeValue(theme, claim);
            Assert.False(string.Equals(accent, value, StringComparison.OrdinalIgnoreCase),
                $"{theme}: AccentBrush and {claim} are both {value} — one colour " +
                "carrying a claim and decoration at once, which is the collision " +
                "the palette was retuned to end");
            var distance = RgbDistance(accent, value);
            Assert.True(distance >= 60,
                $"{theme}: AccentBrush {accent} sits {distance:F0} from {claim} " +
                $"{value} — close enough to be mistaken for it, which is the " +
                "same defect as sharing the value");
        }
    }

    /// Straight-line distance between two "#RRGGBB" strings in RGB. Max 441.
    private static double RgbDistance(string a, string b)
    {
        static int[] Channels(string hex) =>
            Enumerable.Range(0, 3)
                .Select(i => int.Parse(hex.TrimStart('#').Substring(i * 2, 2),
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture))
                .ToArray();
        return Math.Sqrt(Channels(a).Zip(Channels(b), (x, y) => (x - y) * (x - y)).Sum());
    }

    /// A theme switch has to move BOTH of brisk's marks, and only one of
    /// them lives on a window. The title bar is refreshed in the theme
    /// callback; the tray icon is drawn once at startup from the palette
    /// installed at the time, so without a refresh beside it the notification
    /// area keeps yesterday's colour and the two marks describe different
    /// themes — which is the exact defect that pinning the accent was meant
    /// to end, one step out of the window.
    ///
    /// It is a source fact because the callback is a lambda handed to a view
    /// model inside OnStartup: there is no seam to drive and no visible
    /// consequence in a test run, and the failure is a wrong colour in a
    /// place no assertion looks.
    [Fact]
    public void TheThemeSwitchCallback_RefreshesTheTrayMarkToo()
    {
        var body = LambdaBody(
            File.ReadAllText(Path.Combine(BriskDir(), "App.xaml.cs")),
            "themeSetting =>");

        Assert.True(body.Contains("ApplyTitleBar", StringComparison.Ordinal),
            "the theme-change callback no longer refreshes the title bar: " + body);
        Assert.True(body.Contains("SetAccent", StringComparison.Ordinal),
            "the theme-change callback refreshes the title bar but not the " +
            "tray icon, so after an in-session dark/light switch brisk's mark " +
            "in the notification area still carries the previous theme's " +
            "accent while the mark in the title bar carries the new one: " + body);
    }

    /// The braced body of the lambda that starts at `marker`, brace-balanced
    /// so that reformatting the callback across lines cannot fool the test.
    private static string LambdaBody(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"App.xaml.cs has no lambda starting \"{marker}\"");
        var open = source.IndexOf('{', start);
        Assert.True(open >= 0, $"the lambda at \"{marker}\" has no braced body");

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
                return source[open..(i + 1)];
        }
        throw new InvalidOperationException($"unbalanced braces after \"{marker}\"");
    }

    /// A theme entry's value, whether it sits on the Color attribute (a
    /// SolidColorBrush) or in the element's content (a bare Color).
    private static string ThemeValue(string file, string key) =>
        ((string?)ThemeDeclaration(file, key).Attribute("Color")
            ?? ThemeDeclaration(file, key).Value).Trim();

    /// The dictionary ELEMENT behind a key, rather than the value inside it —
    /// for the one question that is about what KIND of thing a key holds.
    private static XElement ThemeDeclaration(string file, string key) =>
        XDocument.Load(Path.Combine(BriskDir(), "Theming", file)).Root!
            .Elements().Single(e => (string?)e.Attribute(X + "Key") == key);

    /// The setters a template trigger for `state` applies to the tile border.
    private static XElement[] TileSetters(XElement template, string state) =>
        template.Descendants()
            .Single(e => e.Name.LocalName == "Trigger"
                && (string?)e.Attribute("Property") == state
                && (string?)e.Attribute("Value") == "True")
            .Elements()
            .Where(s => s.Name.LocalName == "Setter"
                && (string?)s.Attribute("TargetName") == "Tile")
            .ToArray();

    private static bool IsHitTestVisibleInChrome(XElement element) =>
        element.Attributes().Any(a =>
            a.Name.LocalName.EndsWith("WindowChrome.IsHitTestVisibleInChrome",
                StringComparison.Ordinal)
            && string.Equals(a.Value, "True", StringComparison.OrdinalIgnoreCase));

    private static XElement MainWindowXaml() =>
        XDocument.Load(Path.Combine(BriskDir(), "Windows", "MainWindow.xaml")).Root!;

    private static XElement NavRadioTemplate() =>
        XDocument.Load(Path.Combine(BriskDir(), "Theming", "Shared.xaml")).Root!
            .Elements()
            .Single(e => e.Name.LocalName == "Style"
                && (string?)e.Attribute(X + "Key") == "NavRadio")
            .Descendants()
            .Single(e => e.Name.LocalName == "ControlTemplate");

    private static string BriskDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return Path.Combine(dir.FullName, "src", "Brisk");
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }
}
