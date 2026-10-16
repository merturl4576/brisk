using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Brisk.Localization;
using Brisk.Services;
using Brisk.Session;
using Brisk.Tray;
using Brisk.Ui;
using Brisk.Ui.Shell;

namespace Brisk;

/// One window, a tray icon only while brisk still has a job, and the engine
/// behind both. `--tray` (the autostart task) starts without the window.
public partial class App : Application
{
    public static readonly TimeSpan DeferredEvery = TimeSpan.FromSeconds(30);

    private Mutex? _single;
    private EventWaitHandle? _showSignal;
    private TrayIcon? _tray;
    private ShellWindow? _window;
    private ActionSession? _session;
    private IActionBackend? _backend;
    private Settings? _settings;
    private DispatcherTimer? _deferredTimer;
    private bool _looked, _processingDeferred, _exiting;

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
            _settings = composition.Settings;
            Loc.Instance.SetLanguage(_settings.Language);
            // An autostart Run value from before the scheduled task: migrated
            // once, honouring the user's current setting (StartupLauncher).
            composition.Launcher.Migrate(_settings.StartWithWindows);

            // Decorative motion needs a GPU-composed window; on software
            // rendering it would cost the CPU the product is meant to free.
            var settings = _settings;
            AmbientClock.Install(new AmbientClock(() => settings.Motion && (RenderCapability.Tier >> 16) >= 2));

            Action<Action> post = a => Dispatcher.BeginInvoke(a);
            Action<Action> background = a => Task.Run(a);
            _backend = new RealActionBackend(composition.Engine, composition.Scanner);
            _session = new ActionSession(_backend, post);
            var settingsVm = new UiSettingsViewModel(_settings, composition.SettingsPath, Loc.Instance,
                composition.Launcher.IsOn, composition.Launcher.Apply, AmbientClock.Instance.Reevaluate);
            var shell = new ShellViewModel(post, background, _session, _backend, new DispatcherUiTimers(),
                Loc.Instance, AmbientClock.Instance, settingsVm);
            _window = new ShellWindow(shell);
            _window.Closing += OnWindowClosing;

            _tray = new TrayIcon(Accent(), Loc.Instance);
            _tray.LeftClick += ShowWindow;
            _tray.OpenRequested += ShowWindow;
            _tray.ExitRequested += Quit;

            _deferredTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = DeferredEvery };
            _deferredTimer.Tick += (_, _) => OnDeferredTick();
            _deferredTimer.Start();

            var showWaiter = new Thread(() =>
            {
                while (_showSignal.WaitOne())
                    Dispatcher.Invoke(ShowWindow);
            })
            { IsBackground = true };
            showWaiter.Start();

            if (!e.Args.Contains("--tray")) ShowWindow();
        }
        catch (Exception ex)
        {
            MessageBox.Show(SafeFormat("app.startupfailed", ex.Message));
            Shutdown(1);
        }
    }

    private void ShowWindow()
    {
        if (_window is null || _session is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        // Opening brisk is the user asking it to look: the first show starts
        // the plan (a --tray start waits for it).
        if (_looked) return;
        _looked = true;
        _ = _session.LookAsync();
    }

    private bool DeferredPending()
    {
        try { return _backend?.DeferredPending().Count > 0; }
        catch (Exception) { return false; }
    }

    private bool Running => _session?.State == SessionState.Running;

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting) return;
        e.Cancel = true;
        if (CloseRules.OnClose(DeferredPending(), Running, _settings?.StartWithWindows == true) == CloseAction.Hide)
        {
            _window?.Hide();
            UpdateTooltip();
        }
        else Quit();
    }

    /// Every 30 s: clean the queued caches whose app has closed, and leave
    /// once nothing keeps a hidden brisk alive.
    private void OnDeferredTick()
    {
        var pending = DeferredPending();
        if (pending && !_processingDeferred && _backend is { } backend)
        {
            _processingDeferred = true;
            Task.Run(() =>
            {
                try { backend.ProcessDeferred(); }
                catch (Exception) { }
            }).ContinueWith(_ => Dispatcher.BeginInvoke(() =>
            {
                _processingDeferred = false;
                UpdateTooltip();
            }));
        }
        UpdateTooltip();
        if (_window is { IsVisible: false } && !_processingDeferred
            && CloseRules.ShouldExitWhileHidden(pending, Running, _settings?.StartWithWindows == true))
            Quit();
    }

    private void UpdateTooltip()
    {
        if (_tray is null || _backend is null) return;
        try
        {
            var apps = _backend.DeferredApps();
            _tray.UpdateTooltip(apps.Count == 0
                ? "brisk"
                : "brisk · " + Loc.Instance.F("action.result.deferred", string.Join(", ", apps)));
        }
        catch (Exception) { }
    }

    private void Quit()
    {
        _exiting = true;
        _deferredTimer?.Stop();
        _tray?.Dispose();
        _tray = null;
        Shutdown();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
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

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }

    /// The tray mark wears the window's accent.
    private static System.Drawing.Color Accent()
    {
        var accent = ((SolidColorBrush)Current.Resources["Ui.Accent"]).Color;
        return System.Drawing.Color.FromArgb(accent.R, accent.G, accent.B);
    }
}
