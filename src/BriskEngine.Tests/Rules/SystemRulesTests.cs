using System;
using System.IO;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class SystemRulesTests
{
    private const string FxKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string SenseKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy";

    [Fact]
    public void Thermals_Hot_Finds_NullSensors_Silent()
    {
        var ctx = TestContext.Empty();
        var sensors = (FakeSensors)ctx.Sensors;
        Assert.Null(new ThermalsRule().Detect(ctx));       // null temps
        sensors.CpuTemp = 88;
        Assert.NotNull(new ThermalsRule().Detect(ctx));
    }

    [Fact]
    public void VisualEffects_AppearanceMode_FixesToPerformance_AndUndoes()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetInt(FxKey, "VisualFXSetting", 1);
        var rule = new VisualEffectsRule();
        Assert.NotNull(rule.Detect(ctx));
        var prior = rule.Fix(ctx);
        Assert.Equal(2, reg.GetInt(FxKey, "VisualFXSetting"));
        rule.Undo(ctx, prior);
        Assert.Equal(1, reg.GetInt(FxKey, "VisualFXSetting"));
    }

    [Fact]
    public void StorageSense_LowDiskAndOff_Finds()
    {
        var ctx = TestContext.Empty();
        var disk = (FakeDisk)ctx.Disk;
        disk.Free = 50L << 30; disk.Total = 1000L << 30;   // 5% free
        Assert.NotNull(new StorageSenseRule().Detect(ctx));
        ((FakeRegistry)ctx.Registry).SetInt(SenseKey, "01", 1);
        Assert.Null(new StorageSenseRule().Detect(ctx));
    }

    [Fact]
    public void DiskForecast_ShrinkingDisk_Finds()
    {
        var ctx = TestContext.Empty();
        var disk = (FakeDisk)ctx.Disk;
        disk.Free = 40L << 30;
        var history = Path.Combine(ctx.DataDirectory, "disk-history.jsonl");
        File.WriteAllLines(history, new[]
        {
            $"{{\"ts\":\"{DateTime.UtcNow.AddDays(-14):O}\",\"free\":{100L << 30}}}",
            $"{{\"ts\":\"{DateTime.UtcNow.AddDays(-7):O}\",\"free\":{70L << 30}}}",
        });
        var finding = new DiskForecastRule().Detect(ctx);   // appends today's 40 GB sample
        Assert.NotNull(finding);
        Assert.Contains("days", finding!.Evidence);
    }

    [Fact]
    public void DiskForecast_StableDisk_Null()
    {
        var ctx = TestContext.Empty();
        Assert.Null(new DiskForecastRule().Detect(ctx));    // one sample only
    }

    [Fact]
    public void Registry_HasTwelveRules_WithUniqueIds()
    {
        var all = DiagnosticRuleRegistry.All;
        Assert.Equal(12, all.Count);
        Assert.Equal(12, System.Linq.Enumerable.Count(
            System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Select(all, r => r.Id))));
    }
}
