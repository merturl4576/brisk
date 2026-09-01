using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using BriskEngine;
using BriskEngine.Cleaning;
using BriskEngine.Models;

namespace Brisk.ViewModels;

public sealed class ItemRow : ViewModelBase
{
    private bool _isSelected;

    public ItemRow(ResolvedItem item)
    {
        Item = item;
        PathText = item.Path;
        SizeText = Fmt.Bytes(item.Bytes);
    }

    public ResolvedItem Item { get; }
    public string PathText { get; }
    public string SizeText { get; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed class TargetRow : ViewModelBase
{
    private bool _isSelected;

    public TargetRow(TargetScanResult scan, Loc loc, bool isElevated)
    {
        Scan = scan;
        // The engine names targets in English; the GUI looks the stable id
        // up in the resx ("clean.target.user-temp") and only falls back to
        // the engine's DisplayName for a target the resx doesn't know.
        DisplayName = loc.Title($"clean.target.{scan.Target.Id}",
            scan.Target.DisplayName);
        // What ticking this box actually trades away, for the targets where
        // that is a real question (Windows.old, hibernation, DISM, docker).
        // Empty for everything else, and the row collapses the line.
        NoteText = loc.Title($"clean.note.{scan.Target.Id}", "");
        SizeText = Fmt.Bytes(scan.TotalBytes);
        IsPerItem = scan.Target.RequiresIndividualSelection;
        NeedsElevation = scan.Target.RequiresElevation;
        ShowsElevationBadge = NeedsElevation && !isElevated;
        SkippedReason = scan.SkippedReason;
        // The engine's skip reason is English prose; the GUI recomposes it
        // from data it already has (the only skip cause is "app running").
        // AppDisplayName, never the raw candidate list — the user knows
        // "WhatsApp", not "WhatsApp|WhatsApp.Root".
        SkippedText = scan.SkippedReason is null ? ""
            : scan.Target.AppDisplayName is { } app
                ? loc.F("clean.skipped.apprunning", app)
                : loc["clean.skipped"];
        IsSelectable = scan.SkippedReason is null
            && (scan.Items.Count > 0 || scan.Target.PathTemplates.Count == 0);
        _isSelected = IsSelectable && !IsPerItem
            && !scan.Target.RequiresExplicitOptIn && scan.TotalBytes > 0;
        if (IsPerItem)
            foreach (var item in scan.Items)
                Items.Add(new ItemRow(item));
    }

    public TargetScanResult Scan { get; }
    public string Id => Scan.Target.Id;
    public string DisplayName { get; }
    public string NoteText { get; }
    public bool HasNote => NoteText.Length > 0;
    public string SizeText { get; }
    public string? SkippedReason { get; }
    public string SkippedText { get; }
    public bool NeedsElevation { get; }
    /// The badge answers "will this row refuse?", not "does this target need
    /// rights?" — inside the elevated app the second is always yes and the
    /// first is always no, and the workbench found every Deep row wearing it.
    public bool ShowsElevationBadge { get; }
    public bool IsPerItem { get; }
    public bool IsSelectable { get; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public ObservableCollection<ItemRow> Items { get; } = new();
}

/// One human-language row of the simple view ("Geçici dosyalar · 1.2 GB"):
/// a group of safe-default targets, named in the user's words, never in
/// tool vocabulary. Presentation only — the engine's targets, levels and
/// default selection are untouched underneath.
public sealed record CleanGroupRow(string Name, string SizeText);

public sealed class LevelSection
{
    public LevelSection(CleanupLevel level, string titleKey,
        IEnumerable<TargetRow> targets, System.Func<LevelSection, Task> clean)
    {
        Level = level;
        TitleKey = titleKey;
        Targets = new ObservableCollection<TargetRow>(targets);
        // ReclaimableBytes: the level header promises what ITS clean can
        // take right now — skipped and delete-locked content stays out
        // (the per-row sizes still show everything, with the skip note).
        TotalText = Fmt.Bytes(Targets.Sum(t => t.Scan.ReclaimableBytes));
        CleanCommand = new RelayCommand(() => _ = clean(this));
    }

    public CleanupLevel Level { get; }
    public string TitleKey { get; }
    public ObservableCollection<TargetRow> Targets { get; }
    public string TotalText { get; }
    public RelayCommand CleanCommand { get; }
}

public sealed class CleanViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly IEngineHost _host;
    private readonly CleanService _cleanService;
    private readonly SafeCleanRunner _safeClean;
    private readonly IRecycleBinSession _bin;
    private readonly Loc _loc;
    private readonly System.Func<bool> _isDryRun;

    /// Progress pushes are throttled to this cadence so a fast batch of
    /// entries never floods the dispatcher; the final entry always pushes.
    public const int ProgressPushMs = 80;
    /// The big total gets its OWN, slower cadence: every text change
    /// restarts NumeralTick's 170 ms slide, so pushing at the progress
    /// cadence would strobe the numeral mid-animation (round-10 review).
    public const int TotalPushMs = 300;
    /// Below this, a free-space delta is measurement noise, not a story.
    internal const long DiskGainVisibleBytes = 10L << 20;

    private IReadOnlyList<string> _lastRecycled = new List<string>();
    /// Payload identities that were ALREADY in the bin at the same original
    /// paths before the last level clean ran — the banner's "Alanı şimdi
    /// boşalt" must exclude them (fix round 2: with $I matching live, an
    /// unexcluded banner purge could take a USER's earlier deletion — and a
    /// Deep level clean targets Downloads, so that collateral would be user
    /// data, not cache).
    private IReadOnlyList<string> _lastPreExisting = System.Array.Empty<string>();
    private long _simpleTotalBytes;
    private bool _busy;
    private bool _hasBanner;
    private bool _undoAvailable;
    private string _bannerText = "";
    private string _problemsText = "";
    private string _lifetimeText = "";
    private string _freeDiskText = "—";
    private string _lifetimeValueText = "—";
    private string _simpleTotalText = "—";
    private bool _isAdvancedShown;
    private bool _restoreFailed;
    private bool _isBusy;
    private bool _isSimpleCleanBusy;
    private double _progressFraction;
    private bool _isProgressIndeterminate;
    private string _progressText = "";
    private bool _hasReport;
    private bool _hasLockedNotes;
    private string _reportSummary = "";
    private string _reportDiskText = "";
    private string _reportReasonsText = "";
    private int _plannedItems;
    private int _processedItems;
    private long _cleanedBytes;
    private long _countdownStartBytes;
    private long _lastProgressPush;
    private long _lastTotalPush;

    public CleanViewModel(AppState state, IEngineHost host, CleanService cleanService,
        SafeCleanRunner safeClean, IRecycleBinSession bin, Loc loc,
        System.Func<bool> isDryRun)
    {
        _state = state;
        _host = host;
        _cleanService = cleanService;
        _safeClean = safeClean;
        _bin = bin;
        _loc = loc;
        _isDryRun = isDryRun;
        _state.Changed += Refresh;
        // Undo/Reclaim/Dismiss belong to the BANNER (advanced level cleans)
        // alone since round 12 — the simple clean purges its own recycled
        // items automatically and offers no bin choreography at all.
        UndoCommand = new RelayCommand(Undo, () => HasBanner && UndoAvailable);
        ReclaimCommand = new RelayCommand(Reclaim, () => HasBanner && UndoAvailable);
        DismissCommand = new RelayCommand(Dismiss, () => HasBanner);
        OpenBinCommand = new RelayCommand(_bin.OpenRecycleBinUi);
        SimpleCleanCommand = new RelayCommand(() => _ = CleanSimpleAsync(),
            () => _simpleTotalBytes > 0 && !_busy);
    }

    /// The page's XAML disables the simple button during scans via
    /// State.IsScanning — the property the trigger binds to lives here.
    public AppState State => _state;

    /// The simple face of the page: one reclaimable total, a few
    /// human-language group rows, one Temizle. Everything below computes
    /// over CleanService.IsSafeDefault — the same predicate the button
    /// cleans through — so the promise and the action can never disagree.
    public ObservableCollection<CleanGroupRow> SimpleGroups { get; } = new();
    public string SimpleTotalText
    {
        get => _simpleTotalText;
        private set => Set(ref _simpleTotalText, value);
    }
    /// The other half of the honest promise (round 11): bytes the clean
    /// CANNOT take right now, each with its actionable why — "+310 MB when
    /// you close WhatsApp", "+120 MB in files apps are using". The headline
    /// above never contains these.
    public ObservableCollection<string> LockedNotes { get; } = new();
    public bool HasLockedNotes
    {
        get => _hasLockedNotes;
        private set => Set(ref _hasLockedNotes, value);
    }
    /// The full three-level target list stays available behind "Gelişmiş";
    /// collapsed by default — a non-technical user never has to see it.
    public bool IsAdvancedShown
    {
        get => _isAdvancedShown;
        set => Set(ref _isAdvancedShown, value);
    }
    public RelayCommand SimpleCleanCommand { get; }

    /// True for the WHOLE visible act — engine clean plus the closing
    /// rescan — so the button never looks idle while work is running.
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value)) SimpleCleanCommand.RaiseCanExecuteChanged();
        }
    }
    /// The simple card's hairline, counter and "Temizleniyor…" swap bind
    /// HERE, not to IsBusy: a Gelişmiş level clean also raises IsBusy, and
    /// the always-visible card must never light up with the previous simple
    /// clean's stale progress (round-10 review).
    public bool IsSimpleCleanBusy
    {
        get => _isSimpleCleanBusy;
        private set => Set(ref _isSimpleCleanBusy, value);
    }
    /// Real progress, never theater: processed / planned items, advanced
    /// only by the engine's own per-entry stream.
    public double ProgressFraction
    {
        get => _progressFraction;
        private set => Set(ref _progressFraction, value);
    }
    /// True while the run is in a phase the item counter cannot measure —
    /// the bin snapshot before the engine, the purge after it. The bar
    /// sweeps instead of sitting at a determinate figure it isn't earning.
    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => Set(ref _isProgressIndeterminate, value);
    }
    public string ProgressText
    {
        get => _progressText;
        private set => Set(ref _progressText, value);
    }

    /// The completion report of the last simple clean: what went, what the
    /// disk says, and — calmly — why anything was left alone. Stays up
    /// until the next press starts a new story.
    public bool HasReport { get => _hasReport; private set => Set(ref _hasReport, value); }
    public string ReportSummary
    {
        get => _reportSummary;
        private set => Set(ref _reportSummary, value);
    }
    public string ReportDiskText
    {
        get => _reportDiskText;
        private set => Set(ref _reportDiskText, value);
    }
    public string ReportReasonsText
    {
        get => _reportReasonsText;
        private set
        {
            if (Set(ref _reportReasonsText, value)) Raise(nameof(HasReportReasons));
        }
    }
    public bool HasReportReasons => _reportReasonsText.Length > 0;

    public ObservableCollection<LevelSection> Levels { get; } = new();
    public bool HasBanner { get => _hasBanner; private set { Set(ref _hasBanner, value); RaiseBannerCommands(); } }
    /// False when the banner reports past-the-bin work only ("removed"):
    /// nothing sits in the bin, so Geri al / Alanı boşalt would both lie.
    public bool UndoAvailable { get => _undoAvailable; private set { Set(ref _undoAvailable, value); RaiseBannerCommands(); } }
    public string BannerText { get => _bannerText; private set => Set(ref _bannerText, value); }
    public string ProblemsText { get => _problemsText; private set => Set(ref _problemsText, value); }
    public string LifetimeText { get => _lifetimeText; private set => Set(ref _lifetimeText, value); }
    /// Page-hero pods (round 11): the C: drive's free space and the
    /// lifetime reclaimed figure, as bare values.
    public string FreeDiskText { get => _freeDiskText; private set => Set(ref _freeDiskText, value); }
    public string LifetimeValueText
    {
        get => _lifetimeValueText;
        private set => Set(ref _lifetimeValueText, value);
    }
    public bool RestoreFailed { get => _restoreFailed; private set => Set(ref _restoreFailed, value); }
    public RelayCommand UndoCommand { get; }
    public RelayCommand ReclaimCommand { get; }
    public RelayCommand DismissCommand { get; }
    public RelayCommand OpenBinCommand { get; }

    /// The simple view's one button: exactly what today's safe-level
    /// defaults would clean (CleanService.CleanSafe — the same call the
    /// overview and flyout make). Admin-gated and opt-in targets are
    /// structurally outside this path.
    public async Task CleanSimpleAsync()
    {
        if (_busy) return;
        _busy = true;                    // set before the first await — re-entry guard
        IsBusy = true;
        IsSimpleCleanBusy = true;
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
            // A stale level-clean banner must die HERE (review round 1):
            // its "Geri al" points at bin entries this clean's auto-purge
            // could destroy (same regenerating cache paths, recycled again)
            // — an undo offer for permanently deleted data is a lie.
            Dismiss();
            _lastRecycled = System.Array.Empty<string>();
            _lastPreExisting = System.Array.Empty<string>();
            // Live progress starts from what the card currently promises;
            // the engine's per-entry stream ticks it down to the truth.
            var safeDefaults = snapshot.Cleaner.Targets
                .Where(CleanService.IsSafeDefault).ToList();
            _plannedItems = safeDefaults.Sum(t => t.Items.Count);
            _processedItems = 0;
            _cleanedBytes = 0;
            _countdownStartBytes = _simpleTotalBytes;
            _lastProgressPush = 0;
            _lastTotalPush = 0;
            ProgressFraction = 0;
            HasReport = false;
            // Captured BEFORE the clean: the app-held survivors' story
            // ("WhatsApp is open, its 310 MB cache was skipped") belongs to
            // this run's report even after the closing rescan moves on.
            var appHeld = AppHeldReasons(snapshot.Cleaner);
            var freeBefore = _host.FreeDiskBytes();
            // ONE-STEP (round 12, owner directive; shared by all three clean
            // surfaces since round 13): snapshot the bin's matching payload
            // identities, recycle, then immediately purge exactly what THIS
            // run recycled minus that snapshot — the user's own earlier
            // deletions are structurally out of reach. No banner, no
            // "reclaim" button, no undo: the space is simply free.
            // SafeCleanRunner owns the sequence; this page owns the story.
            // The purge wears its own visible state, and the rescan AFTER it
            // re-measures free space so the hero's disk pod and the report's
            // delta show the real gain.
            var result = await _safeClean.RunAsync(lease, snapshot.Cleaner, OnCleanEntry,
                () => Phase(_loc["clean.purging"]),
                () => Phase(_loc["clean.preparing"]));
            if (result.Outcome.WasDryRun)
            {
                ProblemsText = _loc["dryrun.blocked"];
                return;
            }
            // The report tells the problems' story in human language below;
            // the raw path-by-path English dump stays off the simple face.
            ProblemsText = "";
            await _state.ScanAsync();
            ShowReport(result, freeBefore, _host.FreeDiskBytes(), appHeld);
        }
        finally
        {
            // The purge phase leaves the sweep on; the run ending takes it
            // off, so the next press never opens on a stale animation. The
            // re-entry guard is released LAST (round-14 re-review): nothing
            // here can interleave today, but a guard that outlives the state
            // it protects is the habit worth keeping.
            IsProgressIndeterminate = false;
            IsSimpleCleanBusy = false;
            IsBusy = false;
            _busy = false;
        }
    }

    /// A named phase the item counter cannot measure — the bin snapshot
    /// before the engine, the purge after it. Round-14 review: leaving the
    /// bar sitting at a determinate 0 through the preparing phase was the
    /// last frozen element on the card, and the frozen bar is what the
    /// 2026-08-18 report was a photograph of.
    private void Phase(string text)
    {
        ProgressText = text;
        IsProgressIndeterminate = true;
    }

    /// Runs on the engine's worker thread for every recorded entry. Scalar
    /// property changes are safe cross-thread in WPF; pushes are throttled,
    /// and the last entry always lands so the bar finishes at 100%. Only
    /// "recycled" bytes tick the total down — a dry run reclaims nothing,
    /// so the big number must not move (round-10 review).
    private void OnCleanEntry(CleanEntry entry)
    {
        var processed = Interlocked.Increment(ref _processedItems);
        if (entry.Action is "recycled" or "removed")
            Interlocked.Add(ref _cleanedBytes, entry.Bytes);
        var now = System.Environment.TickCount64;
        var final = processed >= _plannedItems;
        if (final || now - _lastProgressPush >= ProgressPushMs)
        {
            _lastProgressPush = now;
            IsProgressIndeterminate = false;   // there is something to count now
            ProgressFraction = _plannedItems == 0
                ? 1 : (double)processed / _plannedItems;
            ProgressText = _loc.F("clean.progress", processed, _plannedItems);
        }
        // The numeral rides its own slower cadence so each NumeralTick
        // slide (170 ms) finishes before the next value lands.
        if (final || now - _lastTotalPush >= TotalPushMs)
        {
            _lastTotalPush = now;
            SimpleTotalText = Fmt.Bytes(System.Math.Max(0,
                _countdownStartBytes - Interlocked.Read(ref _cleanedBytes)));
        }
    }

    /// One line per running app holding safe-level cache back — the same
    /// aggregation the card's LockedNotes show, worn as a report reason
    /// ("WhatsApp is open, so its cache (310 MB) was skipped — close it…").
    private List<string> AppHeldReasons(ScanResult scan) => scan.Targets
        .Where(CleanService.IsAppHeld)
        .GroupBy(t => t.Target.AppDisplayName!)
        .OrderByDescending(g => g.Sum(t => t.TotalBytes))
        .Select(g => _loc.F("clean.report.skipped.appheld",
            g.Key, Fmt.Bytes(g.Sum(t => t.TotalBytes))))
        .ToList();

    /// Round 12: freed = the bytes of exactly the entries whose paths the
    /// auto-purge actually took out of the bin; anything recycled but not
    /// purged is reported as still in the bin — honestly, with no manual
    /// purge button and (round 13) no false promise that a later clean will
    /// come back for it.
    private void ShowReport(SafeCleanResult result,
        long freeBefore, long freeAfter, IReadOnlyList<string> appHeld)
    {
        var outcome = result.Outcome;
        ReportSummary = outcome.RecycledPaths.Count > 0
            ? _loc.F("clean.report.summary.freed",
                outcome.RecycledPaths.Count, Fmt.Bytes(result.FreedBytes))
            : _loc["clean.report.none"];
        ReportDiskText = DiskLine(freeBefore, freeAfter);
        var (inUse, admin, other) = ClassifySkips(outcome.Skipped);
        var reasons = new List<string>(4 + appHeld.Count);
        // This run's own leftover leads (bytes still in the bin), then the
        // app-held lines — they explain the LARGEST absent bytes and carry
        // the action (close the app, clean again).
        if (result.LeftInBinBytes > 0)
            reasons.Add(_loc.F("clean.report.binleft",
                Fmt.Bytes(result.LeftInBinBytes)));
        reasons.AddRange(appHeld);
        if (inUse > 0) reasons.Add(_loc.F("clean.report.skipped.inuse", inUse));
        if (admin > 0) reasons.Add(_loc.F("clean.report.skipped.admin", admin));
        if (other > 0) reasons.Add(_loc.F("clean.report.skipped.other", other));
        ReportReasonsText = string.Join("\n", reasons);
        HasReport = true;
    }

    /// Honest disk arithmetic (round 12): the clean purges its own recycled
    /// items immediately, so the only disk line left is the REAL measured
    /// before→after — shown when the delta is a story, silent when it is
    /// measurement noise (the summary already carries the freed figure).
    private string DiskLine(long before, long after) =>
        after - before >= DiskGainVisibleBytes
            ? _loc.F("clean.report.disk.gained",
                Fmt.Bytes(before), Fmt.Bytes(after), Fmt.Bytes(after - before))
            : "";

    /// ROUND 16: a level clean used to hand the engine's raw problem list
    /// straight to the page — one English line per failed path, above the
    /// hero and OUTSIDE the scroll viewer, so a run with two dozen held
    /// files pushed the whole card off the screen. Round 12 gave the simple
    /// flow human sentences for exactly this; the Gelişmiş levels tell the
    /// same story the same way now. Elevation and dry-run notes are built
    /// localized above and keep their own lines.
    private IEnumerable<string> SkipSentences(IReadOnlyList<CleanEntry> skipped)
    {
        var (inUse, admin, other) = ClassifySkips(skipped);
        if (inUse > 0) yield return _loc.F("clean.report.skipped.inuse", inUse);
        if (admin > 0) yield return _loc.F("clean.report.skipped.admin", admin);
        if (other > 0) yield return _loc.F("clean.report.skipped.other", other);
    }

    /// GUI-edge reason mapping (the round-9 rule): the engine's English
    /// prose is recomposed from the patterns the GUI knows — Win32 error 32
    /// is a sharing violation (a running app holds the file), the runner's
    /// only refusal phrasing for elevation is fixed, the rest stays generic.
    internal static (int InUse, int Admin, int Other) ClassifySkips(
        IEnumerable<CleanEntry> skipped)
    {
        int inUse = 0, admin = 0, other = 0;
        foreach (var entry in skipped)
        {
            if (entry.Action == "refused" && entry.Reason == "requires administrator")
                admin++;
            else if (entry.Action == "error"
                     && (entry.Reason?.Contains("(32)") == true
                         || entry.Reason == BriskEngine.Cleaning.CleanRunner.HeldReason))
                inUse++;
            else
                other++;
        }
        return (inUse, admin, other);
    }

    public async Task CleanLevelAsync(LevelSection section)
    {
        if (_busy) return;
        _busy = true;                    // set before the first await — re-entry guard
        IsBusy = true;
        try
        {
            // Round-13 re-review (minor 15): EVERY bin mutation takes the
            // lease, not just the runner's own sequence. This one recycles,
            // and items it recycles mid-flight sit in no other run's
            // exclusion snapshot — a concurrent simple purge would destroy
            // them while the banner this raises still offers "Geri al".
            using var lease = _safeClean.TryBegin();
            if (lease is null) return;
            var selected = section.Targets.Where(t => t.IsSelected).ToList();
            var problems = new List<string>();

            var scans = new List<TargetScanResult>();
            foreach (var row in selected)
            {
                if (row.NeedsElevation && !_host.IsElevated())
                {
                    if (_isDryRun())
                        problems.Add($"{row.DisplayName} — {_loc["dryrun.blocked"]}");
                    else if (!await Task.Run(() => _host.RunElevated($"clean --target {row.Id} --yes")))
                        problems.Add($"{row.DisplayName} — {_loc["clean.elevation"]}");
                    continue;
                }
                scans.Add(row.IsPerItem
                    ? row.Scan with
                    {
                        Items = row.Items.Where(i => i.IsSelected)
                            .Select(i => i.Item).ToList(),
                    }
                    : row.Scan);
            }

            // Snapshot BEFORE recycling (fix round 2, symmetric with the
            // simple flow): bin entries already matching the paths this
            // level clean is about to recycle belong to the USER — the
            // banner's "Alanı şimdi boşalt" must never take them.
            var plannedPaths = scans
                .SelectMany(s => s.Items).Select(i => i.Path).ToList();
            _lastPreExisting = plannedPaths.Count == 0
                ? System.Array.Empty<string>()
                : await Task.Run(() => _bin.MatchingItemIds(plannedPaths));
            var outcome = await Task.Run(() => _cleanService.CleanTargets(scans));
            problems.AddRange(SkipSentences(outcome.Skipped));
            // A dry run records no skips and raises no banner, so once the
            // notice learned to collapse, this flow could answer a press
            // with NOTHING at all (round-16 review, minor 4) — unless the
            // elevation branch above already named a specific target.
            if (outcome.WasDryRun && problems.Count == 0)
                problems.Add(_loc["dryrun.blocked"]);
            _lastRecycled = outcome.RecycledPaths;
            RestoreFailed = false;
            ProblemsText = string.Join("\n", problems);
            if (!outcome.WasDryRun
                && (outcome.RecycledPaths.Count > 0 || outcome.Removed.Count > 0))
            {
                var lines = new List<string>(2);
                if (outcome.RecycledPaths.Count > 0)
                    lines.Add(_loc.F("clean.recycled",
                        outcome.RecycledPaths.Count, Fmt.Bytes(outcome.RecycledBytes)));
                if (outcome.Removed.Count > 0)
                    lines.Add(_loc.F("clean.removed",
                        outcome.Removed.Count, Fmt.Bytes(outcome.RemovedBytes)));
                BannerText = string.Join("\n", lines);
                UndoAvailable = outcome.RecycledPaths.Count > 0;
                HasBanner = true;
            }
            await _state.ScanAsync();
        }
        finally
        {
            _busy = false;
            IsBusy = false;
        }
    }

    /// Banner-only since round 12 (the simple clean auto-purges and offers
    /// neither): restore what a LEVEL clean recycled, then rescan so the
    /// shelf and the promise return to the truth.
    private void Undo()
    {
        // Round-13 re-review (minor 15): the lease guards restores too — a
        // restore racing another surface's purge would fight over the same
        // entries. The button disables on State.IsCleaning, so this is the
        // backstop, not the user-facing story.
        using var lease = _safeClean.TryBegin();
        if (lease is null) return;
        if (!_bin.Restore(_lastRecycled)) { RestoreFailed = true; return; }
        Dismiss();
        _ = _state.ScanAsync();
    }

    /// Banner-only since round 12: purge what a LEVEL clean recycled —
    /// minus the payload identities that were already in the bin before it
    /// ran (fix round 2: the user's own earlier deletions stay untouched).
    private void Reclaim()
    {
        // Round-13 re-review: this purge overlaps a simple clean's paths
        // (user-temp is both a safe default and individually selectable),
        // so without the lease a concurrent run would under-report its own
        // freed bytes and file a spurious "left in the bin" line.
        using var lease = _safeClean.TryBegin();
        if (lease is null) return;
        _bin.Purge(_lastRecycled, _lastPreExisting);
        Dismiss();
    }

    private void Dismiss()
    {
        HasBanner = false;
        RestoreFailed = false;
    }

    private void RaiseBannerCommands()
    {
        UndoCommand.RaiseCanExecuteChanged();
        ReclaimCommand.RaiseCanExecuteChanged();
        DismissCommand.RaiseCanExecuteChanged();
    }

    private void Refresh()
    {
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        var safeDefaults = snapshot.Cleaner.Targets
            .Where(CleanService.IsSafeDefault).ToList();
        // ReclaimableBytes everywhere (round 11): the headline, the groups
        // and the countdown promise only what Temizle can take RIGHT NOW.
        _simpleTotalBytes = CleanService.ReclaimableNowBytes(snapshot.Cleaner);
        SimpleTotalText = Fmt.Bytes(_simpleTotalBytes);
        SimpleGroups.Clear();
        foreach (var group in safeDefaults
                     .GroupBy(t => t.Target.Category)
                     .Select(g => (g.Key, Bytes: g.Sum(t => t.ReclaimableBytes)))
                     .Where(g => g.Bytes > 0)
                     .OrderByDescending(g => g.Bytes))
            SimpleGroups.Add(new CleanGroupRow(
                _loc[GroupKey(group.Key)], Fmt.Bytes(group.Bytes)));
        // What the headline does NOT contain, made actionable: one line per
        // running app holding cache back, one line for delete-locked files.
        LockedNotes.Clear();
        foreach (var held in snapshot.Cleaner.Targets
                     .Where(CleanService.IsAppHeld)
                     .GroupBy(t => t.Target.AppDisplayName!)
                     .Select(g => (App: g.Key, Bytes: g.Sum(t => t.TotalBytes)))
                     .OrderByDescending(g => g.Bytes))
            LockedNotes.Add(_loc.F("clean.simple.locked.app",
                held.App, Fmt.Bytes(held.Bytes)));
        var lockedInPlace = safeDefaults.Sum(t => t.BlockedBytes);
        if (lockedInPlace > 0)
            LockedNotes.Add(_loc.F("clean.simple.locked.inuse",
                Fmt.Bytes(lockedInPlace)));
        HasLockedNotes = LockedNotes.Count > 0;
        SimpleCleanCommand.RaiseCanExecuteChanged();
        Levels.Clear();
        Add(CleanupLevel.Safe, "clean.level.safe", snapshot);
        Add(CleanupLevel.Developer, "clean.level.developer", snapshot);
        Add(CleanupLevel.Deep, "clean.level.deep", snapshot);
        var lifetime = _host.LifetimeReclaimedBytes();
        LifetimeText = _loc.F("clean.lifetime", Fmt.Bytes(lifetime));
        LifetimeValueText = Fmt.Bytes(lifetime);
        FreeDiskText = Fmt.Bytes(_host.FreeDiskBytes());
    }

    /// Engine categories → human words. Anything the mapping doesn't know
    /// lands under "Other" rather than leaking a technical category name.
    private static string GroupKey(string category) => category switch
    {
        "System" => "clean.group.system",
        "Browser" => "clean.group.browser",
        "App" => "clean.group.app",
        _ => "clean.group.other",
    };

    private void Add(CleanupLevel level, string titleKey, ScanSnapshot snapshot) =>
        Levels.Add(new LevelSection(level, titleKey,
            snapshot.Cleaner.Targets
                .Where(t => t.Target.Level == level)
                .Select(t => new TargetRow(t, _loc, _host.IsElevated())),
            CleanLevelAsync));
}
