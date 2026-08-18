using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Brisk.Tests;

/// ROUND 14 re-review (N2): the round-14 fix set IsIndeterminate on a
/// ThinProgress-styled bar to cure a frozen-looking screen — and made it
/// worse. ThinProgress replaces the ProgressBar template with a track and
/// an indicator and NOTHING else: WPF keeps its indeterminate animation in
/// the template, so all IsIndeterminate does under this style is size the
/// indicator to 100%. The parked EMPTY hairline became a parked FULL one.
///
/// The unit test that shipped it green pinned the view model's flag, not
/// the picture. These read the source instead, which is where the mistake
/// actually lives — and they are the reason the uncountable phases borrow
/// the stock-template bar the scan rows use.
///
/// Source, not render, on purpose: the ABSENCE of a visual is checkable
/// from shape, the correctness of one is not. A render-level check earns
/// its place here only if someone gives ThinProgress a real indeterminate
/// visual — at which point the first test below has to be inverted anyway.
public sealed class ThinProgressTests
{
    private static string SrcDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return Path.Combine(dir.FullName, "src", "Brisk");
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }

    private static IEnumerable<(string File, XElement Element)> Elements(string localName) =>
        Directory.EnumerateFiles(SrcDir(), "*.xaml", SearchOption.AllDirectories)
            .SelectMany(f => XDocument.Load(f).Descendants()
                .Where(e => e.Name.LocalName == localName)
                .Select(e => (Path.GetFileName(f), e)));

    /// The premise the guard below rests on, checked rather than assumed:
    /// nothing in the style reacts to IsIndeterminate.
    [Fact]
    public void ThinProgress_TemplateHasNoIndeterminateVisual()
    {
        var style = Elements("Style")
            .Select(x => x.Element)
            .Single(e => (string?)e.Attributes()
                .FirstOrDefault(a => a.Name.LocalName == "Key") == "ThinProgress");

        Assert.DoesNotContain(style.Descendants(),
            e => e.Name.LocalName is "Storyboard" or "BeginStoryboard"
                or "DoubleAnimation" or "DoubleAnimationUsingKeyFrames");
        Assert.DoesNotContain(style.Descendants().Concat(new[] { style }),
            e => e.Attributes().Any(a =>
                a.Name.LocalName is "Property" or "Binding"
                && ((string)a).Contains("IsIndeterminate", StringComparison.Ordinal)));
    }

    /// The guard: a bar wearing this style must never be asked to be
    /// indeterminate, because the style cannot draw it. Use the stock
    /// template (see the scan rows, and the Depolama card's second bar).
    [Fact]
    public void NoThinProgressBar_IsEverAskedToBeIndeterminate()
    {
        foreach (var (file, bar) in Elements("ProgressBar"))
        {
            var inline = (string?)bar.Attributes()
                .FirstOrDefault(a => a.Name.LocalName == "Style");
            var wearsThinProgress =
                inline?.Contains("ThinProgress", StringComparison.Ordinal) == true
                || bar.Descendants().Any(e => e.Name.LocalName == "Style"
                    && ((string?)e.Attributes()
                        .FirstOrDefault(a => a.Name.LocalName == "BasedOn"))
                        ?.Contains("ThinProgress", StringComparison.Ordinal) == true);
            if (!wearsThinProgress) continue;

            Assert.False(bar.Attributes()
                .Any(a => a.Name.LocalName == "IsIndeterminate"),
                $"{file}: a ThinProgress bar sets IsIndeterminate inline — the "
                + "style has no indeterminate visual, so it renders a static full bar");
            Assert.DoesNotContain(bar.Descendants(),
                e => e.Name.LocalName == "Setter"
                    && (string?)e.Attribute("Property") == "IsIndeterminate");
        }
    }
}
