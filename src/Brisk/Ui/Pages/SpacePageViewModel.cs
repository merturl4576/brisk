using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using BriskEngine.Actions;
using Brisk.Localization;
using Brisk.Session;
using Brisk.ViewModels;

namespace Brisk.Ui.Pages;

/// A page on the stage: the shell tells it when it comes on screen (Enter,
/// then Activate) and when it leaves (Deactivate).
public interface IStagePage
{
    void Enter();
    void Activate();
    void Deactivate();
}

/// Yer aç's levels in words and colours.
public static class SpaceLevels
{
    public static readonly SpaceLevel[] All = { SpaceLevel.Min, SpaceLevel.Mid, SpaceLevel.Max };

    /// A Space row with no level sits under MAX.
    public static SpaceLevel Of(ActionItem item) => item.Level ?? SpaceLevel.Max;

    public static string Key(SpaceLevel level) => level.ToString().ToLowerInvariant();

    public static string Name(SpaceLevel level, Loc loc) => loc[$"gui.level.{Key(level)}"];

    public static string Color(SpaceLevel level) => level switch
    {
        SpaceLevel.Min => "#5FD4E8",
        SpaceLevel.Mid => "#FFB020",
        _ => "#FF3B3B",
    };
}

/// The hero gauge on Yer aç: a size the numeral counts to (null shows a
/// dash). Green once something went through, Bad when nothing did.
public sealed record SpaceGauge(string Label, long? Bytes, string? Caption, double Fraction, bool Green, bool Bad);

/// One of the user's own big files: listed, locked, never counted.
public sealed record MineRow(string Name, string Path, string Size);

/// A level tile: its name, what it holds (its level and every one under
/// it, known sizes only, minus skips) and the press that picks it.
public sealed class LevelTileViewModel : ViewModelBase
{
    private readonly SpacePageViewModel _page;

    internal LevelTileViewModel(SpacePageViewModel page, SpaceLevel level)
    {
        _page = page;
        Level = level;
        Select = new RelayCommand(() => _page.SetLevel(Level), () => _page.CanSetLevel);
    }

    public SpaceLevel Level { get; }
    public string Name => SpaceLevels.Name(Level, _page.L);
    public string Tagline => _page.L[$"gui.level.{SpaceLevels.Key(Level)}.tagline"];
    public string Color => SpaceLevels.Color(Level);
    public bool IsMax => Level == SpaceLevel.Max;
    public bool IsSelected => _page.ShownLevel == Level;
    public string Number => _page.TileSize(Level).Number;
    public string Unit => _page.TileSize(Level).Unit;
    public RelayCommand Select { get; }

    internal void Refresh()
    {
        foreach (var name in new[] { nameof(Name), nameof(Tagline), nameof(IsSelected), nameof(Number), nameof(Unit) })
            Raise(name);
        Select.RaiseCanExecuteChanged();
    }
}

/// "Sileceklerim" under one level's header; locked while the level is above
/// the chosen one. A level with no rows on this machine hides its header.
public sealed class LevelGroupViewModel : ViewModelBase
{
    private readonly SpacePageViewModel _page;

    internal LevelGroupViewModel(SpacePageViewModel page, SpaceLevel level)
    {
        _page = page;
        Level = level;
    }

    public SpaceLevel Level { get; }
    public string Name => SpaceLevels.Name(Level, _page.L);
    public string Color => SpaceLevels.Color(Level);
    public bool Locked => Level > _page.Level;
    public ObservableCollection<RowViewModel> Rows { get; } = new();
    public bool HasRows => Rows.Count > 0;

    internal void Refresh()
    {
        foreach (var name in new[] { nameof(Name), nameof(Locked), nameof(HasRows) }) Raise(name);
    }
}

/// Yer aç: three level tiles (MIN, MID, MAX), the C: block, the rows under
/// their level's header (rows above the chosen level locked), the user's own
/// big files locked and never counted, and one button that frees the chosen
/// level. No Geri al: what Yer aç deletes does not come back from brisk.
///
/// The scan: entering the page plays it, one row every 300 ms in group order,
/// keyed on the row's id, and only rows the look has delivered (the Space
/// rows land whole when the engine's build does; nothing is made up from
/// partial scan data, ruling R46). With brisk's moving background off (or no
/// timers) every delivered row shows at once.
public sealed class SpacePageViewModel : ViewModelBase, IStagePage
{
    private static readonly TimeSpan LightStep = TimeSpan.FromMilliseconds(95);

