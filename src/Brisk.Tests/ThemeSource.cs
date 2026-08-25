using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Brisk.Tests;

/// Reads one colour straight out of a theme .xaml source, the way
/// ThemeDictionaryTests does — no Application, no pack URIs, no WPF thread.
///
/// It exists so that a test asserting something ABOUT a palette value can
/// name the key instead of copying the hex. A copied hex certifies whatever
/// it was copied from, forever: retune the palette and the test keeps passing
/// while describing a colour the app no longer wears. That failure mode is a
/// GREEN test, not a red one, which is the worst kind of guard to own — and
/// the tuning gate that will move these values is the very next task.
internal static class ThemeSource
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// `file` is the dictionary's filename — "Dark.xaml" or "Light.xaml".
    /// Either one can be the live theme, so either one can need asserting.
    internal static Color ColorOf(string file, string key) =>
        (Color)ColorConverter.ConvertFromString(
            XDocument.Load(Path.Combine(ThemingDir(), file)).Root!
                .Elements().Single(e => (string?)e.Attribute(X + "Key") == key)
                .Attribute("Color")!.Value)!;

    private static string ThemingDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return Path.Combine(dir.FullName, "src", "Brisk", "Theming");
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }
}
