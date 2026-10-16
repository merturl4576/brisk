using System;
using System.Windows.Threading;

namespace Brisk.Ui;

/// Timers for view models, behind an interface so tests move time by hand.
public interface IUiTimers
{
    IDisposable Every(TimeSpan period, Action tick);
    IDisposable After(TimeSpan delay, Action tick);
    DateTime UtcNow { get; }
}

public sealed class DispatcherUiTimers : IUiTimers
{
    public DateTime UtcNow => DateTime.UtcNow;

    public IDisposable Every(TimeSpan period, Action tick)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = period };
        timer.Tick += (_, _) => tick();
        timer.Start();
        return new Stopper(timer);
    }

    public IDisposable After(TimeSpan delay, Action tick)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            tick();
        };
        timer.Start();
        return new Stopper(timer);
    }

    private sealed class Stopper : IDisposable
    {
        private readonly DispatcherTimer _timer;
        public Stopper(DispatcherTimer timer) { _timer = timer; }
        public void Dispose() => _timer.Stop();
    }
}
