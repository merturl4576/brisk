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
