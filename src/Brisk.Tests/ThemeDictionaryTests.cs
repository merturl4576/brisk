using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Brisk.Tests;

/// Task 8 contract, checked from source: Dark.xaml and Light.xaml expose the
/// SAME brush key set (rounds only retune values), and every value parses as
/// a color. Reads the .xaml sources by walking up to the repo root, so no
/// WPF Application/pack-URI plumbing is needed.
public sealed class ThemeDictionaryTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string ThemingDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return Path.Combine(dir.FullName, "src", "Brisk", "Theming");
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }

    private static (string Key, string Color)[] Brushes(string file) =>
        XDocument.Load(Path.Combine(ThemingDir(), file)).Root!
            .Elements().Where(e => e.Name.LocalName == "SolidColorBrush")
            .Select(e => ((string)e.Attribute(X + "Key")!, (string)e.Attribute("Color")!))
            .ToArray();

    [Fact]
    public void DarkAndLight_ExposeTheSameBrushKeys()
    {
        var dark = Brushes("Dark.xaml").Select(b => b.Key).ToArray();
        var light = Brushes("Light.xaml").Select(b => b.Key).ToArray();
        Assert.Equal(dark.OrderBy(k => k, StringComparer.Ordinal),
            light.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(dark.Length, dark.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void EveryBrush_HasAParseableColor(string file)
    {
        var brushes = Brushes(file);
        Assert.NotEmpty(brushes);
        foreach (var (key, color) in brushes)
        {
            Assert.False(string.IsNullOrEmpty(key));
            Assert.IsType<Color>(ColorConverter.ConvertFromString(color));
        }
    }
}
