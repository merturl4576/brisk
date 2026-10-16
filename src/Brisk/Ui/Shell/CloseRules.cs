namespace Brisk.Ui.Shell;

public enum CloseAction { Hide, Exit }

/// When the window's X closes brisk and when it only hides it. brisk stays
/// in the tray only while it still has a job: caches queued until their app
/// closes, a run that is not finished, or the user's own "start with
/// Windows". Otherwise X means exit — a cleaner that lingers is the kind of
/// startup clutter it removes.
public static class CloseRules
{
    public static CloseAction OnClose(bool deferredPending, bool running, bool startWithWindows) =>
        deferredPending || running || startWithWindows ? CloseAction.Hide : CloseAction.Exit;

    /// The window is hidden: does anything still keep brisk alive?
    public static bool ShouldExitWhileHidden(bool deferredPending, bool running, bool startWithWindows) =>
        !deferredPending && !running && !startWithWindows;
}
