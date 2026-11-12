using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using BriskEngine.Actions;
using Brisk.Localization;
using Brisk.Session;
using Brisk.ViewModels;

namespace Brisk.Ui.Pages;

/// Where a page stands: playing its scan, waiting for the press, running,
/// or showing what its run did.
public enum PagePhase { Scanning, Ready, Running, Done }

/// The page's lists: Main (Durduracaklarım, Kapatacaklarım, Sileceklerim),
/// Also (Hızlandır's settings, "Ayrıca"), Keep (Bırakıyorum: protected
/// startup apps) and Extra (İstersen ekle: rows off by default).
public enum PageSection { Main, Also, Keep, Extra }

/// The hero gauge's words and fill. Green once something went through, Bad
/// when a run left nothing through.
public sealed record PageGauge(string Label, string Number, string? Unit, string? Caption, double Fraction,
    bool Green, bool Bad);

/// Hızlandır or Gizlilik (Yer aç has SpacePageViewModel): its group's rows
/// in plan order, split in the page's lists; one button that runs the page
/// (or offers a new look once a run is over); Geri al for the last undoable
/// run. The ticks are the session's, shared with Home.
///
/// The scan: entering the page plays it, one Main/Also row every 300 ms,
/// and only rows the look has delivered. With brisk's moving background off
/// (or no timers) every delivered row shows at once. A page that was never
/// entered shows every row.
public sealed class GroupPageViewModel : ViewModelBase, IStagePage
{
    private readonly ActionSession _session;
    private readonly Loc _loc;
    private readonly string _key;
    private readonly PageRows _rows;
    private readonly PageScan _scan;
    private SessionState _lastState;

    public GroupPageViewModel(ActionSession session, ActionGroupKind group, Loc loc,
        Func<IReadOnlyList<string>> deferredApps, IUiTimers? timers = null, Func<bool>? moving = null)
    {
        if (group == ActionGroupKind.Space)
            throw new ArgumentException("Yer aç has its own page (SpacePageViewModel).", nameof(group));
        _session = session;
        Group = group;
        _loc = loc;
        _key = group.ToString().ToLowerInvariant();
        _rows = new PageRows(session, group, loc, deferredApps);
        _scan = new PageScan(session, timers, moving, Listed, () => DataIn, RaiseState, () => Keep.Concat(Extra));
        RunGroup = new RelayCommand(() => _rows.Run(ButtonOk), () => _rows.CanRun(ButtonOk));
        Undo = new RelayCommand(() => _ = _session.UndoAsync(), () => _session.CanUndoFor(Group));
        _lastState = session.State;
        Sync();
        session.Changed += OnChanged;
        loc.PropertyChanged += OnLanguage;
    }

    public ActionGroupKind Group { get; }
    public Loc L => _loc;
    public string Title => _loc[$"gui.page.{_key}.title"];
    public string ListTitle => _loc[$"gui.page.{_key}.list"];
    public string Hint => _loc["gui.page.hint"];
    public string AlsoTitle => _loc["gui.page.speed.also"];
    public string KeepTitle => _loc["gui.page.speed.keep"];
    public string ExtraTitle => _loc["gui.page.extra"];
    /// One run = one undo (ruling R56): after Hepsini yap the copy names
    /// both pages, so nothing comes back on the other page silently.
    public string UndoText => _loc[_session.UndoSpansBothPages ? "gui.button.undo.both" : "gui.button.undo"];

    public ObservableCollection<RowViewModel> Rows => _rows.Rows;
    public ObservableCollection<RowViewModel> Main { get; } = new();
    public ObservableCollection<RowViewModel> Also { get; } = new();
    public ObservableCollection<RowViewModel> Keep { get; } = new();
    public ObservableCollection<RowViewModel> Extra { get; } = new();

    /// The one button: run the page, a new look once a run is over, or
    /// nothing ("✓ Tamam", "Hiçbir şey seçili değil", a run going).
    public RelayCommand RunGroup { get; }
    public RelayCommand Undo { get; }

    public bool IsActive => _scan.Active;

    private double _ringAngle;
    /// The hero rings' turn; each entry turns them a third further.
    public double RingAngle { get => _ringAngle; private set => Set(ref _ringAngle, value); }

    public PagePhase Phase
    {
        get
        {
            var state = _session.State;
            if (state == SessionState.Running && (_session.RunGroup is null || _session.RunGroup == Group))
                return PagePhase.Running;
            if (_scan.Scanning) return PagePhase.Scanning;
            if (state == SessionState.Done && Ran().Any()) return PagePhase.Done;
            return PagePhase.Ready;
        }
    }

