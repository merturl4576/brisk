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

    [Fact]
    public void HeldFile_IsLocked_ReleasedFile_IsNot()
    {
        var path = Path.Combine(_root, "held.bin");
        File.WriteAllBytes(path, new byte[8]);
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.True(_probe.IsLockedForDelete(path));
        Assert.False(_probe.IsLockedForDelete(path));
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
            Assert.True(_probe.IsLockedForDelete(dir));
        Assert.False(_probe.IsLockedForDelete(dir));
    }

    [Fact]
    public void MissingPath_IsNotLocked()
    {
        Assert.False(_probe.IsLockedForDelete(Path.Combine(_root, "no-such-thing")));
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
            Assert.False(_probe.IsLockedForDelete(path));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
