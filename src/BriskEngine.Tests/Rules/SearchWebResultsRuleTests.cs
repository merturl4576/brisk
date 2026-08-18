using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class SearchWebResultsRuleTests
{
    private static (DiagnosticContext ctx, FakeRegistry reg) Context()
    {
        var reg = new FakeRegistry();
        return (TestContext.Empty() with { Registry = reg }, reg);
    }

    [Fact]
    public void UntouchedMachine_IsAFinding()
    {
        var (ctx, _) = Context();
        var finding = new SearchWebResultsRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Equal(RuleCategory.Auto, finding!.Category);
        Assert.True(finding.CanFix);
    }

    [Fact]
    public void AlreadyDisabled_NoFinding()
    {
        var (ctx, reg) = Context();
        reg.SetInt(SearchWebResultsRule.PolicyKey, SearchWebResultsRule.PolicyValue, 1);
        Assert.Null(new SearchWebResultsRule().Detect(ctx));
    }

    // A policy value that exists but says "keep web search on" was written by
    // an administrator. brisk does not fight Group Policy.
    [Fact]
    public void PolicyExplicitlyEnablesWebSearch_NoFinding()
    {
        var (ctx, reg) = Context();
        reg.SetInt(SearchWebResultsRule.PolicyKey, SearchWebResultsRule.PolicyValue, 0);
        Assert.Null(new SearchWebResultsRule().Detect(ctx));
    }

    [Fact]
    public void WindowsTenRouteAlreadyTaken_NoFinding()
    {
        var (ctx, reg) = Context();
        reg.SetInt(SearchWebResultsRule.LegacyKey, SearchWebResultsRule.LegacyValue, 0);
        Assert.Null(new SearchWebResultsRule().Detect(ctx));
    }

    [Fact]
    public void Fix_SetsBothValues()
    {
        var (ctx, reg) = Context();
        new SearchWebResultsRule().Fix(ctx);
        Assert.Equal(1, reg.GetInt(SearchWebResultsRule.PolicyKey, SearchWebResultsRule.PolicyValue));
        Assert.Equal(0, reg.GetInt(SearchWebResultsRule.LegacyKey, SearchWebResultsRule.LegacyValue));
    }

    [Fact]
    public void FixThenUndo_LeavesNoTrace()
    {
        var (ctx, reg) = Context();
        var rule = new SearchWebResultsRule();
        rule.Undo(ctx, rule.Fix(ctx));
        Assert.Null(reg.GetInt(SearchWebResultsRule.PolicyKey, SearchWebResultsRule.PolicyValue));
        Assert.Null(reg.GetInt(SearchWebResultsRule.LegacyKey, SearchWebResultsRule.LegacyValue));
    }

    [Fact]
    public void FixThenUndo_RestoresAPreExistingLegacyValue()
    {
        var (ctx, reg) = Context();
        reg.SetInt(SearchWebResultsRule.LegacyKey, SearchWebResultsRule.LegacyValue, 1);
        var rule = new SearchWebResultsRule();
        rule.Undo(ctx, rule.Fix(ctx));
        Assert.Equal(1, reg.GetInt(SearchWebResultsRule.LegacyKey, SearchWebResultsRule.LegacyValue));
    }
}
