using System;
using Brisk.Services;
using Xunit;

namespace Brisk.Tests;

/// The pure purge decision (review round 1): matching, pre-existing
/// exclusion and $I-sibling derivation are unit-pinned here; the COM
/// enumeration around it is collect-then-delete by construction (PlanPurge
/// runs on an already-collected list) and is exercised live against the
/// real bin by the round-12 verification harness.
public sealed class RecycleBinPurgePlanTests
{
    private const string BinDir = @"C:\$Recycle.Bin\S-1-5-21-1234";

    [Fact]
    public void Plan_TakesWantedEntries_WithTheirIndexSiblings()
    {
        var plan = ShellRecycleBinSession.PlanPurge(
            new[]
            {
                new BinEntry(@"C:\Users\x\AppData\Local\Temp\a.tmp", BinDir + @"\$R1A2B3C.tmp"),
                new BinEntry(@"C:\Users\x\other.txt", BinDir + @"\$RZZZZZZ.txt"),
            },
            new[] { @"C:\Users\x\AppData\Local\Temp\a.tmp" },
            Array.Empty<string>());

        var target = Assert.Single(plan);
        Assert.Equal(@"C:\Users\x\AppData\Local\Temp\a.tmp", target.Original);
        Assert.Equal(BinDir + @"\$R1A2B3C.tmp", target.Payload);
        // C3: the $I metadata sibling dies with the $R payload — no ghosts
        Assert.Equal(BinDir + @"\$I1A2B3C.tmp", target.Index);
    }

    /// I4: two deletions of the SAME original path are two payload
    /// identities — the pre-clean snapshot's identity is excluded (the
    /// user's earlier deletion survives), the new one is purged.
    [Fact]
    public void Plan_ExcludesPreExistingPayloadIdentities_AtTheSamePath()
    {
        const string original = @"C:\Users\x\AppData\Local\Temp\cache.bin";
        var plan = ShellRecycleBinSession.PlanPurge(
            new[]
            {
                new BinEntry(original, BinDir + @"\$ROLD111.bin"),   // user's earlier delete
                new BinEntry(original, BinDir + @"\$RNEW222.bin"),   // this clean's recycle
            },
            new[] { original },
            new[] { BinDir + @"\$ROLD111.bin" });

        var target = Assert.Single(plan);
        Assert.Equal(BinDir + @"\$RNEW222.bin", target.Payload);
    }

    [Fact]
    public void Plan_MatchesOriginalsCaseInsensitively()
    {
        var plan = ShellRecycleBinSession.PlanPurge(
            new[] { new BinEntry(@"C:\USERS\X\A.TMP", BinDir + @"\$RAAAAAA.TMP") },
            new[] { @"c:\users\x\a.tmp" },
            Array.Empty<string>());
        Assert.Single(plan);
    }

    /// A directory payload has no extension: $RABCDEF → $IABCDEF.
    [Fact]
    public void IndexSibling_DirectoryPayload_DerivesWithoutExtension()
    {
        Assert.Equal(BinDir + @"\$IABCDEF",
            ShellRecycleBinSession.IndexSiblingFor(BinDir + @"\$RABCDEF"));
    }

    /// An unrecognized payload name gets NO sibling guess — better a stale
    /// index entry than deleting a file we cannot prove is metadata.
    [Fact]
    public void IndexSibling_UnrecognizedLayout_GetsNoGuess()
    {
        Assert.Null(ShellRecycleBinSession.IndexSiblingFor(BinDir + @"\DC1.tmp"));
    }

    // ---- $I metadata parsing (fix round: identity now comes from the
    // ---- bin's own on-disk records — COM's DeducedOriginalPath returns
    // ---- empty on current Windows 11 builds, verified live) -------------

    private static byte[] V2Index(string originalPath)
    {
        var chars = System.Text.Encoding.Unicode.GetBytes(originalPath + "\0");
        var bytes = new byte[28 + chars.Length];
        BitConverter.GetBytes(2L).CopyTo(bytes, 0);              // version
        BitConverter.GetBytes(1234L).CopyTo(bytes, 8);           // size
        BitConverter.GetBytes(0x1D2L).CopyTo(bytes, 16);         // filetime
        BitConverter.GetBytes(originalPath.Length + 1).CopyTo(bytes, 24);
        chars.CopyTo(bytes, 28);
        return bytes;
    }

    [Fact]
    public void ParseIndex_Version2_YieldsTheExactOriginalPath()
    {
        const string original = @"C:\Users\x\AppData\Local\Temp\önbellek-ğüş.bin";
        Assert.Equal(original,
            ShellRecycleBinSession.ParseIndexFile(V2Index(original)));
    }

    [Fact]
    public void ParseIndex_Version1_FixedBlock_YieldsThePath()
    {
        const string original = @"C:\Users\x\old.txt";
        var bytes = new byte[24 + 520];
        BitConverter.GetBytes(1L).CopyTo(bytes, 0);
        System.Text.Encoding.Unicode.GetBytes(original).CopyTo(bytes, 24);
        Assert.Equal(original, ShellRecycleBinSession.ParseIndexFile(bytes));
    }

    [Theory]
    [InlineData(0)]      // empty
    [InlineData(10)]     // truncated header
    [InlineData(26)]     // v2 without its length field
    public void ParseIndex_TruncatedRecords_YieldNull(int length)
    {
        var bytes = new byte[length];
        if (length >= 8) BitConverter.GetBytes(2L).CopyTo(bytes, 0);
        Assert.Null(ShellRecycleBinSession.ParseIndexFile(bytes));
    }

    [Fact]
    public void ParseIndex_UnknownVersion_YieldsNull()
    {
        var bytes = new byte[64];
        BitConverter.GetBytes(9L).CopyTo(bytes, 0);
        Assert.Null(ShellRecycleBinSession.ParseIndexFile(bytes));
    }

    [Fact]
    public void ParseIndex_AbsurdLength_YieldsNull()
    {
        var bytes = new byte[32];
        BitConverter.GetBytes(2L).CopyTo(bytes, 0);
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 24);
        Assert.Null(ShellRecycleBinSession.ParseIndexFile(bytes));
    }
}
