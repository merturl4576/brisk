using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using BriskEngine.Actions;
using BriskEngine.Live;
using Brisk.Localization;
using Brisk.Session;
using Brisk.ViewModels;

namespace Brisk.Ui.Home;

public sealed record LoadLine(string Name, string Value);

/// The home screen: the figures, the two short number columns, and the one
/// button. Live readers (what is slowing the machine, how many apps are
/// online) and receipt polling run only while home is on screen.
public sealed class HomeViewModel : ViewModelBase
{
    public static readonly TimeSpan LoadEvery = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan OnlineEvery = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ReceiptEvery = TimeSpan.FromSeconds(30);
    public const int LoadRows = 3;

    private readonly Action<Action> _post;
    private readonly Action<Action> _background;
    private readonly ActionSession _session;
    private readonly IActionBackend _backend;
    private readonly IUiTimers _timers;
    private readonly Loc _loc;
    private readonly Action<IReadOnlyList<ActionItem>, ActionGroupKind?> _confirm;
    private readonly Action<ActionGroupKind> _open;
    private readonly List<IDisposable> _running = new();
    private IReadOnlyList<AppLoad>? _lastLoad;
    private bool _sampling, _readingOnline;
    private int _enabledStartup;
    private SessionState _lastState;

    public HomeViewModel(Action<Action> post, Action<Action> background, ActionSession session,
        IActionBackend backend, IUiTimers timers, Loc loc,
        Action<IReadOnlyList<ActionItem>, ActionGroupKind?> confirm, Action<ActionGroupKind> open)
    {
        _post = post;
        _background = background;
        _session = session;
        _backend = backend;
        _timers = timers;
        _loc = loc;
        _confirm = confirm;
        _open = open;
        Primary = new RelayCommand(OnPrimary, () => Figures.ButtonEnabled);
        Rescan = new RelayCommand(() => _ = _session.RescanAsync());
        UndoPrevious = new RelayCommand(() => _ = _session.UndoAsync());
        OpenFailed = new RelayCommand(OnOpenFailed);
        ReadStartupCount();
        _figures = Compute();
        _lastState = session.State;
        session.Changed += OnSessionChanged;
        loc.PropertyChanged += OnLanguageChanged;
    }

    private HomeFigures _figures;
    public HomeFigures Figures { get => _figures; private set => Set(ref _figures, value); }

    public CultureInfo Culture => _loc.Culture;

    /// The view's fixed words: {Binding L[key]} follows a language switch.
    public Loc L => _loc;

    private IReadOnlyList<LoadLine> _load = Array.Empty<LoadLine>();
    public IReadOnlyList<LoadLine> Load { get => _load; private set => Set(ref _load, value); }

    private bool _loadMeasuring = true;
    public bool LoadMeasuring { get => _loadMeasuring; private set => Set(ref _loadMeasuring, value); }

    private bool _loadEmpty;
    public bool LoadEmpty { get => _loadEmpty; private set => Set(ref _loadEmpty, value); }

    private string? _loadError;
    public string? LoadError { get => _loadError; private set => Set(ref _loadError, value); }

    private int? _onlineApps;
    public int? OnlineApps { get => _onlineApps; private set => Set(ref _onlineApps, value); }

    public RelayCommand Primary { get; }
    public RelayCommand Rescan { get; }
    public RelayCommand UndoPrevious { get; }
    public RelayCommand OpenFailed { get; }

    public bool IsActive => _running.Count > 0;

    public void Activate()
    {
        if (IsActive) return;
        _running.Add(_timers.Every(LoadEvery, SampleLoad));
        _running.Add(_timers.Every(OnlineEvery, ReadOnline));
        _running.Add(_timers.Every(ReceiptEvery, PollReceipt));
        SampleLoad();
        ReadOnline();
    }

    public void Deactivate()
    {
        foreach (var t in _running) t.Dispose();
        _running.Clear();
    }

