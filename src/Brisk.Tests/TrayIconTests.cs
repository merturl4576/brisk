using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Brisk.Tray;
using Xunit;

namespace Brisk.Tests;

/// brisk's mark exists in two places — the title bar and the notification
/// area — and since the signature accent stopped following the Windows
/// accent, both of them are drawn from the theme palette. The palette moves
/// when the theme does, so the tray mark has to be redrawable, and what it is
/// filled with has to be checkable by something other than a human looking at
/// their taskbar.
///
/// The drawing is asserted rather than the NotifyIcon: making a real
/// TrayIcon would put a real icon in the developer's notification area for
/// the length of a test run, which is the tray's version of the offscreen
/// window rule the snapshot harness already follows.
public class TrayIconTests
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    /// The dark theme's turquoise and the light theme's teal — the two
    /// signatures a running brisk actually switches between.
    private static readonly Color Turquoise = Color.FromArgb(0x5F, 0xD4, 0xE8);
    private static readonly Color Teal = Color.FromArgb(0x0F, 0x6E, 0x7E);

    /// The fill is the accent it was asked for, and a different accent gives
    /// a different mark. Without this, SetAccent could redraw the icon in the
    /// same colour forever and nothing would notice.
    [Fact]
    public void TheMark_IsFilledWithTheAccentItWasDrawnFor()
    {
        var turquoise = Filled(Turquoise);
        var teal = Filled(Teal);

        Assert.True(turquoise > 40,
            $"only {turquoise} of 256 pixels carry the turquoise accent — the " +
            "tray mark is not being filled with the colour it was handed");
        Assert.True(teal > 40,
            $"only {teal} of 256 pixels carry the teal accent — the tray mark " +
            "is not being filled with the colour it was handed");
        Assert.True(CountOf(Turquoise, Teal) == 0,
            "the mark drawn for the light theme's teal still contains the dark " +
            "theme's turquoise, so the accent argument is not reaching the fill");
    }

    private static int Filled(Color accent) => CountOf(accent, accent);

    /// How many pixels of the mark drawn for `drawnFor` are exactly `wanted`.
    private static int CountOf(Color wanted, Color drawnFor)
    {
        var (icon, handle) = TrayIcon.DrawIcon(drawnFor);
        try
        {
            using var bitmap = icon.ToBitmap();
            var count = 0;
            for (var y = 0; y < bitmap.Height; y++)
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.R == wanted.R && pixel.G == wanted.G && pixel.B == wanted.B
                        && pixel.A == 255)
                        count++;
                }
            return count;
        }
        finally
        {
            icon.Dispose();
            DestroyIcon(handle);
        }
    }
}
