using System;
using System.Collections.ObjectModel;
using System.Linq;
using Brisk.Services;
using BriskEngine.Diagnostics;

namespace Brisk.ViewModels;

public sealed class StartupItemRow : ViewModelBase
{
    private readonly Func<StartupItemRow, bool, bool> _toggle;
    private bool _isEnabled;

    public StartupItemRow(StartupEntry entry, Func<StartupItemRow, bool, bool> toggle)
    {
        _toggle = toggle;
        Hive = entry.Hive;
        Name = entry.Name;
        IsHeavy = entry.KnownHeavy;
        _isEnabled = entry.Enabled;
    }

    public string Hive { get; }
    public string Name { get; }
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
    private readonly Func<bool> _isDryRun;
    private bool _toggleFailed;

    public StartupViewModel(AppState state, IEngineHost host, Func<bool> isDryRun)
    {
        _host = host;
        _isDryRun = isDryRun;
        state.Changed += Refresh;
    }

    public ObservableCollection<StartupItemRow> Items { get; } = new();
    public bool ToggleFailed { get => _toggleFailed; private set => Set(ref _toggleFailed, value); }

    private void Refresh()
    {
        Items.Clear();
        foreach (var entry in _host.ListStartup()
                     .OrderByDescending(e => e.KnownHeavy).ThenBy(e => e.Name,
                         StringComparer.OrdinalIgnoreCase))
            Items.Add(new StartupItemRow(entry, (row, enabled) =>
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
