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
