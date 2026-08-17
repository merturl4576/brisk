using System;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using BriskEngine;
using BriskEngine.Models;

namespace Brisk.ViewModels;

public sealed class FlyoutViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly CleanService _cleanService;
    private readonly FixAllService _fixAll;
    private readonly Loc _loc;
    private readonly Func<bool> _isDryRun;

    private string _healthText = "—";
    private string _healthBrushKey = "";
    private string _findingsLine = "";
    private string _reclaimLine = "";
    private string _lastScanLine = "";
    private CleanOutcome? _lastCleanOutcome;
    private bool _busy;

    public FlyoutViewModel(AppState state,
        CleanService cleanService, FixAllService fixAll, Loc loc, Func<bool> isDryRun)
    {
        _state = state;
        _cleanService = cleanService;
        _fixAll = fixAll;
        _loc = loc;
        _isDryRun = isDryRun;
        _state.Changed += Refresh;
        ScanCommand = new RelayCommand(() => _ = ScanNowAsync());
        FixAllCommand = new RelayCommand(() => _ = FixAllAsync(), () => HasSnapshot);
        CleanSafeCommand = new RelayCommand(() => _ = CleanSafeAsync(), () => HasSnapshot);
        OpenDetailsCommand = new RelayCommand(() => OpenDetailsRequested?.Invoke());
    }

    public string HealthText { get => _healthText; private set => Set(ref _healthText, value); }
    public string HealthBrushKey
    {
        get => _healthBrushKey;
        private set => Set(ref _healthBrushKey, value);
    }
    public string FindingsLine { get => _findingsLine; private set => Set(ref _findingsLine, value); }
    public string ReclaimLine { get => _reclaimLine; private set => Set(ref _reclaimLine, value); }
    public string LastScanLine { get => _lastScanLine; private set => Set(ref _lastScanLine, value); }
    public bool HasSnapshot => _state.Snapshot is not null;
    public CleanOutcome? LastCleanOutcome
    {
        get => _lastCleanOutcome;
        private set => Set(ref _lastCleanOutcome, value);
    }
    public AppState State => _state;
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }

    public RelayCommand ScanCommand { get; }
    public RelayCommand FixAllCommand { get; }
    public RelayCommand CleanSafeCommand { get; }
    public RelayCommand OpenDetailsCommand { get; }

    public event Action? OpenDetailsRequested;

    public Task ScanNowAsync() => _state.ScanAsync();

    public async Task FixAllAsync()
    {
        if (_busy) return;
        IsBusy = true;                   // set before the first await — re-entry guard
        try
        {
            var snapshot = _state.Snapshot;
            if (snapshot is null) return;
            if (_isDryRun()) return;   // dry run: report only, nothing to fix here
            await Task.Run(() => _fixAll.Run(snapshot));
            await _state.ScanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task CleanSafeAsync()
    {
        if (_busy) return;
        IsBusy = true;                   // set before the first await — re-entry guard
        try
        {
            var snapshot = _state.Snapshot;
            if (snapshot is null) return;
            LastCleanOutcome = await Task.Run(() => _cleanService.CleanSafe(snapshot.Cleaner));
            await _state.ScanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Refresh()
    {
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        HealthText = snapshot.Health.ToString();
        HealthBrushKey = HealthBrush.KeyFor(snapshot.Health);
        var fixable = snapshot.Findings.Count(f =>
            f.Category != RuleCategory.Advise && f.CanFix);
        FindingsLine = _loc.F("flyout.findings", snapshot.Findings.Count, fixable);
        // The honest figure (round 11): the flyout's Clean button runs the
        // safe defaults, so its "reclaimable" line promises exactly that —
        // never deep/dev shelves or locked bytes it would not touch.
        ReclaimLine = _loc.F("flyout.reclaimable",
            Fmt.Bytes(CleanService.ReclaimableNowBytes(snapshot.Cleaner)));
        LastScanLine = _loc.F("flyout.lastscan",
            snapshot.CompletedUtc.ToLocalTime().ToString("HH:mm"));
        Raise(nameof(HasSnapshot));
        FixAllCommand.RaiseCanExecuteChanged();
        CleanSafeCommand.RaiseCanExecuteChanged();
    }
}
