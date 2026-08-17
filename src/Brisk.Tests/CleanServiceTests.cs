using System.Collections.Generic;
using System.Linq;
using Brisk.Services;
using BriskEngine.Cleaning;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

public class CleanServiceTests
{
    [Fact]
    public void CleanTargets_CollectsRecycledPathsAndBytes()
    {
        var host = new FakeEngineHost();
        var service = new CleanService(host, new Settings());
        var outcome = service.CleanTargets(new[]
        {
            TestData.Target("user-temp", CleanupLevel.Safe, 2048),
            TestData.Target("chrome-cache", CleanupLevel.Safe, 1024),
        });

        Assert.Equal(2, outcome.RecycledPaths.Count);
        Assert.Equal(3072, outcome.RecycledBytes);
        Assert.Empty(outcome.Problems);
        Assert.False(outcome.WasDryRun);
        Assert.All(host.Cleans, c => Assert.False(c.DryRun));
    }

    [Fact]
    public void CleanTargets_DryRunSetting_PassesThrough_NothingRecycled()
    {
        var host = new FakeEngineHost();
        var service = new CleanService(host, new Settings { DryRun = true });
        var outcome = service.CleanTargets(new[]
            { TestData.Target("user-temp", CleanupLevel.Safe, 2048) });

        Assert.True(outcome.WasDryRun);
        Assert.Empty(outcome.RecycledPaths);
        Assert.All(host.Cleans, c => Assert.True(c.DryRun));
    }

    /// ROUND 11: the one honest figure every GUI surface promises — safe
    /// defaults only, minus delete-locked items, minus app-held targets.
    [Fact]
    public void ReclaimableNowBytes_CountsOnlyWhatTheCleanCanTakeRightNow()
    {
        var scan = new ScanResult(new[]
        {
            TestData.Target("user-temp", CleanupLevel.Safe, 2048, lockedBytes: 4096),
            TestData.Target("whatsapp-cache", CleanupLevel.Safe, 310L << 20,
                skipped: "WhatsApp is running — close it to include this target",
                app: "WhatsApp|WhatsApp.Root"),
            TestData.Target("npm-cache", CleanupLevel.Developer, 1024),
            TestData.Target("old-installers", CleanupLevel.Deep, 512, pick: true),
        });

        Assert.Equal(2048, CleanService.ReclaimableNowBytes(scan));
        // the app-held classifier sees exactly the skipped safe target
        Assert.Equal(new[] { "whatsapp-cache" }, scan.Targets
            .Where(CleanService.IsAppHeld).Select(t => t.Target.Id));
    }

    [Fact]
    public void CleanTargets_CollectsRefusalsAndErrors_AsProblems()
    {
        var host = new FakeEngineHost
        {
            OnClean = (scan, _) => new CleanReport(new List<CleanEntry>
            {
                new(scan.Target.Id, @"C:\x\a", 0, "refused", "requires administrator"),
                new(scan.Target.Id, @"C:\x\b", 512, "recycled"),
                new(scan.Target.Id, @"C:\x\c", 0, "error", "locked"),
            }),
        };
        var outcome = new CleanService(host, new Settings())
            .CleanTargets(new[] { TestData.Target("windows-temp", CleanupLevel.Deep, 512) });

        Assert.Single(outcome.RecycledPaths);
        Assert.Equal(2, outcome.Problems.Count);
        Assert.Contains(outcome.Problems, p => p.Contains("administrator"));
    }
}
