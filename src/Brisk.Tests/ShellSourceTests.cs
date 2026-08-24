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
        Assert.Contains("Converter", margin!, StringComparison.Ordinal);
    }

    /// The rail is gone, so a tile floats directly on the atmosphere and has
    /// to answer "which page am I on" by itself. Both answers — hover and
    /// selection — must differ from the tile's resting fill AND from the
    /// ground behind it, in BOTH themes: the regression this refuses shipped
    /// as two keys that had quietly become the same colour.
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
                Assert.NotEqual(ThemeSource.ColorOf(theme, "Bg"),
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
    /// it cannot be handed a Brush. So each theme now carries the accent glow
    /// twice: once as the AccentGlow brush the atmosphere binds, and once as
    /// the AccentGlowColor the effects are built from. Two copies of one
    /// value is a drift waiting to happen, and a drifted glow would be a
    /// second turquoise nobody chose — so they are pinned to each other.
    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void AccentGlow_BrushAndColor_AgreeInEachTheme(string theme)
    {
        Assert.Equal(ThemeValue(theme, "AccentGlow"),
            ThemeValue(theme, "AccentGlowColor"));
    }

    /// A theme entry's value, whether it sits on the Color attribute (a
    /// SolidColorBrush) or in the element's content (a bare Color).
    private static string ThemeValue(string file, string key)
    {
        var element = XDocument.Load(Path.Combine(BriskDir(), "Theming", file)).Root!
            .Elements().Single(e => (string?)e.Attribute(X + "Key") == key);
        return ((string?)element.Attribute("Color") ?? element.Value).Trim();
    }

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