    /// The rows the page's run takes: the button's, the gauge's and the nav badge's count (ruling R35).
    public int SelectedCount => Items().Count(_session.WillRun);

    public string Button
    {
        get
        {
            switch (Phase)
            {
                case PagePhase.Running:
                    return BusyText();
                case PagePhase.Done when Succeeded().Any():
                    return _loc["gui.page.button.ok"];
            }
            if (_session.State is SessionState.Done or SessionState.Failed) return _loc["gui.button.rescan"];
            // This page's rows are in but the rest of the look is not: the run
            // waits for it, and the button says so (ruling R41).
            if (_session.State == SessionState.Looking) return _loc["gui.page.scan"];
            return RowWords.Button(Group, Selected().ToList(), _loc);
        }
    }

    public bool ButtonOk => Phase == PagePhase.Done && Succeeded().Any();
    public bool ButtonBusy => Phase == PagePhase.Running;
    public bool ShowButton => Phase != PagePhase.Scanning;
    public bool ShowRight => Phase != PagePhase.Scanning && (Keep.Count > 0 || Extra.Count > 0);

    /// The "Ayrıca" heading: once the scan has reached one of its rows.
    public bool ShowAlso => Also.Any(r => r.Revealed);

    /// Geri al: the last run can be undone and changed this page's settings (ruling R31).
    public bool ShowUndo => _session.CanUndoFor(Group);

