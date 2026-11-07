using System;
using BriskEngine.Actions;
using Brisk.Localization;
using Brisk.Session;
using Brisk.Ui.Home;
using Brisk.Ui.Live;
using Brisk.Ui.Pages;
using Brisk.ViewModels;

namespace Brisk.Ui.Shell;

public enum Place { Home, Space, Speed, Privacy, Settings }

/// The window: which place is shown and what runs while it is on screen.
/// Live readers and decorative motion run only for the place being looked
/// at, and only while the window is visible. Nothing asks for a confirmation
/// before deleting.
public sealed class ShellViewModel : ViewModelBase
{
    private readonly Loc _loc;
    private readonly AmbientClock _clock;
    private bool _visible;

    public ShellViewModel(Action<Action> post, Action<Action> background, ActionSession session,
        IActionBackend backend, IUiTimers timers, Loc loc, AmbientClock clock, UiSettingsViewModel settings)
    {
        _loc = loc;
        _clock = clock;
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

    /// Rain and the grid floor are home's alone; the pages are plain.
    public bool Decorated => _current == Place.Home;

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
