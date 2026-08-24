using System;

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

    // AccentFrom lived here: it turned DWM's ColorizationColor dword into the
    // accent that was injected over AccentBrush. The injection is gone — the
    // signature is the palette's, not the desktop's, see ThemeManager.Apply —
    // and so is the reader that fed it. Theme DETECTION above stays.
}
