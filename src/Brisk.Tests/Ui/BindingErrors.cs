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

    /// Inside the snapshot harness's lock: other test classes render in
    /// parallel, and their old views' binding errors are not this render's.
    public static IReadOnlyList<string> Capture(Action render) => Snapshots.SnapshotRenderer.Exclusively(() =>
    {
        var listener = new Listener();
        PresentationTraceSources.Refresh();
        var source = PresentationTraceSources.DataBindingSource;
        source.Switch.Level = SourceLevels.Error;
        source.Listeners.Add(listener);
        try { render(); }
        finally { source.Listeners.Remove(listener); }
        lock (listener.Messages) return (IReadOnlyList<string>)listener.Messages.Where(Ours).ToList();
    });

    /// The trace source is the whole process's, and the report card renders on
    /// its own thread outside any lock: only errors naming one of the new
    /// window's types (DataItem='HomeViewModel', …) belong to the render.
    private static readonly string[] OurTypes = typeof(Brisk.Session.ActionSession).Assembly.GetTypes()
        .Where(t => t.Namespace is { } ns && (ns.StartsWith("Brisk.Ui") || ns.StartsWith("Brisk.Session")))
        .Select(t => $"'{t.Name}'").ToArray();

    private static bool Ours(string message) => OurTypes.Any(message.Contains);
}
