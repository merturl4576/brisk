using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Live;
using Brisk.Localization;
using Brisk.Session;
using Brisk.ViewModels;

namespace Brisk.Ui.Live;

/// "Slowing you now" on the speed page: the apps using the machine right
/// now, each with Close — and Force close only once a polite close has not
/// worked for five seconds, because forcing can lose unsaved work.
public sealed class LoadPanelViewModel : ViewModelBase
{
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(15);

    private readonly Action<Action> _post;
    private readonly Action<Action> _background;
    private readonly IActionBackend _backend;
    private readonly ActionSession _session;
    private readonly IUiTimers _timers;
    private readonly Loc _loc;
    private IDisposable? _ticker;
    private bool _sampling;

    public LoadPanelViewModel(Action<Action> post, Action<Action> background, IActionBackend backend,
        ActionSession session, IUiTimers timers, Loc loc)
    {
        _post = post;
        _background = background;
        _backend = backend;
        _session = session;
        _timers = timers;
        _loc = loc;
        session.Changed += () => { foreach (var r in Rows) r.RefreshTick(); };
        // One subscription for the panel; rows are replaced every sample and
        // must not each hang a handler on the app-long Loc.
        loc.PropertyChanged += (_, _) =>
        {
            Raise(nameof(L));
            foreach (var r in Rows) r.RefreshText();
        };
    }

    public Loc L => _loc;

    private IReadOnlyList<LoadRowViewModel> _rows = Array.Empty<LoadRowViewModel>();
    public IReadOnlyList<LoadRowViewModel> Rows { get => _rows; private set => Set(ref _rows, value); }

    private bool _measuring = true;
    public bool Measuring { get => _measuring; private set => Set(ref _measuring, value); }

    private bool _empty;
    public bool Empty { get => _empty; private set => Set(ref _empty, value); }

    private string? _error;
    public string? Error { get => _error; private set => Set(ref _error, value); }

    public void Activate()
    {
        if (_ticker is not null) return;
        _ticker = _timers.Every(Every, Sample);
        Sample();
    }

    public void Deactivate()
    {
        _ticker?.Dispose();
        _ticker = null;
    }

    internal void Sample()
    {
        if (_sampling) return;
        _sampling = true;
        _background(() =>
        {
            IReadOnlyList<AppLoad>? loads = null;
            IReadOnlyList<StartupEntry> entries = Array.Empty<StartupEntry>();
            try
            {
                loads = _backend.SampleLoad();
                entries = _backend.StartupEntries();
            }
            catch (Exception) { }
            _post(() =>
            {
                _sampling = false;
                Measuring = false;
                if (loads is null)
                {
                    Error = _loc["action.unread"];
                    return;
                }
                Error = null;
                var rows = _session.Items.Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Rows = loads.Select(l =>
                {
                    var startup = LoadSampler.StartupIdFor(l, entries);
                    return new LoadRowViewModel(l, startup is not null && rows.Contains(startup) ? startup : null,
                        _backend, _session, _timers, _loc, Sample);
                }).ToList();
                Empty = Rows.Count == 0;
            });
        });
    }
}

public sealed class LoadRowViewModel : ViewModelBase
{
    public static readonly TimeSpan ForceAfter = TimeSpan.FromSeconds(5);

    private readonly AppLoad _load;
    private readonly IActionBackend _backend;
    private readonly ActionSession _session;
    private readonly IUiTimers _timers;
    private readonly Loc _loc;
    private readonly Action _resample;

    public LoadRowViewModel(AppLoad load, string? startupRowId, IActionBackend backend, ActionSession session,
        IUiTimers timers, Loc loc, Action resample)
    {
        _load = load;
        StartupRowId = startupRowId;
        _backend = backend;
        _session = session;
        _timers = timers;
        _loc = loc;
        _resample = resample;
        Close = new RelayCommand(OnClose, () => CanClose && !_closing);
        Force = new RelayCommand(OnForce, () => ShowForce);
    }

    public string Name => _load.DisplayName;

    public string Value => _load.CpuPercent >= LoadSampler.CpuThresholdPercent
        ? _loc.F("gui.home.cpu", Math.Round(_load.CpuPercent))
        : Sizes.Format(_load.PrivateBytes, _loc.Culture);

    public string? Note => _load.NoteKey is null ? null : _loc.Title(_load.NoteKey, _load.Note ?? "");
    public bool CanClose => LoadSampler.CanClose(_load);
    public string? StartupRowId { get; }
    public bool HasStartup => StartupRowId is not null;

    public bool StartupTicked
    {
        get => StartupRowId is not null && _session.IsTicked(StartupRowId);
        set
        {
            if (StartupRowId is null || value == StartupTicked) return;
            _session.SetTicked(StartupRowId, value);
        }
    }

    private bool _closing;
    private bool _showForce;
    public bool ShowForce { get => _showForce; private set { if (Set(ref _showForce, value)) Force.RaiseCanExecuteChanged(); } }

    public RelayCommand Close { get; }
    public RelayCommand Force { get; }

    private void OnClose()
    {
        _closing = true;
        Close.RaiseCanExecuteChanged();
        _backend.Close(_load.Pids);
        _timers.After(ForceAfter, () => ShowForce = _backend.IsRunning(_load.Pids));
    }

    private void OnForce()
    {
        _backend.Kill(_load.Pids);
        ShowForce = false;
        _resample();
    }

    internal void RefreshTick() => Raise(nameof(StartupTicked));

    internal void RefreshText()
    {
        Raise(nameof(Value));
        Raise(nameof(Note));
    }
}
