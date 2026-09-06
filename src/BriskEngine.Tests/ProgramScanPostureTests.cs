using Brisk.Cli;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

/// The two lines `brisk scan` prints under the findings. What they must NOT
/// do is as pinned as what they do: a row that is not fine is already a
/// finding above and is left out rather than restated, and a row brisk did
/// not read is named on its own line rather than folded into "fine" (spec
/// red line 2). Neither line carries a count, a badge or a verdict (red
/// line 4).
public class ProgramScanPostureTests
{
    [Fact]
    public void AllFine_IsOneLine_AndNoUnreadLine()
    {
        var (fine, unread) = Program.PostureLines(new SecurityPostureReport(true, true, true, true, true, true, true));
        Assert.Equal("Checked and fine: firewall on for all 3 profiles, Remote Desktop off, " +
                     "Windows asks before a program takes control, Defender real-time protection on, " +
                     "Defender tamper protection on.", fine);
        Assert.Null(unread);
    }

    [Fact]
    public void ABadRow_IsLeftOut_BecauseItIsAlreadyAFinding()
    {
        var (fine, _) = Program.PostureLines(new SecurityPostureReport(true, true, true, true, false, true, true));
        Assert.DoesNotContain("asks before", fine);
        Assert.Contains("firewall on", fine);
    }

    [Fact]
    public void NullRows_GoToTheUnreadLine_NeverToFine()
    {
        var (fine, unread) = Program.PostureLines(new SecurityPostureReport(null, null, null, true, true, null, true));
        Assert.DoesNotContain("firewall", fine);
        Assert.Equal("Not read: firewall, Defender real-time protection.", unread);
    }

    [Fact]
    public void NothingFine_IsNoFineLine()
    {
        var (fine, unread) = Program.PostureLines(new SecurityPostureReport(null, null, null, null, null, null, null));
        Assert.Null(fine);
        Assert.NotNull(unread);
    }
}
