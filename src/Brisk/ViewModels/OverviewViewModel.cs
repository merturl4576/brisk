using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using BriskEngine;
using BriskEngine.Diagnostics;
using BriskEngine.Models;

namespace Brisk.ViewModels;

/// One undoable fix in the overview's "recent actions" area (the undo
/// capability formerly living on the Log page).
public sealed class UndoableRow
{
    public UndoableRow(UndoableFix fix, Loc loc, Func<UndoableRow, Task> undo)
    {
        RuleId = fix.RuleId;
        Title = loc.Title($"rule.{fix.RuleId}.title", fix.RuleId);
        WhenText = fix.FixedAtUtc.ToLocalTime()
            .ToString("dd.MM HH:mm", CultureInfo.InvariantCulture);
        UndoCommand = new RelayCommand(() => _ = undo(this));
    }

    public string RuleId { get; }
    public string Title { get; }
    public string WhenText { get; }
    public RelayCommand UndoCommand { get; }
}

/// The whole-PC page: status hero, one-click actions, a "what was done"
/// report after each action, and the recent (undoable) actions list. The
/// raw action log is no longer a page — ActionLog keeps recording in the
/// engine; only the undo surface lives here.
public sealed class OverviewViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly IEngineHost _host;
    private readonly FixAllService _fixAll;
    private readonly CleanService _cleanService;
    private readonly Loc _loc;
    private readonly Func<bool> _isDryRun;
    private string _scoreText = "—";
    private string _scoreBrushKey = "";
    private string _statusText = "";
    private string _summaryText = "";
    private bool _busy;

    public OverviewViewModel(AppState state, IEngineHost host, FixAllService fixAll,
        CleanService cleanService, Loc loc, Func<bool> isDryRun)
    {
        _state = state;
        _host = host;
        _fixAll = fixAll;
        _cleanService = cleanService;
        _loc = loc;
        _isDryRun = isDryRun;
        _state.Changed += Refresh;
        ScanCommand = new RelayCommand(() =>
        {
            ReportLines.Clear();   // a new scan starts a new story
            _ = _state.ScanAsync();
        });
        // Enabled only while fix-all would actually change something —
        // FixAllService.HasWork is the single source of truth. A disabled
        // button after a run is the reassurance, not a defect.
        FixAllCommand = new RelayCommand(() => _ = FixAllAsync(),
            () => _state.Snapshot is { } s && _fixAll.HasWork(s));
        CleanSafeCommand = new RelayCommand(() => _ = CleanSafeAsync(), () => HasSnapshot);
    }

    public ObservableCollection<string> ReportLines { get; } = new();
    public ObservableCollection<UndoableRow> Recent { get; } = new();
    public AppState State => _state;
    public bool HasSnapshot => _state.Snapshot is not null;
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public string ScoreText { get => _scoreText; private set => Set(ref _scoreText, value); }
    public string ScoreBrushKey
    {
        get => _scoreBrushKey;
        private set => Set(ref _scoreBrushKey, value);
    }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string SummaryText { get => _summaryText; private set => Set(ref _summaryText, value); }
    public RelayCommand ScanCommand { get; }
    public RelayCommand FixAllCommand { get; }
    public RelayCommand CleanSafeCommand { get; }

    public async Task FixAllAsync()
    {
        if (_busy) return;
        IsBusy = true;                   // set before the first await — re-entry guard
        try
        {
            var snapshot = _state.Snapshot;
            if (snapshot is null) return;
            ReportLines.Clear();
            if (_isDryRun())
            {
                ReportLines.Add(_loc["dryrun.blocked"]);
                return;
            }
            var result = await Task.Run(() => _fixAll.Run(snapshot));
            foreach (var finding in result.FixedRules)
                ReportLines.Add(_loc.F("overview.report.fixed",
                    _loc.Title(finding.TitleKey, finding.Title)));
            foreach (var name in result.DisabledStartup)
                ReportLines.Add(_loc.F("overview.report.disabled", name));
            if (result.Attempted == 0)
                ReportLines.Add(_loc["health.nofixables"]);
            else if (result.Applied < result.Attempted)
                ReportLines.Add(_loc.F("health.fixpartial",
                    result.Applied, result.Attempted));
            if (result.Applied > 0)
                ReportLines.Add(_loc["overview.report.enjoy"]);
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
            ReportLines.Clear();
            var outcome = await Task.Run(() => _cleanService.CleanSafe(snapshot.Cleaner));
            if (outcome.WasDryRun)
            {
                ReportLines.Add(_loc["dryrun.blocked"]);
                return;
            }
            ReportLines.Add(_loc.F("clean.recycled",
                outcome.RecycledPaths.Count, Fmt.Bytes(outcome.RecycledBytes)));
            if (outcome.RecycledPaths.Count > 0)
                ReportLines.Add(_loc["overview.report.enjoy"]);
            await _state.ScanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UndoAsync(UndoableRow row)
    {
        if (_busy) return;
        IsBusy = true;                   // set before the first await — re-entry guard
        try
        {
            ReportLines.Clear();
            if (_isDryRun())
            {
                ReportLines.Add(_loc["dryrun.blocked"]);
                return;
            }
            await Task.Run(() => _host.Undo(row.RuleId));
            await _state.ScanAsync();   // Changed handler refreshes Recent
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Refresh()
    {
        Recent.Clear();
        foreach (var fix in _host.ListUndoable().OrderByDescending(f => f.FixedAtUtc))
            Recent.Add(new UndoableRow(fix, _loc, UndoAsync));
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        ScoreText = snapshot.Health.ToString(CultureInfo.InvariantCulture);
        ScoreBrushKey = HealthBrush.KeyFor(snapshot.Health);
        // Three-state headline driven by the same predicate as the fix-all
        // button: work to do → attention; only recommendations left →
        // positive with a count; nothing at all → plain good news.
        var hasWork = _fixAll.HasWork(snapshot);
        var advise = snapshot.Findings.Count(f => f.Category == RuleCategory.Advise);
        StatusText = hasWork ? _loc["overview.status.attention"]
            : advise > 0 ? _loc.F("overview.status.advise", advise)
            : _loc["overview.status.good"];
        // The "{m} one-click fixable" phrase only appears while it is a
        // promise (m > 0); "0 tanesi düzelir" would read as failure.
        var fixable = snapshot.Findings.Count(f =>
            f.Category != RuleCategory.Advise && f.CanFix);
        var parts = new List<string>();
        if (hasWork)
            parts.Add(_loc.F("flyout.findings", snapshot.Findings.Count, fixable));
        parts.Add(_loc.F("flyout.reclaimable", Fmt.Bytes(snapshot.Cleaner.TotalBytes)));
        parts.Add(_loc.F("flyout.lastscan",
            snapshot.CompletedUtc.ToLocalTime().ToString("HH:mm")));
        SummaryText = string.Join("   ·   ", parts);
        Raise(nameof(HasSnapshot));
        FixAllCommand.RaiseCanExecuteChanged();
        CleanSafeCommand.RaiseCanExecuteChanged();
    }
}