    private void OnSessionChanged()
    {
        // The startup count moves after a run or an undo: read it again once.
        if (_session.State != _lastState && _session.State is SessionState.Done or SessionState.Ready)
            ReadStartupCount();
        _lastState = _session.State;
        Refresh();
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        Raise(nameof(Culture));
        if (_lastLoad is not null) Load = Lines(_lastLoad);
        Refresh();
    }

    private void Refresh()
    {
        Figures = Compute();
        Primary.RaiseCanExecuteChanged();
    }

    private HomeFigures Compute() => HomeFigures.From(_session, _loc, _enabledStartup, OnlineApps,
        _timers.UtcNow, _session.Receipt is { DeferredBytes: > 0 } ? SafeDeferredApps() : null);

    private IReadOnlyList<string> SafeDeferredApps()
    {
        try { return _backend.DeferredApps(); }
        catch (Exception) { return Array.Empty<string>(); }
    }

    private void ReadStartupCount()
    {
        try { _enabledStartup = _backend.EnabledStartupCount(); }
        catch (Exception) { }
    }

    private void OnPrimary()
    {
        switch (Figures.ButtonAction)
        {
            case HomeButton.RunAll:
                var unrecoverable = _session.NeedsConfirmation(null);
                if (unrecoverable.Count > 0) _confirm(unrecoverable, null);
                else _ = _session.RunAsync(null);
                break;
            case HomeButton.Undo:
                _ = _session.UndoAsync();
                break;
            case HomeButton.Rescan:
            case HomeButton.Retry:
                _ = _session.RescanAsync();
                break;
        }
    }

    private void OnOpenFailed()
    {
        var items = _session.Items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        var failed = _session.Results.Values.Where(r => r.Outcome == RowOutcome.Failed)
            .Select(r => items.TryGetValue(r.RowId, out var i) ? i.Group : (ActionGroupKind?)null)
            .OfType<ActionGroupKind>()
            .OrderBy(g => g)
            .Cast<ActionGroupKind?>()
            .FirstOrDefault();
        if (failed is { } group) _open(group);
    }

    private IReadOnlyList<LoadLine> Lines(IReadOnlyList<AppLoad> loads) => loads.Take(LoadRows)
        .Select(l => new LoadLine(l.DisplayName, l.CpuPercent >= LoadSampler.CpuThresholdPercent
            ? _loc.F("gui.home.cpu", Math.Round(l.CpuPercent))
            : Sizes.Format(l.PrivateBytes, _loc.Culture)))
        .ToList();

    private void SampleLoad()
    {
        if (_sampling) return;
        _sampling = true;
        _background(() =>
        {
            IReadOnlyList<AppLoad>? loads = null;
            try { loads = _backend.SampleLoad(); }
            catch (Exception) { }
            _post(() =>
            {
                _sampling = false;
                LoadMeasuring = false;
                if (loads is null)
                {
                    LoadError = _loc["action.unread"];
                    return;
                }
                LoadError = null;
                _lastLoad = loads;
                Load = Lines(loads);
                LoadEmpty = Load.Count == 0;
            });
        });
    }

    private void ReadOnline()
    {
        if (_readingOnline) return;
        _readingOnline = true;
        _background(() =>
        {
            int? apps = null;
            try { apps = _backend.ReadConnections().Rows.Select(r => r.App).Distinct(StringComparer.OrdinalIgnoreCase).Count(); }
            catch (Exception) { }
            _post(() =>
            {
                _readingOnline = false;
                OnlineApps = apps;
                Refresh();
            });
        });
    }

    /// Until the receipt has its after-restart numbers (boot, RAM, telemetry).
    private void PollReceipt()
    {
        if (_session.LastRun is null || _session.Busy) return;
        if (_session.Receipt is { BootMsAfter: not null, StartupRamConfirmed: true, TelemetryAfter: not null }) return;
        _ = _session.RefreshReceiptAsync();
    }
}