    private readonly ActionSession _session;
    private readonly Loc _loc;
    private readonly Func<long?> _freeBytes;
    private readonly Action<Action> _background;
    private readonly Action<Action> _post;
    private readonly IUiTimers? _timers;
    private readonly PageRows _rows;
    private readonly PageScan _scan;
    private readonly List<IDisposable> _lights = new();
    private SessionState _lastState;
    private SpaceLevel _shownLevel;
    private SpaceLevel _ranLevel;
    private long? _freeBefore;

    public SpacePageViewModel(ActionSession session, Loc loc, Func<IReadOnlyList<string>> deferredApps,
        Func<long?> freeBytes, Action<Action> background, Action<Action> post,
        IUiTimers? timers = null, Func<bool>? moving = null)
    {
        _session = session;
        _loc = loc;
        _freeBytes = freeBytes;
        _background = background;
        _post = post;
        _timers = timers;
        Levels = SpaceLevels.All.Select(l => new LevelTileViewModel(this, l)).ToList();
        Groups = SpaceLevels.All.Select(l => new LevelGroupViewModel(this, l)).ToList();
        _rows = new PageRows(session, ActionGroupKind.Space, loc, deferredApps);
        _scan = new PageScan(session, timers, moving, Ordered, () => DataIn, RaiseState);
        RunGroup = new RelayCommand(() => _rows.Run(ButtonOk), () => _rows.CanRun(ButtonOk));
        _lastState = session.State;
        _shownLevel = session.Level;
        Sync();
        FillMine(force: false);
        // The drive as it is before any run, so a page first opened after
        // Home's Hepsini yap still has its "before" (review I1).
        if (session.State is not (SessionState.Running or SessionState.Done)) ReadFree();
        session.Changed += OnChanged;
        loc.PropertyChanged += OnLanguage;
    }

    public Loc L => _loc;
    public System.Globalization.CultureInfo Culture => _loc.Culture;
    public string Title => _loc["gui.page.space.title"];
    public string ListTitle => _loc["gui.page.space.list"];
    public string Hint => _loc["gui.page.hint"];
    public string LevelsTitle => _loc["gui.page.space.levels"];
    public string MineTitle => _loc["gui.page.space.mine"];

    public IReadOnlyList<LevelTileViewModel> Levels { get; }
    public IReadOnlyList<LevelGroupViewModel> Groups { get; }

    /// Every Space row in plan order; the groups hold the same instances.
    public ObservableCollection<RowViewModel> Rows => _rows.Rows;
    public ObservableCollection<MineRow> Mine { get; } = new();
    public bool ShowMine => _session.PersonalFilesReady && Mine.Count > 0;

    /// The one button: free the chosen level, a new look once a run is over,
    /// or nothing ("✓ Tamam", "Hiçbir şey seçili değil", a run going).
    public RelayCommand RunGroup { get; }

    public bool IsActive => _scan.Active;

    private double _ringAngle;
    /// The hero rings' turn; each entry turns them a third further.
    public double RingAngle { get => _ringAngle; private set => Set(ref _ringAngle, value); }

    public SpaceLevel Level => _session.Level;

    /// The level the tiles, the colour and "Sonra boş" show: the picked one,
    /// or during and after a run the one it ran at — Hepsini yap frees MIN
    /// whatever the page had picked (ruling R48).
    internal SpaceLevel ShownLevel => Phase is PagePhase.Running or PagePhase.Done ? _ranLevel : Level;

    public string LevelColor => SpaceLevels.Color(ShownLevel);

    public PagePhase Phase
    {
        get
        {
            var state = _session.State;
            if (state == SessionState.Running && (_session.RunGroup is null || _session.RunGroup == ActionGroupKind.Space))
                return PagePhase.Running;
            if (_scan.Scanning) return PagePhase.Scanning;
            if (state == SessionState.Done && Ran().Any()) return PagePhase.Done;
            return PagePhase.Ready;
        }
    }

    /// The rows the page's run takes.
    public int SelectedCount => Items().Count(_session.WillRun);

    public string Button
    {
        get
        {
            switch (Phase)
            {
                case PagePhase.Running:
                    return _loc["gui.page.space.busy"];
                case PagePhase.Done when Succeeded().Any():
                    return _loc["gui.page.button.ok"];
            }
            if (_session.State is SessionState.Done or SessionState.Failed) return _loc["gui.button.rescan"];
            // The rows are in but the rest of the look is not: the run waits
            // for it, and the button says so (ruling R41).
            if (_session.State == SessionState.Looking) return _loc["gui.page.space.scan"];
            if (SelectedCount == 0) return _loc["gui.page.button.none"];
            return _loc.F("gui.page.button.space", Sizes.Format(_session.LevelBytes(Level), _loc.Culture));
        }
    }

