using System;
using System.Threading;
using Brisk.Theming;
using Brisk.Views;
using Xunit;
// WinForms is on in this project, so bare Color and ColorConverter are ambiguous.
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Brisk.Tests;

/// WCAG 2.x contrast, pinned against the two values everyone knows — and then
/// pointed at the thing it exists for: the cockpit atmosphere is drawn UNDER
/// page text, so the atmosphere's own brightness is a legibility budget, not
/// a taste question.
public class ContrastTests
{
    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    [Fact]
    public void BlackOnWhite_IsTheFullRange()
    {
        var white = Parse("#FFFFFF");
        var black = Parse("#000000");

        Assert.Equal(1.0, Contrast.RelativeLuminance(white), 6);
        Assert.Equal(0.0, Contrast.RelativeLuminance(black), 6);
        Assert.Equal(21.0, Contrast.Ratio(black, white), 3);
    }

    /// Order must not matter: the formula divides the lighter by the darker,
    /// not the first by the second.
    [Fact]
    public void ARatio_IsTheSameBothWaysRound()
    {
        var a = Parse("#7E93A0");
        var b = Parse("#0A1626");

        Assert.Equal(Contrast.Ratio(a, b), Contrast.Ratio(b, a), 9);
    }

    [Fact]
    public void AColourAgainstItself_IsOneToOne()
    {
        var color = Parse("#5FD4E8");

        Assert.Equal(1.0, Contrast.Ratio(color, color), 9);
    }

    /// The rule from the spec: body text must stay legible on bare atmosphere.
    /// Worst case, not average — an average passes while one glyph sits behind
    /// the brightest column of rain.
    ///
    /// TextMuted is READ from the dictionary, never copied. A hex copied into
    /// a test certifies the value it was copied from forever: retune the
    /// palette and this keeps passing while describing a colour the app has
    /// stopped wearing. The layer's own end of the comparison is pinned to
    /// Dark.xaml the same way, by AtmosphereLayerTests.
    [Fact]
    public void TextMuted_OnTheBrightestAtmosphere_StaysLegible()
    {
        var worst = BrightestComposite(_ => { });          // dark mode default
        var textMuted = ThemeSource.ColorOf("Dark.xaml", "TextMuted");

        Assert.True(Contrast.Ratio(textMuted, worst) >= 4.5,
            $"TextMuted on the brightest atmosphere is " +
            $"{Contrast.Ratio(textMuted, worst):F2}:1 — below the 4.5:1 floor");
    }

    /// The light theme paints one flat fill, so its worst case IS the fill —
    /// no rain to composite, and TextMuted has to survive it just the same.
    /// Both ends read from Light.xaml, for the reason above: the tuning gate
    /// that will move these two values is the very next task.
    [Fact]
    public void TextMuted_OnTheFlatAtmosphere_StaysLegible()
    {
        var worst = BrightestComposite(layer =>
        {
            layer.IsFlat = true;
            layer.GroundBrush = new SolidColorBrush(ThemeSource.ColorOf("Light.xaml", "Bg"));
        });
        var textMuted = ThemeSource.ColorOf("Light.xaml", "TextMuted");

        Assert.True(Contrast.Ratio(textMuted, worst) >= 4.5,
            $"TextMuted on the flat atmosphere is " +
            $"{Contrast.Ratio(textMuted, worst):F2}:1 — below the 4.5:1 floor");
    }

    /// A FrameworkElement's constructor demands an STA thread and the test
    /// runner does not have one — the same reason ReportCardRenderer
    /// .RenderOnStaThread exists. Nothing here renders: BrightestComposite is
    /// arithmetic over the layer's constants, which is the whole point of it.
    private static Color BrightestComposite(Action<AtmosphereLayer> arrange)
    {
        Color composite = default;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var layer = new AtmosphereLayer();
                arrange(layer);
                composite = layer.BrightestComposite();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
        return composite;
    }
}
