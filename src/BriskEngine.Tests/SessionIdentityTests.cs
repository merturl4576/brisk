using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

/// WAVE B, B2. The decision this record makes is whether brisk tells a user
/// that the profile it is reading and cleaning is not theirs. Getting it wrong
/// in either direction is a lie, so the rule is pinned here rather than left
/// implicit in the probe's P/Invoke.
public class SessionIdentityTests
{
    [Fact]
    public void SameAccount_IsNotAMismatch() =>
        Assert.False(new SessionIdentity(@"PC\alice", @"PC\alice")
            .DiffersFromInteractiveUser);

    [Fact]
    public void CaseOnlyDifference_IsNotAMismatch() =>
        Assert.False(new SessionIdentity(@"PC\Alice", @"pc\alice")
            .DiffersFromInteractiveUser);

    /// Over-the-shoulder elevation: brisk holds the admin's token while the
    /// standard user is the one signed in.
    [Fact]
    public void ElevatedByAnotherAccount_IsAMismatch() =>
        Assert.True(new SessionIdentity(@"PC\Admin", @"PC\alice")
            .DiffersFromInteractiveUser);

    /// Unknown must never become a claim: no session to query (a service, a
    /// locked-down machine, no Terminal Services) means brisk says nothing.
    [Fact]
    public void UnknownInteractiveUser_IsNotAMismatch() =>
        Assert.False(new SessionIdentity(@"PC\Admin", null)
            .DiffersFromInteractiveUser);
}

/// The record above is pure; this exercises the P/Invoke itself, which is the
/// part nothing else can check. It cannot assert WHO the machine says is
/// signed in — that depends on the box the suite runs on — but a wrong
/// signature, a wrong WTS info class or a bad marshal shows up here as a
/// throw or as garbage rather than silently mis-accusing a real user later.
public class RealSessionProbeTests
{
    [Fact]
    public void AnswersWithoutThrowing_AndNamesTheProcessAccount()
    {
        var identity = new BriskEngine.Diagnostics.RealProbes.RealSessionProbe().Current();

        Assert.False(string.IsNullOrWhiteSpace(identity.ProcessUser));
        Assert.Contains("\\", identity.ProcessUser);        // DOMAIN\user
        if (identity.InteractiveUser is { } interactive)
        {
            Assert.False(string.IsNullOrWhiteSpace(interactive));
            Assert.DoesNotContain('\0', interactive);      // no marshalling debris
        }
    }
}
