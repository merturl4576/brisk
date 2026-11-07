using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using BriskEngine.Actions;
using Brisk.Localization;
using Brisk.Session;
using Brisk.Ui.Home;
using Brisk.Ui.Live;
using Brisk.Ui.Pages;
using Brisk.ViewModels;

namespace Brisk.Ui.Shell;

public enum Place { Home, Space, Speed, Privacy, Settings }

/// What a nav tile carries in its corner. Text: Yer aç's GB at the selected
/// level, or Hızlandır's and Gizlilik's count; null hides it (nothing to
/// show, or 0). Spinning: the look has not delivered this tile's numbers
/// yet. Done: the tile's own run went through (✓).
public sealed record NavBadge(string? Text, bool Spinning, bool Done);

/// The window: which place is shown and what runs while it is on screen.
/// Live readers and decorative motion run only for the place being looked
/// at, and only while the window is visible. Nothing asks for a confirmation
/// before deleting. The window opens on Genel; the nav badges spin until the
/// look's numbers land.
public sealed class ShellViewModel : ViewModelBase
{
    private readonly Loc _loc;
    private readonly AmbientClock _clock;
    private readonly ActionSession _session;
    private bool _visible;
    // Tiles whose run went through since the last look.
    private readonly HashSet<Place> _done = new();
    private SessionState _lastState;
    private IReadOnlyList<RowResult>? _lastUndo;

    public ShellViewModel(Action<Action> post, Action<Action> background, ActionSession session,
        IActionBackend backend, IUiTimers timers, Loc loc, AmbientClock clock, UiSettingsViewModel settings)
    {
        _loc = loc;
        _clock = clock;
        _session = session;
        Settings = settings;
        Home = new HomeViewModel(post, background, session, backend, timers, loc, Open);
        Space = new GroupPageViewModel(session, ActionGroupKind.Space, loc, backend.DeferredApps);
        Speed = new GroupPageViewModel(session, ActionGroupKind.Speed, loc, backend.DeferredApps);
        Privacy = new GroupPageViewModel(session, ActionGroupKind.Privacy, loc, backend.DeferredApps);
        Load = new LoadPanelViewModel(post, background, backend, session, timers, loc);
        Connections = new ConnectionsViewModel(post, background, backend, timers, loc);
        Usage = new UsageViewModel(post, background, backend, loc);
        Speed.Above = new object[] { Load };
        Privacy.Below = new object[] { Connections, Usage };
        _lastState = session.State;
        _lastUndo = session.UndoResults;
        _homeBadge = Badge(Place.Home);
        _spaceBadge = Badge(Place.Space);
        _speedBadge = Badge(Place.Speed);
        _privacyBadge = Badge(Place.Privacy);
        session.Changed += OnSessionChanged;
        loc.PropertyChanged += OnLanguageChanged;
    }

    public Loc L => _loc;
    public HomeViewModel Home { get; }
    public GroupPageViewModel Space { get; }
    public GroupPageViewModel Speed { get; }
    public GroupPageViewModel Privacy { get; }
    public LoadPanelViewModel Load { get; }
    public ConnectionsViewModel Connections { get; }
    public UsageViewModel Usage { get; }
    public UiSettingsViewModel Settings { get; }

    private Place _current = Place.Home;
    public Place Current
    {
        get => _current;
        set
        {
            if (value == _current) return;
            Deactivate(_current);
            _current = value;
            if (_visible) Activate(_current);
            Raise(nameof(Current));
            Raise(nameof(CurrentContent));
            Raise(nameof(Decorated));
            foreach (var p in new[] { nameof(IsHome), nameof(IsSpace), nameof(IsSpeed), nameof(IsPrivacy), nameof(IsSettings) })
                Raise(p);
        }
    }

    public object CurrentContent => _current switch
    {
        Place.Space => Space,
        Place.Speed => Speed,
        Place.Privacy => Privacy,
        Place.Settings => Settings,
        _ => Home,
    };

    /// Rain and the grid floor: Genel and Ayarlar stand on the stage; the
    /// pages are plain until they get theirs.
    public bool Decorated => _current is Place.Home or Place.Settings;

    public bool IsHome { get => _current == Place.Home; set { if (value) Current = Place.Home; } }
    public bool IsSpace { get => _current == Place.Space; set { if (value) Current = Place.Space; } }
    public bool IsSpeed { get => _current == Place.Speed; set { if (value) Current = Place.Speed; } }
    public bool IsPrivacy { get => _current == Place.Privacy; set { if (value) Current = Place.Privacy; } }
    public bool IsSettings { get => _current == Place.Settings; set { if (value) Current = Place.Settings; } }

