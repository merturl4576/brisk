using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Brisk.Localization;
using Brisk.Services;
using Brisk.Theming;
using Brisk.Tray;
using Brisk.ViewModels;
using Brisk.Windows;
using BriskEngine;

namespace Brisk;

public partial class App : Application
{
    private Mutex? _single;
    private EventWaitHandle? _showSignal;
    private TrayIcon? _tray;
    private MainWindow? _main;
    private FlyoutWindow? _flyout;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _single = new Mutex(true, "brisk-app-single", out var isFirst);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "brisk-app-show");
        if (!isFirst)
        {
            _showSignal.Set();   // ask the running instance to show itself
            Shutdown();
            return;
        }

        try
        {
            var composition = AppServices.Build();
            Loc.Instance.SetLanguage(composition.Settings.Language);
            var theme = new ThemeManager();
            theme.Apply(composition.Settings.Theme);

            // Before the scheduled task, brisk registered itself under HKCU's
            // Run key. A machine that still carries that value shows a second,
            // identical "brisk" row in brisk's own startup list whose toggle
            // changes nothing real — and the value itself is an autostart that
            // Windows skips anyway, now that brisk requires elevation.
            // Migrated once, at startup.
            // Gated on the setting, not on the value alone: the value is
            // evidence of an OLD intent. A user who upgraded to the
            // task-based build and then explicitly turned autostart OFF has
            // exactly this machine state — stale value, no task — and
            // recreating the task there would let the oldest implicit choice
            // beat the newest explicit one, silently, before any window
            // exists. The dead value goes either way.
            composition.Launcher.Migrate(composition.Settings.StartWithWindows);

            // Dispatcher.Invoke is the third use of the same precedent in
            // this file (the tray's Changed handler, ShowMain below): the
            // display rescue resolves on a thread-pool thread, and every
            // Changed / DisplayNotice subscriber touches UI objects.
            var state = new AppState(composition.Host, Loc.Instance,
                action => Dispatcher.Invoke(action));
            var cleanService = new CleanService(composition.Host, composition.Settings);
            var fixAllService = new FixAllService(composition.Host);
            // The confirmation starts at the mode change, not at the end of
            // the batch: FixAllService reports each rule as it lands, and a
            // display raised first would otherwise sit black through every
            // remaining fix before its 15 seconds even began.
            state.TrackFixes(fixAllService);
            // ONE bin session and ONE safe-clean runner behind all three
            // clean surfaces (round 13) — the flyout, the overview button
            // and the Depolama card now run the identical recycle→purge
            // sequence, so the same promise cannot mean two things.
            var bin = new ShellRecycleBinSession();
            var safeClean = new SafeCleanRunner(cleanService, bin);
            // The lease refuses a second clean; this is how the UI SHOWS it
            // (round-13 re-review N1) — every clean button disables while
            // any surface holds the runner.
            state.TrackCleaning(safeClean);
            Func<bool> isDryRun = () => composition.Settings.DryRun;
            var flyoutVm = new FlyoutViewModel(state, safeClean, fixAllService,
                Loc.Instance, isDryRun);
            var overviewVm = new OverviewViewModel(state, composition.Host,
                fixAllService, safeClean, composition.LiveMetrics,
                Loc.Instance, isDryRun);
            var healthVm = new HealthViewModel(state, composition.Host, Loc.Instance,
                isDryRun, fixAllService, FindingSections.IsHealth,
                doneFilter: FindingSections.IsHealth,
                crossLinkKey: "health.crosslink");
            var perfVm = new HealthViewModel(state, composition.Host, Loc.Instance,
                isDryRun, fixAllService, FindingSections.IsPerformance,
                doneFilter: FindingSections.IsPerformance,
                crossLinkKey: "performance.crosslink");
            var startupVm = new StartupViewModel(state, composition.Host,
                Loc.Instance, isDryRun, composition.Launcher);
            var cleanVm = new CleanViewModel(state, composition.Host, cleanService,
                safeClean, bin, Loc.Instance, isDryRun);
            var settingsVm = new SettingsViewModel(composition.Settings,
                composition.SettingsPath, composition.Launcher,
                themeSetting =>
                {
                    theme.Apply(themeSetting);
                    _main?.ApplyTitleBar();
                    // Both of brisk's marks, not one. The tray icon is drawn
                    // from the installed palette, and a switch that moved the
                    // title bar without it would leave the notification area
                    // carrying the previous theme's accent — the marks
                    // disagreeing about what brisk looks like, which is what
                    // sourcing the tray from the palette was meant to stop.
                    _tray?.SetAccent(SignatureAccent());
                },
                state.SetLanguage);

            _flyout = new FlyoutWindow(flyoutVm);
            _main = new MainWindow(state, overviewVm, healthVm, perfVm, startupVm,
                cleanVm, settingsVm, theme);
            flyoutVm.OpenDetailsRequested += () =>
            {
                _flyout.Hide();
                _main?.ShowOverview();
                ShowMain();
            };
            // The flyout, not the main window, is what --tray-less startup
            // shows (below) — so a confirmation raised while only the
            // flyout is open must bring the window with the overlay on
            // screen itself, the same way the tray's "Open" item already
            // does. ShowMain() is the one Show/Activate sequence; reused
            // here rather than duplicated — marshalled, because TrackFixes
            // raises this from the fix batch's worker thread and touching a
            // window off the dispatcher throws. A confirmation nobody can see
            // is the exact failure this whole mechanism exists to prevent.
            state.ConfirmationRaised += () => Dispatcher.Invoke(ShowMain);

            _tray = new TrayIcon(SignatureAccent(), Loc.Instance);
            _tray.LeftClick += () => _flyout.ShowAt();
            _tray.OpenRequested += ShowMain;
            _tray.ScanRequested += () => _ = state.ScanAsync();
            _tray.ExitRequested += () =>
            {
                _tray?.Dispose();
                _tray = null;
                Shutdown();
            };
            state.Changed += () => Dispatcher.Invoke(() => _tray?.UpdateTooltip(
                Loc.Instance.F("tray.tooltip",
                    Fmt.Bytes(composition.Host.FreeDiskBytes()),
                    state.Snapshot?.Health ?? 0)));

            // The flyout is the product's face; the main window is the
            // back-office reached via "Open details" or the tray menu.
            var showWaiter = new Thread(() =>
            {
                while (_showSignal.WaitOne())
                    Dispatcher.Invoke(() => _flyout?.ShowAt());
            })
            { IsBackground = true };
            showWaiter.Start();

            if (!e.Args.Contains("--tray")) _flyout.ShowAt();   // --tray = silent autostart
            _ = state.ScanAsync();   // launching brisk is a user action — scan once
        }
        catch (Exception ex)
        {
            MessageBox.Show(SafeFormat("app.startupfailed", ex.Message));
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(SafeFormat("app.unexpected", e.Exception.Message));
        e.Handled = true;
    }

    /// Loc itself may be the thing that failed; fall back to the raw message.
    private static string SafeFormat(string key, string arg)
    {
        try { return string.Format(Loc.Instance[key], arg); }
        catch { return arg; }
    }

    private void ShowMain()
    {
        if (_main is null) return;
        _main.Show();
        _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }

    /// brisk's signature, as the tray's drawing code wants it, read from the
    /// palette that is installed right now.
    ///
    /// The tray icon is brisk's mark in the notification area, so it wears
    /// brisk's own colour rather than the Windows accent it used to be drawn
    /// in — see ThemeManager.Apply for why the signature stopped following
    /// the desktop. One method rather than two reads, because the first draw
    /// and the after-a-theme-switch redraw have to agree, and two copies of
    /// "where does the accent come from" is exactly how they would stop.
    private static System.Drawing.Color SignatureAccent()
    {
        var accent = ((SolidColorBrush)Current.Resources["AccentBrush"]).Color;
        return System.Drawing.Color.FromArgb(accent.R, accent.G, accent.B);
    }

}
