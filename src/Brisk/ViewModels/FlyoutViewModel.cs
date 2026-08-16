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
    private readonly IEngineHost _host;
    private readonly CleanService _cleanService;
    private readonly Loc _loc;
    private readonly Func<bool> _isDryRun;

    private string _healthText = "—";
    private string _findingsLine = "";
    private string _reclaimLine = "";
    private string _lastScanLine = "";
    private CleanOutcome? _lastCleanOutcome;

    public FlyoutViewModel(AppState state, IEngineHost host,
        CleanService cleanService, Loc loc, Func<bool> isDryRun)
    {
        _state = state;
        _host = host;
        _cleanService = cleanService;
        _loc = loc;
        _isDryRun = isDryRun;
        _state.Changed += Refresh;
        ScanCommand = new RelayCommand(() => _ = ScanNowAsync());
        FixAllCommand = new RelayCommand(() => _ = FixAllAsync(), () => HasSnapshot);
        CleanSafeCommand = new RelayCommand(() => _ = CleanSafeAsync(), () => HasSnapshot);
        OpenDetailsCommand = new RelayCommand(() => OpenDetailsRequested?.Invoke());
    }

    public string HealthText { get => _healthText; private set => Set(ref _healthText, value); }
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

    public RelayCommand ScanCommand { get; }
    public RelayCommand FixAllCommand { get; }
    public RelayCommand CleanSafeCommand { get; }
    public RelayCommand OpenDetailsCommand { get; }

    public event Action? OpenDetailsRequested;

    public Task ScanNowAsync() => _state.ScanAsync();

    public async Task FixAllAsync()
    {
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        if (_isDryRun()) return;   // dry run: report only, nothing to fix here
        foreach (var finding in snapshot.Findings
                     .Where(f => f.Category == RuleCategory.Auto && f.CanFix))
            _host.Fix(finding.RuleId);
        await _state.ScanAsync();
    }

    public async Task CleanSafeAsync()
    {
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        var eligible = snapshot.Cleaner.Targets.Where(t =>
            t.Target.Level == CleanupLevel.Safe
            && t.SkippedReason is null
            && !t.Target.RequiresIndividualSelection
            && !t.Target.RequiresExplicitOptIn
            && t.Items.Count > 0);
        LastCleanOutcome = _cleanService.CleanTargets(eligible);
        await _state.ScanAsync();
    }

    private void Refresh()
    {
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        HealthText = snapshot.Health.ToString();
        var fixable = snapshot.Findings.Count(f =>
            f.Category == RuleCategory.Auto && f.CanFix);
        FindingsLine = _loc.F("flyout.findings", snapshot.Findings.Count, fixable);
        ReclaimLine = _loc.F("flyout.reclaimable", Fmt.Bytes(snapshot.Cleaner.TotalBytes));
        LastScanLine = _loc.F("flyout.lastscan",
            snapshot.CompletedUtc.ToLocalTime().ToString("HH:mm"));
        Raise(nameof(HasSnapshot));
        FixAllCommand.RaiseCanExecuteChanged();
        CleanSafeCommand.RaiseCanExecuteChanged();
    }
}
