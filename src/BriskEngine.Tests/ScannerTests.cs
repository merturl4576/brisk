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
    public void RunningApp_SkipsTarget()
    {
        var dir = Path.Combine(_root, "appcache");
        Directory.CreateDirectory(dir);
        _processes.Running.Add("chrome");
        var result = new Scanner(new[] { Target("t2", dir, app: "chrome") }, _processes).Scan();
        var t = result.Targets.Single();
        Assert.NotNull(t.SkippedReason);
        Assert.Empty(t.Items);
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
