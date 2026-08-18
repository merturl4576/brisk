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
    private readonly SafeCleanRunner _safeClean;
    private readonly FixAllService _fixAll;
    private readonly Loc _loc;
    private readonly Func<bool> _isDryRun;

    private string _healthText = "—";
    private string _healthBrushKey = "";
    private string _findingsLine = "";
    private string _reclaimLine = "";
    private string _lastScanLine = "";
    private string _lastCleanLine = "";
    private bool _busy;

    public FlyoutViewModel(AppState state,
        SafeCleanRunner safeClean, FixAllService fixAll, Loc loc, Func<bool> isDryRun)
    {
        _state = state;
        _safeClean = safeClean;
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
    /// What the last flyout clean actually did (round 13) — the full result,
    /// purge included, so nothing here can quote bytes-moved-to-bin.
    public SafeCleanResult? LastCleanResult { get; private set; }
    /// One brief line, proportionate to a 300 px popup: how much space the
    /// clean really freed. Empty until a clean has run in this session.
    public string LastCleanLine
    {
        get => _lastCleanLine;
        private set
        {
            if (Set(ref _lastCleanLine, value)) Raise(nameof(HasLastClean));
        }
    }
    public bool HasLastClean => _lastCleanLine.Length > 0;
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
            // Round-13 review (I1): the busy flag above guards only THIS
            // button; the lease guards the ONE runner all three clean
            // surfaces share. Taken before anything below mutates the page,
            // so a clean running elsewhere leaves this one untouched.
            using var lease = _safeClean.TryBegin();
            if (lease is null) return;
            var snapshot = _state.Snapshot;
            if (snapshot is null) return;
            LastCleanLine = "";              // a new press starts a new story
            // ROUND 13: the tray Clean runs the same ONE-STEP flow as the
            // Depolama page and the overview button — recycle, then purge
            // exactly this run's own items. The line below therefore reports
            // space that is actually free, not bytes parked in the bin.
            var result = await _safeClean.RunAsync(snapshot.Cleaner);
            LastCleanResult = result;
            LastCleanLine = result.Outcome.WasDryRun
                ? _loc["dryrun.blocked"]
                : result.CleanedCount == 0
                    ? _loc["clean.report.none"]
                    : _loc.F("clean.report.summary.freed",
                        result.CleanedCount, Fmt.Bytes(result.FreedBytes));
            // Round-13 review (I2): the other two surfaces name a partial
            // purge; the tray was the one place that computed the leftover
            // and dropped it, so "1 item cleaned — 0 B freed" arrived with
            // its reason missing. The window is SizeToContent="Height" and
            // the line wraps, so the second sentence costs nothing.
            if (result.LeftInBinBytes > 0)
                LastCleanLine += "\n" + _loc.F("clean.report.binleft",
                    Fmt.Bytes(result.LeftInBinBytes));
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
