using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Models;
using BriskEngine.Safety;
using Xunit;

namespace BriskEngine.Tests;

public sealed class ScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-scan-").FullName;
    private readonly FakeRunningApps _processes = new();

    private CleanupTarget Target(string id, string template, string? app = null,
        bool pick = false) => new(
        id, id, CleanupLevel.Safe, new List<string> { template }, "Test",
        RequiresAppClosedProcess: app, RequiresIndividualSelection: pick);

    private static CleanupTarget ContentsOnlyTarget(string id, string template) => new(
        id, id, CleanupLevel.Safe, new List<string> { template }, "Test",
        DeletesContentsNotDirectory: true);

    [Fact]
    public void ResolvesItemsWithSizes()
    {
        var dir = Path.Combine(_root, "cache");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "x.bin"), new byte[42]);
        var result = new Scanner(new[] { Target("t1", dir) }, _processes).Scan();
        var t = result.Targets.Single();
        Assert.Null(t.SkippedReason);
        Assert.Equal(42, t.TotalBytes);
    }

    [Fact]
    public void RunningApp_SkipsTarget_ButStillSizesIt()
    {
        var dir = Path.Combine(_root, "appcache");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "held.bin"), new byte[64]);
        _processes.Running.Add("chrome");
        var result = new Scanner(new[] { Target("t2", dir, app: "chrome") }, _processes).Scan();
        var t = result.Targets.Single();
        Assert.NotNull(t.SkippedReason);
        // Round 11: the skipped target is still SIZED (so the GUI can say
        // "+64 B when you close chrome") but promises NOTHING.
        Assert.Equal(64, t.TotalBytes);
        Assert.Equal(0, t.ReclaimableBytes);
        Assert.Equal(64, t.BlockedBytes);
    }

    /// REGRESSION PIN — the 2026-08-17 promise bug: modern WhatsApp Desktop
    /// runs as "WhatsApp.Root", the registry excluded on "WhatsApp" only, so
    /// the exclusion never fired and a locked 310 MB cache entered the
    /// promise. ANY '|'-separated candidate must count as running, and the
    /// skip reason must name the app in its human form.
    [Fact]
    public void RunningApp_SecondaryProcessName_AlsoSkips()
    {
        var dir = Path.Combine(_root, "whatsapp");
        Directory.CreateDirectory(dir);
        _processes.Running.Add("WhatsApp.Root");
        var result = new Scanner(
            new[] { Target("t-wa", dir, app: "WhatsApp|WhatsApp.Root") }, _processes).Scan();
        var t = result.Targets.Single();
        Assert.NotNull(t.SkippedReason);
        Assert.StartsWith("WhatsApp is running", t.SkippedReason);
        Assert.Equal(0, t.ReclaimableBytes);
    }

    /// Round 11 honest total: a delete-locked item stays on the shelf (the
    /// clean still attempts it) but leaves the promise.
    [Fact]
    public void LockedItem_StaysInItems_ButLeavesThePromise()
    {
        var dir = Path.Combine(_root, "lockedcache");
        Directory.CreateDirectory(dir);
        var free = Path.Combine(dir, "free.bin");
        var held = Path.Combine(dir, "held.bin");
        File.WriteAllBytes(free, new byte[10]);
        File.WriteAllBytes(held, new byte[30]);
        var probe = new FakeLockProbe();
        probe.LockedPaths.Add(held);

        var result = new Scanner(
            new[] { ContentsOnlyTarget("t-lock", dir) }, _processes, probe).Scan();
        var t = result.Targets.Single();

        Assert.Equal(2, t.Items.Count);
        Assert.Equal(40, t.TotalBytes);
        Assert.Equal(10, t.ReclaimableBytes);
        Assert.Equal(30, t.BlockedBytes);
        Assert.True(t.Items.Single(i => i.Path == held).Locked);
        Assert.False(t.Items.Single(i => i.Path == free).Locked);
    }

    [Fact]
    public void OldInstallers_FiltersByAge()
    {
        var downloads = Path.Combine(_root, "Downloads");
        Directory.CreateDirectory(downloads);
        var old = Path.Combine(downloads, "old-setup.exe");
        var fresh = Path.Combine(downloads, "fresh-setup.exe");
        File.WriteAllBytes(old, new byte[10]);
        File.WriteAllBytes(fresh, new byte[10]);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-45));
        var target = new CleanupTarget("old-installers", "Old installers", CleanupLevel.Deep,
            new List<string> { Path.Combine(downloads, "*.exe") }, "Downloads",
            RequiresIndividualSelection: true);
        var result = new Scanner(new[] { target }, _processes).Scan();
        var item = result.Targets.Single().Items.Single();
        Assert.EndsWith("old-setup.exe", item.Path);
    }

    [Fact]
    public void VanishedFile_DoesNotKillScan()
    {
        var testdir = Path.Combine(_root, "testdir");
        Directory.CreateDirectory(testdir);
        File.WriteAllBytes(Path.Combine(testdir, "good.bin"), new byte[42]);

        // Create a dangling junction: point to a directory, then delete it
        var tempdir = Path.Combine(_root, "tempdir");
        Directory.CreateDirectory(tempdir);
        var junction = Path.Combine(testdir, "dangling");
        var p = Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c mklink /J \"{junction}\" \"{tempdir}\"")
        { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        Directory.Delete(tempdir);

        // Scan should complete without throwing, counting only the good file
        var result = new Scanner(new[] { Target("t1", testdir) }, _processes).Scan();
        var t = result.Targets.Single();
        Assert.Null(t.SkippedReason);
        Assert.Equal(42, t.TotalBytes);
    }

    [Fact]
    public void ContentsOnlyTarget_EmitsChildrenNotTheDirectory()
    {
        var dir = Path.Combine(_root, "usertemp");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "a.tmp"), new byte[10]);
        File.WriteAllBytes(Path.Combine(dir, "b.tmp"), new byte[20]);
        var subdir = Path.Combine(dir, "subdir");
        Directory.CreateDirectory(subdir);
        File.WriteAllBytes(Path.Combine(subdir, "c.tmp"), new byte[30]);

        var result = new Scanner(new[] { ContentsOnlyTarget("t-contents", dir) }, _processes).Scan();
        var t = result.Targets.Single();

        Assert.Null(t.SkippedReason);
        Assert.Equal(3, t.Items.Count);
        Assert.All(t.Items, item => Assert.NotEqual(dir, item.Path, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(60, t.TotalBytes);
    }

    [Fact]
    public void ContentsOnlyItems_PassTheValidator()
    {
        var dir = Path.Combine(_root, "usertemp2");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "a.tmp"), new byte[10]);

        var target = ContentsOnlyTarget("t-contents2", dir);
        var result = new Scanner(new[] { target }, _processes).Scan();
        var t = result.Targets.Single();
        Assert.NotEmpty(t.Items);

        var validator = new SafetyValidator();
        foreach (var item in t.Items)
        {
            var authorization = validator.Authorize(item.Path, target);
            Assert.True(authorization.Allowed, $"'{item.Path}' should be allowed: {authorization.Reason}");
        }
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}

public sealed class FakeLockProbe : ILockProbe
{
    public HashSet<string> LockedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsLockedForDelete(string path, System.Threading.CancellationToken ct = default)
        => LockedPaths.Contains(path);
}
