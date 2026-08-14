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
        Assert.Equal(RuleCategory.Auto, finding!.Category);
        Assert.Contains("Balanced", finding.Evidence);
        Assert.True(finding.CanFix);
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
