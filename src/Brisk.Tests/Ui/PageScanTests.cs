using BriskEngine.Actions;
using Brisk.Localization;
using Brisk.Session;
using Brisk.Tests.Session;
using Brisk.Ui.Pages;

namespace Brisk.Tests.Ui;

/// The scan Yer aç, Hızlandır and Gizlilik share (review I2). The reveal is
/// keyed on the row's id. A row that lands before rows already shown waits
/// its turn, and the shown ones stay shown. This closes the Task 11 carry
/// for Hızlandır and Gizlilik.
public sealed class PageScanTests
{
    [Fact]
    public void A_row_landing_before_shown_rows_waits_its_turn()
    {
        var session = new ActionSession(FakeBackend.Typical(), a => a());
        var loc = new Loc();
        loc.SetLanguage("tr");
        RowViewModel R(string id) =>
            new(FakeBackend.Row(id, ActionGroupKind.Speed), session, loc, Array.Empty<string>);
        var rows = new List<RowViewModel> { R("a"), R("b") };
        var timers = new ManualTimers();
        var scan = new PageScan(session, timers, () => true, () => rows, () => true, () => { });
        string[] Shown() => rows.Where(r => r.Revealed).Select(r => r.Id).ToArray();

        Assert.True(scan.Enter());
        scan.Activate();
        Assert.Empty(Shown());
        timers.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(new[] { "a" }, Shown());

        rows.Insert(0, R("new"));
        scan.Apply();
        Assert.Equal(new[] { "a" }, Shown());
        Assert.False(scan.Reached(rows[0]));

        timers.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(new[] { "new", "a" }, Shown());
        timers.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(new[] { "new", "a", "b" }, Shown());
        Assert.True(scan.Scanning);
        timers.Advance(TimeSpan.FromMilliseconds(300));
        Assert.False(scan.Scanning);
    }
}
