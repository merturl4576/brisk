using System;
using System.Threading.Tasks;
using Brisk.Services;

namespace Brisk.ViewModels;

/// Reports synchronously on the calling thread. Progress<T> would post to a
/// captured SynchronizationContext, which unit tests do not have.
public sealed class DelegateProgress : IProgress<string>
{
    private readonly Action<string> _handler;
    public DelegateProgress(Action<string> handler) { _handler = handler; }
    public void Report(string value) => _handler(value);
}

/// Health score → theme brush key, shared by the health page and the flyout.
public static class HealthBrush
{
    public static string KeyFor(int health) =>
        health >= 90 ? "Good"
        : health >= 70 ? "SeverityWarning"
        : "SeverityCritical";
}

/// The one shared scan state. Every page and the flyout render from here.
public sealed class AppState : ViewModelBase
{
    /// FixAllService.Run acts on the whole snapshot regardless of which
    /// page's filter is showing, and three different surfaces (Health,
    /// Performance and the tray) each run their own fix batch through it —
    /// so the one rule that can blank the screen is confirmed here, not on
    /// any one page's view model, or three of those four entry points would
    /// ship the fix with no net under it at all.
    private const string DisplayRefreshRuleId = "display-refresh";

    private readonly IEngineHost _host;
    private ScanSnapshot? _snapshot;
    private bool _isScanning;
    private bool _isCleaning;
    private string _progressText = "";
    private RefreshConfirmation? _pendingConfirmation;

    public AppState(IEngineHost host)
    {
        _host = host;
        KeepDisplayCommand = new RelayCommand(() => PendingConfirmation?.Keep());
    }

    public ScanSnapshot? Snapshot { get => _snapshot; private set => Set(ref _snapshot, value); }
    public bool IsScanning { get => _isScanning; private set => Set(ref _isScanning, value); }

    /// Round-13 re-review (N1): the sibling of IsScanning. One runner sits
    /// behind three clean buttons, so while ANY surface is cleaning the
    /// other two would be refused the lease — silently, since each view
    /// model's busy flag only knows about its own button. Every clean
    /// button binds here, so the refusal is visible instead of swallowed.
    public bool IsCleaning { get => _isCleaning; private set => Set(ref _isCleaning, value); }

    /// Wired once at composition: the runner owns the lease, this owns the
    /// signal the UI binds to. Nothing else may set it — a flag that could
    /// drift from the lease would disable buttons for a clean that ended.
    public void TrackCleaning(SafeCleanRunner runner)
        => runner.RunningChanged += running => IsCleaning = running;
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }

    /// Non-null while a display mode change fixed on any surface (a page's
    /// own Fix, or any of the Fix-all buttons — Health, Performance,
    /// Overview or the tray) is still provisional. MainWindow overlays
    /// whichever page is showing with a "Keep this setting" button while
    /// this is set; a user staring at a black screen cannot press it, which
    /// is exactly what the window elapsing means.
    public RefreshConfirmation? PendingConfirmation
    {
        get => _pendingConfirmation;
        private set => Set(ref _pendingConfirmation, value);
    }

    /// Fixed at 15 seconds in the shipped app — nothing in the composition
    /// root overrides it, which is what lets the resx body text hardcode
    /// "15 seconds" instead of formatting this value in. Settable only so
    /// tests can elapse the window without waiting.
    public TimeSpan ConfirmationWindow { get; set; } = TimeSpan.FromSeconds(15);

    public RelayCommand KeepDisplayCommand { get; }

    /// Raised when the rollback itself could not restore the previous
    /// display mode (e.g. the journal already had no prior state) — so a
    /// failed rescue never reads the same as a successful one.
    public event Action<string>? RollbackFailed;

    /// Non-null while a ConfirmDisplayFix run is in flight. Exposed only so
    /// tests have a real join point — awaiting this, rather than trusting a
    /// zero-length window to resolve synchronously, is what makes their
    /// timing structural instead of incidental.
    internal Task? PendingConfirmTask { get; private set; }

    public event Action? Changed;

    public async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;                    // set before the first await — re-entry guard
        await Task.Yield();
        try
        {
            Snapshot = await _host.ScanAsync(new DelegateProgress(m => ProgressText = m));
        }
        finally
        {
            IsScanning = false;
        }
        Changed?.Invoke();
    }

    /// The one place every fix surface reports a rule it just fixed — a
    /// single row's Fix, or each rule in a Fix-all batch. A no-op for any
    /// rule but "display-refresh", so callers can call it unconditionally
    /// per fixed rule without checking first.
    public void ConfirmDisplayFix(string ruleId)
    {
        if (!string.Equals(ruleId, DisplayRefreshRuleId, StringComparison.OrdinalIgnoreCase))
            return;
        var confirmation = new RefreshConfirmation(() =>
        {
            var outcome = _host.Undo(ruleId);
            if (!outcome.Ok) RollbackFailed?.Invoke(outcome.Message);
        })
        {
            Window = ConfirmationWindow,
        };
        // Set synchronously, on the caller's thread — so a caller that
        // awaits the method that called this (FixAsync, FixAllAsync, …)
        // observes PendingConfirmation already set the moment control
        // returns to it, with no dependence on when a background task
        // happens to get scheduled.
        PendingConfirmation = confirmation;
        // Only the wait-then-maybe-rollback goes on a background thread:
        // ChangeDisplaySettingsEx plus the journal/log writes it triggers
        // must never block the UI thread.
        PendingConfirmTask = Task.Run(async () =>
        {
            try
            {
                await confirmation.AwaitConfirmationAsync();
            }
            finally
            {
                PendingConfirmation = null;
            }
        });
    }
}
