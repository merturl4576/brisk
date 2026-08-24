using System;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Models;
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
        // A measured fact about what is running right now, with nothing brisk
        // can do about it — so it reports, and does not charge for it.
        Assert.Equal(FindingKind.Notice, finding.Kind);
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

        // The engine's English is not a fallback nobody reads: the CLI prints
        // Evidence verbatim and the GUI falls back to it whenever a resx key
        // is missing. Pinning only the key let a rewrite into "Memory integrity
        // is on, so Windows blocked the driver — turn it off in Windows
        // Security" pass the whole suite, which is a stated cause brisk never
        // read plus the one imperative this rule exists to refuse.
        Assert.Contains("Usually", finding.Evidence);
        Assert.Contains("cannot confirm from here", finding.Evidence);
        foreach (var order in new[] { "turn off", "turn it off", "switch it off",
                                      "disable", "Windows Security", "you should" })
            Assert.DoesNotContain(order, finding.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    /// The case the three tests around this one lean on never reaching a
    /// template: nothing read means no finding, so a note has nothing to
    /// qualify. Thresholds pinned in the same place — one degree under each is
    /// silence, which is what makes "hot" mean anything. Lived in
    /// SystemRulesTests until Task 5; ThermalsRule is an AdviseRuleBase and
    /// its coverage belongs in one file.
    [Fact]
    public void Thermals_NothingRead_OrNothingHot_Silent()
    {
        var ctx = TestContext.Empty();
        var sensors = (FakeSensors)ctx.Sensors;
        Assert.Null(new ThermalsRule().Detect(ctx));        // neither sensor read

        sensors.GpuTemp = 69;                               // GPU fires at 70
        Assert.Null(new ThermalsRule().Detect(ctx));

        sensors.GpuTemp = null;
        sensors.CpuTemp = 74;                               // CPU fires at 75
        Assert.Null(new ThermalsRule().Detect(ctx));
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
        foreach (var cause in new[] { "blocklist", "WinRing0", "driver",
                                      "memory integrity", "Core Isolation" })
            Assert.DoesNotContain(cause, finding.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    /// A present-but-silent sensor reports NaN, which is not a reading: it
    /// fails every threshold so nothing calls it hot, but it is not null
    /// either, so it used to print "CPU NaN°C" and take the both-read
    /// template — a template outrunning what was read, in the one rule whose
    /// whole subject is that.
    [Fact]
    public void Thermals_NaNReading_CountsAsUnread()
    {
        var ctx = TestContext.Empty();
        var sensors = (FakeSensors)ctx.Sensors;
        sensors.CpuTemp = double.NaN;
        sensors.GpuTemp = 78;
        var finding = new ThermalsRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Equal("rule.thermals.evidence.cpu-unread", finding!.EvidenceKey);
        Assert.Equal("GPU 78°C", Assert.Single(finding.EvidenceArgs!));
        Assert.DoesNotContain("NaN", finding.Evidence);
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
        // A temperature reading, not a fault brisk found: the advice is fans
        // and thermal paste, which is not something a score should demand.
        Assert.Equal(FindingKind.Notice, finding.Kind);
    }

    /// Memory integrity is readable without a driver, so the one explanation
    /// this rule offers can now be checked against the machine it is offered
    /// to. With memory integrity OFF, a driver Windows refuses to load while
    /// memory integrity is on is not why the CPU went unread — and brisk was
    /// handing that sentence to every unread CPU regardless.
    [Fact]
    public void Thermals_CpuUnread_MemoryIntegrityOff_DropsTheBlocklistReason()
    {
        var ctx = TestContext.Empty();
        ((FakeSensors)ctx.Sensors).GpuTemp = 78;            // hot; CPU stays unread
        ((FakeMemoryIntegrity)ctx.MemoryIntegrity).On = false;

        var finding = new ThermalsRule().Detect(ctx);

        Assert.NotNull(finding);
        Assert.Equal("rule.thermals.evidence.cpu-unread.integrity-off", finding!.EvidenceKey);
        Assert.Contains("could not read", finding.Evidence);
        foreach (var cause in new[] { "blocklist", "WinRing0" })
            Assert.DoesNotContain(cause, finding.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    /// Measured on, so the sentence stops hedging about whether memory
    /// integrity is on — and keeps hedging about whether that is the whole
    /// reason, because an unsupported chip and a probe that threw look the
    /// same from here.
    [Fact]
    public void Thermals_CpuUnread_MemoryIntegrityOn_StatesItAsMeasuredWithoutClaimingProof()
    {
        var ctx = TestContext.Empty();
        ((FakeSensors)ctx.Sensors).GpuTemp = 78;
        ((FakeMemoryIntegrity)ctx.MemoryIntegrity).On = true;

        var finding = new ThermalsRule().Detect(ctx);

        Assert.NotNull(finding);
        Assert.Equal("rule.thermals.evidence.cpu-unread.integrity-on", finding!.EvidenceKey);
        Assert.Contains("memory integrity is on", finding.Evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot", finding.Evidence, StringComparison.OrdinalIgnoreCase);
        foreach (var order in new[] { "turn off", "turn it off", "switch it off",
                                      "disable", "Windows Security", "you should" })
            Assert.DoesNotContain(order, finding.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    /// Unknown stays hedged. A machine whose Device Guard query failed must
    /// not be told either story, and null is the default a test that says
    /// nothing about memory integrity gets.
    [Fact]
    public void Thermals_CpuUnread_MemoryIntegrityUnknown_KeepsTheHedgedReason()
    {
        var ctx = TestContext.Empty();
        ((FakeSensors)ctx.Sensors).GpuTemp = 78;

        var finding = new ThermalsRule().Detect(ctx);

        Assert.NotNull(finding);
        Assert.Equal("rule.thermals.evidence.cpu-unread", finding!.EvidenceKey);
        Assert.Contains("cannot confirm from here", finding.Evidence);
    }

    /// 71 GB in Local, 25 GB in Roaming — the headline is the largest
    /// over-threshold folder, and Fmt.Bytes keeps its invariant formatting.
    [Fact]
    public void DiskBreakdown_Headline_IsTheLargestOverThresholdFolder()
    {
        var ctx = TestContext.Empty();
        var files = (FakeFiles)ctx.Files;
        files.Sizes[PathExpander.Expand("%LOCALAPPDATA%")!] = 71L << 30;
        files.Sizes[PathExpander.Expand("%APPDATA%")!] = 25L << 30;

        var h = new DiskBreakdownRule().Detect(ctx)!.Headline;

        Assert.NotNull(h);
        Assert.Equal("71.0 GB", h!.Value);
        Assert.Equal("AppData\\Local — the largest measured folder", h.Caption);
        Assert.Equal("rule.disk-breakdown.headline.value", h.ValueKey);
        Assert.Equal(new[] { "71.0 GB" }, h.ValueArgs);
        Assert.Equal("rule.disk-breakdown.headline.caption", h.CaptionKey);
        Assert.Equal(new[] { "AppData\\Local" }, h.CaptionArgs);
    }

    /// 45 GB in Local (under its 50 GB threshold) and 12 GB in Downloads
    /// (over its 10 GB threshold): the finding fires because of Downloads,
    /// but the caption promises "the largest measured folder" — so the
    /// headline must be Local, threshold or not.
    [Fact]
    public void DiskBreakdown_Headline_IsTheLargestFolder_EvenUnderItsOwnThreshold()
    {
        var ctx = TestContext.Empty();
        var files = (FakeFiles)ctx.Files;
        files.Sizes[PathExpander.Expand("%LOCALAPPDATA%")!] = 45L << 30;
        files.Sizes[PathExpander.Expand(@"%USERPROFILE%\Downloads")!] = 12L << 30;

        var finding = new DiskBreakdownRule().Detect(ctx);

        Assert.NotNull(finding);
        var h = finding!.Headline;
        Assert.NotNull(h);
        Assert.Equal("45.0 GB", h!.Value);
        Assert.Equal(new[] { "45.0 GB" }, h.ValueArgs);
        Assert.Equal(new[] { "AppData\\Local" }, h.CaptionArgs);
    }
}
