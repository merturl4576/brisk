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

    /// The other direction the same idea runs in: advice whose follow-up is
    /// not in brisk at all, but in a page of Windows' own Settings app. One
    /// entry, and the reason it is one is the reason the entry exists —
    /// RecallStatusRule reports the policy and refuses to write it, because
    /// the surface is new, it differs between builds, and a fix brisk cannot
    /// check afterwards is the one thing this project refuses to ship. The
    /// spec's answer to that refusal is this link, in as many words: "Recall
    /// appears here as state only, with a link to Windows' own setting."
    ///
    /// THE URI IS THE PRIVACY ROOT AND NOT A RECALL PAGE, deliberately.
    /// "ms-settings:privacy" is Privacy & security, which every Windows brisk
    /// runs on has, and which is where Recall's own setting sits on the
    /// builds that have Recall. A Recall-specific sub-page is something brisk
    /// cannot check exists from here — an ms-settings URI Windows does not
    /// know opens the Settings app on nothing and throws nothing — so
    /// pointing at one would be brisk sending a reader somewhere it never
    /// established was there. That is the same claim-without-evidence the
    /// rule itself declines to make about the setting.
    ///
    /// THE CAPTION IS PART OF THE ENTRY, not a fixed string in the markup.
    /// A destination and the words on the button that reaches it are one
    /// decision: a second entry pointing anywhere else, over a caption
    /// written for this one, would render a button naming a page it does not
    /// open — and nothing would object, because a caption that is not a
    /// function of the URI cannot disagree with it. Declaring both here is
    /// what makes adding a destination without saying what its button says
    /// impossible rather than merely unwise.
    internal static readonly Dictionary<string, (string Uri, string CaptionKey)>
        WindowsSettingRules = new(StringComparer.OrdinalIgnoreCase)
        {
            ["recall-status"] = ("ms-settings:privacy",
                "finding.action.windowssetting"),
        };

    private bool _isExpanded;
    private bool _isDetailsShown;
    private bool _isFixing;
    private bool _isUndoing;
    private bool _isFixed;

    public FindingRow(DiagnosticFinding finding, Loc loc, bool canUndo,
        Action<FindingRow> onFix, Action<FindingRow> onUndo,
        Action<FindingRow>? onOpenStorage = null,
        Action<FindingRow>? onOpenWindowsSetting = null)
    {
        RuleId = finding.RuleId;
        Title = loc.Title(finding.TitleKey, finding.Title);
        DoneTitle = DoneLabel.For(loc, finding.RuleId, finding.TitleKey,
            finding.Title);
        Evidence = LocalizedText.Evidence(finding, loc);
        HasHeadline = finding.Headline is not null;
        HeadlineValue = finding.Headline is { } headline
            ? LocalizedText.Headline(headline, loc).Value : "";
        ImpactText = new string('●', finding.ImpactStars)
                   + new string('○', 5 - finding.ImpactStars);
        // Whether the meter above is a reading at all. ImpactStars is
        // documented 1..5 and measures expected PERFORMANCE impact — so the
        // privacy rules, which cost no speed, all report ONE, and they report
        // it because zero is outside the range rather than because anything
        // was measured. Both engine families say so in as many words
        // (TelemetrySwitchRule.Detect, PrivacyDisclosureRule.Disclosure).
        // Rendered, that is ●○○○○ on every privacy row: a meter claiming a
        // measurement nobody made, on the one page whose whole subject is
        // brisk not claiming what it did not read.
        //
        // Advise rows lose it for a different reason — "how big is the
        // problem" is the wrong frame for advice — and one property answers
        // for both, because the question the card asks is the same one: does
        // this row have an impact reading worth showing?
        ShowsImpact = finding.Category != RuleCategory.Advise
            && !FindingSections.IsPrivacy(finding);
        // Kind is read BEFORE Severity, and that order is the claim: Severity
        // still says how bad the thing is, but Kind says whether brisk is in a
        // position to call it a problem at all. v0.4 took the score penalty off
        // a Notice for that reason and left the colour charging.
        SeverityKey = finding.Kind == FindingKind.Notice ? "SeverityNotice"
            : finding.Severity switch
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
        // What this switch takes away, in the rule's own words, for the rows
        // that sit on the Privacy page's second tier. Same shape as the
        // advice key above: a rule that declares no cost simply has no key,
        // and the indexer answering with the key itself is what says so. No
        // list of "the costly ones" lives here — the rule declares its own.
        var costKey = $"rule.{finding.RuleId}.cost";
        var cost = loc[costKey];
        HasCost = !string.Equals(cost, costKey, StringComparison.Ordinal);
        CostText = HasCost ? cost : "";
        HasStorageAction = IsAdvise && onOpenStorage is not null
            && StorageAdviceRules.Contains(finding.RuleId);
        // Same three-part test as HasStorageAction above, for the same three
        // reasons: the rule declares the destination, the row only advertises
        // an action somebody is wired to perform, and IsAdvise is what keeps
        // the link on a row brisk offers no fix for — a link beside a Fix
        // button would be brisk pointing at the change it just declined to
        // make.
        var setting = IsAdvise && onOpenWindowsSetting is not null
            && WindowsSettingRules.TryGetValue(finding.RuleId, out var declared)
                ? declared : default;
        WindowsSettingUri = setting.Uri ?? "";
        // Resolved here rather than bound to a fixed key in the card, so the
        // words on the button come from the same entry the click does. Row-
        // computed like Title and CostText, and re-read for the same reason
        // they are: AppState.SetLanguage raises Changed, and every row is
        // rebuilt from it.
        WindowsSettingCaption = setting.CaptionKey is { } key ? loc[key] : "";
        HasWindowsSettingAction = WindowsSettingUri.Length > 0;
        FixCommand = new RelayCommand(() => onFix(this), () => CanFix);
        UndoCommand = new RelayCommand(() => onUndo(this), () => CanUndo);
        OpenStorageCommand = new RelayCommand(
            () => onOpenStorage?.Invoke(this), () => HasStorageAction);
        OpenWindowsSettingCommand = new RelayCommand(
            () => onOpenWindowsSetting?.Invoke(this), () => HasWindowsSettingAction);
    }

    public string RuleId { get; }
    public string Title { get; }
    /// Past-tense outcome the title crossfades to when the row reaches its
    /// Fixed state (same label the reports use).
    public string DoneTitle { get; }
    public string Evidence { get; }
    public string AdviceText { get; }
    public string ImpactText { get; }
    /// Whether ImpactText is a reading rather than a placeholder — see the
    /// constructor. The card binds the meter's visibility to this.
    public bool ShowsImpact { get; }
    public string SeverityKey { get; }
    public string CategoryText { get; }
    public bool IsAdvise { get; }
    /// The named loss beside a switch that costs the user something
    /// ("Find my device stops working"), and false for every rule that
    /// declares none.
    public bool HasCost { get; }
    public string CostText { get; }
    public bool CanFix { get; }
    public bool CanUndo { get; }
    public bool HasDetails { get; }
    public bool HasStorageAction { get; }
    /// Whether this row points at a setting WINDOWS owns — the one action on
    /// the Privacy page that is not a change brisk makes.
    public bool HasWindowsSettingAction { get; }
    /// The page of Windows' own Settings app the link opens, and "" for every
    /// row that points nowhere.
    public string WindowsSettingUri { get; }
    /// What the link's button says, in the user's language — declared in the
    /// same entry that names the URI, so a caption and a destination that
    /// describe different pages are visible in one place rather than sitting
    /// in two files. One declaration site is not a consistency check:
    /// ("ms-settings:bluetooth", "finding.action.windowssetting") compiles and
    /// renders "Open Windows privacy settings" over a button that opens
    /// Bluetooth. What the entry makes impossible is adding a destination
    /// without saying what its button says.
    public string WindowsSettingCaption { get; }
    public bool HasHeadline { get; }
    /// The lead value in the user's language ("57 sn"); "" when the finding
    /// carries no headline — the card collapses the column then.
    public string HeadlineValue { get; }
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public bool IsDetailsShown
    {
        get => _isDetailsShown;
        set => Set(ref _isDetailsShown, value);
    }

    /// Visual fix lifecycle: Normal → Fixing → Fixed, back to Normal on
    /// failure. Undo keeps its own working flag so each button can wear its
    /// own in-progress label; both share the row-level working pulse. The
    /// view disables the action buttons off these flags (via triggers, not
    /// CanExecute — fix-all publishes progress from a worker thread, and
    /// scalar property changes are the only thing WPF marshals for free).
    public bool IsFixing
    {
        get => _isFixing;
        private set { if (Set(ref _isFixing, value)) Raise(nameof(IsWorking)); }
    }
    public bool IsUndoing
    {
        get => _isUndoing;
        private set { if (Set(ref _isUndoing, value)) Raise(nameof(IsWorking)); }
    }
    public bool IsWorking => _isFixing || _isUndoing;
    public bool IsFixed { get => _isFixed; private set => Set(ref _isFixed, value); }

    public void BeginFix() => IsFixing = true;
    public void CompleteFix(bool ok) { IsFixing = false; IsFixed = ok; }
    public void BeginUndo() => IsUndoing = true;
    public void CompleteUndo() => IsUndoing = false;

    public RelayCommand FixCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand OpenStorageCommand { get; }
    public RelayCommand OpenWindowsSettingCommand { get; }
}

