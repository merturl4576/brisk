using System;
using System.Collections.Generic;
using System.ComponentModel;
using Brisk.Localization;
using Brisk.Services;

namespace Brisk.ViewModels;

/// One instance per option for the life of the page, with a label that
/// ANNOUNCES a change rather than being replaced.
///
/// The obvious fix — rebuild both lists in the new language — is the one that
/// does not work here, and a real ComboBox says why twice over. Keep the label
/// out of the value and the two lists compare EQUAL, so a Selector handed the
/// new selection keeps the OLD object and the closed box goes on drawing
/// "Koyu" under an English page. Put the label INTO the value and they stop
/// comparing equal, so replacing the list makes the ComboBox lose the
/// selection outright — SelectedItem goes null, and SelectedValue is bound
/// TwoWay in SettingsPage, so the null would be written back over the stored
/// setting. Both were seen on a real ComboBox in ChoiceComboBoxTests.
///
/// So nothing is replaced. The same object stays selected and simply reports
/// that its label now reads differently, which is what the template's binding
/// is already listening for.
public sealed class ChoiceOption : INotifyPropertyChanged
{
    private readonly Loc _loc;

    public ChoiceOption(string value, string labelKey, Loc loc)
    {
        Value = value;
        LabelKey = labelKey;
        _loc = loc;
    }

    public string Value { get; }
    public string LabelKey { get; }

    /// Read live rather than stored: the key is the same string in every
    /// language, so a converter bound to it never re-evaluates. This is the
    /// end that moves.
    public string Label => _loc[LabelKey];

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Relabel() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
}

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly Settings _settings;
    private readonly string _settingsPath;
    private readonly StartupLauncher _launcher;
    private readonly Action<string> _applyTheme;
    private readonly Action<string> _applyLanguage;
    private readonly Loc _loc;
    private bool _startupFailed;

    public SettingsViewModel(Settings settings, string settingsPath,
        StartupLauncher launcher, Action<string> applyTheme,
        Action<string> applyLanguage, Loc? loc = null)
    {
        _settings = settings;
        _settingsPath = settingsPath;
        _launcher = launcher;
        _applyTheme = applyTheme;
        _applyLanguage = applyLanguage;
        _loc = loc ?? Loc.Instance;
        LanguageOptions = new[]
        {
            new ChoiceOption("system", "settings.value.system", _loc),
            new ChoiceOption("en", "settings.value.en", _loc),
            new ChoiceOption("tr", "settings.value.tr", _loc),
        };
        ThemeOptions = new[]
        {
            new ChoiceOption("system", "settings.value.system", _loc),
            new ChoiceOption("light", "settings.value.light", _loc),
            new ChoiceOption("dark", "settings.value.dark", _loc),
        };
        // The Startup page can turn brisk's autostart off too. One backing
        // truth is not enough on its own: WPF caches a bound value until
        // something raises PropertyChanged, so without this the checkbox kept
        // whatever it read when the page was built.
        _launcher.Changed += () =>
        {
            // ...and while we are here, keep settings.json in step. It is only
            // read by the one-time HKCU\Run migration now, but a stored answer
            // that contradicts the machine is exactly the kind of drift this
            // wave exists to remove.
            var on = _launcher.IsOn();
            if (_settings.StartWithWindows != on)
            {
                _settings.StartWithWindows = on;
                _settings.Save(_settingsPath);
            }
            Raise(nameof(StartWithWindows));
        };
    }

    public IReadOnlyList<ChoiceOption> LanguageOptions { get; }

    public IReadOnlyList<ChoiceOption> ThemeOptions { get; }

    public string Language
    {
        get => _settings.Language;
        set
        {
            if (_settings.Language == value) return;
            _settings.Language = value;
            Persist(nameof(Language));
            _applyLanguage(value);
            // After the language is in force, never before: Label is read live,
            // so what each option announces is whatever _applyLanguage just
            // made true. Nothing is replaced — see ChoiceOption for why
            // replacing is the fix that does not work here.
            foreach (var option in LanguageOptions) option.Relabel();
            foreach (var option in ThemeOptions) option.Relabel();
        }
    }

    public string Theme
    {
        get => _settings.Theme;
        set
        {
            if (_settings.Theme == value) return;
            _settings.Theme = value;
            Persist(nameof(Theme));
            _applyTheme(value);
        }
    }

    public bool DryRun
    {
        get => _settings.DryRun;
        set
        {
            if (_settings.DryRun == value) return;
            _settings.DryRun = value;
            Persist(nameof(DryRun));
        }
    }

    /// Reads the SCHEDULED TASK, not settings.json. The Startup page's brisk
    /// row already read the task, so with two owners and nothing reconciling
    /// them, turning brisk off there left this checkbox still showing "on".
    /// The task is the machine's truth; the stored flag is only kept as the
    /// record of the user's last explicit answer, which is what the
    /// HKCU\Run migration consults on an un-migrated machine.
    public bool StartWithWindows
    {
        get => _launcher.IsOn();
        set
        {
            if (_launcher.IsOn() == value) return;
            // schtasks can refuse. Persisting first would make settings.json
            // claim an autostart that does not exist — the same lie from the
            // other end.
            if (!_launcher.Apply(value))
            {
                StartupFailed = true;
                Raise(nameof(StartWithWindows));   // revert the checkbox visual
                return;
            }
            StartupFailed = false;
            _settings.StartWithWindows = value;
            Persist(nameof(StartWithWindows));
        }
    }

    /// True when the last attempt to change brisk's own autostart was refused.
    /// The page shows the line; without it a refused toggle just snapped back
    /// with no explanation.
    public bool StartupFailed
    {
        get => _startupFailed;
        private set => Set(ref _startupFailed, value);
    }

    private void Persist(string property)
    {
        _settings.Save(_settingsPath);
        Raise(property);
    }
}
