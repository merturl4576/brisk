using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using Brisk.Localization;
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
            // without going through our button, so what the button SHOWS and
            // what it is CALLED both follow the state rather than the click.
            UpdateMaximizeButton();
        };
        // The other two caption buttons take their name and tooltip from
        // bindings, which re-read themselves when Loc raises Item[]. This one
        // is set in code, so it has to be told — otherwise switching language
        // on the Settings page would leave the middle button announcing its
        // old name while every label around it changed.
        Loc.Instance.PropertyChanged += OnLanguageRepublished;
        UpdateMaximizeButton();
    }

    /// Loc.Instance is a process-lifetime singleton, and SetLanguage raises
    /// PropertyChanged SYNCHRONOUSLY on whichever thread called it. In the
    /// running app that is always the dispatcher, because the only caller is
    /// the Settings page — but "always" there is a habit, not a guarantee,
    /// and UpdateMaximizeButton reads WindowState, which VerifyAccess()es.
    /// A window that is subscribed to a singleton is reachable from anywhere,
    /// including from a thread that has never heard of it.
    ///
    /// So it is marshalled, for the same reason and in the same shape as
    /// AppState.ConfirmationRaised in App.xaml.cs: a UI update whose trigger
    /// can be raised off the dispatcher has to put itself back on it. In
    /// thread, it stays synchronous — the caption-button tests assert the
    /// name immediately after republishing the language, and would be
    /// racing an async hop otherwise.
    private void OnLanguageRepublished(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) UpdateMaximizeButton();
        else _ = Dispatcher.InvokeAsync(UpdateMaximizeButton);
    }

    /// Segoe Fluent Icons ChromeMaximize / ChromeRestore. Codepoints in an
    /// icon font are iconography, not localizable text — the same reason the
    /// nav's glyphs sit in Tag rather than in the string table.
    private const string MaximizeGlyph = "\uE922";
    private const string RestoreGlyph = "\uE923";

    /// Glyph, accessible name and tooltip in one place, because this one
    /// button is two buttons: Maximize on a normal window, Restore on a
    /// maximized one. Windows' own caption buttons swap their name the same
    /// way, so following the state is the LESS surprising choice for a screen
    /// reader user, not the more. A name left saying "Maximize" while the
    /// glyph said restore would be a quietly wrong claim aimed at exactly the
    /// people least able to check it against the picture.
    private void UpdateMaximizeButton()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? RestoreGlyph : MaximizeGlyph;
        // ONE string behind both, so the spoken name and the tooltip cannot
        // drift apart the way two keys eventually would.
        var name = Loc.Instance[maximized ? "chrome.restore" : "chrome.maximize"];
        AutomationProperties.SetName(MaximizeButton, name);
        MaximizeButton.ToolTip = name;
    }

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
