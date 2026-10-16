using System;
using System.Collections.Generic;
using BriskEngine.Actions.Rules;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using Xunit;

namespace BriskEngine.Tests;

sealed class FakeOverlay : IPowerOverlayProbe
{
    public bool Ac = true;
    public List<Guid> Applied = new();
    public bool OnAcPower() => Ac;
    public void ApplyOverlay(Guid overlay) => Applied.Add(overlay);
}

sealed class FakeFeatures : IOptionalFeatureProbe
{
    public Dictionary<string, int> States = new(StringComparer.OrdinalIgnoreCase);
    public int? InstallState(string featureName) => States.TryGetValue(featureName, out var s) ? s : 3;
}

public sealed class PowerAndFeatureRuleTests
{
    private static DiagnosticContext Balanced()
    {
        var ctx = TestContext.Empty();
        ((FakePowercfg)ctx.Powercfg).Active = (PowerPlanRule.Balanced, "Balanced");
        return ctx;
    }

    [Fact]
    public void PowerMode_OnlyWhileTheBalancedPlanIsActive()
    {
        var ctx = TestContext.Empty();
        ((FakePowercfg)ctx.Powercfg).Active = (PowerPlanRule.HighPerformance, "High performance");
        Assert.False(new PowerModeRule(new FakeOverlay()).IsOn(ctx));
        Assert.True(new PowerModeRule(new FakeOverlay()).IsOn(Balanced()));
    }

    [Fact]
    public void PowerMode_FixWritesThePluggedInValue_AndAppliesItOnAc()
    {
        var ctx = Balanced();
        var overlay = new FakeOverlay { Ac = true };
        var rule = new PowerModeRule(overlay);

        var prior = rule.Fix(ctx);

        Assert.Equal(PowerModeRule.BestPerformance.ToString(),
            ctx.Registry.GetString(PowerModeRule.Key, PowerModeRule.AcValue));
        Assert.Equal(new[] { PowerModeRule.BestPerformance }, overlay.Applied);
        Assert.False(rule.IsOn(ctx));

        rule.Undo(ctx, prior);
        Assert.Null(ctx.Registry.GetString(PowerModeRule.Key, PowerModeRule.AcValue));
        Assert.Equal(Guid.Empty, overlay.Applied[^1]);
    }

    [Fact]
    public void PowerMode_OnBattery_WritesButDoesNotApply()
    {
        var ctx = Balanced();
        var overlay = new FakeOverlay { Ac = false };
        new PowerModeRule(overlay).Fix(ctx);
        Assert.Empty(overlay.Applied);
        Assert.Equal(PowerModeRule.BestPerformance.ToString(),
            ctx.Registry.GetString(PowerModeRule.Key, PowerModeRule.AcValue));
    }

    [Fact]
    public void Vmp_OnlyWhenEnabled()
    {
        var features = new FakeFeatures();
        var rule = new VirtualMachinePlatformRule(features, new FakeRunner());
        Assert.False(rule.IsOn(TestContext.Empty()));
        features.States[VirtualMachinePlatformRule.Feature] = 1;
        Assert.True(rule.IsOn(TestContext.Empty()));
    }

    [Fact]
    public void Vmp_FixAndUndoRunDism_AndRestartPendingCountsAsSuccess()
    {
        var features = new FakeFeatures();
        features.States[VirtualMachinePlatformRule.Feature] = 1;
        var runner = new ScriptedRunner((_, _) => (3010, ""));
        var rule = new VirtualMachinePlatformRule(features, runner);

        var prior = rule.Fix(TestContext.Empty());
        rule.Undo(TestContext.Empty(), prior);

        Assert.Contains("dism.exe /Online /Disable-Feature /FeatureName:VirtualMachinePlatform /NoRestart", runner.Commands);
        Assert.Contains("dism.exe /Online /Enable-Feature /FeatureName:VirtualMachinePlatform /All /NoRestart", runner.Commands);
    }

    [Fact]
    public void Vmp_DismFailure_Throws()
    {
        var features = new FakeFeatures();
        features.States[VirtualMachinePlatformRule.Feature] = 1;
        var rule = new VirtualMachinePlatformRule(features, new ScriptedRunner((_, _) => (740, "")));
        Assert.Throws<InvalidOperationException>(() => rule.Fix(TestContext.Empty()));
    }
}
