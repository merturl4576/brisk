using System.ComponentModel;
using System.Windows;
using Brisk.Theming;
using Brisk.ViewModels;

namespace Brisk.Windows;

public partial class MainWindow : Window
{
    private readonly ThemeManager _theme;
    private readonly OverviewViewModel _overview;

    public MainWindow(AppState state, OverviewViewModel overview, HealthViewModel health,
        HealthViewModel performance, StartupViewModel startup,
        CleanViewModel clean, SettingsViewModel settings, ThemeManager theme)
    {
        _theme = theme;
        _overview = overview;
        InitializeComponent();
        // Every hosted page sets its own DataContext below, so this only
        // reaches the window-level overlay (the display-refresh confirm
        // prompt) — the one piece of UI that belongs to no single page.
        DataContext = state;
        OverviewView.DataContext = overview;
        HealthView.Bind(health);
        PerfView.Bind(performance, startup);
        CleanView.DataContext = clean;
        SettingsView.DataContext = settings;
        SourceInitialized += (_, _) => ApplyTitleBar();
        // An advise card's "Open Storage" lands on the Depolama page, from
        // whichever page hosted the card.
        health.OpenStorageRequested += ShowStorage;
        performance.OpenStorageRequested += ShowStorage;
        // The findings pages cross-link each other (category split): the
        // quiet "{n} more findings …" row navigates to the sibling page.
        health.CrossNavigateRequested += () => NavPerf.IsChecked = true;
        performance.CrossNavigateRequested += () => NavHealth.IsChecked = true;
        // The band's "see the evidence" goes to the page that HOSTS the
        // finding — the boot finding lives on Performans, and sending its
        // reader to Sağlık was the first defect live use found.
        overview.OpenFindingRequested += ruleId =>
        {
            if (FindingSections.IsPerformance(ruleId))
            {
                NavPerf.IsChecked = true;
                performance.ExpandFinding(ruleId);
            }
            else
            {
                NavHealth.IsChecked = true;
                health.ExpandFinding(ruleId);
            }
        };
        // Live tiles pulse only while this window is truly on screen: shown
        // starts it; hide/close-to-tray/minimize stops it. The flyout never
        // hosts live tiles, so no other window can start the timer.
        IsVisibleChanged += (_, _) => UpdateLiveTicking();
        StateChanged += (_, _) =>
        {
            UpdateLiveTicking();
            // Snap layouts and double-click-on-caption change the state
            // without going through our button, so the glyph follows the
            // STATE rather than the click.
            UpdateMaximizeGlyph();
        };
        UpdateMaximizeGlyph();
    }

    /// Segoe Fluent Icons ChromeMaximize / ChromeRestore. Codepoints in an
    /// icon font are iconography, not localizable text — the same reason the
    /// nav's glyphs sit in Tag rather than in the string table.
    private const string MaximizeGlyph = "\uE922";
    private const string RestoreGlyph = "\uE923";

    private void UpdateMaximizeGlyph() =>
        MaximizeButton.Content =
            WindowState == WindowState.Maximized ? RestoreGlyph : MaximizeGlyph;

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    /// Close, not Shutdown: OnClosing below turns this into a hide, because
    /// brisk lives in the tray. Quitting is the tray menu's Exit.
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateLiveTicking()
    {
        var visible = IsVisible && WindowState != WindowState.Minimized;
        _overview.SetLiveVisible(visible);
        // The hero's ambient motion layer obeys the exact same signal as
        // the live ticker — one code path, so the two can never drift.
        OverviewView.SetMotionActive(visible);
    }

    public void ApplyTitleBar() => Dwm.DarkTitleBar(this, _theme.Current == "dark");

    /// The flyout's "Open details" always lands on the whole-PC overview.
    public void ShowOverview() => NavOverview.IsChecked = true;

    private void ShowStorage() => NavClean.IsChecked = true;

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
