using System.Diagnostics;
using System.Windows;

namespace Brisk.Tests.Ui;

/// Collects WPF's binding errors while `render` runs. A binding that names a
/// property the view model does not have fails silently on screen; here it
/// fails the test.
public static class BindingErrors
{
    private sealed class Listener : TraceListener
    {
        public readonly List<string> Messages = new();
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (message is not null) lock (Messages) Messages.Add(message);
        }
    }

    private static readonly object Gate = new();

    public static IReadOnlyList<string> Capture(Action render)
    {
        lock (Gate)
        {
            var listener = new Listener();
            PresentationTraceSources.Refresh();
            var source = PresentationTraceSources.DataBindingSource;
            source.Switch.Level = SourceLevels.Error;
            source.Listeners.Add(listener);
            try { render(); }
            finally { source.Listeners.Remove(listener); }
            lock (listener.Messages) return listener.Messages.ToList();
        }
    }
}
