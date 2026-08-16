using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Brisk.Localization;
using Brisk.Services;
using Brisk.Theming;
using Brisk.Tray;
using Brisk.ViewModels;
using Brisk.Windows;
using BriskEngine;
using Microsoft.Win32;

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

            var state = new AppState(composition.Host);
            var cleanService = new CleanService(composition.Host, composition.Settings);
            Func<bool> isDryRun = () => composition.Settings.DryRun;
            var flyoutVm = new FlyoutViewModel(state, composition.Host, cleanService,
                Loc.Instance, isDryRun);
            var healthVm = new HealthViewModel(state, composition.Host, Loc.Instance, isDryRun);
            var startupVm = new StartupViewModel(state, composition.Host, isDryRun);
            var cleanVm = new CleanViewModel(state, composition.Host, cleanService,
                new ShellRecycleBinSession(), Loc.Instance, isDryRun);
            var logVm = new LogViewModel(state, composition.Host, isDryRun);
            var settingsVm = new SettingsViewModel(composition.Settings,
                composition.SettingsPath, composition.Launcher,
                themeSetting => { theme.Apply(themeSetting); _main?.ApplyTitleBar(); },
                Loc.Instance.SetLanguage);

            _flyout = new FlyoutWindow(flyoutVm);
            _main = new MainWindow(healthVm, startupVm, cleanVm, logVm, settingsVm, theme);
            flyoutVm.OpenDetailsRequested += () => { _flyout.Hide(); ShowMain(); };

            var accent = ThemeResolver.AccentFrom(
                Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM")
                    ?.GetValue("ColorizationColor") as int?);
            _tray = new TrayIcon(System.Drawing.Color.FromArgb(accent.R, accent.G, accent.B),
                Loc.Instance);
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
                $"brisk — {Fmt.Bytes(composition.Host.FreeDiskBytes())} free" +
                $" · {state.Snapshot?.Health}"));

            var showWaiter = new Thread(() =>
            {
                while (_showSignal.WaitOne())
                    Dispatcher.Invoke(ShowMain);
            })
            { IsBackground = true };
            showWaiter.Start();

            if (!e.Args.Contains("--tray")) ShowMain();
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
}
