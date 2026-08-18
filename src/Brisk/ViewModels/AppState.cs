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
    private readonly Action<Action> _toUi;
    private readonly object _confirmGate = new();
    private ScanSnapshot? _snapshot;
    private bool _isScanning;
    private bool _isCleaning;
    private string _progressText = "";
    private RefreshConfirmation? _pendingConfirmation;
    private string _displayNotice = "";
    private Task _scan = Task.CompletedTask;

    /// toUiThread is how Changed and DisplayNotice get back to the dispatcher.
    /// Both fire from the display rescue, which resolves on a thread-pool
    /// thread with no SynchronizationContext under it, and every subscriber is
    /// UI-affine: FlyoutViewModel.Refresh ends in RaiseCanExecuteChanged (which
    /// sets IsEnabled on a ButtonBase) and HealthViewModel.Refresh clears an
    /// ObservableCollection behind a CollectionView. The first one to throw
    /// aborts the rest of the invocation list, so a rescan that never reaches
    /// the pages is exactly as useless as no rescan at all.
    ///
    /// App.xaml.cs passes Dispatcher.Invoke — the same marshalling it already
    /// uses for the tray's Changed handler and for ShowMain. The default runs
    /// inline, which is what unit tests (no dispatcher) need.
    public AppState(IEngineHost host, Loc? loc = null, Action<Action>? toUiThread = null)
    {
        _host = host;
        _loc = loc ?? Loc.Instance;
        _toUi = toUiThread ?? (action => action());
        KeepDisplayCommand = new RelayCommand(() => PendingConfirmation?.Keep());
        DismissDisplayNoticeCommand = new RelayCommand(() => DisplayNotice = "");
        IdentityWarning = IdentityWarningFor(host, _loc);
    }

    /// Asked once, at composition. A probe that cannot answer reports the
    /// interactive user as unknown, and an unknown answer must never become a
    /// confident claim about whose files these are — so it stays silent.
    private static string IdentityWarningFor(IEngineHost host, Loc loc)
    {
        try
        {
            var session = host.Session();
            return session.DiffersFromInteractiveUser
                ? loc.F("identity.otheraccount", session.ProcessUser,
                    session.InteractiveUser ?? "")
                : "";
        }
        catch (Exception)
        {
            return "";
        }
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

    /// Everything the display rescue owes the user, in ONE place: a banner on
    /// the window itself, where the confirmation overlay already lives.
    /// Subscribing individual pages left Storage and Settings silent — brisk
    /// could put a display back and say nothing at all on two of five pages —
    /// and the Overview version had to live in that page's ReportLines, where
    /// it hid the journal panel until the next scan. The window outlives every
    /// page, so it is the honest home. Empty when there is nothing to say.
    ///
    /// It carries:
    ///   * the ordinary outcome, where nobody answered and the previous mode
    ///     went back on. The spec requires brisk to say so and to name cable
    ///     or adapter as the likely reason the rate did not carry;
    ///   * a rollback that could not restore the previous mode;
    ///   * a confirmed mode that could not be written to the registry, and so
    ///     will not survive a restart.
    ///
    /// It goes away two ways, because a sentence that never leaves stops being
    /// read: the banner's own Dismiss button, and a new display attempt, which
    /// supersedes whatever the last one had to say. Deliberately NOT cleared by
    /// a scan — a scan re-detects the display running slow, so the sentence
    /// explaining why the raise did not stick belongs right beside it.
    public string DisplayNotice
    {
        get => _displayNotice;
        private set
        {
            if (Set(ref _displayNotice, value)) Raise(nameof(HasDisplayNotice));
        }
    }

    public bool HasDisplayNotice => _displayNotice.Length > 0;

    public RelayCommand DismissDisplayNoticeCommand { get; }

    /// Non-empty only when brisk's process token belongs to a DIFFERENT
    /// account than the one signed in — over-the-shoulder elevation from a
    /// standard account. Then HKCU, %LOCALAPPDATA% and the Recycle Bin all
    /// follow the token, so the profile brisk scans and cleans is not the
    /// profile of the person reading the screen. Fixed for the life of the
    /// process: it is a fact about this run, not a state that changes.
    public string IdentityWarning { get; }

    public bool HasIdentityWarning => IdentityWarning.Length > 0;

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
        // Marshalled: a scan started by the rollback resolves on the thread
        // pool, and every subscriber touches UI objects (see the constructor).
        _toUi(() => Changed?.Invoke());
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
            // A new attempt supersedes whatever the last one had to say.
            DisplayNotice = "";
            // Set synchronously, on the caller's thread — so a caller that
            // awaits the method that called this (FixAsync, FixAllAsync, …)
            // observes PendingConfirmation already set the moment control
            // returns to it, with no dependence on when a background task
            // happens to get scheduled.
            PendingConfirmation = confirmation;
        }
        // Started BEFORE the raise below. The raise reaches a window
        // (App.xaml.cs calls ShowMain), and a dispatcher shutting down throws
        // there — which would leave the gate above latched with no rollback
        // task behind it at all: fix-all dead app-wide, the window topmost
        // until restart, and the rest of the batch abandoned on the worker
        // thread. The rescue has to exist before anything can fail.
        //
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

            // Without this, both findings pages keep showing "Displays raised
            // to their highest refresh rate" as an active, undoable fix for a
            // mode that went back minutes ago. Only a scan repopulates those
            // rows, and nothing scans on a timer, so the claim would stand
            // until something unrelated happened to trigger one.
            // The rescan runs first, so the pages are already telling the
            // truth by the time the sentence explaining it arrives.
            if (!kept) await RescanAsync();
            // Marshalled for the same reason Changed is: OverviewViewModel
            // puts this straight into an ObservableCollection.
            if (notice is not null) _toUi(() => DisplayNotice = notice);
        });

        try
        {
            ConfirmationRaised?.Invoke();
        }
        catch (Exception)
        {
            // There is nowhere to report this: the failure IS the surface that
            // would have carried the report. The rescue above is already
            // running, so the worst case is a confirmation with no overlay,
            // which resolves itself in 15 seconds by putting the display back.
            // Letting it escape would abort the fix batch and strand the gate.
        }
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
