using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using BriskEngine;
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

    public TargetRow(TargetScanResult scan)
    {
        Scan = scan;
        SizeText = Fmt.Bytes(scan.TotalBytes);
        IsPerItem = scan.Target.RequiresIndividualSelection;
        NeedsElevation = scan.Target.RequiresElevation;
        SkippedReason = scan.SkippedReason;
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
    public string DisplayName => Scan.Target.DisplayName;
    public string SizeText { get; }
    public string? SkippedReason { get; }
    public bool NeedsElevation { get; }
    public bool IsPerItem { get; }
    public bool IsSelectable { get; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public ObservableCollection<ItemRow> Items { get; } = new();
}

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

    private IReadOnlyList<string> _lastRecycled = new List<string>();
    private bool _hasBanner;
    private string _bannerText = "";
    private string _problemsText = "";
    private string _lifetimeText = "";
    private bool _restoreFailed;

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
    }

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

    public async Task CleanLevelAsync(LevelSection section)
    {
        var selected = section.Targets.Where(t => t.IsSelected).ToList();
        var problems = new List<string>();

        var scans = new List<TargetScanResult>();
        foreach (var row in selected)
        {
            if (row.NeedsElevation && !_host.IsElevated())
            {
                if (_isDryRun())
                    problems.Add($"{row.Id} — {_loc["dryrun.blocked"]}");
                else if (!_host.RunElevated($"clean --target {row.Id} --yes"))
                    problems.Add($"{row.Id} — {_loc["clean.elevation"]}");
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

        var outcome = _cleanService.CleanTargets(scans);
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

    private void Undo()
    {
        if (_bin.Restore(_lastRecycled)) Dismiss();
        else RestoreFailed = true;
    }

    private void Reclaim()
    {
        _bin.Purge(_lastRecycled);
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
        Levels.Clear();
        Add(CleanupLevel.Safe, "clean.level.safe", snapshot);
        Add(CleanupLevel.Developer, "clean.level.developer", snapshot);
        Add(CleanupLevel.Deep, "clean.level.deep", snapshot);
        LifetimeText = _loc.F("clean.lifetime", Fmt.Bytes(_host.LifetimeReclaimedBytes()));
    }

    private void Add(CleanupLevel level, string titleKey, ScanSnapshot snapshot) =>
        Levels.Add(new LevelSection(level, titleKey,
            snapshot.Cleaner.Targets
                .Where(t => t.Target.Level == level)
                .Select(t => new TargetRow(t)),
            CleanLevelAsync));
}
