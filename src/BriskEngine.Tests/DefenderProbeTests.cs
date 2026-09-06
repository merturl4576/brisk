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
}
