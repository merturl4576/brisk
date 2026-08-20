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
    public void OrphanedData_InstalledOnlyUnderHkcuUninstall_Null()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        var files = (FakeFiles)ctx.Files;
        files.Sizes[PathExpander.Expand(@"%LOCALAPPDATA%\JetBrains")!] = 3L << 30;
        // Per-user install (e.g. JetBrains Toolbox) — only the HKCU uninstall hive has it.
        const string uninstall = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        reg.SubKeys[uninstall] = new() { "JetBrains" };
        reg.SetString($@"{uninstall}\JetBrains", "DisplayName", "JetBrains Toolbox");
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

    /// TASK 5. Wave 1's elevation manifest was justified by CPU temperature and
    /// does not deliver it: LibreHardwareMonitor reads CPU temps through the
    /// WinRing0 kernel driver, which sits on Microsoft's vulnerable-driver
    /// blocklist, so a machine running memory integrity refuses to load it at
    /// any privilege level. GPU temperature reads with no elevation at all.
    /// On a default Windows 11 this finding is therefore permanently GPU-only,
    /// and one number with nothing beside it reads as the whole machine.
    [Fact]
    public void Thermals_CpuUnread_SaysSo_AndStillNamesWhatItRead()
    {
        var ctx = TestContext.Empty();
        var sensors = (FakeSensors)ctx.Sensors;
        sensors.GpuTemp = 78;                       // hot; CPU stays unread
        var finding = new ThermalsRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Equal("rule.thermals.evidence.cpu-unread", finding!.EvidenceKey);
        Assert.Equal("GPU 78°C", Assert.Single(finding.EvidenceArgs!));
        Assert.Contains("CPU", finding.Evidence);
        Assert.Contains("could not read", finding.Evidence);
    }

    /// The mirror case has the same defect and a different honest answer: a
    /// missing GPU reading has no cause brisk knows, so the note names none.
    /// Borrowing the CPU sentence here would assert a blocklisted driver as the
    /// reason a GPU sensor is silent, which is not a thing that happens.
    [Fact]
    public void Thermals_GpuUnread_SaysSo_WithoutInventingAReason()
    {
        var ctx = TestContext.Empty();
        var sensors = (FakeSensors)ctx.Sensors;
        sensors.CpuTemp = 88;                       // hot; GPU stays unread
        var finding = new ThermalsRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Equal("rule.thermals.evidence.gpu-unread", finding!.EvidenceKey);
        Assert.Equal("CPU 88°C", Assert.Single(finding.EvidenceArgs!));
        Assert.Contains("GPU", finding.Evidence);
        Assert.Contains("could not read", finding.Evidence);
        Assert.DoesNotContain("blocklist", finding.Evidence);
    }

    /// And when both answered, the note must not appear — a permanent "some of
    /// this was not read" would train the reader to skip the sentence on the
    /// machines where it is true.
    [Fact]
    public void Thermals_BothSensorsAnswer_NoUnreadNote()
    {
        var ctx = TestContext.Empty();
        var sensors = (FakeSensors)ctx.Sensors;
        sensors.CpuTemp = 88;
        sensors.GpuTemp = 78;
        var finding = new ThermalsRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Equal("rule.thermals.evidence", finding!.EvidenceKey);
        Assert.Equal("CPU 88°C, GPU 78°C", Assert.Single(finding.EvidenceArgs!));
        Assert.DoesNotContain("could not read", finding.Evidence);
    }
}
