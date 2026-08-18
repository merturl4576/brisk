using System;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using BriskEngine.Diagnostics.Rules;

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
    private readonly IEngineHost _host;
    private readonly Loc _loc;
    private readonly object _confirmGate = new();
    private ScanSnapshot? _snapshot;
    private bool _isScanning;
    private bool _isCleaning;
    private string _progressText = "";
    private RefreshConfirmation? _pendingConfirmation;
    private Task _scan = Task.CompletedTask;

    public AppState(IEngineHost host, Loc? loc = null)
    {
        _host = host;
        _loc = loc ?? Loc.Instance;
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

    /// The moment of the mode change, not the end of the batch. FixAllService
    /// raises FixedRule as each rule lands, so a display raised FIRST in a
    /// batch starts its 15 seconds right then — instead of after every
    /// remaining fix and the report's morph pause, all of which the user may
    /// be sitting through in front of a black screen with no timer running at
    /// all. Wired once at composition, exactly like TrackCleaning.
    ///
    /// The event fires on the worker thread the batch runs on. Setting
    /// PendingConfirmation from there is safe — WPF marshals a property
    /// change to the dispatcher — and the one thing here that touches a
    /// window, ConfirmationRaised, is marshalled by App.xaml.cs.
    public void TrackFixes(FixAllService fixAll)
        => fixAll.FixedRule += (finding, ok) => { if (ok) ConfirmDisplayFix(finding.RuleId); };

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
        private set
        {
            if (Set(ref _pendingConfirmation, value))
                Raise(nameof(IsAwaitingDisplayConfirmation));
        }
    }

    /// The bindable sibling of IsCleaning, and there for the same reason:
    /// every fix surface has to refuse to start while brisk is still asking
    /// whether the picture came back. A second batch would find the displays
    /// already raised, journal an empty prior state on top of the real one,
    /// and the rollback meant to bring the picture back would restore nothing.
    public bool IsAwaitingDisplayConfirmation => _pendingConfirmation is not null;

    /// Fixed at 15 seconds in the shipped app: the setter is internal, so
    /// only the test project (InternalsVisibleTo) can change it — production
    /// code cannot make the resx body text's hardcoded "15 seconds" a lie.
    public TimeSpan ConfirmationWindow { get; internal set; } = TimeSpan.FromSeconds(15);

    public RelayCommand KeepDisplayCommand { get; }

    /// Everything the display rescue owes the user, on one channel because it
    /// all lands in the same place — the findings page's message line:
    ///   * the ordinary outcome, where nobody answered and the previous mode
    ///     went back on. The spec requires brisk to say so and to name cable
    ///     or adapter as the likely reason the rate did not carry;
    ///   * a rollback that could not restore the previous mode;
    ///   * a confirmed mode that could not be written to the registry, and so
    ///     will not survive a restart.
    /// Raised on the rescue's own background thread.
    public event Action<string>? DisplayNotice;

    /// Raised right after PendingConfirmation is set. The flyout — not
    /// MainWindow — is the app's default startup surface (App.xaml.cs shows
    /// it, not the main window, unless "--tray"), and the overlay lives only
    /// in MainWindow. Without this, a confirmation raised while only the
    /// flyout is open would set PendingConfirmation on a window nobody ever
    /// showed: no HWND, no Keep button, a silent revert 15 seconds later —
    /// the original bug's exact shape, on the app's most common path.
    /// App.xaml.cs subscribes and calls its existing ShowMain().
    public event Action? ConfirmationRaised;

    /// Non-null while a ConfirmDisplayFix run is in flight. Exposed only so
    /// tests have a real join point — awaiting this, rather than trusting a
    /// zero-length window to resolve synchronously, is what makes their
    /// timing structural instead of incidental.
    internal Task? PendingConfirmTask { get; private set; }

    public event Action? Changed;

    public Task ScanAsync()
    {
        // A caller arriving mid-scan is handed the run already in flight, not
        // a completed task: the answer it is waiting for is that run's.
        if (IsScanning) return _scan;
        IsScanning = true;                    // set before the first await — re-entry guard
        return _scan = ScanCoreAsync();
    }

    private async Task ScanCoreAsync()
    {
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
    /// single row's Fix, or each rule in a Fix-all batch (through
    /// TrackFixes). A no-op for any rule but "display-refresh", so callers
    /// can call it unconditionally per fixed rule without checking first.
    public void ConfirmDisplayFix(string ruleId)
    {
        if (!string.Equals(ruleId, DisplayRefreshRule.RuleId, StringComparison.OrdinalIgnoreCase))
            return;

        string? notice = null;
        var confirmation = new RefreshConfirmation(() =>
        {
            // The rescue itself must never throw. It runs inside a background
            // task nobody but the resolve below observes, so an escaping
            // exception would leave RolledBack false and the user told
            // nothing at all about a display that just changed under them.
            try
            {
                var outcome = _host.Undo(DisplayRefreshRule.RuleId);
                notice = outcome.Ok ? _loc["display-confirm.rolledback"] : outcome.Message;
            }
            catch (Exception ex)
            {
                notice = _loc.F("display-confirm.rollbackfailed", ex.Message);
            }
        })
        {
            Window = ConfirmationWindow,
        };

        lock (_confirmGate)
        {
            // One screen, one rescue. Each view model's busy flag guards only
            // its own button, so a flyout Fix-all and a page Fix-all can
            // overlap; a second confirmation would replace the first, and the
            // first run's exit would then pull the overlay out from under a
            // window still counting down. See IsAwaitingDisplayConfirmation
            // for the worse half of the same race.
            if (_pendingConfirmation is not null) return;
            // Set synchronously, on the caller's thread — so a caller that
            // awaits the method that called this (FixAsync, FixAllAsync, …)
            // observes PendingConfirmation already set the moment control
            // returns to it, with no dependence on when a background task
            // happens to get scheduled.
            PendingConfirmation = confirmation;
        }
        ConfirmationRaised?.Invoke();

        // Only the wait-then-maybe-rollback goes on a background thread:
        // ChangeDisplaySettingsEx plus the journal/log writes it triggers
        // must never block the UI thread.
        PendingConfirmTask = Task.Run(async () =>
        {
            var kept = false;
            try
            {
                kept = await confirmation.AwaitConfirmationAsync();
                if (kept)
                {
                    // Only now does the registry hear about it. Until this
                    // moment the raise was session-only, so holding the power
                    // button through a black screen was a way out rather than
                    // a sentence — the user can see the screen, so make it
                    // stick.
                    try
                    {
                        if (!_host.KeepDisplayFix().Ok)
                            notice = _loc["display-confirm.notkept"];
                    }
                    catch (Exception)
                    {
                        notice = _loc["display-confirm.notkept"];
                    }
                }
            }
            finally
            {
                // Only if it is still OURS (see the gate above): an earlier
                // run must never take down a later run's overlay.
                lock (_confirmGate)
                {
                    if (ReferenceEquals(_pendingConfirmation, confirmation))
                        PendingConfirmation = null;
                }
            }

            if (notice is not null) DisplayNotice?.Invoke(notice);

            // Without this, both findings pages keep showing "Displays raised
            // to their highest refresh rate" as an active, undoable fix for a
            // mode that went back minutes ago. Only a scan repopulates those
            // rows, and nothing scans on a timer, so the claim would stand
            // until something unrelated happened to trigger one.
            if (!kept) await RescanAsync();
        });
    }

    /// A rescan a scan already running cannot swallow: that one started BEFORE
    /// the mode went back, so its findings are exactly the stale ones.
    private async Task RescanAsync()
    {
        try
        {
            await _scan;
            await ScanAsync();
        }
        catch (Exception)
        {
            // A scan that fails is the scan's problem to report; it must not
            // take down the rescue that was only trying to tell the truth.
        }
    }
}
