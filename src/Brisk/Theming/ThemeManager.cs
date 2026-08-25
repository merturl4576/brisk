using System;
using System.Windows;
using Microsoft.Win32;

namespace Brisk.Theming;

public sealed class ThemeManager
{
    private const string Personalize =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public string Current { get; private set; } = "dark";

    public void Apply(string setting)
    {
        // The registry read that stays: which theme the user is in. brisk
        // follows Windows here, because dark-or-light is the user's setting
        // about their whole desktop and not a claim about their machine.
        Current = ThemeResolver.Resolve(setting, () =>
            Registry.CurrentUser.OpenSubKey(Personalize)
                ?.GetValue("AppsUseLightTheme") as int?);

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries.Clear();
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Theming/{(Current == "dark" ? "Dark" : "Light")}.xaml"),
        });
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Theming/Shared.xaml"),
        });

        // And the read that is GONE, deliberately: the Windows accent colour
        // used to be injected over AccentBrush and AccentTextBrush here, so
        // the dictionary's value was a fallback nobody with a configured
        // desktop ever saw. It is the dictionary's value now, in both themes,
        // and that is a decision rather than a simplification.
        //
        // In brisk a colour carries a claim. Severity is a claim; the
        // signature is decoration. Letting Windows choose the signature meant
        // brisk could not know what its own decoration was saying — a user
        // whose accent is red would have had the critical-claim colour on
        // decorative brackets, the horizon glow and the selected nav tile,
        // and a user whose accent is the default blue put it a ΔE of 11 from
        // SeverityInfo, which is the exact two-surfaces-one-colour collision
        // the palette was retuned to end. Following the system and then
        // deriving the glow from it does not help: it keeps the collision and
        // only hides where it came from.
        //
        // So the signature is pinned. Turquoise in dark, the same teal
        // darkened for light. The cost is honest and small — brisk no longer
        // colour-matches the taskbar — and what is bought is that every
        // decorative surface in the app is a colour brisk chose and can
        // therefore keep out of the severity vocabulary.
    }
}