    public bool ButtonOk => Phase == PagePhase.Done && Succeeded().Any();
    public bool ButtonBusy => Phase == PagePhase.Running;
    public bool ShowButton => Phase != PagePhase.Scanning;

    public SpaceGauge Gauge
    {
        get
        {
            var total = Items().Sum(i => i.Bytes ?? 0);
            switch (Phase)
            {
                case PagePhase.Done:
                    return DoneGauge();
                case PagePhase.Scanning:
                {
                    var shown = Rows.Where(r => r.Revealed && r.Item.Bytes is not null && _session.WillRun(r.Item))
                        .Sum(r => _session.SelectedBytes(r.Item));
                    return new SpaceGauge(_loc["gui.page.space.scan"], shown, null, Ratio(shown, total), false, false);
                }
                case PagePhase.Running:
                {
                    // Counts down as the rows go.
                    var all = _session.RunGroup is null;
                    var left = Items().Where(i => (all ? _session.InRunAll(i) : _session.WillRun(i))
                            && !_session.Results.ContainsKey(i.Id))
                        .Sum(_session.SelectedBytes);
                    return new SpaceGauge(_loc["gui.page.space.busy"], left, Caption(total), Ratio(left, total),
                        false, false);
                }
                default:
                {
                    if (!RowsIn) return new SpaceGauge(_loc["gui.page.space.label"], null, null, 0, false, false);
                    var bytes = _session.LevelBytes(Level);
                    return new SpaceGauge(_loc["gui.page.space.label"], bytes, Caption(total), Ratio(bytes, total),
                        false, false);
                }
            }
        }
    }

    // ---- the C: block

    public string DiskTitle => _loc.F("gui.page.space.disk", SystemDrive());
    public string FreeNowLabel => _loc["gui.page.space.free.now"];
    public string FreeNow => _freeBefore is { } f ? Sizes.Format(f, _loc.Culture) : "—";
    public string FreeAfterLabel => _loc[Phase == PagePhase.Done ? "gui.page.space.free.done" : "gui.page.space.free.after"];

    /// Free now plus the chosen level; once the run is over, plus what it freed.
    public string FreeAfter
    {
        get
        {
            if (_freeBefore is not { } before) return "—";
            if (Phase == PagePhase.Done) return Sizes.Format(before + Freed(), _loc.Culture);
            return RowsIn ? Sizes.Format(before + _session.LevelBytes(ShownLevel), _loc.Culture) : "—";
        }
    }

