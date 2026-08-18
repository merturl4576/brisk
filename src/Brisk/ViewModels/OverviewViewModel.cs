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

/// One line of the "What brisk did" report. Undo capability stays on the
/// row, but with no visible affordance at all: it lives in the row's
/// right-click context menu (round 9) — the report reads purely as good
/// work done, and the escape hatch is one quiet, native gesture away.
public sealed class UndoableRow
{
    public UndoableRow(UndoableFix fix, Loc loc, Func<UndoableRow, Task> undo,
        bool isNew = false)
    {
        RuleId = fix.RuleId;
        Title = DoneLabel.For(loc, fix.RuleId, $"rule.{fix.RuleId}.title", fix.RuleId);
        WhenText = fix.FixedAtUtc.ToLocalTime()
            .ToString("dd.MM HH:mm", CultureInfo.InvariantCulture);
        IsNew = isNew;
        UndoCommand = new RelayCommand(() => _ = undo(this));
    }

    public string RuleId { get; }
    public string Title { get; }
    public string WhenText { get; }
    /// True only for a row added after a fix run in this session — it gets
    /// the one-shot entry animation. The startup population stays calm.
    public bool IsNew { get; }
    public RelayCommand UndoCommand { get; }
}

/// The whole-PC page: status hero, one-click actions, and ONE report block
/// with two faces — the run-scoped "what was done" story right after an
/// action, and the journal-driven "what brisk did" report the rest of the
/// time (every fix still in effect, with its date). The raw action log is
/// no longer a page — ActionLog keeps recording in the engine.
public sealed class OverviewViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly IEngineHost _host;
    private readonly FixAllService _fixAll;
    private readonly SafeCleanRunner _safeClean;
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
    private double _liveCpuPercent;
    private string _liveRamText = "—";
    private string _liveTempText = "—";
    private string _liveTempBadgeText = "";
    private string _liveTempCaption;
    private string _liveDiskText = "—";
    private string _doneLead = "";
    private string _cleanSafeText;
    private bool _liveBusy;
    private bool _busy;
    /// Rule ids seen in the undoable list on the previous refresh; null until
    /// the first population so nothing animates at startup.
    private HashSet<string>? _seenUndoable;

    public OverviewViewModel(AppState state, IEngineHost host, FixAllService fixAll,
        SafeCleanRunner safeClean, ILiveMetrics live, Loc loc, Func<bool> isDryRun)
    {
        _state = state;
        _host = host;
        _fixAll = fixAll;
        _safeClean = safeClean;
        _live = live;
        _loc = loc;
        _isDryRun = isDryRun;
        _liveTempCaption = loc["overview.live.temp"];
        _cleanSafeText = loc["overview.cleanspace.none"];
        _state.Changed += Refresh;
        // Finding 4 re-review: DisplayNotice reached only Health and
        // Performance, but ShowMain() surfaces the window on whichever page is
        // selected — Overview by default. So the likeliest user watched the
        // screen flick back and was told nothing at all. It lands in the
        // run-scoped report, dotless: a rollback is not a green ✓.
        _state.DisplayNotice += msg =>
            ReportLines.Add(new ReportLine(msg, IsDone: false));
        // The report block's two faces share one visibility contract; any
        // mutation of either collection re-evaluates it (and the lead line).
        ReportLines.CollectionChanged += (_, _) => RaiseReportState();
        DoneRows.CollectionChanged += (_, _) => RaiseReportState();
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

    public ObservableCollection<ReportLine> ReportLines { get; } = new();
    /// Journal-driven report rows: every fix still in effect, newest first.
    public ObservableCollection<UndoableRow> DoneRows { get; } = new();
    /// The journal report's lead sentence ("{n} improvements are active…");
    /// empty while the journal is empty.
    public string DoneLead
    {
        get => _doneLead;
        private set => Set(ref _doneLead, value);
    }
    /// The journal face shows only while no run-scoped report is on screen
    /// and the journal actually has something to say — never an empty frame.
    public bool ShowDoneReport => ReportLines.Count == 0 && DoneRows.Count > 0;
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
    /// The report's ✓ lead line ("Result: 210 MB freed · 3 fixes applied").
    /// Empty when the last run changed nothing; the pages hide the lead
    /// and the closing note then.
    public string ReportSummary
    {
        get => _reportSummary;
        private set => Set(ref _reportSummary, value);
    }
    public string LiveCpuText { get => _liveCpuText; private set => Set(ref _liveCpuText, value); }
    /// Numeric twin of LiveCpuText for the hero's inner CPU ring — real data
    /// driving visible motion every tick. 0 (an empty arc) until the CPU
    /// sensor has a delta to report.
    public double LiveCpuPercent
    {
        get => _liveCpuPercent;
        private set => Set(ref _liveCpuPercent, value);
    }
    public string LiveRamText { get => _liveRamText; private set => Set(ref _liveRamText, value); }
    public string LiveTempText { get => _liveTempText; private set => Set(ref _liveTempText, value); }
    /// The gauge's compact center readout ("GPU 78°C"). Empty (and hidden)
    /// until a real sensor speaks — the cockpit never renders a dash there.
    public string LiveTempBadgeText
    {
        get => _liveTempBadgeText;
        private set => Set(ref _liveTempBadgeText, value);
    }
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
    /// The clean button wears its benefit (round 11): "Free up 1.2 GB",
    /// using the SAME honest figure the Depolama card promises. Before a
    /// scan, or with nothing to take, it stays the plain generic label.
    public string CleanSafeText
    {
        get => _cleanSafeText;
        private set => Set(ref _cleanSafeText, value);
    }

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
            LiveCpuPercent = reading.CpuPercent ?? 0;
            LiveRamText = Percent(reading.RamPercent);
            LiveTempText = reading.TempC is { } t
                ? Math.Round(t).ToString(CultureInfo.InvariantCulture) + "°C"
                : "—";
            LiveTempBadgeText = reading.TempC is { } badge && reading.TempSource is { } via
                ? via + " " + Math.Round(badge).ToString(CultureInfo.InvariantCulture) + "°C"
                : "";
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
        // Held while a display change is still unconfirmed — every fix
        // surface is, not just this one (AppState.IsAwaitingDisplayConfirmation).
        if (_busy || _state.IsAwaitingDisplayConfirmation) return;
        IsBusy = true;                   // set before the first await — re-entry guard
        try
        {
            var snapshot = _state.Snapshot;
            if (snapshot is null) return;
            ClearReport();
            if (_isDryRun())
            {
                ReportLines.Add(new ReportLine(_loc["dryrun.blocked"], IsDone: false));
                return;
            }
            var result = await Task.Run(() => _fixAll.Run(snapshot));
            ReportSummary = FixReport.Populate(_loc, result, ReportLines);
            // The display confirmation is raised as the mode changes, from
            // FixAllService itself (AppState.TrackFixes) — not from this
            // report-time loop, which only runs once the whole batch is done.
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
            ClearReport();
            // ROUND 13: the same ONE-STEP flow the Depolama page runs. This
            // button promises "Free up 1.2 GB" — so the bytes have to leave
            // the disk, not move to the Recycle Bin and sit there. Every
            // figure below is POST-purge truth.
            var result = await _safeClean.RunAsync(lease, snapshot.Cleaner);
            if (result.Outcome.WasDryRun)
            {
                ReportLines.Add(new ReportLine(_loc["dryrun.blocked"], IsDone: false));
                return;
            }
            ReportLines.Add(result.CleanedCount > 0
                ? new ReportLine(_loc.F("clean.report.summary.freed",
                    result.CleanedCount, Fmt.Bytes(result.FreedBytes)), IsDone: true)
                : new ReportLine(_loc["clean.report.none"], IsDone: false));
            // Partial purge stays visible and honest here too — never folded
            // into the freed figure, never wearing the done dot.
            if (result.LeftInBinBytes > 0)
                ReportLines.Add(new ReportLine(_loc.F("clean.report.binleft",
                    Fmt.Bytes(result.LeftInBinBytes)), IsDone: false));
            ReportSummary = result.CleanedCount == 0
                ? ""
                : _loc.F("overview.report.summary", _loc.F("overview.report.part.freed",
                    Fmt.Bytes(result.FreedBytes)));
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
                ReportLines.Add(new ReportLine(_loc["dryrun.blocked"], IsDone: false));
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

    private void RaiseReportState()
    {
        DoneLead = DoneRows.Count > 0
            ? _loc.F("overview.report.live", DoneRows.Count) : "";
        Raise(nameof(ShowDoneReport));
    }

    private void Refresh()
    {
        DoneRows.Clear();
        var undoable = _host.ListUndoable();
        foreach (var fix in undoable.OrderByDescending(f => f.FixedAtUtc))
            DoneRows.Add(new UndoableRow(fix, _loc, UndoAsync,
                isNew: _seenUndoable is { } seen && !seen.Contains(fix.RuleId)));
        _seenUndoable = undoable.Select(f => f.RuleId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
        // Honest figure (round 11): what the safe clean can take right now —
        // the summary line and the clean button's label share it.
        var reclaimable = CleanService.ReclaimableNowBytes(snapshot.Cleaner);
        parts.Add(_loc.F("flyout.reclaimable", Fmt.Bytes(reclaimable)));
        CleanSafeText = reclaimable > 0
            ? _loc.F("overview.cleanspace", Fmt.Bytes(reclaimable))
            : _loc["overview.cleanspace.none"];
        parts.Add(_loc.F("flyout.lastscan",
            snapshot.CompletedUtc.ToLocalTime().ToString("HH:mm")));
        SummaryText = string.Join("   ·   ", parts);
        Raise(nameof(HasSnapshot));
        FixAllCommand.RaiseCanExecuteChanged();
        CleanSafeCommand.RaiseCanExecuteChanged();
    }
}
