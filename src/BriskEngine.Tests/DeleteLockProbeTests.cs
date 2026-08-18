using System;
using System.IO;
using BriskEngine.Cleaning;
using Xunit;

namespace BriskEngine.Tests;

/// The real probe against the real filesystem: a file held open WITHOUT
/// FILE_SHARE_DELETE (the .NET default) must read as locked — that is the
/// exact wall a recycle-move hits — and everything released must read free.
public sealed class DeleteLockProbeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-lock-").FullName;
    private readonly DeleteLockProbe _probe = new();

    private static LockProbeBudget Budget() => new();

    [Fact]
    public void HeldFile_IsLocked_ReleasedFile_IsNot()
    {
        var path = Path.Combine(_root, "held.bin");
        File.WriteAllBytes(path, new byte[8]);
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.True(_probe.IsLockedForDelete(path, Budget()));
        Assert.False(_probe.IsLockedForDelete(path, Budget()));
    }

    /// The EBWebView shape of the 2026-08-17 incident: ONE held file deep in
    /// a directory makes the whole directory unmovable — the probe must call
    /// the directory locked while the file is held, free after.
    [Fact]
    public void DirectoryWithOneHeldFile_IsLocked_WholeFreeDirectory_IsNot()
    {
        var dir = Path.Combine(_root, "profile");
        var sub = Path.Combine(dir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(dir, "free.bin"), new byte[8]);
        var held = Path.Combine(sub, "lockfile");
        File.WriteAllBytes(held, new byte[8]);

        using (File.Open(held, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.True(_probe.IsLockedForDelete(dir, Budget()));
        Assert.False(_probe.IsLockedForDelete(dir, Budget()));
    }

    [Fact]
    public void MissingPath_IsNotLocked()
    {
        Assert.False(_probe.IsLockedForDelete(
            Path.Combine(_root, "no-such-thing"), Budget()));
    }

    /// A file opened WITH full sharing (delete included) does not block a
    /// move — the probe must not cry wolf over it.
    [Fact]
    public void FileHeldWithDeleteSharing_IsNotLocked()
    {
        var path = Path.Combine(_root, "shared.bin");
        File.WriteAllBytes(path, new byte[8]);
        using (File.Open(path, FileMode.Open, FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
            Assert.False(_probe.IsLockedForDelete(path, Budget()));
    }

    /// REVIEW ROUND 1 (I1): the raw CreateFileW has no MAX_PATH handling
    /// and the app has no longPathAware manifest, so before the \\?\ prefix
    /// a held file past 260 chars failed with ERROR_PATH_NOT_FOUND and
    /// silently read "not locked" — on exactly the deep WebView2-profile
    /// trees this round targets. Real filesystem: .NET's System.IO builds
    /// the long tree (it prefixes internally), the probe must see the lock.
    [Fact]
    public void LongPath_HeldFileBeyondMaxPath_IsStillDetected()
    {
        var dir = _root;
        while (dir.Length < 300)
            dir = Path.Combine(dir, new string('a', 40));
        Directory.CreateDirectory(dir);
        var held = Path.Combine(dir, "lockfile.bin");
        File.WriteAllBytes(held, new byte[8]);
        Assert.True(held.Length > 260);   // the trap this test exists for

        using (File.Open(held, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.True(_probe.IsLockedForDelete(held, Budget()));
            Assert.True(_probe.IsLockedForDelete(dir, Budget()));
        }
        Assert.False(_probe.IsLockedForDelete(dir, Budget()));
    }

    [Theory]
    [InlineData(@"C:\Users\x\file.bin", @"\\?\C:\Users\x\file.bin")]
    [InlineData(@"\\server\share\f.bin", @"\\?\UNC\server\share\f.bin")]
    [InlineData(@"\\?\C:\already.bin", @"\\?\C:\already.bin")]
    [InlineData(@"relative\path.bin", @"relative\path.bin")]
    public void ExtendedLength_PrefixesRootedPathsOnly(string given, string expected)
    {
        Assert.Equal(expected, DeleteLockProbe.ExtendedLength(given));
    }

    /// REVIEW ROUND 1 (I2): the budget is a hard bound on handle opens —
    /// exhausted means stop probing and assume unlocked (over-promise,
    /// round-10 behavior), never keep opening. A 1-probe budget is spent
    /// on the directory's own handle, so the held file inside goes unseen;
    /// an ample budget finds it.
    [Fact]
    public void ExhaustedBudget_StopsProbing_AssumesUnlocked()
    {
        var dir = Path.Combine(_root, "budgeted");
        Directory.CreateDirectory(dir);
        var held = Path.Combine(dir, "held.bin");
        File.WriteAllBytes(held, new byte[8]);

        using (File.Open(held, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.True(_probe.IsLockedForDelete(dir, new LockProbeBudget(64)));
            Assert.False(_probe.IsLockedForDelete(dir, new LockProbeBudget(1)));
        }
    }

    /// ROUND 15, the defect behind the 2026-08-18 promise (card said
    /// 180 MB, clean delivered 48): the allowance was drained by RECURSION,
    /// not breadth. One deep directory early in %TEMP% spent everything,
    /// and every held top-level file after it — one probe each, and exactly
    /// where the locks turned out to be — was waved through unprobed.
    [Fact]
    public void ADeepDirectory_CannotStarveTheItemsBehindIt()
    {
        var deep = Path.Combine(_root, "deep");
        Directory.CreateDirectory(deep);
        for (var i = 0; i < LockProbeBudget.MaxPerItem * 2; i++)
            File.WriteAllBytes(Path.Combine(deep, $"f{i:D5}.tmp"), new byte[1]);

        var held = Path.Combine(_root, "held-behind.bin");
        File.WriteAllBytes(held, new byte[8]);

        // A target allowance the deep tree would swallow whole if it could.
        var budget = new LockProbeBudget(LockProbeBudget.MaxPerItem + 400);
        using (File.Open(held, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(_probe.IsLockedForDelete(deep, budget));
            // Capped at MaxPerItem, so the cheap check behind it is still
            // affordable — uncapped, this read as unlocked.
            Assert.True(_probe.IsLockedForDelete(held, budget));
            Assert.True(budget.Remaining > 0);
        }
    }

    /// The cap is per ITEM, so a single tree can never spend more than its
    /// share however deep it goes.
    [Fact]
    public void OneTree_SpendsAtMostItsOwnShare()
    {
        var deep = Path.Combine(_root, "wide");
        Directory.CreateDirectory(deep);
        for (var i = 0; i < LockProbeBudget.MaxPerItem * 2; i++)
            File.WriteAllBytes(Path.Combine(deep, $"w{i:D5}.tmp"), new byte[1]);

        var budget = new LockProbeBudget(LockProbeBudget.DefaultPerTarget);
        Assert.False(_probe.IsLockedForDelete(deep, budget));
        Assert.True(LockProbeBudget.DefaultPerTarget - budget.Remaining
            <= LockProbeBudget.MaxPerItem);
    }

    /// ROUND 15 review (I1): the cap has to clear a REAL profile tree, or a
    /// "free" verdict is truncation wearing evidence's clothes. Both
    /// WebView2 profiles on the reporting machine are 384 and 985 entries
    /// deep, and at MaxPerItem = 64 both stopped at their 64th and reported
    /// free — the exact 2026-08-17 shape the probe exists to catch, which
    /// rounds 11-14 caught because such a tree fits the shared allowance.
    ///
    /// The filler is sized ABOVE the larger of those two (re-review minor
    /// 9): at 600 this pin still passed with a cap of ~700, while the
    /// 985-entry profile re-truncated in silence. It pins the cap's EDGE.
    [Fact]
    public void AHeldFile_DeepInAProfileSizedTree_IsStillFound()
    {
        var profile = Path.Combine(_root, "EBWebView");
        Directory.CreateDirectory(profile);
        // Filler that sorts BEFORE the held file, so the walk must get past
        // more entries than the largest real profile that produced I1.
        for (var i = 0; i < 1000; i++)
            File.WriteAllBytes(Path.Combine(profile, $"a{i:D5}.dat"), new byte[1]);
        var held = Path.Combine(profile, "zzz-LOCK");
        File.WriteAllBytes(held, new byte[8]);

        using (File.Open(held, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.True(_probe.IsLockedForDelete(profile, Budget()));
        Assert.False(_probe.IsLockedForDelete(profile, Budget()));
    }

    /// Re-review minor 12: the two constants bound different things — time
    /// per target, and verification depth per tree — but if the per-item
    /// share ever exceeded the target's whole allowance the cap would stop
    /// binding and I1 would evaporate without a test noticing.
    [Fact]
    public void ThePerItemShare_NeverExceedsTheTargetAllowance()
    {
        Assert.True(LockProbeBudget.MaxPerItem <= LockProbeBudget.DefaultPerTarget);
    }

    [Fact]
    public void Budget_CountsDownAndRefuses()
    {
        var budget = new LockProbeBudget(2);
        Assert.True(budget.TryTake());
        Assert.True(budget.TryTake());
        Assert.False(budget.TryTake());
        Assert.Equal(0, budget.Remaining);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
