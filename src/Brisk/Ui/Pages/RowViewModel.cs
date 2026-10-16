using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BriskEngine.Actions;
using Brisk.Localization;
using Brisk.Session;
using Brisk.ViewModels;

namespace Brisk.Ui.Pages;

/// One plain row: a box, a title in everyday words, a size or the result,
/// a short cost line. Clicking the row shows Windows' own name (and, for
/// Downloads, every file).
public sealed class RowViewModel : ViewModelBase
{
    private readonly ActionSession _session;
    private readonly Loc _loc;
    private readonly Func<IReadOnlyList<string>> _deferredApps;

    public RowViewModel(ActionItem item, ActionSession session, Loc loc, Func<IReadOnlyList<string>> deferredApps)
    {
        Item = item;
        _session = session;
        _loc = loc;
        _deferredApps = deferredApps;
        Files = item.Files.OrderByDescending(f => f.Bytes)
            .Select(f => new FileViewModel(f, session, loc)).ToList();
        ToggleExpand = new RelayCommand(() => IsExpanded = !IsExpanded, () => HasDetails);
        SelectAllFiles = new RelayCommand(() => SetAllFiles(true));
        SelectNoFiles = new RelayCommand(() => SetAllFiles(false));
    }

    public ActionItem Item { get; private set; }
    public string Id => Item.Id;

    public string Title => Item.TitleKey is null ? Item.Title : _loc.Title(Item.TitleKey, Item.Title);
    public string? Cost => Item.Cost is null ? null : Item.CostKey is null ? Item.Cost : _loc.Title(Item.CostKey, Item.Cost);
    public bool NeedsRestart => Item.NeedsRestart;
    public string? RestartText => Item.NeedsRestart ? _loc["gui.row.restart"] : null;

    public string? WindowsName => Item.WindowsName is null
        ? null
        : _loc.F("gui.row.windows-name", Item.WindowsNameKey is null ? Item.WindowsName : _loc.Title(Item.WindowsNameKey, Item.WindowsName));

    public bool HasDetails => WindowsName is not null || Files.Count > 0;
    public bool HasFiles => Files.Count > 0;

    public string? Size => Item.Bytes is null ? null : Sizes.Format(_session.SelectedBytes(Item), _loc.Culture);

    public bool IsTicked
    {
        get => _session.IsTicked(Item.Id);
        set
        {
            if (value == IsTicked) return;
            _session.SetTicked(Item.Id, value);
        }
    }

    private bool _expanded;
    public bool IsExpanded { get => _expanded; set => Set(ref _expanded, value); }

    public RelayCommand ToggleExpand { get; }
    public RelayCommand SelectAllFiles { get; }
    public RelayCommand SelectNoFiles { get; }
    public IReadOnlyList<FileViewModel> Files { get; }

    private RowResult? ResultRow => _session.Results.TryGetValue(Item.Id, out var r) ? r : null;

    public string? Result
    {
        get
        {
            if (ResultRow is not { } r) return null;
            // A cache whose app was open: nothing went yet, it waits for the app to close.
            if (r.Outcome == RowOutcome.Done && r.BytesFreed == 0 && r.BytesDeferred > 0)
                return _loc.F("action.result.deferred", string.Join(", ", _deferredApps()));
            var w = ActionResultWords.For(Item.Group, r);
            return _loc.Title(w.Key, w.English);
        }
    }

    public ResultTone? ResultTone => ResultRow is { } r ? ActionResultWords.For(Item.Group, r).Tone : null;
    /// Only a failure says why. A skip's reason is the engine's own English
    /// ("nothing to clean") and the word "Skipped" already says enough.
    public string? ResultReason => ResultRow is { Outcome: RowOutcome.Failed } r ? ActionResultWords.For(Item.Group, r).Reason : null;

    private void SetAllFiles(bool on)
    {
        foreach (var f in Files) f.IsTicked = on;
    }

    /// The same row in a newer plan: keep the instance, take the new facts.
    internal void Replace(ActionItem item)
    {
        Item = item;
        Refresh();
    }

    internal void Refresh()
    {
        foreach (var name in new[] { nameof(Title), nameof(Cost), nameof(RestartText), nameof(WindowsName), nameof(Size),
                     nameof(IsTicked), nameof(Result), nameof(ResultTone), nameof(ResultReason) })
            Raise(name);
        foreach (var f in Files) f.Refresh();
    }
}

public sealed class FileViewModel : ViewModelBase
{
    private readonly ActionFile _file;
    private readonly ActionSession _session;
    private readonly Loc _loc;

    public FileViewModel(ActionFile file, ActionSession session, Loc loc)
    {
        _file = file;
        _session = session;
        _loc = loc;
    }

    public string Path => _file.Path;
    public string Name => System.IO.Path.GetFileName(_file.Path);
    public string Size => Sizes.Format(_file.Bytes, _loc.Culture);
    public string Date => _file.LastWriteUtc is { } d ? d.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "";

    public bool IsTicked
    {
        get => _session.IsFileTicked(_file.Path);
        set
        {
            if (value == IsTicked) return;
            _session.SetFileTicked(_file.Path, value);
        }
    }

    internal void Refresh()
    {
        Raise(nameof(IsTicked));
        Raise(nameof(Size));
    }
}
