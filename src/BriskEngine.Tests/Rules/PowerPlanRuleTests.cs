using System;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class PowerPlanRuleTests
{
    private static (DiagnosticContext ctx, FakePowercfg power) Context(
        Guid active, string name, params (Guid, string)[] extra)
    {
        var power = new FakePowercfg { Active = (active, name) };
        power.Schemes.Add((active, name));
        foreach (var s in extra) power.Schemes.Add(s);
        var baseCtx = TestContext.Empty();
        return (baseCtx with { Powercfg = power }, power);
    }

    [Fact]
    public void BalancedPlan_IsAFinding()
    {
        var (ctx, _) = Context(PowerPlanRule.Balanced, "Balanced",
            (PowerPlanRule.HighPerformance, "High performance"));
        var finding = new PowerPlanRule().Detect(ctx);
        Assert.NotNull(finding);
        // Confirm, not Auto: a power plan is a preference brisk asks about.
        Assert.Equal(RuleCategory.Confirm, finding!.Category);
        Assert.Contains("Balanced", finding.Evidence);
        Assert.True(finding.CanFix);
        // Warning with two stars. It was Critical with five, which read as
        // "your CPU is throttled" over a setting brisk cannot measure.
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(2, finding.ImpactStars);
        // brisk reads the plan name back, never the effect. Hygiene: 2 points,
        // not the 25 that once moved a quarter of the gauge on a laptop.
        Assert.Equal(ImpactClass.Hygiene, finding.ImpactClass);
        // The copy promises nothing it cannot measure.
        Assert.DoesNotContain("throttl", finding.Evidence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("throttl", finding.Title, StringComparison.OrdinalIgnoreCase);
    }

    /// A laptop on Balanced is a laptop doing the right thing. High
    /// performance costs battery for a gain brisk cannot measure, so there is
    /// nothing to find, not even advice.
    [Fact]
    public void OnAMachineWithABattery_NoFinding()
    {
        var (ctx, power) = Context(PowerPlanRule.Balanced, "Balanced",
            (PowerPlanRule.HighPerformance, "High performance"));
        power.Battery = true;
        Assert.Null(new PowerPlanRule().Detect(ctx));
    }

    /// Modern Standby machines often list Balanced alone. A finding there is a
    /// fix button that fails; no finding is the honest reading.
    [Fact]
    public void NoPerformancePlanToSwitchTo_NoFinding()
    {
        var (ctx, _) = Context(PowerPlanRule.Balanced, "Balanced");
        Assert.Null(new PowerPlanRule().Detect(ctx));
    }

    [Fact]
    public void HighPerformancePlan_NoFinding()
    {
        var (ctx, _) = Context(PowerPlanRule.HighPerformance, "High performance");
        Assert.Null(new PowerPlanRule().Detect(ctx));
    }

    [Fact]
    public void Fix_PrefersUltimate_WhenAvailable()
    {
        var (ctx, power) = Context(PowerPlanRule.Balanced, "Balanced",
            (PowerPlanRule.HighPerformance, "High performance"),
            (PowerPlanRule.Ultimate, "Ultimate Performance"));
        new PowerPlanRule().Fix(ctx);
        Assert.Equal(PowerPlanRule.Ultimate, power.Active.Id);
    }

    [Fact]
    public void FixThenUndo_RestoresBalanced()
    {
        var (ctx, power) = Context(PowerPlanRule.Balanced, "Balanced",
            (PowerPlanRule.HighPerformance, "High performance"));
        var rule = new PowerPlanRule();
        var prior = rule.Fix(ctx);
        Assert.Equal(PowerPlanRule.HighPerformance, power.Active.Id);
        rule.Undo(ctx, prior);
        Assert.Equal(PowerPlanRule.Balanced, power.Active.Id);
    }
}