    private static string SystemDrive()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            return string.IsNullOrEmpty(root) ? "C:" : root.TrimEnd('\\', '/');
        }
        catch
        {
            return "C:";
        }
    }

    /// Reads the system drive's free space off the UI thread; an unreadable
    /// drive shows "—".
    private void ReadFree()
    {
        _background(() =>
        {
            long? free;
            try { free = _freeBytes(); }
            catch { free = null; }
            _post(() =>
            {
                _freeBefore = free;
                Raise(nameof(FreeNow));
                Raise(nameof(FreeAfter));
            });
        });
    }

    // ---- levels

    /// R22: picked freely, during the look too; refused while a run or an
    /// undo goes, and once the page's run is over.
    internal bool CanSetLevel => Phase is not (PagePhase.Done or PagePhase.Running)
        && !(_session.Busy && _session.State != SessionState.Looking);

    internal void SetLevel(SpaceLevel level)
    {
        if (!CanSetLevel) return;
        _session.SetLevel(level);
    }

    /// The Space rows have landed: before that the tiles read "—".
    private bool RowsIn => _session.SpaceReady || _session.Plan is not null;

    internal (string Number, string Unit) TileSize(SpaceLevel level) =>
        RowsIn ? Sizes.Split(_session.LevelBytes(level), _loc.Culture) : ("—", "");

    /// A level up: the rows it takes in light up one by one; nothing when
    /// the background does not move.
    private void LightUp(SpaceLevel from, SpaceLevel to)
    {
        foreach (var light in _lights) light.Dispose();
        _lights.Clear();
        if (to <= from || !_scan.Moving) return;
        var rows = Ordered().Where(r => SpaceLevels.Of(r.Item) > from && SpaceLevels.Of(r.Item) <= to).ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            row.Lit = false;
            _lights.Add(_timers!.After(LightStep * i, () => row.Lit = true));
        }
    }

    public void Expand(string rowId)
    {
        if (Rows.FirstOrDefault(r => r.Id == rowId) is { } row) row.IsExpanded = true;
    }

    // ---- the scan (PageScan)

    /// The page came on screen: play the scan, unless a run is going or over.
    public void Enter()
    {
        if (_scan.Enter())
        {
            RingAngle += 120;
            ReadFree();
        }
        RaiseState();
    }

    public void Activate()
    {
        _scan.Activate();
        Raise(nameof(IsActive));
        RaiseState();
    }

    public void Deactivate()
    {
        _scan.Deactivate();
        Raise(nameof(IsActive));
    }

    /// The look has delivered the Space rows.
    private bool DataIn => _session.SpaceReady || _session.State != SessionState.Looking;

    private IEnumerable<RowViewModel> Ordered() => Groups.SelectMany(g => g.Rows);

    // ---- what the page runs and ran (PageRows)

    private IEnumerable<ActionItem> Items() => _rows.Items();

    private IEnumerable<ActionItem> Ran() => _rows.Ran();

    private IEnumerable<ActionItem> Succeeded() => _rows.Succeeded();

    private long Freed() => Succeeded().Sum(i => _session.Results[i.Id].BytesFreed);

    // ---- words

    /// Every known size of every Space row, whatever the level and the skips (ruling R5).
    private string Caption(long total) => _loc.F("gui.page.space.caption", Sizes.Format(total, _loc.Culture));

    private static double Ratio(double part, double whole) => whole <= 0 ? 0 : Math.Clamp(part / whole, 0, 1);

    /// What the run freed, with the words of the level it ran at; none
    /// through says "Olmadı" in red.
    private SpaceGauge DoneGauge()
    {
        var ran = Ran().Count();
        var through = Succeeded().Count();
        if (through == 0) return new SpaceGauge(_loc["action.result.failed"], 0, null, 0, false, true);
        return new SpaceGauge(_loc["gui.page.space.done"], Freed(),
            _loc[$"gui.page.space.done.caption.{SpaceLevels.Key(_ranLevel)}"], Ratio(through, ran), true, false);
    }

    // ---- changes

    private void OnChanged()
    {
        var state = _session.State;
        if (state != _lastState)
        {
            _scan.OnState(state);
            // Hepsini yap frees MIN, whatever the page shows.
            if (state == SessionState.Running) _ranLevel = _session.RunGroup is null ? SpaceLevel.Min : _session.Level;
            else if (state == SessionState.Ready) ReadFree();
            _lastState = state;
        }
        Sync();
        if (_session.Level != _shownLevel)
        {
            LightUp(_shownLevel, _session.Level);
            _shownLevel = _session.Level;
        }
        FillMine(force: false);
        _scan.Settle();
        RaiseState();
    }

    private void RaiseState()
    {
        foreach (var name in new[] { nameof(Phase), nameof(SelectedCount), nameof(Button), nameof(ButtonOk),
                     nameof(ButtonBusy), nameof(ShowButton), nameof(Gauge), nameof(Level), nameof(LevelColor),
                     nameof(FreeNow), nameof(FreeAfterLabel), nameof(FreeAfter), nameof(ShowMine) })
            Raise(name);
        foreach (var tile in Levels) tile.Refresh();
        foreach (var group in Groups) group.Refresh();
        RunGroup.RaiseCanExecuteChanged();
    }

    private void OnLanguage(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var name in new[] { nameof(Culture), nameof(Title), nameof(ListTitle), nameof(Hint), nameof(LevelsTitle),
                     nameof(MineTitle), nameof(DiskTitle), nameof(FreeNowLabel) })
            Raise(name);
        foreach (var r in Rows) r.Refresh();
        FillMine(force: true);
        RaiseState();
    }

    /// The user's own big files once the walk has landed; empty before.
    private void FillMine(bool force)
    {
        var files = _session.PersonalFilesReady ? _session.PersonalFiles : Array.Empty<BriskEngine.Cleaning.PersonalFile>();
        if (!force && files.Select(f => f.Path).SequenceEqual(Mine.Select(m => m.Path), StringComparer.OrdinalIgnoreCase))
            return;
        Mine.Clear();
        foreach (var f in files) Mine.Add(new MineRow(f.Name, f.Path, Sizes.Format(f.Bytes, _loc.Culture)));
    }

    private void Sync()
    {
        _rows.Sync();
        foreach (var group in Groups)
        {
            var rows = Rows.Where(r => SpaceLevels.Of(r.Item) == group.Level).ToList();
            if (rows.SequenceEqual(group.Rows)) continue;
            group.Rows.Clear();
            foreach (var row in rows) group.Rows.Add(row);
        }
        _scan.Apply();
    }
}
