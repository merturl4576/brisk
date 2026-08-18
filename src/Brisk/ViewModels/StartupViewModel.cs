using System;
using System.Collections.ObjectModel;
using System.Linq;
using Brisk.Localization;
using Brisk.Services;
using BriskEngine.Diagnostics;

namespace Brisk.ViewModels;

public sealed class StartupItemRow : ViewModelBase
{
    /// Registry value names rarely say what a program is, so known apps get
    /// a friendly one-liner. Tokens mirror the engine's KnownHeavy table
    /// (Steam, Discord, Spotify, Docker Desktop, EpicGamesLauncher,
    /// WhatsApp, Teams, BlueStacks, WallpaperEngine) plus common
    /// non-heavy residents (OneDrive, Skype, Cortana); matching is the same
    /// contains + OrdinalIgnoreCase the engine uses, so the two can never
    /// disagree about what "Teams" means.
    private static readonly (string Token, string Key)[] KnownApps =
    {
        ("Teams", "teams"), ("OneDrive", "onedrive"), ("Spotify", "spotify"),
        ("Discord", "discord"), ("Steam", "steam"),
        ("EpicGamesLauncher", "epicgameslauncher"), ("Skype", "skype"),
        ("Cortana", "cortana"), ("Docker Desktop", "dockerdesktop"),
        ("WhatsApp", "whatsapp"), ("BlueStacks", "bluestacks"),
        ("WallpaperEngine", "wallpaperengine"), ("brisk", "brisk"),
    };

    /// Names that are Windows/driver plumbing even in HKCU.
    private static readonly string[] SystemTokens =
        { "SecurityHealth", "RtkAudUService", "IAStorIcon" };

    private readonly Func<StartupItemRow, bool, bool> _toggle;
    private bool _isEnabled;

    public StartupItemRow(StartupEntry entry, Loc loc,
        Func<StartupItemRow, bool, bool> toggle)
    {
        _toggle = toggle;
        Hive = entry.Hive;
        Name = entry.Name;
        IsHeavy = entry.KnownHeavy;
        Description = DescriptionFor(entry, loc);
        _isEnabled = entry.Enabled;
    }

    /// A known app's friendly line wins; otherwise system-ish entries (known
    /// system names, or anything HKLM outside the heavy table) get the
    /// "turning it off is not recommended" hint. Unknown user apps get
    /// nothing — an invented description would be a lie.
    private static string DescriptionFor(StartupEntry entry, Loc loc)
    {
        foreach (var (token, key) in KnownApps)
            if (entry.Name.Contains(token, StringComparison.OrdinalIgnoreCase))
                return loc[$"startup.app.{key}"];
        var systemish = SystemTokens.Any(t =>
                entry.Name.Contains(t, StringComparison.OrdinalIgnoreCase))
            || (string.Equals(entry.Hive, "HKLM", StringComparison.OrdinalIgnoreCase)
                && !entry.KnownHeavy);
        return systemish ? loc["startup.system.hint"] : "";
    }

    public string Hive { get; }
    public string Name { get; }
    public string Description { get; }
    public bool IsHeavy { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            if (_toggle(this, value)) { _isEnabled = value; Raise(nameof(IsEnabled)); }
            else Raise(nameof(IsEnabled));   // revert the checkbox visual
        }
    }
}

public sealed class StartupViewModel : ViewModelBase
{
    private readonly IEngineHost _host;
    private readonly Loc _loc;
    private readonly Func<bool> _isDryRun;
    private readonly StartupLauncher _launcher;
    private bool _toggleFailed;

    public StartupViewModel(AppState state, IEngineHost host, Loc loc,
        Func<bool> isDryRun, StartupLauncher launcher)
    {
        _host = host;
        _loc = loc;
        _isDryRun = isDryRun;
        _launcher = launcher;
        state.Changed += Refresh;
    }

    public ObservableCollection<StartupItemRow> Items { get; } = new();
    public bool ToggleFailed { get => _toggleFailed; private set => Set(ref _toggleFailed, value); }

    private void Refresh()
    {
        Items.Clear();

        // brisk criticizes startup bloat, so when it joins startup it shows up
        // in the same list, switchable by the same toggle.
        if (_launcher.IsOn())
            Items.Add(new StartupItemRow(
                new StartupEntry("Task", "brisk", true, false), _loc,
                (_, enabled) =>
                {
                    if (_isDryRun()) { ToggleFailed = true; return false; }
                    _launcher.Apply(enabled);
                    return true;
                }));

        foreach (var entry in _host.ListStartup()
                     .OrderByDescending(e => e.KnownHeavy).ThenBy(e => e.Name,
                         StringComparer.OrdinalIgnoreCase))
            Items.Add(new StartupItemRow(entry, _loc, (row, enabled) =>
            {
                if (_isDryRun())
                {
                    ToggleFailed = true;   // treated like a failed toggle: revert + flag
                    return false;
                }
                var ok = _host.SetStartupEnabled(row.Hive, row.Name, enabled);
                ToggleFailed = !ok;
                return ok;
            }));
    }
}
