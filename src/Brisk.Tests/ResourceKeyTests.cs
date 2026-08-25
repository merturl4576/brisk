using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Brisk.Tests;

/// A {DynamicResource} pointing at a key that does not exist fails SILENTLY:
/// WPF resolves nothing, the property keeps its default, and the element
/// renders transparent — no exception, no error a test run would ever see.
/// So a brush key renamed in the dictionaries and missed at one binding site
/// ships as an invisible panel. This reads the XAML sources the way
/// ThemeDictionaryTests does and refuses that state: every key the app binds
/// to must exist in BOTH themes, because either one can be the live theme.
///
/// "Key", not "brush key": a theme dictionary may hold something that is not
/// a colour when the thing genuinely differs BY theme — FlatAtmosphere is a
/// bool, and it decides whether the atmosphere is drawn at all.
public sealed class ResourceKeyTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Regex DynamicReference =
        new(@"\{DynamicResource\s+([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    private static readonly Regex KeyDeclaration =
        new(@"x:Key=""([A-Za-z0-9_]+)""", RegexOptions.Compiled);

    [Fact]
    public void EveryDynamicResourceKey_ExistsInBothThemes()
    {
        var referenced = ReferencedKeys();
        var shared = SharedOwnKeys();
        var dark = ThemeKeys("Dark.xaml");
        var light = ThemeKeys("Light.xaml");

        var missing = referenced
            .Except(shared, StringComparer.Ordinal)
            .Where(k => !dark.Contains(k) || !light.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
    }

    /// Every .xaml the app ships, not just the pages: styles in Shared.xaml
    /// bind brushes too, and a template that resolves nothing is exactly as
    /// invisible as a page that does.
    private static HashSet<string> ReferencedKeys() =>
        Directory.EnumerateFiles(BriskDir(), "*.xaml", SearchOption.AllDirectories)
            .SelectMany(f => DynamicReference.Matches(File.ReadAllText(f))
                .Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

    /// Shared.xaml declares styles, templates and the always-dark Hero*
    /// family — keys the app MAY legitimately bind by {DynamicResource} and
    /// that have no business in a theme dictionary. "May": today every one of
    /// them is reached by {StaticResource}, so this subtraction is a standing
    /// permission rather than a description of current bindings. Subtracting
    /// what Shared.xaml
    /// actually declares beats an allow-list, which would go stale the first
    /// time a style is renamed.
    private static HashSet<string> SharedOwnKeys() =>
        KeyDeclaration
            .Matches(File.ReadAllText(Path.Combine(BriskDir(), "Theming", "Shared.xaml")))
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// Everything the dictionary declares, whatever its type — what makes a
    /// {DynamicResource} resolve is the key being there, not it being a brush.
    private static HashSet<string> ThemeKeys(string file) =>
        XDocument.Load(Path.Combine(BriskDir(), "Theming", file)).Root!
            .Elements().Select(e => (string?)e.Attribute(X + "Key"))
            .Where(k => k is not null)
            .ToHashSet(StringComparer.Ordinal)!;

    private static string BriskDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return Path.Combine(dir.FullName, "src", "Brisk");
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }
}
