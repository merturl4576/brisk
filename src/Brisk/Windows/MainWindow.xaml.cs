using System.ComponentModel;
using System.Windows;
using Brisk.Theming;
using Brisk.ViewModels;

namespace Brisk.Windows;

public partial class MainWindow : Window
{
    private readonly ThemeManager _theme;

    public MainWindow(HealthViewModel health, StartupViewModel startup,
        CleanViewModel clean, LogViewModel log, SettingsViewModel settings,
        ThemeManager theme)
    {
        _theme = theme;
        InitializeComponent();
        HealthView.Bind(health, startup);
        CleanView.DataContext = clean;
        LogView.DataContext = log;
        SettingsView.DataContext = settings;
        SourceInitialized += (_, _) => ApplyTitleBar();
    }

    public void ApplyTitleBar() => Dwm.DarkTitleBar(this, _theme.Current == "dark");

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (HealthView is null) return;   // fires during InitializeComponent
        HealthView.Visibility = sender == NavHealth ? Visibility.Visible : Visibility.Collapsed;
        CleanView.Visibility = sender == NavClean ? Visibility.Visible : Visibility.Collapsed;
        LogView.Visibility = sender == NavLog ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = sender == NavSettings ? Visibility.Visible : Visibility.Collapsed;
    }

    /// The app lives in the tray; the window close button only hides it.
    /// Quitting is the tray menu's Exit (per the approved design).
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
