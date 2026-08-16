using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using BriskEngine.Models;

namespace Brisk.ViewModels;

public sealed class FindingRow : ViewModelBase
{
    private bool _isExpanded;

    public FindingRow(DiagnosticFinding finding, Loc loc, bool canUndo,
        Action<FindingRow> onFix, Action<FindingRow> onUndo)
    {
        RuleId = finding.RuleId;
        Title = loc.Title(finding.TitleKey, finding.Title);
        Evidence = finding.Evidence;
        ImpactText = new string('●', finding.ImpactStars)
                   + new string('○', 5 - finding.ImpactStars);
        SeverityKey = finding.Severity switch
        {
            Severity.Critical => "SeverityCritical",
            Severity.Warning => "SeverityWarning",
            _ => "SeverityInfo",
        };
        IsAdvise = finding.Category == RuleCategory.Advise;
        CategoryText = IsAdvise ? loc["health.advise"] : "";
        CanFix = finding.CanFix && !IsAdvise;
        CanUndo = canUndo;
        FixCommand = new RelayCommand(() => onFix(this), () => CanFix);
        UndoCommand = new RelayCommand(() => onUndo(this), () => CanUndo);
    }

    public string RuleId { get; }
    public string Title { get; }
    public string Evidence { get; }
    public string ImpactText { get; }
    public string SeverityKey { get; }
    public string CategoryText { get; }
    public bool IsAdvise { get; }
    public bool CanFix { get; }
    public bool CanUndo { get; }
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public RelayCommand FixCommand { get; }
    public RelayCommand UndoCommand { get; }
}

public sealed class HealthViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly IEngineHost _host;
    private readonly Loc _loc;
    private readonly Func<bool> _isDryRun;
    private readonly FixAllService _fixAll;
    private readonly Func<DiagnosticFinding, bool>? _filter;
    private string _scoreText = "—";
    private string _scoreBrushKey = "";
    private string _message = "";
    private bool _createRestorePointFirst;
    private bool _busy;

    public HealthViewModel(AppState state, IEngineHost host, Loc loc, Func<bool> isDryRun,
        FixAllService fixAll, Func<DiagnosticFinding, bool>? filter = null)
    {
        _state = state;
        _host = host;
        _loc = loc;
        _isDryRun = isDryRun;
        _fixAll = fixAll;
        _filter = filter;
        _state.Changed += Refresh;
        ScanCommand = new RelayCommand(() => _ = _state.ScanAsync());
        FixAllCommand = new RelayCommand(() => _ = FixAllAsync(),
            () => Rows.Any(r => r.CanFix));
    }

    public ObservableCollection<FindingRow> Rows { get; } = new();
    public ObservableCollection<FindingRow> AdviseRows { get; } = new();
    public AppState State => _state;
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public string ScoreText { get => _scoreText; private set => Set(ref _scoreText, value); }
    public string ScoreBrushKey
    {
        get => _scoreBrushKey;
        private set => Set(ref _scoreBrushKey, value);
    }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool CreateRestorePointFirst
    {
        get => _createRestorePointFirst;
        set => Set(ref _createRestorePointFirst, value);
    }
    public RelayCommand ScanCommand { get; }
    public RelayCommand FixAllCommand { get; }

    public async Task FixAllAsync()
    {
        if (_busy) return;
        IsBusy = true;                   // set before the first await — re-entry guard
        try
        {
            var snapshot = _state.Snapshot;
            if (snapshot is null) return;
            if (_isDryRun())
            {
                Message = _loc["dryrun.blocked"];
                return;
            }
            if (CreateRestorePointFirst && !await Task.Run(() => _host.CreateRestorePoint()))
            {
                Message = _loc["health.restorepointfailed"];
                return;
            }
            var result = await Task.Run(() => _fixAll.Run(snapshot));
            Message = result.Attempted == 0
                ? _loc["health.nofixables"]
                : result.Applied == result.Attempted
                    ? _loc.F("health.fixdone", result.Applied)
                    : _loc.F("health.fixpartial", result.Applied, result.Attempted);
            await _state.ScanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task FixAsync(FindingRow row)
    {
        if (_busy) return;
        IsBusy = true;                   // set before the first await — re-entry guard
        try
        {
            if (_isDryRun())
            {
                Message = _loc["dryrun.blocked"];
                return;
            }
            Message = (await Task.Run(() => _host.Fix(row.RuleId))).Message;
            await _state.ScanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UndoAsync(FindingRow row)
    {
        if (_busy) return;
        IsBusy = true;                   // set before the first await — re-entry guard
        try
        {
            if (_isDryRun())
            {
                Message = _loc["dryrun.blocked"];
                return;
            }
            Message = (await Task.Run(() => _host.Undo(row.RuleId))).Message;
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
        var undoable = _host.ListUndoable().Select(u => u.RuleId).ToHashSet();
        Rows.Clear();
        AdviseRows.Clear();
        foreach (var finding in snapshot.Findings
                     .Where(f => _filter?.Invoke(f) ?? true)
                     .OrderByDescending(f => f.Severity)
                     .ThenByDescending(f => f.ImpactStars))
            (finding.Category == RuleCategory.Advise ? AdviseRows : Rows)
                .Add(new FindingRow(finding, _loc, undoable.Contains(finding.RuleId),
                    row => _ = FixAsync(row), row => _ = UndoAsync(row)));
        ScoreText = snapshot.Health.ToString();
        ScoreBrushKey = HealthBrush.KeyFor(snapshot.Health);
        FixAllCommand.RaiseCanExecuteChanged();
    }
}
