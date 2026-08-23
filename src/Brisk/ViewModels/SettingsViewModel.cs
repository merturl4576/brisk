using System;
using System.Collections.Generic;
using Brisk.Services;

namespace Brisk.ViewModels;

public sealed record ChoiceOption(string Value, string LabelKey);

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly Settings _settings;
    private readonly string _settingsPath;
    private readonly StartupLauncher _launcher;
    private readonly Action<string> _applyTheme;
    private readonly Action<string> _applyLanguage;
    private bool _startupFailed;

    public SettingsViewModel(Settings settings, string settingsPath,
        StartupLauncher launcher, Action<string> applyTheme, Action<string> applyLanguage)
    {
        _settings = settings;
        _settingsPath = settingsPath;
        _launcher = launcher;
        _applyTheme = applyTheme;
        _applyLanguage = applyLanguage;
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

    public IReadOnlyList<ChoiceOption> LanguageOptions { get; } = new[]
    {
        new ChoiceOption("system", "settings.value.system"),
        new ChoiceOption("en", "settings.value.en"),
        new ChoiceOption("tr", "settings.value.tr"),
    };

    public IReadOnlyList<ChoiceOption> ThemeOptions { get; } = new[]
    {
        new ChoiceOption("system", "settings.value.system"),
        new ChoiceOption("light", "settings.value.light"),
        new ChoiceOption("dark", "settings.value.dark"),
    };

    public string Language
    {
        get => _settings.Language;
        set
        {
            if (_settings.Language == value) return;
            _settings.Language = value;
            Persist(nameof(Language));
            _applyLanguage(value);
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