public sealed class HealthViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly IEngineHost _host;
    private readonly Loc _loc;
    private readonly Func<bool> _isDryRun;
    private readonly FixAllService _fixAll;
    private readonly Func<DiagnosticFinding, bool>? _filter;
    private readonly Func<string, bool>? _doneFilter;
    private readonly Func<Task> _morphPause;
    private readonly string? _crossLinkKey;
    private string _scoreText = "—";
    private double _scoreValue;
    private string _scoreBrushKey = "";
    private string _statusLine = "";
    private string _message = "";
    private string _reportSummary = "";
    private string _crossLinkText = "";
    private string _doneLead = "";
    private bool _hasCrossLink;
    private bool _createRestorePointFirst;
    private bool _busy;

    /// How long a row's Fixed morph gets to play before the follow-up rescan
    /// may rebuild the list. Chosen approach: delay the rescan trigger (not
    /// preserve rows across the rebuild) — the morph storyboard is 250 ms,
    /// and the rescan itself takes far longer than this on a real machine.
    internal const int FixedMorphMs = 400;

    public HealthViewModel(AppState state, IEngineHost host, Loc loc, Func<bool> isDryRun,
        FixAllService fixAll, Func<DiagnosticFinding, bool>? filter = null,
        Func<string, bool>? doneFilter = null, string? crossLinkKey = null,
        Func<Task>? morphPause = null)
    {
        _state = state;
        _host = host;
        _loc = loc;
        _isDryRun = isDryRun;
        _fixAll = fixAll;
        _filter = filter;
        _doneFilter = doneFilter;
        _crossLinkKey = crossLinkKey;
        _morphPause = morphPause ?? (() => Task.Delay(FixedMorphMs));
        // The report block's two faces share one visibility contract; any
        // mutation of either collection re-evaluates it (and the lead line).
        ReportLines.CollectionChanged += (_, _) => RaiseReportState();
        DoneRows.CollectionChanged += (_, _) => RaiseReportState();
        // Fix-all drives the same per-row states no matter which surface
        // launched it (this page, the overview, or the flyout): the walk
        // publishes per-rule progress and every page updates its own rows.
        fixAll.FixingRule += f => RowFor(f.RuleId)?.BeginFix();
        fixAll.FixedRule += (f, ok) => RowFor(f.RuleId)?.CompleteFix(ok);
        _state.Changed += Refresh;
        ScanCommand = new RelayCommand(() =>
        {
            ClearReport();   // a new scan starts a new story
            _ = _state.ScanAsync();
        });
        // Enabled only while fix-all would actually change something. The
        // predicate lives on FixAllService (single source of truth) and is
        // deliberately unfiltered: fix-all acts on the whole snapshot, not
        // just the rows this page shows.
        FixAllCommand = new RelayCommand(() => _ = FixAllAsync(),
            () => _state.Snapshot is { } s && _fixAll.HasWork(s));
        CrossNavigateCommand = new RelayCommand(
            () => CrossNavigateRequested?.Invoke());
    }

    public ObservableCollection<FindingRow> Rows { get; } = new();
    public ObservableCollection<FindingRow> AdviseRows { get; } = new();
    /// Findings brisk can only report — the ones whose Kind is Notice. They
    /// cost the score nothing, so they get their own band under the advice
    /// instead of sitting among suggestions the user can act on. Splitting
    /// them out is the only thing Kind changes here: a notice is never
    /// hidden, and its card renders exactly like an advise card.
    public ObservableCollection<FindingRow> NoticeRows { get; } = new();
    /// Journal-driven report rows for this page's slice of the rules (the
    /// doneFilter): every fix still in effect, newest first. Replaces the
    /// round-5 static "optimized" checklist — the report claims only what
    /// brisk actually did, straight from the journal.
    public ObservableCollection<UndoableRow> DoneRows { get; } = new();

    /// Post-run completion report, same shape the overview shows (the
    /// shared CompletionReport template binds these by name).
    public ObservableCollection<ReportLine> ReportLines { get; } = new();

    /// Raised by an advise card's "Open Storage" button; MainWindow answers
    /// by switching to the Depolama page (same pattern as the flyout's
    /// OpenDetailsRequested).
    public event Action? OpenStorageRequested;

    /// Raised by the quiet cross-page link under the findings list;
    /// MainWindow answers by switching to the sibling findings page.
    public event Action? CrossNavigateRequested;

    public AppState State => _state;
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public string ScoreText { get => _scoreText; private set => Set(ref _scoreText, value); }
    /// Numeric twin of ScoreText for the page hero's gauge sweep (round 11)
    /// — 0 until the first scan, an empty track under the "—" digits.
    public double ScoreValue { get => _scoreValue; private set => Set(ref _scoreValue, value); }
    public string ScoreBrushKey
    {
        get => _scoreBrushKey;
        private set => Set(ref _scoreBrushKey, value);
    }
    /// The page hero's status sentence, over THIS page's slice of the
    /// findings: work to do → attention; only advice left → positive with
    /// the count; nothing → plain good news (round 11).
    public string StatusLine { get => _statusLine; private set => Set(ref _statusLine, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    /// The report's ✓ lead line; empty while the last run changed nothing.
    public string ReportSummary
    {
        get => _reportSummary;
        private set => Set(ref _reportSummary, value);
    }
    /// The journal report's lead sentence; empty while DoneRows is empty.
    public string DoneLead
    {
        get => _doneLead;
        private set => Set(ref _doneLead, value);
    }
    /// The journal face shows only while no run-scoped report is on screen
    /// and this page's journal slice has something to say.
    public bool ShowDoneReport => ReportLines.Count == 0 && DoneRows.Count > 0;
    /// "{n} more findings in <sibling page> →" — shown only on pages built
    /// with a crossLinkKey, and only while the sibling actually has findings.
    public string CrossLinkText
    {
        get => _crossLinkText;
        private set => Set(ref _crossLinkText, value);
    }
    public bool HasCrossLink
    {
        get => _hasCrossLink;
        private set => Set(ref _hasCrossLink, value);
    }
    public bool CreateRestorePointFirst
    {
        get => _createRestorePointFirst;
        set => Set(ref _createRestorePointFirst, value);
    }
    public RelayCommand ScanCommand { get; }
    public RelayCommand FixAllCommand { get; }
    public RelayCommand CrossNavigateCommand { get; }

    public async Task FixAllAsync()
    {
        // A display change still waiting to be confirmed holds every fix
        // surface, not just this page's button — see
        // AppState.IsAwaitingDisplayConfirmation.
        if (_busy || _state.IsAwaitingDisplayConfirmation) return;
        IsBusy = true;                   // set before the first await — re-entry guard
        try
        {
            var snapshot = _state.Snapshot;
            if (snapshot is null) return;
            ClearReport();
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
            if (result.Attempted == 0)
            {
                Message = _loc["health.nofixables"];
            }
            else
            {
                Message = "";
                ReportSummary = FixReport.Populate(_loc, result, ReportLines);
                if (result.Applied > 0) await _morphPause();
            }
            // The display confirmation is NOT raised here. FixAllService
            // raises it as the mode changes (AppState.TrackFixes): reporting
            // it from this point would leave the screen possibly black through
            // every remaining fix and the morph pause above before the 15
            // seconds even started.
            await _state.ScanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task FixAsync(FindingRow row)
    {
        if (_busy || _state.IsAwaitingDisplayConfirmation) return;
        IsBusy = true;                   // set before the first await — re-entry guard
        try
        {
            if (_isDryRun())
            {
                Message = _loc["dryrun.blocked"];
                return;
            }
            row.BeginFix();              // instant feedback, before any await
            var outcome = await Task.Run(() => _host.Fix(row.RuleId));
            row.CompleteFix(outcome.Ok);
            // Before the morph pause, not after it: the countdown has to start
            // when the mode changes, or 400 ms of animation runs in front of a
            // screen that may already be black with nothing counting down.
            if (outcome.Ok) _state.ConfirmDisplayFix(row.RuleId);
            if (outcome.Ok)
            {
                Message = "";
                ClearReport();           // the report carries the memory once
                ReportLines.Add(new ReportLine(row.DoneTitle, IsDone: true));
                ReportSummary = _loc.F("overview.report.summary",
                    _loc.F("overview.report.part.fixes", 1));
                await _morphPause();     // let the Fixed morph play first
            }
            else
            {
                Message = outcome.Message;
            }
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
            row.BeginUndo();             // instant feedback, before any await
            var outcome = await Task.Run(() => _host.Undo(row.RuleId));
            row.CompleteUndo();
            if (outcome.Ok)
            {
                Message = "";
                ClearReport();           // a celebration of the undone fix is stale
            }
            else
            {
                Message = outcome.Message;
            }
            await _state.ScanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// Undo launched from a journal-report row's context menu — the one
    /// quiet path to undo on this page (the finding cards carry no undo
    /// affordance since round 9).
    public async Task UndoDoneAsync(UndoableRow row)
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
            var outcome = await Task.Run(() => _host.Undo(row.RuleId));
            if (outcome.Ok)
            {
                Message = "";
                ClearReport();           // a celebration of the undone fix is stale
            }
            else
            {
                Message = outcome.Message;
            }
            await _state.ScanAsync();   // Changed handler refreshes DoneRows
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

    /// The revelation band's "see the evidence" lands here: open the named
    /// card so the user reads the evidence instead of hunting for it. All
    /// three bands are searched — a notice is a finding like any other, and
    /// the band it sits in is not the caller's business.
    public void ExpandFinding(string ruleId)
    {
        foreach (var row in Rows.Concat(AdviseRows).Concat(NoticeRows))
            if (string.Equals(row.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))
            {
                row.IsExpanded = true;
                return;
            }
    }

    /// Row lookup for fix-all's per-rule progress. Runs on the fix-all worker
    /// thread; the scalar state writes it leads to are marshaled by WPF's
    /// binding engine. If a concurrent rescan rebuilds the list mid-walk,
    /// skip quietly — the rebuild renders fresh state anyway.
    private FindingRow? RowFor(string ruleId)
    {
        try
        {
            return Rows.FirstOrDefault(r =>
                string.Equals(r.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void Refresh()
    {
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        var journal = _host.ListUndoable();
        var undoable = journal.Select(u => u.RuleId).ToHashSet();
        Rows.Clear();
        AdviseRows.Clear();
        NoticeRows.Clear();
        foreach (var finding in snapshot.Findings
                     .Where(f => _filter?.Invoke(f) ?? true)
                     .OrderByDescending(f => f.Severity)
                     .ThenByDescending(f => f.ImpactStars))
            (finding.Kind == FindingKind.Notice ? NoticeRows
                : finding.Category == RuleCategory.Advise ? AdviseRows
                : Rows)
                .Add(new FindingRow(finding, _loc, undoable.Contains(finding.RuleId),
                    row => _ = FixAsync(row), row => _ = UndoAsync(row),
                    _ => OpenStorageRequested?.Invoke()));
        DoneRows.Clear();
        if (_doneFilter is not null)
            foreach (var fix in journal
                         .Where(f => _doneFilter(f.RuleId))
                         .OrderByDescending(f => f.FixedAtUtc))
                DoneRows.Add(new UndoableRow(fix, _loc, UndoDoneAsync));
        // The sibling findings page's count: this page's filter complemented.
        // Answers "where did my finding go?" after the category split.
        if (_crossLinkKey is not null && _filter is not null)
        {
            var elsewhere = snapshot.Findings.Count(f => !_filter(f));
            HasCrossLink = elsewhere > 0;
            CrossLinkText = elsewhere > 0 ? _loc.F(_crossLinkKey, elsewhere) : "";
        }
        ScoreText = snapshot.Health.ToString();
        ScoreValue = snapshot.Health;
        ScoreBrushKey = HealthBrush.KeyFor(snapshot.Health);
        // Notices cost the score nothing, but they are still worth
        // reading — so the sentence counts both bands. Counting only
        // the advice would announce good news over a page still
        // showing what brisk measured and cannot fix.
        var advisory = AdviseRows.Count + NoticeRows.Count;
        StatusLine = Rows.Count > 0 ? _loc["overview.status.attention"]
            : advisory > 0 ? _loc.F("overview.status.advise", advisory)
            : _loc["overview.status.good"];
        FixAllCommand.RaiseCanExecuteChanged();
    }
}
