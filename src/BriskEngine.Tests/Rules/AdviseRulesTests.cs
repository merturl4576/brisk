using System;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Paths;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class AdviseRulesTests
{
    [Fact]
    public void RamPressure_HighLoad_Finds_AndFixRefused()
    {
        var ctx = TestContext.Empty();
        var procs = (FakeProcessInfo)ctx.Processes;
        procs.MemoryLoad = 91;
        procs.Top.Add(("chrome", 900L << 20));
        var rule = new RamPressureRule();
        var finding = rule.Detect(ctx);
        Assert.NotNull(finding);
        Assert.False(finding!.CanFix);
        Assert.Throws<InvalidOperationException>(() => rule.Fix(ctx));
    }

    [Fact]
    public void RamPressure_NormalLoad_Null()
    {
        Assert.Null(new RamPressureRule().Detect(TestContext.Empty()));
    }

    [Fact]
    public void DiskBreakdown_BloatedLocalAppData_Finds()
    {
        var ctx = TestContext.Empty();
        var files = (FakeFiles)ctx.Files;
        files.Sizes[PathExpander.Expand("%LOCALAPPDATA%")!] = 71L << 30;
        var finding = new DiskBreakdownRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Contains("AppData", finding!.Evidence);
    }

    [Fact]
    public void DiskBreakdown_DistinguishesLocalFromRoaming()
    {
        var ctx = TestContext.Empty();
        var files = (FakeFiles)ctx.Files;
        files.Sizes[PathExpander.Expand("%LOCALAPPDATA%")!] = 71L << 30;
        files.Sizes[PathExpander.Expand("%APPDATA%")!] = 25L << 30;
        var finding = new DiskBreakdownRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Contains("AppData\\Local", finding!.Evidence);
        Assert.Contains("AppData\\Roaming", finding!.Evidence);
    }

    [Fact]
    public void OrphanedData_UninstalledDocker_WithBigData_Finds()
    {
        var ctx = TestContext.Empty();
        var files = (FakeFiles)ctx.Files;
        files.Sizes[PathExpander.Expand(@"%LOCALAPPDATA%\Docker")!] = 3L << 30;
        // registry has no uninstall entries at all -> Docker not installed
        var finding = new OrphanedDataRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Contains("Docker", finding!.Evidence);
    }

    [Fact]
    public void OrphanedData_InstalledDocker_Null()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        var files = (FakeFiles)ctx.Files;
        files.Sizes[PathExpander.Expand(@"%LOCALAPPDATA%\Docker")!] = 3L << 30;
        const string uninstall = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        reg.SubKeys[uninstall] = new() { "Docker" };
        reg.SetString($@"{uninstall}\Docker", "DisplayName", "Docker Desktop 4.30");
        Assert.Null(new OrphanedDataRule().Detect(ctx));
    }

    [Fact]
    public void StaleDevCaches_OldBigNpmCache_Finds()
    {
        var ctx = TestContext.Empty();
        var files = (FakeFiles)ctx.Files;
        var npm = PathExpander.Expand(@"%LOCALAPPDATA%\npm-cache")!;
        files.Sizes[npm] = 2L << 30;
        files.NewestWrites[npm] = DateTime.UtcNow.AddDays(-90);
        var finding = new StaleDevCachesRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Contains("npm", finding!.Evidence);
    }
}
