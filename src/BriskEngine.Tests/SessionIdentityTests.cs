using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

/// WAVE B, B2. The decision this record makes is whether brisk tells a user
/// that the profile it is reading and cleaning is not theirs. Getting it wrong
/// in either direction is a lie, so the rule is pinned here rather than left
/// implicit in the probe's P/Invoke.
public class SessionIdentityTests
{
    /// WAVE C, C3. The probe compares SIDs; the record must CARRY that verdict
    /// rather than re-derive it from names. Two accounts in different forests
    /// can share one DOMAIN\user spelling — the exact case SIDs exist to tell
    /// apart — and re-deriving would turn the answer back into a match.
    [Fact]
    public void SidVerdict_IsNotSecondGuessedByTheNames()
    {
        var differentAccountsSameSpelling =
            new SessionIdentity(@"CORP\alice", @"OTHER\alice", true);

        Assert.True(differentAccountsSameSpelling.DiffersFromInteractiveUser);
    }

    [Fact]
    public void Unknown_IsNeverAMismatch()
    {
        var unknown = SessionIdentity.Unknown(@"PC\Admin");

        Assert.Null(unknown.InteractiveUser);
        Assert.False(unknown.DiffersFromInteractiveUser);
    }

    [Fact]
    public void SameAccount_IsNotAMismatch() =>
        Assert.False(SessionIdentity.NamesDiffer(@"PC\alice", @"PC\alice"));

    [Fact]
    public void CaseOnlyDifference_IsNotAMismatch() =>
        Assert.False(SessionIdentity.NamesDiffer(@"PC\Alice", @"pc\alice"));

    /// Over-the-shoulder elevation: brisk holds the admin's token while the
    /// standard user is the one signed in.
    [Fact]
    public void ElevatedByAnotherAccount_IsAMismatch() =>
        Assert.True(SessionIdentity.NamesDiffer(@"PC\Admin", @"PC\alice"));

    /// WAVE C, C3. WTSDomainName can come back empty, which leaves a bare
    /// "alice" against "PC\alice". With the SID translation also failing —
    /// both must fail together, so it is unlikely but not impossible — the
    /// name fallback used to declare a mismatch that does not exist, and a
    /// false accusation is the one direction this work must never take.
    [Fact]
    public void BareAccountName_IsNotAMismatchAgainstAQualifiedOne() =>
        Assert.False(SessionIdentity.NamesDiffer(@"PC\alice", "alice"));

    /// The trade that buys it: comparing leaves can MISS a real difference
    /// across two domains, and missing means brisk says nothing. Silence is
    /// the only safe way to be wrong here — pinned so the trade stays
    /// deliberate rather than becoming a surprise.
    [Fact]
    public void SameLeafInTwoDomains_IsTreatedAsTheSamePerson_OnPurpose() =>
        Assert.False(SessionIdentity.NamesDiffer(@"PC1\alice", @"CORP\alice"));

    [Fact]
    public void UnknownInteractiveUser_IsNotAMismatch() =>
        Assert.False(SessionIdentity.NamesDiffer(@"PC\Admin", null));
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
