using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Brisk.Theming;

public sealed class ThemeManager
{
    private const string Personalize =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Dwm = @"Software\Microsoft\Windows\DWM";

    public string Current { get; private set; } = "dark";

    public void Apply(string setting)
    {
        Current = ThemeResolver.Resolve(setting, () =>
            Registry.CurrentUser.OpenSubKey(Personalize)
                ?.GetValue("AppsUseLightTheme") as int?);
        var accent = ThemeResolver.AccentFrom(
            Registry.CurrentUser.OpenSubKey(Dwm)?.GetValue("ColorizationColor") as int?);

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries.Clear();
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Theming/{(Current == "dark" ? "Dark" : "Light")}.xaml"),
        });
        // Real system accent wins over the dictionary's fallback value. In dark
        // mode a light accent needs dark text on it and vice versa.
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush(accent);
        var luminance = 0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B;
        Application.Current.Resources["AccentTextBrush"] = new SolidColorBrush(
            luminance > 140 ? Color.FromRgb(0x0B, 0x0B, 0x0B) : Colors.White);
    }
}
