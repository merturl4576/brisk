using System.ComponentModel;
using System.Windows;
using Brisk.Theming;
using Brisk.ViewModels;

namespace Brisk.Windows;

public partial class MainWindow : Window
{
    private readonly ThemeManager _theme;

    public MainWindow(OverviewViewModel overview, HealthViewModel health,
        HealthViewModel performance, StartupViewModel startup,
        CleanViewModel clean, SettingsViewModel settings, ThemeManager theme)
    {
        _theme = theme;
        InitializeComponent();
        OverviewView.DataContext = overview;
        HealthView.Bind(health);
        PerfView.Bind(performance, startup);
        CleanView.DataContext = clean;
        SettingsView.DataContext = settings;
        SourceInitialized += (_, _) => ApplyTitleBar();
    }

    public void ApplyTitleBar() => Dwm.DarkTitleBar(this, _theme.Current == "dark");

    /// The flyout's "Open details" always lands on the whole-PC overview.
    public void ShowOverview() => NavOverview.IsChecked = true;

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (OverviewView is null) return;   // fires during InitializeComponent
        OverviewView.Visibility = sender == NavOverview ? Visibility.Visible : Visibility.Collapsed;
        HealthView.Visibility = sender == NavHealth ? Visibility.Visible : Visibility.Collapsed;
        PerfView.Visibility = sender == NavPerf ? Visibility.Visible : Visibility.Collapsed;
        CleanView.Visibility = sender == NavClean ? Visibility.Visible : Visibility.Collapsed;
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
