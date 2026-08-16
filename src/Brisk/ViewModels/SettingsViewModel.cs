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

    public SettingsViewModel(Settings settings, string settingsPath,
        StartupLauncher launcher, Action<string> applyTheme, Action<string> applyLanguage)
    {
        _settings = settings;
        _settingsPath = settingsPath;
        _launcher = launcher;
        _applyTheme = applyTheme;
        _applyLanguage = applyLanguage;
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

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set
        {
            if (_settings.StartWithWindows == value) return;
            _settings.StartWithWindows = value;
            _launcher.Apply(value);
            Persist(nameof(StartWithWindows));
        }
    }

    private void Persist(string property)
    {
        _settings.Save(_settingsPath);
        Raise(property);
    }
}
