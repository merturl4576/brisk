using System;
using System.Windows.Media;

namespace Brisk.Theming;

/// WCAG 2.x contrast, as arithmetic — no WPF beyond the Color struct itself,
/// so a legibility rule can be asserted in a plain unit test instead of being
/// eyeballed on a rendered page.
///
/// It exists because the cockpit atmosphere is drawn UNDERNEATH page text.
/// Once a background stops being one flat fill, "is this readable?" is no
/// longer a question about the palette; it is a question about the brightest
/// pixel the background can produce, and that has to be computable.
public static class Contrast
{
    /// The sRGB kink: the bottom of the range is linear, the rest is a 2.4
    /// gamma. Both halves are straight from the WCAG definition.
    private static double Linearize(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// 0 for black, 1 for white. The weights are the eye's, not the display's
    /// — green carries most of the perceived brightness, which is why a
    /// turquoise texture costs more legibility budget than a navy one.
    public static double RelativeLuminance(Color color) =>
        0.2126 * Linearize(color.R)
        + 0.7152 * Linearize(color.G)
        + 0.0722 * Linearize(color.B);

    /// Lighter over darker, both offset by 0.05 so the ratio stays finite at
    /// black. Symmetric by construction: which colour is the text and which
    /// is the ground does not change the answer.
    public static double Ratio(Color a, Color b)
    {
        var first = RelativeLuminance(a);
        var second = RelativeLuminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }
}
