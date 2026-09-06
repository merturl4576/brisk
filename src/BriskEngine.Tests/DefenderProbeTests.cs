using BriskEngine.Diagnostics.RealProbes;
using Xunit;

namespace BriskEngine.Tests;

/// The one decision in RealDefenderProbe that can be taken without WMI: how
/// it tells an exclusion list from Windows' refusal to show one.
///
/// Windows does not refuse a non-administrator the exclusions and does not
/// answer empty. It answers each of the three exclusion properties with ONE
/// string — "N/A: Must be an administrator to view exclusions" — verified
/// unelevated on the maintainer's machine 2026-09-06. The elevation check in
/// the probe normally keeps that shape out; this is what stops it being
/// counted as three exclusions and printed as an excluded path if the token
/// and Windows ever disagree.
public class DefenderProbeTests
{
    [Fact]
    public void TheSentinelWindowsHandsANonAdministrator_ReadsAsHidden()
    {
        Assert.True(RealDefenderProbe.IsHidden(
            new[] { "N/A: Must be an administrator to view exclusions" }));
    }

    [Fact]
    public void ARealExclusion_IsNotHidden()
    {
        Assert.False(RealDefenderProbe.IsHidden(new[] { @"C:\Users\U\.claude" }));
        Assert.False(RealDefenderProbe.IsHidden(new[] { ".log" }));
    }

    /// A list is a list: the sentinel is the WHOLE answer or it is not the
    /// sentinel, so a machine that really does exclude a path beginning
    /// "N/A:" alongside another one keeps both.
    [Fact]
    public void AListWithMoreThanOneEntry_IsNeverHidden()
    {
        Assert.False(RealDefenderProbe.IsHidden(
            new[] { "N/A: Must be an administrator to view exclusions", @"C:\real" }));
    }

    /// Nothing excluded is a reading, and this is not the shape that hides
    /// one.
    [Fact]
    public void AnEmptyList_IsNotHidden()
    {
        Assert.False(RealDefenderProbe.IsHidden(System.Array.Empty<string>()));
    }

    /// THE COUNT IS THE LIST'S LENGTH. The finding counts paths, extensions
    /// and processes together, so the fold the Privacy page opens has to hold
    /// all three or a machine excluding two processes and no folder reads
    /// "2 entries Defender skips" over an empty list.
    [Fact]
    public void EveryKindOfExclusion_IsInTheList_AndTheListIsTheCount()
    {
        var entries = RealDefenderProbe.Entries(
            new[] { @"C:\Users\U\.claude", @"D:\vm" },
            new[] { ".vdi" },
            new[] { "chrome.exe", "node.exe" });

        Assert.Equal(5, entries.Count);
        Assert.Equal(
            new[] { @"C:\Users\U\.claude", @"D:\vm", "extension: .vdi",
                "process: chrome.exe", "process: node.exe" },
            entries);
    }

    /// A path travels bare and the other two say what they are: ".vdi" and
    /// "chrome.exe" listed among folders would read as files Defender skips,
    /// which is a smaller claim than the true one.
    [Fact]
    public void OnlyPaths_TravelWithoutALabel()
    {
        var paths = RealDefenderProbe.Entries(
            new[] { @"C:\only" }, System.Array.Empty<string>(),
            System.Array.Empty<string>());

        Assert.Equal(new[] { @"C:\only" }, paths);
    }

    /// Nothing excluded anywhere is an empty list and a count of zero, which
    /// is a reading and not the shape the probe reports as unread.
    [Fact]
    public void NoExclusionsAtAll_IsAnEmptyList()
    {
        Assert.Empty(RealDefenderProbe.Entries(
            System.Array.Empty<string>(), System.Array.Empty<string>(),
            System.Array.Empty<string>()));
    }
}
