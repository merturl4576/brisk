using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Brisk.Localization;

public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private readonly ResourceManager _resources =
        new("Brisk.Localization.Strings", typeof(Loc).Assembly);
    private CultureInfo _culture = CultureInfo.GetCultureInfo("en");

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => _resources.GetString(key, _culture) ?? key;

    public string F(string key, params object[] args) =>
        string.Format(_culture, this[key], args);

    /// Rule titles come from the engine with a stable TitleKey; the engine's
    /// English is the fallback when a translation is missing.
    public string Title(string titleKey, string english) =>
        _resources.GetString(titleKey, _culture) ?? english;

    public void SetLanguage(string setting)
    {
        _culture = setting switch
        {
            "en" => CultureInfo.GetCultureInfo("en"),
            "tr" => CultureInfo.GetCultureInfo("tr"),
            _ => CultureInfo.CurrentUICulture,
        };
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
