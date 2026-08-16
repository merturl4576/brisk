using System;
using System.Windows.Media;

namespace Brisk.Theming;

public static class ThemeResolver
{
    public static string Resolve(string setting, Func<int?> appsUseLightTheme) =>
        setting switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => appsUseLightTheme() == 0 ? "dark" : "light",
        };

    /// DWM ColorizationColor is an ARGB dword; alpha carries blur opacity,
    /// so it is forced to FF for use as a UI accent.
    public static Color AccentFrom(int? colorizationColor)
    {
        if (colorizationColor is not { } raw)
            return Color.FromArgb(0xFF, 0x4C, 0xC2, 0xFF);
        var v = unchecked((uint)raw);
        return Color.FromArgb(0xFF, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }
}
