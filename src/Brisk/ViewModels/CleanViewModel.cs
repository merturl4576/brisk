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

    public TargetRow(TargetScanResult scan, Loc loc)
    {
        Scan = scan;
        // The engine names targets in English; the GUI looks the stable id
        // up in the resx ("clean.target.user-temp") and only falls back to
        // the engine's DisplayName for a target the resx doesn't know.
        DisplayName = loc.Title($"clean.target.{scan.Target.Id}",
            scan.Target.DisplayName);
        SizeText = Fmt.Bytes(scan.TotalBytes);
        IsPerItem = scan.Target.RequiresIndividualSelection;
        NeedsElevation = scan.Target.RequiresElevation;
        SkippedReason = scan.SkippedReason;
        // The engine's skip reason is English prose; the GUI recomposes it
        // from data it already has (the only skip cause is "app running").
        SkippedText = scan.SkippedReason is null ? ""
            : scan.Target.RequiresAppClosedProcess is { } app
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
    public string SizeText { get; }
    public string? SkippedReason { get; }
    public string SkippedText { get; }
    public bool NeedsElevation { get; }
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
        TotalText = Fmt.Bytes(Targets.Sum(t => t.Scan.TotalBytes));
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
    private long _simpleTotalBytes;
    private bool _busy;
    private bool _hasBanner;
    private string _bannerText = "";
    private string _problemsText = "";
    private string _lifetimeText = "";
    private string _simpleTotalText = "—";
    private bool _isAdvancedShown;
    private bool _restoreFailed;
    private bool _isBusy;
    private bool _isSimpleCleanBusy;
    private double _progressFraction;
    private string _progressText = "";
    private bool _hasReport;
    private string _reportSummary = "";
    private string _reportDiskText = "";
    private string _reportReasonsText = "";
    private int _plannedItems;
    private int _processedItems;
    private long _cleanedBytes;
    private long _countdownStartBytes;
    private long _lastProgressPush;
    private long _lastTotalPush;
    private long _reportFreeBefore;

    public CleanViewModel(AppState state, IEngineHost host, CleanService cleanService,
        IRecycleBinSession bin, Loc loc, System.Func<bool> isDryRun)
    {
        _state = state;
        _host = host;
        _cleanService = cleanService;
        _bin = bin;
        _loc = loc;
        _isDryRun = isDryRun;
        _state.Changed += Refresh;
        UndoCommand = new RelayCommand(Undo, () => HasBanner);
        ReclaimCommand = new RelayCommand(Reclaim, () => HasBanner);
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
    public string BannerText { get => _bannerText; private set => Set(ref _bannerText, value); }
    public string ProblemsText { get => _problemsText; private set => Set(ref _problemsText, value); }
    public string LifetimeText { get => _lifetimeText; private set => Set(ref _lifetimeText, value); }
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
            var snapshot = _state.Snapshot;
            if (snapshot is null) return;
            // Live progress starts from what the card currently promises;
            // the engine's per-entry stream ticks it down to the truth.
            _plannedItems = snapshot.Cleaner.Targets
                .Where(CleanService.IsSafeDefault).Sum(t => t.Items.Count);
            _processedItems = 0;
            _cleanedBytes = 0;
            _countdownStartBytes = _simpleTotalBytes;
            _lastProgressPush = 0;
            _lastTotalPush = 0;
            ProgressFraction = 0;
            ProgressText = _loc.F("clean.progress", 0, _plannedItems);
            HasReport = false;
            var freeBefore = _host.FreeDiskBytes();
            var outcome = await Task.Run(() =>
                _cleanService.CleanSafe(snapshot.Cleaner, OnCleanEntry));
            if (outcome.WasDryRun)
            {
                ProblemsText = _loc["dryrun.blocked"];
                return;
            }
            // The report tells the problems' story in human language below;
            // the raw path-by-path English dump stays off the simple face.
            ProblemsText = "";
            _lastRecycled = outcome.RecycledPaths;
            RestoreFailed = false;
            if (outcome.RecycledPaths.Count > 0)
            {
                BannerText = _loc.F("clean.recycled",
                    outcome.RecycledPaths.Count, Fmt.Bytes(outcome.RecycledBytes));
                HasBanner = true;
            }
            await _state.ScanAsync();
            ShowReport(outcome, freeBefore, _host.FreeDiskBytes());
        }
        finally
        {
            _busy = false;
            IsBusy = false;
            IsSimpleCleanBusy = false;
        }
    }

    /// Runs on the engine's worker thread for every recorded entry. Scalar
    /// property changes are safe cross-thread in WPF; pushes are throttled,
    /// and the last entry always lands so the bar finishes at 100%. Only
    /// "recycled" bytes tick the total down — a dry run reclaims nothing,
    /// so the big number must not move (round-10 review).
    private void OnCleanEntry(CleanEntry entry)
    {
        var processed = Interlocked.Increment(ref _processedItems);
        if (entry.Action == "recycled")
            Interlocked.Add(ref _cleanedBytes, entry.Bytes);
        var now = System.Environment.TickCount64;
        var final = processed >= _plannedItems;
        if (final || now - _lastProgressPush >= ProgressPushMs)
        {
            _lastProgressPush = now;
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

    private void ShowReport(CleanOutcome outcome, long freeBefore, long freeAfter)
    {
        _reportFreeBefore = freeBefore;
        ReportSummary = outcome.RecycledPaths.Count > 0
            ? _loc.F("clean.report.summary",
                outcome.RecycledPaths.Count, Fmt.Bytes(outcome.RecycledBytes))
            : _loc["clean.report.none"];
        ReportDiskText = DiskLine(freeBefore, freeAfter);
        var (inUse, admin, other) = ClassifySkips(outcome.Skipped);
        var reasons = new List<string>(3);
        if (inUse > 0) reasons.Add(_loc.F("clean.report.skipped.inuse", inUse));
        if (admin > 0) reasons.Add(_loc.F("clean.report.skipped.admin", admin));
        if (other > 0) reasons.Add(_loc.F("clean.report.skipped.other", other));
        ReportReasonsText = string.Join("\n", reasons);
        HasReport = true;
    }

    /// Honest disk arithmetic: recycling moves bytes to the bin on the same
    /// volume, so free space often barely moves until "reclaim" purges — the
    /// gained-phrase only appears once the measured delta is a real story.
    private string DiskLine(long before, long after) =>
        after - before >= DiskGainVisibleBytes
            ? _loc.F("clean.report.disk.gained",
                Fmt.Bytes(before), Fmt.Bytes(after), Fmt.Bytes(after - before))
            : _loc.F("clean.report.disk", Fmt.Bytes(before), Fmt.Bytes(after));

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
                     && entry.Reason?.Contains("(32)") == true)
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

            var outcome = await Task.Run(() => _cleanService.CleanTargets(scans));
            problems.AddRange(outcome.Problems);
            _lastRecycled = outcome.RecycledPaths;
            RestoreFailed = false;
            ProblemsText = string.Join("\n", problems);
            if (!outcome.WasDryRun && outcome.RecycledPaths.Count > 0)
            {
                BannerText = _loc.F("clean.recycled",
                    outcome.RecycledPaths.Count, Fmt.Bytes(outcome.RecycledBytes));
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

    private void Undo()
    {
        if (_bin.Restore(_lastRecycled)) Dismiss();
        else RestoreFailed = true;
    }

    private void Reclaim()
    {
        _bin.Purge(_lastRecycled);
        Dismiss();
        // Purging is the moment free space actually rises — the report's
        // disk line re-measures so the promise and the disk agree.
        if (HasReport)
            ReportDiskText = DiskLine(_reportFreeBefore, _host.FreeDiskBytes());
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
        _simpleTotalBytes = safeDefaults.Sum(t => t.TotalBytes);
        SimpleTotalText = Fmt.Bytes(_simpleTotalBytes);
        SimpleGroups.Clear();
        foreach (var group in safeDefaults
                     .GroupBy(t => t.Target.Category)
                     .Select(g => (g.Key, Bytes: g.Sum(t => t.TotalBytes)))
                     .Where(g => g.Bytes > 0)
                     .OrderByDescending(g => g.Bytes))
            SimpleGroups.Add(new CleanGroupRow(
                _loc[GroupKey(group.Key)], Fmt.Bytes(group.Bytes)));
        SimpleCleanCommand.RaiseCanExecuteChanged();
        Levels.Clear();
        Add(CleanupLevel.Safe, "clean.level.safe", snapshot);
        Add(CleanupLevel.Developer, "clean.level.developer", snapshot);
        Add(CleanupLevel.Deep, "clean.level.deep", snapshot);
        LifetimeText = _loc.F("clean.lifetime", Fmt.Bytes(_host.LifetimeReclaimedBytes()));
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
                .Select(t => new TargetRow(t, _loc)),
            CleanLevelAsync));
}