    public void Open(ActionGroupKind group) => Current = group switch
    {
        ActionGroupKind.Space => Place.Space,
        ActionGroupKind.Speed => Place.Speed,
        _ => Place.Privacy,
    };

    // ---- nav badges

    private NavBadge _homeBadge, _spaceBadge, _speedBadge, _privacyBadge;
    public NavBadge HomeBadge { get => _homeBadge; private set => Set(ref _homeBadge, value); }
    public NavBadge SpaceBadge { get => _spaceBadge; private set => Set(ref _spaceBadge, value); }
    public NavBadge SpeedBadge { get => _speedBadge; private set => Set(ref _speedBadge, value); }
    public NavBadge PrivacyBadge { get => _privacyBadge; private set => Set(ref _privacyBadge, value); }

    /// A tile's badge now. Yer aç: the known bytes of the selected level
    /// minus skips (ActionSession.LevelBytes — the Yer aç button's number;
    /// rows with no size and personal files never count). Hızlandır and
    /// Gizlilik: their rows that are not skipped (the page run's count).
    /// Genel carries no number, only its ✓ after Hepsini yap.
    public NavBadge Badge(Place place)
    {
        var done = _done.Contains(place);
        switch (place)
        {
            case Place.Home:
                return new NavBadge(null, false, done);
            case Place.Space:
            {
                if (_session.State == SessionState.Looking && !_session.SpaceReady) return new NavBadge(null, true, false);
                var bytes = _session.LevelBytes(_session.Level);
                return new NavBadge(bytes > 0 ? Sizes.Format(bytes, _loc.Culture) : null, false, done);
            }
            case Place.Speed:
            case Place.Privacy:
            {
                if (_session.State == SessionState.Looking && _session.Plan is null && _session.FastGroups.Count == 0)
                    return new NavBadge(null, true, false);
                var group = place == Place.Speed ? ActionGroupKind.Speed : ActionGroupKind.Privacy;
                var count = _session.Items.Count(i => i.Group == group && !_session.IsSkipped(i.Id));
                return new NavBadge(count > 0 ? count.ToString(_loc.Culture) : null, false, done);
            }
            default:
                return new NavBadge(null, false, false);
        }
    }

    /// ✓ lands when a run ends (Hepsini yap: Genel and every page; a page's
    /// run: that page). It goes when Geri al reverses that run's settings,
    /// and when a new look starts: the badge spins until the look delivers
    /// and then shows the new numbers without a ✓.
    private void OnSessionChanged()
    {
        var state = _session.State;
        if (state != _lastState)
        {
            if (state == SessionState.Looking) _done.Clear();
            else if (state == SessionState.Done && _lastState == SessionState.Running) MarkDone(_session.RunGroup);
            _lastState = state;
        }
        if (!ReferenceEquals(_session.UndoResults, _lastUndo))
        {
            _lastUndo = _session.UndoResults;
            // Geri al reverses Speed and Privacy only; Yer aç keeps its ✓.
            if (_lastUndo is not null)
                foreach (var p in PlacesOf(_session.RunGroup).Where(p => p != Place.Space)) _done.Remove(p);
        }
        RefreshBadges();
    }

    private void MarkDone(ActionGroupKind? group)
    {
        foreach (var p in PlacesOf(group)) _done.Add(p);
    }

    /// The tiles a run speaks for: Hepsini yap (no group) is Genel and all three pages.
    private static IEnumerable<Place> PlacesOf(ActionGroupKind? group) => group switch
    {
        null => new[] { Place.Home, Place.Space, Place.Speed, Place.Privacy },
        ActionGroupKind.Space => new[] { Place.Space },
        ActionGroupKind.Speed => new[] { Place.Speed },
        _ => new[] { Place.Privacy },
    };

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => RefreshBadges();

    private void RefreshBadges()
    {
        HomeBadge = Badge(Place.Home);
        SpaceBadge = Badge(Place.Space);
        SpeedBadge = Badge(Place.Speed);
        PrivacyBadge = Badge(Place.Privacy);
    }

    /// Shown and not minimized.
    public void SetWindowVisible(bool visible)
    {
        if (visible == _visible) return;
        _visible = visible;
        _clock.SetVisible(visible);
        if (visible) Activate(_current); else Deactivate(_current);
    }

    private void Activate(Place place)
    {
        switch (place)
        {
            case Place.Home: Home.Activate(); break;
            case Place.Speed: Load.Activate(); break;
            case Place.Privacy:
                Connections.Activate();
                Usage.Activate();
                break;
        }
    }

    private void Deactivate(Place place)
    {
        switch (place)
        {
            case Place.Home: Home.Deactivate(); break;
            case Place.Speed: Load.Deactivate(); break;
            case Place.Privacy: Connections.Deactivate(); break;
        }
    }
}
