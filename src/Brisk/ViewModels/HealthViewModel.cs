using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using BriskEngine.Models;

namespace Brisk.ViewModels;

public sealed class FindingRow : ViewModelBase
{
    /// Advise rules whose advice has a real in-app follow-up: they point at
    /// space that the Depolama page can actually show and clean. Thermals
    /// and RAM pressure deliberately get no button — brisk has no in-app
    /// action for them and a fake one would be worse than none.
    private static readonly HashSet<string> StorageAdviceRules = new(
        new[] { "disk-breakdown", "disk-forecast", "orphaned-data",
                "stale-dev-caches" },
        StringComparer.OrdinalIgnoreCase);

    private bool _isExpanded;
    private bool _isDetailsShown;

    public FindingRow(DiagnosticFinding finding, Loc loc, bool canUndo,
        Action<FindingRow> onFix, Action<FindingRow> onUndo,
        Action<FindingRow>? onOpenStorage = null)
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
        // Advise rows speak in the user's language: the localized advice is
        // the body and the engine's English evidence retreats behind the
        // "Details" fold. A rule without an advice key keeps its evidence as
        // the body (still informative) and gets no redundant fold.
        var adviceKey = $"rule.{finding.RuleId}.advice";
        var advice = loc[adviceKey];   // the indexer returns the key when missing
        var hasAdviceKey = !string.Equals(advice, adviceKey, StringComparison.Ordinal);
        AdviceText = IsAdvise && hasAdviceKey ? advice : Evidence;
        HasDetails = IsAdvise && hasAdviceKey;
        HasStorageAction = IsAdvise && onOpenStorage is not null
            && StorageAdviceRules.Contains(finding.RuleId);
        FixCommand = new RelayCommand(() => onFix(this), () => CanFix);
        UndoCommand = new RelayCommand(() => onUndo(this), () => CanUndo);
        OpenStorageCommand = new RelayCommand(
            () => onOpenStorage?.Invoke(this), () => HasStorageAction);
    }

    public string RuleId { get; }
    public string Title { get; }
    public string Evidence { get; }
    public string AdviceText { get; }
    public string ImpactText { get; }
    public string SeverityKey { get; }
    public string CategoryText { get; }
    public bool IsAdvise { get; }
    public bool CanFix { get; }
    public bool CanUndo { get; }
    public bool HasDetails { get; }
    public bool HasStorageAction { get; }
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public bool IsDetailsShown
    {
        get => _isDetailsShown;
        set => Set(ref _isDetailsShown, value);
    }
    public RelayCommand FixCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand OpenStorageCommand { get; }
}

public sealed class HealthViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly IEngineHost _host;
    private readonly Loc _loc;
    private readonly Func<bool> _isDryRun;
    private readonly FixAllService _fixAll;
    private readonly Func<DiagnosticFinding, bool>? _filter;
    private readonly IReadOnlyList<string>? _optimizedRuleIds;
    private string _scoreText = "—";
    private string _scoreBrushKey = "";
    private string _message = "";
    private bool _createRestorePointFirst;
    private bool _busy;

    public HealthViewModel(AppState state, IEngineHost host, Loc loc, Func<bool> isDryRun,
        FixAllService fixAll, Func<DiagnosticFinding, bool>? filter = null,
        IReadOnlyList<string>? optimizedRuleIds = null)
    {
        _state = state;
        _host = host;
        _loc = loc;
        _isDryRun = isDryRun;
        _fixAll = fixAll;
        _filter = filter;
        _optimizedRuleIds = optimizedRuleIds;
        _state.Changed += Refresh;
        ScanCommand = new RelayCommand(() => _ = _state.ScanAsync());
        // Enabled only while fix-all would actually change something. The
        // predicate lives on FixAllService (single source of truth) and is
        // deliberately unfiltered: fix-all acts on the whole snapshot, not
        // just the rows this page shows.
        FixAllCommand = new RelayCommand(() => _ = FixAllAsync(),
            () => _state.Snapshot is { } s && _fixAll.HasWork(s));
    }

    public ObservableCollection<FindingRow> Rows { get; } = new();
    public ObservableCollection<FindingRow> AdviseRows { get; } = new();
    /// Past-tense done labels for the configured fixable rules that have no
    /// finding right now — "this is already in good shape", not a to-do.
    /// Only pages given optimizedRuleIds (Performans) populate it; a rule
    /// with an active finding shows in the findings list instead, never both.
    public ObservableCollection<string> OptimizedRows { get; } = new();

    /// Raised by an advise card's "Open Storage" button; MainWindow answers
    /// by switching to the Depolama page (same pattern as the flyout's
    /// OpenDetailsRequested).
    public event Action? OpenStorageRequested;

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
                    row => _ = FixAsync(row), row => _ = UndoAsync(row),
                    _ => OpenStorageRequested?.Invoke()));
        OptimizedRows.Clear();
        if (_optimizedRuleIds is not null)
            foreach (var id in _optimizedRuleIds)
                if (!snapshot.Findings.Any(f =>
                        string.Equals(f.RuleId, id, StringComparison.OrdinalIgnoreCase)))
                    OptimizedRows.Add(DoneLabel.For(_loc, id, $"rule.{id}.title", id));
        ScoreText = snapshot.Health.ToString();
        ScoreBrushKey = HealthBrush.KeyFor(snapshot.Health);
        FixAllCommand.RaiseCanExecuteChanged();
    }
}
