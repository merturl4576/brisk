using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Logging;
using BriskEngine.Models;
using BriskEngine.Safety;
using Xunit;

namespace BriskEngine.Tests;

public sealed class PermanentCleanTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-perm-").FullName;
    private readonly FakeRecycler _recycler = new();

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private CleanRunner Runner() => new(new SafetyValidator(), _recycler,
        new ActionLog(Path.Combine(_root, "log.jsonl")), new FakeRunner(), () => false,
        lockProbe: null, systemDriveFreeBytes: () => 0, deleter: new WindowsDeleter());

    private TargetScanResult ScanOver(string dir, params (string Name, int Bytes)[] entries)
    {
        Directory.CreateDirectory(dir);
        var items = new List<ResolvedItem>();
        foreach (var (name, bytes) in entries)
        {
            var p = Path.Combine(dir, name);
            if (name.EndsWith('\\'))
            {
                Directory.CreateDirectory(p);
                File.WriteAllBytes(Path.Combine(p, "inner.bin"), new byte[bytes]);
                items.Add(new ResolvedItem("t", p.TrimEnd('\\'), bytes, DateTime.UtcNow));
            }
            else
            {
                File.WriteAllBytes(p, new byte[bytes]);
                items.Add(new ResolvedItem("t", p, bytes, DateTime.UtcNow));
            }
        }
        var target = new CleanupTarget("t", "T", CleanupLevel.Safe, new List<string> { dir }, "Test",
            DeletesContentsNotDirectory: true, Regenerates: true);
        return new TargetScanResult(target, items, null);
    }

    [Fact]
    public void Permanent_DeletesFilesAndFolders_WithoutTheBin()
    {
        var dir = Path.Combine(_root, "cache");
        var scan = ScanOver(dir, ("a.tmp", 10), ("sub\\", 30));

        var report = Runner().Clean(scan, dryRun: false, permanent: true);

        Assert.Empty(_recycler.Recycled);
        Assert.Equal(40, report.DeletedBytes);
        Assert.All(report.Entries, e => Assert.Equal("deleted", e.Action));
        Assert.Empty(Directory.EnumerateFileSystemEntries(dir));
    }

    [Fact]
    public void Permanent_DeletesAReadOnlyFile()
    {
        var dir = Path.Combine(_root, "ro");
        var scan = ScanOver(dir, ("ro.bin", 10));
        File.SetAttributes(scan.Items[0].Path, FileAttributes.ReadOnly);

        var report = Runner().Clean(scan, dryRun: false, permanent: true);

        Assert.Equal(10, report.DeletedBytes);
        Assert.False(File.Exists(scan.Items[0].Path));
    }

    [Fact]
    public void Permanent_ALockedFileInsideAFolder_IsLeft_AndOnlyWhatWentIsCounted()
    {
        var dir = Path.Combine(_root, "locked");
        var scan = ScanOver(dir, ("sub\\", 30));
        var keep = Path.Combine(scan.Items[0].Path, "held.bin");
        File.WriteAllBytes(keep, new byte[20]);
        var withHeld = scan with { Items = new[] { scan.Items[0] with { Bytes = 50 } } };

        using (new FileStream(keep, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var report = Runner().Clean(withHeld, dryRun: false, permanent: true);
            var entry = Assert.Single(report.Entries);
            Assert.Equal("deleted", entry.Action);
            Assert.Equal(30, entry.Bytes);
            Assert.NotNull(entry.Reason);
        }
        Assert.True(File.Exists(keep));
    }

    [Fact]
    public void Permanent_AnItemOutsideTheTarget_IsRefused()
    {
        var dir = Path.Combine(_root, "allowed");
        var scan = ScanOver(dir, ("a.tmp", 10));
        var outside = Path.Combine(_root, "outside.tmp");
        File.WriteAllBytes(outside, new byte[10]);
        var rogue = scan with { Items = new[] { new ResolvedItem("t", outside, 10, DateTime.UtcNow) } };

        var report = Runner().Clean(rogue, dryRun: false, permanent: true);

        Assert.Equal("refused", Assert.Single(report.Entries).Action);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void NotPermanent_StillRecycles()
    {
        var scan = ScanOver(Path.Combine(_root, "bin"), ("a.tmp", 10));
        var report = Runner().Clean(scan, dryRun: false);
        Assert.Single(_recycler.Recycled);
        Assert.Equal(10, report.RecycledBytes);
        Assert.Equal(0, report.DeletedBytes);
    }
}
