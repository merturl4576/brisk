using System;

namespace Brisk.Views;

/// Gates the cockpit hero's perpetual storyboards (orbit, comet, sheen,
/// breathing glow). Two signals decide whether they may run:
///
/// - Visibility: MainWindow feeds the exact same IsVisibleChanged/
///   StateChanged signal that starts/stops LiveMetrics, so "no brisk window
///   on screen" always means "no ambient clocks" — stopped, not merely an
///   invisible window still compositing.
/// - Reduce motion: when Windows' "show animations" accessibility setting is
///   off, the perpetual layer never starts; only data-driven motion (CPU
///   ring slew, numeral ticks) remains. The setting is re-read on every
///   activation, so a toggle takes effect on the next show/hide cycle.
///
/// Start/Stop are injected as plain delegates so the gating logic stays
/// unit-testable without a WPF visual tree.
public sealed class AmbientMotionController
{
    private readonly Func<bool> _animationsEnabled;
    private readonly Action _start;
    private readonly Action _stop;

    public AmbientMotionController(Func<bool> animationsEnabled,
        Action start, Action stop)
    {
        _animationsEnabled = animationsEnabled;
        _start = start;
        _stop = stop;
    }

    public bool IsRunning { get; private set; }

    /// Idempotent: repeated calls with an unchanged outcome do nothing, so
    /// the storyboards restart only on a real hidden→visible transition.
    public void SetActive(bool active)
    {
        var run = active && _animationsEnabled();
        if (run == IsRunning) return;
        IsRunning = run;
        if (run) _start();
        else _stop();
    }
}
