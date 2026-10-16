using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BriskEngine;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;

namespace Brisk.Ui.Shell;

public sealed class LanguageOption : ViewModelBase
{
    private readonly UiSettingsViewModel _owner;

    public LanguageOption(string code, UiSettingsViewModel owner)
    {
        Code = code;
        _owner = owner;
    }

    public string Code { get; }
    public string Label => Code switch { "tr" => "Türkçe", "en" => "English", _ => _owner.L["gui.settings.language.system"] };

    public bool IsSelected
    {
        get => _owner.Language == Code;
        set { if (value) _owner.Language = Code; }
    }

    internal void Refresh()
    {
        Raise(nameof(IsSelected));
        Raise(nameof(Label));
    }
}

/// Three controls: language, start with Windows, moving background.
public sealed class UiSettingsViewModel : ViewModelBase
{
    public const string RepositoryUrl = "https://github.com/merturl4576/brisk";

    private readonly Settings _settings;
    private readonly string _path;
    private readonly Func<bool> _autostartIsOn;
    private readonly Func<bool, bool> _applyAutostart;
    private readonly Action _motionChanged;

    public UiSettingsViewModel(Settings settings, string path, Loc loc, Func<bool> autostartIsOn,
        Func<bool, bool> applyAutostart, Action motionChanged)
    {
        _settings = settings;
        _path = path;
        L = loc;
        _autostartIsOn = autostartIsOn;
        _applyAutostart = applyAutostart;
        _motionChanged = motionChanged;
        Languages = new[] { "system", "tr", "en" }.Select(c => new LanguageOption(c, this)).ToList();
        OpenGitHub = new RelayCommand(() =>
        {
            try { Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true }); }
            catch (Exception) { }
        });
        loc.PropertyChanged += (_, _) =>
        {
            Raise(nameof(Version));
            foreach (var o in Languages) o.Refresh();
        };
    }

    public Loc L { get; }
    public IReadOnlyList<LanguageOption> Languages { get; }
    public RelayCommand OpenGitHub { get; }
    public string Version => L.F("gui.settings.version", EngineInfo.Version);

    public string Language
    {
        get => _settings.Language;
        set
        {
            if (value == _settings.Language) return;
            _settings.Language = value;
            Save();
            L.SetLanguage(value);
            Raise(nameof(Language));
            foreach (var o in Languages) o.Refresh();
        }
    }

    public bool StartWithWindows
    {
        get => _autostartIsOn();
        set
        {
            if (value != _autostartIsOn() && _applyAutostart(value))
            {
                _settings.StartWithWindows = value;
                Save();
            }
            Raise(nameof(StartWithWindows));
        }
    }

    public bool Motion
    {
        get => _settings.Motion;
        set
        {
            if (value == _settings.Motion) return;
            _settings.Motion = value;
            Save();
            _motionChanged();
            Raise(nameof(Motion));
        }
    }

    private void Save()
    {
        try { _settings.Save(_path); }
        catch (Exception) { }
    }
}
