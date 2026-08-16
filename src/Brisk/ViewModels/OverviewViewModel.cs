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

/// Past-tense outcome label for a completed fix ("Power plan switched to
/// high performance"). A finished action must read as an outcome, never as
/// the problem it solved — a problem title next to an Undo button looks
/// like something fix-all forgot. Prefers the per-rule "rule.&lt;id&gt;.done"
/// key; a rule without one still reads as an outcome via the generic
/// "Fixed: &lt;title&gt;" composition.
internal static class DoneLabel
{
    public static string For(Loc loc, string ruleId, string titleKey, string english)
    {
        var key = $"rule.{ruleId}.done";
        var text = loc[key];   // the indexer returns the key itself when missing
        return string.Equals(text, key, StringComparison.Ordinal)
            ? loc.F("overview.report.fixed", loc.Title(titleKey, english))
            : text;
    }
}

/// One undoable fix in the overview's "recent actions" area (the undo
/// capability formerly living on the Log page).
public sealed class UndoableRow
{
    public UndoableRow(UndoableFix fix, Loc loc, Func<UndoableRow, Task> undo)
    {
        RuleId = fix.RuleId;
        Title = DoneLabel.For(loc, fix.RuleId, $"rule.{fix.RuleId}.title", fix.RuleId);
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
    private readonly ILiveMetrics _live;
    private readonly Loc _loc;
    private readonly Func<bool> _isDryRun;
    private string _scoreText = "—";
    private double _scoreValue;
    private string _scoreBrushKey = "";
    private string _statusText = "";
    private string _summaryText = "";
    private string _reportSummary = "";
    private string _liveCpuText = "—";
    private string _liveRamText = "—";
    private string _liveTempText = "—";
    private string _liveTempCaption;
    private string _liveDiskText = "—";
    private bool _liveBusy;
    private bool _busy;

    public OverviewViewModel(AppState state, IEngineHost host, FixAllService fixAll,
        CleanService cleanService, ILiveMetrics live, Loc loc, Func<bool> isDryRun)
    {
        _state = state;
        _host = host;
        _fixAll = fixAll;
        _cleanService = cleanService;
        _live = live;
        _loc = loc;
        _isDryRun = isDryRun;
        _liveTempCaption = loc["overview.live.temp"];
        _state.Changed += Refresh;
        ScanCommand = new RelayCommand(() =>
        {
            ClearReport();   // a new scan starts a new story
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
    /// Numeric twin of ScoreText for the gauge sweep (0 until the first scan,
    /// which reads as an empty track under the "—" digits).
    public double ScoreValue { get => _scoreValue; private set => Set(ref _scoreValue, value); }
    public string ScoreBrushKey
    {
        get => _scoreBrushKey;
        private set => Set(ref _scoreBrushKey, value);
    }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string SummaryText { get => _summaryText; private set => Set(ref _summaryText, value); }
    /// The report's bottom line ("Result: 210 MB freed · 3 fixes applied").
    /// Empty when the last run changed nothing; the page hides the block.
    public string ReportSummary
    {
        get => _reportSummary;
        private set => Set(ref _reportSummary, value);
    }
    public string LiveCpuText { get => _liveCpuText; private set => Set(ref _liveCpuText, value); }
    public string LiveRamText { get => _liveRamText; private set => Set(ref _liveRamText, value); }
    public string LiveTempText { get => _liveTempText; private set => Set(ref _liveTempText, value); }
    /// "Temperature · GPU" — caption plus the sensor the reading came from.
    public string LiveTempCaption
    {
        get => _liveTempCaption;
        private set => Set(ref _liveTempCaption, value);
    }
    public string LiveDiskText { get => _liveDiskText; private set => Set(ref _liveDiskText, value); }
    public RelayCommand ScanCommand { get; }
    public RelayCommand FixAllCommand { get; }
    public RelayCommand CleanSafeCommand { get; }

    /// MainWindow calls this from IsVisibleChanged/StateChanged. The live
    /// pulse exists only while the window is actually on screen — hidden,
    /// closed-to-tray or minimized means no timer at all (spec promise).
    public void SetLiveVisible(bool visible)
    {
        if (visible) _live.Start(() => _ = LiveTickAsync());
        else _live.Stop();
    }

    /// One tile refresh. The read runs off the UI thread; a tick that finds
    /// the previous one still in flight simply skips (no queue, no overlap).
    public async Task LiveTickAsync()
    {
        if (_liveBusy) return;
        _liveBusy = true;
        try
        {
            var reading = await Task.Run(_live.Read);
            LiveCpuText = Percent(reading.CpuPercent);
            LiveRamText = Percent(reading.RamPercent);
            LiveTempText = reading.TempC is { } t
                ? Math.Round(t).ToString(CultureInfo.InvariantCulture) + "°C"
                : "—";
            LiveTempCaption = reading.TempSource is { } source
                ? _loc["overview.live.temp"] + " · " + source
                : _loc["overview.live.temp"];
            LiveDiskText = Fmt.Bytes(reading.FreeDiskBytes);
        }
        catch
        {
            // A failing sensor read never breaks the page — tiles keep the
            // last good values and the next tick tries again.
        }
        finally
        {
            _liveBusy = false;
        }
    }

    private static string Percent(double? value) => value is { } v
        ? Math.Round(v).ToString(CultureInfo.InvariantCulture) + "%"
        : "—";

    public async Task FixAllAsync()
    {
        if (_busy) return;
        IsBusy = true;                   // set before the first await — re-entry guard
        try
        {
            var snapshot = _state.Snapshot;
            if (snapshot is null) return;
            ClearReport();
            if (_isDryRun())
            {
                ReportLines.Add(_loc["dryrun.blocked"]);
                return;
            }
            var result = await Task.Run(() => _fixAll.Run(snapshot));
            foreach (var finding in result.FixedRules)
                ReportLines.Add(DoneLabel.For(_loc, finding.RuleId,
                    finding.TitleKey, finding.Title));
            foreach (var name in result.DisabledStartup)
                ReportLines.Add(_loc.F("overview.report.disabled", name));
            if (result.Attempted == 0)
                ReportLines.Add(_loc["health.nofixables"]);
            else if (result.Applied < result.Attempted)
                ReportLines.Add(_loc.F("health.fixpartial",
                    result.Applied, result.Attempted));
            var parts = new List<string>();
            if (result.DisabledStartup.Count > 0)
                parts.Add(_loc.F("overview.report.part.startup",
                    result.DisabledStartup.Count));
            if (result.FixedRules.Count > 0)
                parts.Add(_loc.F("overview.report.part.fixes",
                    result.FixedRules.Count));
            SetReportSummary(parts);
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
            ClearReport();
            var outcome = await Task.Run(() => _cleanService.CleanSafe(snapshot.Cleaner));
            if (outcome.WasDryRun)
            {
                ReportLines.Add(_loc["dryrun.blocked"]);
                return;
            }
            ReportLines.Add(_loc.F("clean.recycled",
                outcome.RecycledPaths.Count, Fmt.Bytes(outcome.RecycledBytes)));
            var parts = new List<string>();
            if (outcome.RecycledPaths.Count > 0)
                parts.Add(_loc.F("overview.report.part.freed",
                    Fmt.Bytes(outcome.RecycledBytes)));
            SetReportSummary(parts);
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
            ClearReport();
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

    private void ClearReport()
    {
        ReportLines.Clear();
        ReportSummary = "";
    }

    /// "Result: …" bottom line from the parts a run actually produced. A
    /// non-empty summary is also what shows the closing "enjoy" sentence,
    /// so it is set only when at least one action ran.
    private void SetReportSummary(List<string> parts) =>
        ReportSummary = parts.Count == 0
            ? ""
            : _loc.F("overview.report.summary", string.Join(" · ", parts));

    private void Refresh()
    {
        Recent.Clear();
        foreach (var fix in _host.ListUndoable().OrderByDescending(f => f.FixedAtUtc))
            Recent.Add(new UndoableRow(fix, _loc, UndoAsync));
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        ScoreText = snapshot.Health.ToString(CultureInfo.InvariantCulture);
        ScoreValue = snapshot.Health;
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