    public PageGauge Gauge
    {
        get
        {
            var phase = Phase;
            if (phase == PagePhase.Done) return DoneGauge();
            var shown = phase == PagePhase.Scanning
                ? Listed().Where(r => _scan.Reached(r) && _session.WillRun(r.Item)).Select(r => r.Item).ToList()
                : Selected().ToList();
            var label = phase switch
            {
                PagePhase.Scanning => _loc[$"gui.page.{_key}.scan"],
                PagePhase.Running => BusyText(),
                _ => ReadyLabel(shown),
            };
            var caption = phase == PagePhase.Scanning ? null : ReadyCaption(shown);
            var listed = phase == PagePhase.Scanning ? Math.Max(Listed().Count(), 1) : Denominator(shown);
            return new PageGauge(label, shown.Count.ToString(_loc.Culture), Unit(shown), caption,
                Ratio(shown.Count, listed), false, false);
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
        if (_scan.Enter()) RingAngle += 120;
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

    /// The look has delivered this page's rows.
    private bool DataIn =>
        _session.Plan is not null || _session.FastGroups.Count > 0 || _session.State != SessionState.Looking;

    private IEnumerable<RowViewModel> Listed() => Main.Concat(Also);

    // ---- what the page runs and ran (PageRows)

    private IEnumerable<ActionItem> Items() => _rows.Items();

    private IEnumerable<ActionItem> Selected() => _rows.Selected();

    private IEnumerable<ActionItem> Ran() => _rows.Ran();

    private IEnumerable<ActionItem> Succeeded() => _rows.Succeeded();

    // ---- words

    private bool AppsOnly(IEnumerable<ActionItem> items) => RowWords.Count(items).Settings == 0;

    private string BusyText() => Group switch
    {
        ActionGroupKind.Speed => _loc[AppsOnly(Selected()) ? "gui.page.speed.busy.apps" : "gui.page.speed.busy.mixed"],
        _ => _loc[$"gui.page.{_key}.busy"],
    };

    private string ReadyLabel(IReadOnlyCollection<ActionItem> shown) => Group switch
    {
        ActionGroupKind.Speed => _loc[AppsOnly(shown) ? "gui.page.speed.label.apps" : "gui.page.speed.label.mixed"],
        _ => _loc[$"gui.page.{_key}.label"],
    };

    private string ReadyCaption(IReadOnlyCollection<ActionItem> shown) => Group switch
    {
        ActionGroupKind.Speed => SpeedCaption(shown),
        _ => _loc["gui.page.privacy.caption"],
    };

    /// Apps only: out of the apps that start with Windows. Otherwise what the
    /// number holds, "2 uygulama · 3 ayar", or "3 ayar" (rulings R34, R39).
    private string SpeedCaption(IReadOnlyCollection<ActionItem> shown)
    {
        var (apps, settings) = RowWords.Count(shown);
        if (settings == 0) return _loc.F("gui.page.speed.caption.apps", Items().Count(RowWords.IsStartupApp));
        var words = RowWords.Settings(settings, _loc);
        return apps == 0 ? words : RowWords.Apps(apps, _loc) + " · " + words;
    }

    /// "uygulama" when every row is a startup app, "ayar" when every one is
    /// a setting, "değişiklik" when mixed; Gizlilik counts settings.
    private string Unit(IReadOnlyCollection<ActionItem> items)
    {
        var (apps, settings) = RowWords.Count(items);
        var key = Group == ActionGroupKind.Privacy ? "gui.page.unit.settings"
            : settings == 0 ? "gui.page.unit.apps"
            : apps == 0 ? "gui.page.unit.settings"
            : "gui.page.unit.changes";
        return RowWords.Counted(key, items.Count, _loc);
    }

    /// Apps out of the startup apps; anything else out of the page's rows.
    private int Denominator(IReadOnlyCollection<ActionItem> shown) =>
        Math.Max(Group == ActionGroupKind.Speed && AppsOnly(shown)
            ? Items().Count(RowWords.IsStartupApp)
            : Items().Count(), 1);

    private static double Ratio(double part, double whole) => whole <= 0 ? 0 : Math.Clamp(part / whole, 0, 1);

    /// What the run did: the rows that went through, in their own word;
    /// none through says "Olmadı" in red.
    private PageGauge DoneGauge()
    {
        var ran = Ran().ToList();
        var through = Succeeded().ToList();
        if (through.Count == 0)
            return new PageGauge(_loc["action.result.failed"], "0", Unit(through), null, 0, false, true);
        var label = Group switch
        {
            ActionGroupKind.Speed => _loc[AppsOnly(through) ? "gui.page.speed.done.apps" : "gui.page.speed.done.mixed"],
            _ => _loc[$"gui.page.{_key}.done"],
        };
        var caption = _loc[$"gui.page.{_key}.done.caption"];
        return new PageGauge(label, through.Count.ToString(_loc.Culture), Unit(through), caption,
            Ratio(through.Count, ran.Count), true, false);
    }

    // ---- changes

    private void OnChanged()
    {
        var state = _session.State;
        if (state != _lastState)
        {
            // A new look restarts the scan on a page that was entered; a run ends it.
            _scan.OnState(state);
            _lastState = state;
        }
        Sync();
        _scan.Settle();
        RaiseState();
    }

    private void RaiseState()
    {
        foreach (var name in new[] { nameof(Phase), nameof(SelectedCount), nameof(Button), nameof(ButtonOk),
                     nameof(ButtonBusy), nameof(ShowButton), nameof(ShowRight), nameof(ShowAlso), nameof(ShowUndo),
                     nameof(UndoText), nameof(Gauge) })
            Raise(name);
        RunGroup.RaiseCanExecuteChanged();
        Undo.RaiseCanExecuteChanged();
    }

    private void OnLanguage(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var name in new[] { nameof(Title), nameof(ListTitle), nameof(Hint), nameof(AlsoTitle),
                     nameof(KeepTitle), nameof(ExtraTitle), nameof(UndoText) })
            Raise(name);
        RaiseState();
        foreach (var r in Rows) r.Refresh();
    }

    /// Hızlandır: startup apps on by default, then the settings ("Ayrıca");
    /// a startup app off by default with a cost is protected (Bırakıyorum);
    /// every other row off by default may be added. Gizlilik: on by default,
    /// or İstersen ekle.
    private PageSection SectionOf(ActionItem item)
    {
        if (item.DefaultOn)
            return Group == ActionGroupKind.Speed && !RowWords.IsStartupApp(item) ? PageSection.Also : PageSection.Main;
        return Group == ActionGroupKind.Speed && RowWords.IsStartupApp(item) && item.Cost is not null
            ? PageSection.Keep
            : PageSection.Extra;
    }

    private void Sync()
    {
        _rows.Sync();
        foreach (var row in Rows) row.Section = SectionOf(row.Item);
        Fill(Main, PageSection.Main);
        Fill(Also, PageSection.Also);
        Fill(Keep, PageSection.Keep);
        Fill(Extra, PageSection.Extra);
        _scan.Apply();
    }

    private void Fill(ObservableCollection<RowViewModel> list, PageSection section)
    {
        var rows = Rows.Where(r => r.Section == section).ToList();
        if (rows.SequenceEqual(list)) return;
        list.Clear();
        foreach (var row in rows) list.Add(row);
    }
}
