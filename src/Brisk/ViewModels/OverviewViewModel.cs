using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
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
    private readonly Func<ReportCardModel, string, bool> _renderReport;
    private string _scoreText = "—";
    private double _scoreValue;
    private string _scoreBrushKey = "";
    private string _statusText = "";
    private string _summaryText = "";
    private string _reportSummary = "";
    private string _liveCpuText = "—";
    private double _liveCpuPercent;
    private bool _hasCpuArc;
    private string _liveRamText = "—";
    private double _liveRamPercent;
    private bool _hasRamArc;
    private string _liveTempText = "—";
    private string _liveTempBadgeText = "";
    private string _liveTempCaption;
    private string _liveDiskText = "—";
    private string _doneLead = "";
    private string _cleanSafeText;
    private bool _liveBusy;
    private bool _busy;
    private bool _hasRevelation;
    private string _revelationValue = "";
    private string _revelationCaption = "";
    private string _revelationClaim = "";
    private string _revelationEvidence = "";
    private string _revelationMoreText = "";
    private string _revelationEmptyText = "";
    private string _reportSavedText = "";
    /// The rule id behind the revelation band, and "" for BOTH of the cases
    /// with nowhere to go: no revelation at all, and a revelation no findings
    /// page hosts. It used to say the empty id was "what keeps the link
    /// silent" — it was not, and it never had been: the link was a Button on
    /// a command with no canExecute, so it rendered enabled and swallowed the
    /// click. HasRevelationLink is what withholds it now.
    private string _revelationRuleId = "";
    /// Kept in step with _revelationRuleId by RevelationTarget and by nothing
    /// else — the link's visibility, the command's CanExecute and the id the
    /// click carries are three readings of one fact.
    private bool _hasRevelationLink;
    /// Rule ids seen in the undoable list on the previous refresh; null until
    /// the first population so nothing animates at startup.
    private HashSet<string>? _seenUndoable;

    public OverviewViewModel(AppState state, IEngineHost host, FixAllService fixAll,
        SafeCleanRunner safeClean, ILiveMetrics live, Loc loc, Func<bool> isDryRun,
        Func<ReportCardModel, string, bool>? renderReport = null)
    {
        _state = state;
        _host = host;
        _fixAll = fixAll;
        _safeClean = safeClean;
        _live = live;
        _loc = loc;
        _isDryRun = isDryRun;
        _renderReport = renderReport ?? RenderAndCopy;
        _liveTempCaption = loc["overview.live.temp"];
        _cleanSafeText = loc["overview.cleanspace.none"];
        _state.Changed += Refresh;
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
        SaveReportCommand = new RelayCommand(SaveReport, () => HasSnapshot);
        // canExecute, not just a guard inside the body: a RelayCommand built
        // without one answers CanExecute true forever, and the band's link is
        // a Button bound to this command. A command that accepts the click
        // and then does nothing is how the dead affordance got shipped once.
        // Both arms read HasRevelationLink, so there is one fact and two
        // readings of it rather than two predicates that can drift. Execute
        // is guarded as well as CanExecute because a RelayCommand is a plain
        // object: WPF asks first, a direct caller does not have to.
        OpenFindingCommand = new RelayCommand(
            () => { if (HasRevelationLink) OpenFindingRequested?.Invoke(_revelationRuleId); },
            () => HasRevelationLink);
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
    public bool HasRevelation { get => _hasRevelation; private set => Set(ref _hasRevelation, value); }
    public string RevelationValue { get => _revelationValue; private set => Set(ref _revelationValue, value); }
    public string RevelationCaption { get => _revelationCaption; private set => Set(ref _revelationCaption, value); }
    public string RevelationClaim { get => _revelationClaim; private set => Set(ref _revelationClaim, value); }
    public string RevelationEvidence { get => _revelationEvidence; private set => Set(ref _revelationEvidence, value); }
    public string RevelationMoreText { get => _revelationMoreText; private set => Set(ref _revelationMoreText, value); }
    public string RevelationEmptyText { get => _revelationEmptyText; private set => Set(ref _revelationEmptyText, value); }
    public RelayCommand OpenFindingCommand { get; }
    /// Whether the band has anywhere to send a reader. The link's own
    /// Visibility binds to this, so a revelation no page hosts shows the
    /// number and the claim and NO link at all — rather than a live link
    /// that swallows the click.
    public bool HasRevelationLink
    {
        get => _hasRevelationLink;
        private set => Set(ref _hasRevelationLink, value);
    }
    /// Carries the rule id of the finding the band is showing: "see the
    /// evidence" has to open THAT card, so MainWindow routes to whichever
    /// page hosts the rule instead of to a fixed page name.
    public event Action<string>? OpenFindingRequested;
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
    /// Numeric twin of LiveCpuText for the hero's inner CPU arc — real data
    /// driving visible motion every tick. Meaningful only while HasCpuArc is
    /// true: a machine whose CPU counter has not spoken has no percentage,
    /// and the arc is absent rather than resting at zero.
    public double LiveCpuPercent
    {
        get => _liveCpuPercent;
        private set => Set(ref _liveCpuPercent, value);
    }
    /// Whether the CPU sensor spoke at all. The arc's Visibility, and the
    /// whole of decision 4 on this page: an empty arc is a picture of a
    /// measurement that does not exist, so there is no arc to draw.
    public bool HasCpuArc { get => _hasCpuArc; private set => Set(ref _hasCpuArc, value); }
    public string LiveRamText { get => _liveRamText; private set => Set(ref _liveRamText, value); }
    /// Numeric twin of LiveRamText for the hero's inner RAM arc, under
    /// exactly the rule LiveCpuPercent obeys — see HasRamArc.
    public double LiveRamPercent
    {
        get => _liveRamPercent;
        private set => Set(ref _liveRamPercent, value);
    }
    /// Whether the RAM sensor spoke at all; the RAM arc's Visibility.
    public bool HasRamArc { get => _hasRamArc; private set => Set(ref _hasRamArc, value); }
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
    /// The quiet confirmation under the buttons — the saved card's path, or
    /// empty (and hidden) until one has been written this session.
    public string ReportSavedText
    {
        get => _reportSavedText;
        private set => Set(ref _reportSavedText, value);
    }
    public RelayCommand SaveReportCommand { get; }
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
            // The arc flag goes UP with the reading and DOWN with a null, so
            // a sensor that stops answering takes its arc off the glass
            // rather than leaving the last sweep on it as if it were current.
            LiveCpuPercent = reading.CpuPercent ?? 0;
            HasCpuArc = reading.CpuPercent is not null;
            LiveRamText = Percent(reading.RamPercent);
            LiveRamPercent = reading.RamPercent ?? 0;
            HasRamArc = reading.RamPercent is not null;
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

    /// The band is a glance surface: one sentence of evidence, with the full
    /// text one click away behind "see the evidence". Nothing is lost —
    /// it moves to the layer built for reading.
    private static string FirstSentence(string text)
    {
        var cut = text.IndexOf(". ", StringComparison.Ordinal);
        return cut < 0 ? text : text[..(cut + 1)];
    }

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

    /// The card is built from the snapshot already on screen — no rescan, so
    /// the picture a person shares is the one they were just looking at.
    ///
    /// Three outcomes, three sentences. The line used to promise "(copied to
    /// the clipboard)" unconditionally, including in the one case the copy's
    /// catch exists to absorb — the page stating something untrue about work
    /// it had just watched fail.
    private void SaveReport()
    {
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        // The try opens HERE, not after the model is built. Building it reads
        // the fix journal, and a corrupt fix-journal.jsonl throws in that read
        // — outside the old try, which left the GUI as the one surface with no
        // honest answer for a card it could not BUILD, only for one it could
        // not write. Same failure, same button, same sentence owed.
        try
        {
            var path = ReportRunner.DefaultPath();
            var model = ReportCardModel.Build(snapshot, _host.ListUndoable(), _loc);
            ReportSavedText = _renderReport(model, path)
                ? _loc.F("overview.report.card.saved", path)
                : _loc.F("overview.report.card.saved.fileonly", path);
        }
        catch (Exception ex)
        {
            // A read-only Pictures folder or a full disk is what the console
            // verb answers with "brisk: {message}". The button owes the same
            // sentence, not an unhandled-exception dialog over a
            // confirmation line that says nothing at all.
            ReportSavedText = _loc.F("overview.report.card.failed", ex.Message);
        }
    }

    /// The default surface behavior: write the PNG, then best-effort copy to
    /// the clipboard — a locked clipboard must not turn a saved card into an
    /// error. Returns whether the copy actually happened, because the line
    /// the page shows has to say which of the two it was.
    private static bool RenderAndCopy(ReportCardModel model, string path)
    {
        ReportCardRenderer.RenderToFile(model, path);
        try
        {
            // OnLoad rather than the OnDemand default, and frozen: a
            // BitmapImage left on demand holds the file open until a GC gets
            // round to it, and the renderer writes the card with File.Create
            // — FileShare.None. The next Save would meet a sharing violation
            // on a handle nothing was using any more.
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            System.Windows.Clipboard.SetImage(image);
            return true;
        }
        catch (Exception) { return false; }
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
        // The saved line names a card built from the snapshot being replaced
        // right now. Left up, it would point at a picture of the machine as
        // it was — so it goes with the scan it described.
        ReportSavedText = "";
        DoneRows.Clear();
        var undoable = _host.ListUndoable();
        foreach (var fix in undoable.OrderByDescending(f => f.FixedAtUtc))
            DoneRows.Add(new UndoableRow(fix, _loc, UndoAsync,
                isNew: _seenUndoable is { } seen && !seen.Contains(fix.RuleId)));
        _seenUndoable = undoable.Select(f => f.RuleId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var snapshot = _state.Snapshot;
        if (snapshot is null)
        {
            // Refresh is also the re-projection a language switch gets, and a
            // switch can land before the first scan. The clean button's
            // generic label is the one caption that lives out here and cannot
            // heal itself — the live caption is rewritten on its next tick.
            CleanSafeText = _loc["overview.cleanspace.none"];
            return;
        }
        ScoreText = snapshot.Health.ToString(CultureInfo.InvariantCulture);
        ScoreValue = snapshot.Health;
        ScoreBrushKey = HealthBrush.KeyFor(snapshot.Health);
        // The revelation band: the scan's leading measured number, chosen by
        // the same picker every other surface will use.
        var revelations = RevelationPicker.Pick(snapshot.Findings);
        HasRevelation = revelations.Count > 0;
        if (revelations.Count > 0)
        {
            var top = revelations[0];
            var (value, caption) = LocalizedText.Headline(top.Headline!, _loc);
            RevelationValue = value;
            RevelationCaption = caption;
            RevelationClaim = _loc.Title(top.TitleKey, top.Title);
            RevelationEvidence = FirstSentence(LocalizedText.Evidence(top, _loc));
            RevelationMoreText = revelations.Count > 1
                ? _loc.F("overview.revelation.more", revelations.Count - 1) : "";
            RevelationEmptyText = "";
            // Every picked finding has a page that hosts it, so every one of
            // them gets a link. FindingSections routes a rule id to exactly
            // one of the three findings pages — IsPerformance and IsPrivacy
            // are the named topics, Sağlık is what is left — and MainWindow's
            // handler has an arm for each.
            //
            // It was not always so. A privacy revelation used to be given an
            // empty target and no link: MainWindow sent everything that was
            // not a performance rule to Sağlık, whose filter excludes the
            // privacy ids, so the link changed the page and opened nothing.
            // The MECHANISM stays exactly where it was — a band with nowhere
            // to send a reader still has to withhold the control rather than
            // render a Button that swallows the click, which is the defect it
            // was built for — and what changed is that a privacy finding now
            // has somewhere to go. What it still answers for is the else
            // below, and WithNoRevelationToShow_TheBandWithholdsItsLink_And
            // TheCommandRefuses is what holds it there: for one commit that
            // sentence had no test behind it anywhere, because the pair that
            // used to assert the false case were rewritten into their
            // opposites when the privacy finding got its page.
            RevelationTarget(top.RuleId);
        }
        else
        {
            RevelationValue = ""; RevelationCaption = "";
            RevelationClaim = ""; RevelationEvidence = ""; RevelationMoreText = "";
            RevelationEmptyText = _loc.F("overview.revelation.none",
                DiagnosticRuleRegistry.All.Count);
            RevelationTarget("");
        }
        // Three-state headline driven by the same predicate as the fix-all
        // button: work to do → attention; only recommendations left →
        // positive with a count; neither → plain good news.
        //
        // "Neither" is not "nothing found". HasWork stopped seeing privacy
        // findings when they were excluded from the button, so a machine
        // whose only fixable findings are the four telemetry switches reads
        // "good shape" here and suppresses the {n} findings line below with
        // it. That is the wave's red line working as written — the health
        // score grades speed and hygiene and does not grade privacy, and a
        // fast clean machine reads 100 whether or not telemetry is on.
        //
        // The "{n} findings" figure below counts the WHOLE snapshot, privacy
        // included, and it always did. What changed with the Gizlilik page is
        // that the count stopped promising rows nobody could reach: on a
        // machine with one power-plan finding and four telemetry switches
        // this said "5 findings · 1 one-click fixable", both numbers true and
        // four of the five with no surface anywhere. The figure was left
        // exactly as it is — the page is what made it honest, not an
        // adjustment to the arithmetic, because subtracting privacy from the
        // total would have understated what brisk found in order to match a
        // gap in the GUI.
        var hasWork = _fixAll.HasWork(snapshot);
        var advise = snapshot.Findings.Count(f => f.Category == RuleCategory.Advise);
        StatusText = hasWork ? _loc["overview.status.attention"]
            : advise > 0 ? _loc.F("overview.status.advise", advise)
            : _loc["overview.status.good"];
        // The "{m} one-click fixable" phrase only appears while it is a
        // promise (m > 0); "0 tanesi düzelir" would read as failure. Here
        // that is HasWork's doing rather than a test on m — the phrase rides
        // the whole findings line, and HasWork is false exactly when this
        // page's button would do nothing.
        //
        // The same predicate THIS page's button obeys, not a second copy of
        // it: "one-click fixable" counts what pressing "Fix all (safe)" will
        // actually do. It is not a count of everything brisk can fix in one
        // click, and has not been since the Gizlilik page shipped a button of
        // its own over the set this predicate excludes by rule id.
        var fixable = snapshot.Findings.Count(FixAllService.IsOneClickFixable);
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
        SaveReportCommand.RaiseCanExecuteChanged();
    }

    /// The one place _revelationRuleId is written, so the id, the link's
    /// visibility and the command's enabled state cannot disagree. An empty
    /// id means there is nowhere to go.
    private void RevelationTarget(string ruleId)
    {
        _revelationRuleId = ruleId;
        HasRevelationLink = ruleId.Length > 0;
        OpenFindingCommand.RaiseCanExecuteChanged();
    }
}
