using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

file sealed class CollectingProgress : IProgress<ScanProgress>
{
    public ConcurrentBag<ScanProgress> Reports { get; } = new();
    public void Report(ScanProgress value) => Reports.Add(value);
}

file sealed class NoProcesses : IProcessLister
{
    public bool IsRunning(string processName) => false;
}

public sealed class ScannerProgressTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-prog-").FullName;

    private CleanupTarget Target(string id)
    {
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        return new CleanupTarget(id, id, CleanupLevel.Safe,
            new List<string> { dir }, "Test");
    }

    [Fact]
    public void ReportsOncePerTarget_WithStableTotal()
    {
        var targets = new[] { Target("t1"), Target("t2"), Target("t3") };
        var progress = new CollectingProgress();
        new Scanner(targets, new NoProcesses()).Scan(default, progress);

        Assert.Equal(3, progress.Reports.Count);
        Assert.All(progress.Reports, r => Assert.Equal(3, r.Total));
        Assert.Equal(new[] { 1, 2, 3 },
            progress.Reports.Select(r => r.Completed).OrderBy(c => c).ToArray());
        Assert.Equal(new[] { "t1", "t2", "t3" },
            progress.Reports.Select(r => r.TargetId).OrderBy(t => t).ToArray());
    }

    [Fact]
    public void NullProgress_StillScans()
    {
        var result = new Scanner(new[] { Target("t1") }, new NoProcesses()).Scan();
        Assert.Single(result.Targets);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
