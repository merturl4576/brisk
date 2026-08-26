using System.Linq;
using System.Windows;
using Brisk.Tests.Snapshots;
using Brisk.ViewModels;
using Xunit;
// WinForms is on in this project, so these two bare names are ambiguous.
using RadioButton = System.Windows.Controls.RadioButton;
using UserControl = System.Windows.Controls.UserControl;

namespace Brisk.Tests;

/// Where the overview band's "see the evidence" actually lands, driven on the
/// REAL MainWindow rather than on the view model that raises the request.
///
/// The routing lives in a lambda inside the window's constructor, so nothing
/// in the view-model suite can see it — and for five commits it was an
/// if/else with two arms: performance rules to Performans, and EVERYTHING
/// ELSE to Sağlık. A privacy id fell into that else and opened a page whose
/// own filter excludes privacy ids, so the page changed and no card opened.
/// Five separate reviews recorded it and none of them could watch it happen,
/// because there was no third page to route to and the band withheld the link
/// rather than offering a dead one. This is that watch.
public sealed class ShellRoutingTests
{
    [Fact]
    public void TheBandsLink_OverAPrivacyFinding_OpensTheGizlilikPage()
    {
        SnapshotRenderer.OnUiThread(() =>
        {
            var window = SnapshotTests.CockpitWindow();
            var overview = (OverviewViewModel)Page(window, "OverviewView").DataContext;
            var privacy = (PrivacyViewModel)Page(window, "PrivacyView").DataContext;

            // The control, and it is not decoration: every assertion below is
            // about a change, and a window that opened on Gizlilik already
            // would pass all of them without the click doing anything.
            Assert.True(Tile(window, "NavOverview").IsChecked,
                "the cockpit did not open on Genel Bakış, so what the click " +
                "below changes cannot be told from where it started");
            // Visibility, not IsVisible: this window is built but never
            // shown — physically showing it would throw a cockpit across the
            // developer's desktop mid-run, the same reason CaptionButtonTests
            // drives the window without one — and IsVisible answers false for
            // every element in an unshown window whatever Nav_Checked set.
            // Visibility is the property Nav_Checked actually writes.
            Assert.Equal(Visibility.Collapsed, Page(window, "PrivacyView").Visibility);

            // The fixture's leading revelation is a privacy disclosure, which
            // is the whole point: the band is driven through its own command,
            // the way the Button on the page does it.
            Assert.True(overview.HasRevelationLink,
                "the band offered no link at all, so this test would pass by " +
                "never asking anything to be routed");
            overview.OpenFindingCommand.Execute(null);

            Assert.True(Tile(window, "NavPrivacy").IsChecked,
                "the band's link over a privacy finding did not select " +
                "Gizlilik — the routing sent it to whichever page the else " +
                "arm names, which is the defect this test exists for");
            Assert.Equal(Visibility.Visible, Page(window, "PrivacyView").Visibility);
            foreach (var other in new[] { "HealthView", "PerfView", "CleanView",
                                          "SettingsView", "OverviewView" })
                Assert.True(Page(window, other).Visibility == Visibility.Collapsed,
                    $"{other} is showing beside Gizlilik");

            // The other half of the same defect, and the one a page swap
            // alone would hide: the reader was sent somewhere to READ a
            // finding, so the card has to be open when they arrive.
            var opened = privacy.DisclosureRows.Concat(privacy.UnreadableRows)
                .Concat(privacy.SafeSwitchRows).Concat(privacy.CostlySwitchRows)
                .Where(row => row.IsExpanded)
                .Select(row => row.RuleId)
                .ToArray();
            Assert.True(opened.Length == 1,
                "the band sent a reader to Gizlilik to look at one finding and " +
                $"{opened.Length} cards are open there");
        });
    }

    private static RadioButton Tile(Window window, string name) =>
        (RadioButton)window.FindName(name)!;

    private static UserControl Page(Window window, string name) =>
        (UserControl)window.FindName(name)!;
}
