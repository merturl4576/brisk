using System;
using System.Collections.Generic;
using System.Linq;

namespace Brisk.Ui;

/// Starts and stops every piece of decorative motion (rain, grid floor,
/// spinning arcs, pulses) together. It runs only while the window is on
/// screen and `allowed` says so — the "Moving background" setting and a
/// graphics card that composes on the GPU. Windows' own animation flag is not
/// asked: brisk's "Window opening animations" row switches it off.
/// Single-threaded: the UI thread registers, shows and hides.
public sealed class AmbientClock
{
    private sealed class Entry
    {
        public Action Start = () => { };
        public Action Stop = () => { };
        public bool Running;
    }

    public static AmbientClock Instance { get; private set; } = new(() => true);

    public static void Install(AmbientClock clock) => Instance = clock;

    private readonly Func<bool> _allowed;
    private readonly Dictionary<object, Entry> _entries = new();
    private bool _visible;

    public AmbientClock(Func<bool> allowed) { _allowed = allowed; }

    public bool IsActive { get; private set; }

    public void Register(object owner, Action start, Action stop)
    {
        Unregister(owner);
        var entry = new Entry { Start = start, Stop = stop };
        _entries[owner] = entry;
        if (IsActive) Run(entry, true);
    }

    public void Unregister(object owner)
    {
        if (_entries.Remove(owner, out var entry) && entry.Running) entry.Stop();
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        Reevaluate();
    }

    /// Asks `allowed` again (after the setting changed).
    public void Reevaluate()
    {
        var active = _visible && _allowed();
        if (active == IsActive) return;
        IsActive = active;
        foreach (var entry in _entries.Values.ToList()) Run(entry, active);
    }

    private static void Run(Entry entry, bool on)
    {
        if (on == entry.Running) return;
        entry.Running = on;
        if (on) entry.Start(); else entry.Stop();
    }
}
