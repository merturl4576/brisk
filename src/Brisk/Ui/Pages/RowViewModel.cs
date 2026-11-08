using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BriskEngine.Actions;
using BriskEngine.Diagnostics;
using Brisk.Localization;
using Brisk.Session;
using Brisk.ViewModels;

namespace Brisk.Ui.Pages;

/// One plain row: a title in everyday words, a short cost line, and a value
/// on the right: what brisk will do ("durdur", a size), then what it did
/// ("✓ durdu"). A tap skips the row or takes it back; a row off by default
/// is added. Windows' own name shows on hover (and, for Downloads, every file).
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
        TapCommand = new RelayCommand(Tap);
        SelectAllFiles = new RelayCommand(() => SetAllFiles(true));
        SelectNoFiles = new RelayCommand(() => SetAllFiles(false));
    }

    public ActionItem Item { get; private set; }
    public string Id => Item.Id;

    public string Title => Item.TitleKey is null ? Item.Title : _loc.Title(Item.TitleKey, Item.Title);
    public string? Cost => Item.Cost is null ? null : Item.CostKey is null ? Item.Cost : _loc.Title(Item.CostKey, Item.Cost);
    public bool NeedsRestart => Item.NeedsRestart;
    /// "Yeniden başlatınca", muted under the cost: before the run on a row
    /// that needs a restart; after it on a row whose change waits for one,
    /// next to its done word (ruling R40).
    public string? RestartText => ResultRow is { } r
        ? r.Outcome == RowOutcome.PendingRestart ? _loc["gui.row.restart"] : null
        : Item.NeedsRestart ? _loc["gui.row.restart"] : null;

    public string? WindowsName => Item.WindowsName is null
        ? null
        : _loc.F("gui.row.windows-name", Item.WindowsNameKey is null ? Item.WindowsName : _loc.Title(Item.WindowsNameKey, Item.WindowsName));

    public bool HasDetails => WindowsName is not null || Files.Count > 0;
    public bool HasFiles => Files.Count > 0;

    public string? Size => Item.Bytes is null ? null : Sizes.Format(_session.SelectedBytes(Item), _loc.Culture);

    /// Where the page shows the row; set by the page.
    private PageSection _section;
    public PageSection Section
    {
        get => _section;
        internal set
        {
            if (!Set(ref _section, value)) return;
            Raise(nameof(Off));
            Raise(nameof(Added));
            Raise(nameof(Value));
        }
    }

    /// Shown by the page's scan; a row the reveal has not reached is hidden.
    private bool _revealed = true;
    public bool Revealed { get => _revealed; internal set => Set(ref _revealed, value); }

    public bool Skipped => _session.IsSkipped(Item.Id);

    /// A row of the list that will not run: tapped out, or above the level.
    public bool Off => _section is PageSection.Main or PageSection.Also
        && (Skipped || _session.IsLocked(Item));

    /// An "İstersen ekle" row a tap added to the run.
    public bool Added => _section == PageSection.Extra && _session.WillRun(Item);

    /// What went through (ruling R34): the row turns green.
    public bool Done => ResultRow is { } r && RowWords.Succeeded(r);

    /// The right-hand word: the result once there is one, else what brisk
    /// will do; Bırakıyorum says "kalsın", İstersen ekle "+ ekle" or "✓ eklendi".
    public string? Value
    {
        get
        {
            if (Result is { } result) return result;
            return _section switch
            {
                PageSection.Keep => _loc["gui.page.keep.value"],
                PageSection.Extra => _loc[Added ? "gui.page.extra.added" : "gui.page.extra.add"],
                _ when Item.Group == ActionGroupKind.Space => Size,
                _ => RowWords.Verb(Item, _loc),
            };
        }
    }

    /// Skip the row or take it back; Bırakıyorum rows stay as they are. The
    /// session refuses it while a run or an undo goes. Once the page's run is
    /// over a tap changes nothing: it would not run, and the row would lie.
    public void Tap()
    {
        if (_section == PageSection.Keep) return;
        if (ResultRow is not null || PageDone) return;
        _session.ToggleSkip(Item.Id);
    }

    /// The page's run is over: the session is done and ran this row's group.
    private bool PageDone => _session.State == SessionState.Done
        && _session.Items.Any(i => i.Group == Item.Group && _session.Results.ContainsKey(i.Id));

    public RelayCommand TapCommand { get; }

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
            // What went through says it in the row's own word: "✓ durdu", "✓ kapandı".
            if (r.Outcome == RowOutcome.PendingRestart
                || r.Outcome == RowOutcome.Done && r.ReadBack is null or ReadBackState.Held)
                return RowWords.DoneWord(Item, _loc);
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
                     nameof(IsTicked), nameof(Result), nameof(ResultTone), nameof(ResultReason),
                     nameof(Skipped), nameof(Off), nameof(Added), nameof(Done), nameof(Value) })
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
