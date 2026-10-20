using System.Linq;
using BriskEngine.Actions.Switches;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

public sealed class SwitchRuleTests
{
    private static (SwitchRule Rule, FakeRegistry Reg, DiagnosticContext Ctx) Make(SwitchSpec spec)
    {
        var ctx = TestContext.Empty();
        return (new SwitchRule(spec), (FakeRegistry)ctx.Registry, ctx);
    }

    [Fact]
    public void AbsentValueDeclaredOff_IsNotOn_AndIsNeverCreated()
    {
        var (rule, reg, ctx) = Make(SwitchCatalog.GpuScheduling);
        Assert.False(rule.IsOn(ctx));
        Assert.Null(rule.Detect(ctx));
        rule.Fix(ctx);
        Assert.Null(reg.GetInt(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"));
    }

    [Fact]
    public void GpuScheduling_RecordedOff_IsOn_FixTurnsItOn_UndoRestores()
    {
        var (rule, reg, ctx) = Make(SwitchCatalog.GpuScheduling);
        const string key = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
        reg.SetInt(key, "HwSchMode", 1);
        Assert.NotNull(rule.Detect(ctx));

        var prior = rule.Fix(ctx);
        Assert.Equal(2, reg.GetInt(key, "HwSchMode"));
        Assert.False(rule.IsOn(ctx));

        rule.Undo(ctx, prior);
        Assert.Equal(1, reg.GetInt(key, "HwSchMode"));
    }

    [Fact]
    public void Fix_WritesOnlyTheValuesThatReadAsOn()
    {
        var (rule, reg, ctx) = Make(SwitchCatalog.TelemetryServices);
        const string dmw = @"HKLM\SYSTEM\CurrentControlSet\Services\dmwappushservice";
        reg.SetInt(dmw, "Start", 3);   // DiagTrack absent: no such service here

        var prior = rule.Fix(ctx);

        Assert.Equal(4, reg.GetInt(dmw, "Start"));
        Assert.Null(reg.GetInt(@"HKLM\SYSTEM\CurrentControlSet\Services\DiagTrack", "Start"));
        rule.Undo(ctx, prior);
        Assert.Equal(3, reg.GetInt(dmw, "Start"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(3, true)]
    [InlineData(1, false)]
    [InlineData(0, false)]
    [InlineData(99, false)]
    [InlineData(100, false)]
    public void DeliveryPeers_ModesThatKeepTrafficLocalReadAsOff(int? mode, bool on)
    {
        var (rule, reg, ctx) = Make(SwitchCatalog.DeliveryPeers);
        if (mode is { } m)
            reg.SetInt(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", m);
        Assert.Equal(on, rule.IsOn(ctx));
    }

    [Fact]
    public void APolicySwitch_ReadsBackAsWrittenButUnverified()
    {
        var (rule, _, ctx) = Make(SwitchCatalog.Copilot);
        Assert.True(rule.IsOn(ctx));
        rule.Fix(ctx);
        Assert.Equal(ReadBackState.WrittenButUnverified, ReadBack.StateOf(ctx, rule));
    }

    [Fact]
    public void ErrorReporting_AbsentMeansOn()
    {
        var (rule, reg, ctx) = Make(SwitchCatalog.ErrorReporting);
        Assert.True(rule.IsOn(ctx));
        rule.Fix(ctx);
        Assert.Equal(1, reg.GetInt(@"HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled"));
    }

    [Fact]
    public void CatalogIdsAreUnique_AndPlainExcludesTheServiceSwitch()
    {
        var ids = SwitchCatalog.Plain.Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.DoesNotContain("telemetry-services", ids);
        Assert.Contains("memory-integrity", ids);
    }

    [Fact]
    public void TelemetryServices_StopsTheServicesItDisabled_AndUndoStartsDiagTrack()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetInt(@"HKLM\SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 2);
        var runner = new FakeRunner();
        var rule = new TelemetryServicesRule(runner);

        var prior = rule.Fix(ctx);

        Assert.Contains("sc.exe stop DiagTrack", runner.Commands);
        rule.Undo(ctx, prior);
        Assert.Equal(2, reg.GetInt(@"HKLM\SYSTEM\CurrentControlSet\Services\DiagTrack", "Start"));
        Assert.Contains("sc.exe start DiagTrack", runner.Commands);
    }
}
