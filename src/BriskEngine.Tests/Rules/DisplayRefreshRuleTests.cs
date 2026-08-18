using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class DisplayRefreshRuleTests
{
    private static (DiagnosticContext ctx, FakeDisplays displays) Context(
        params DisplayInfo[] attached)
    {
        var displays = new FakeDisplays();
        displays.Attached.AddRange(attached);
        return (TestContext.Empty() with { Displays = displays }, displays);
    }

    [Fact]
    public void SixtyOnA144HzPanel_IsAFinding()
    {
        var (ctx, _) = Context(new DisplayInfo(@"\\.\DISPLAY1", "Dell U2720Q", 60, 144));
        var finding = new DisplayRefreshRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Equal(RuleCategory.Auto, finding!.Category);
        Assert.True(finding.CanFix);
        Assert.Contains("144", finding.Evidence);
    }

    [Fact]
    public void AlreadyAtMaximum_NoFinding()
    {
        var (ctx, _) = Context(new DisplayInfo(@"\\.\DISPLAY1", "Dell U2720Q", 144, 144));
        Assert.Null(new DisplayRefreshRule().Detect(ctx));
    }

    // 59.94 Hz is reported as 59 next to a nominal 60. That is a unit-rounding
    // artefact, not a display left on the wrong mode, and reporting it would
    // make brisk look like it is inventing problems.
    [Fact]
    public void OneHzOfRounding_IsNotAFinding()
    {
        var (ctx, _) = Context(new DisplayInfo(@"\\.\DISPLAY1", "Generic PnP Monitor", 59, 60));
        Assert.Null(new DisplayRefreshRule().Detect(ctx));
    }

    [Fact]
    public void OnlyDisplaysBehind_AreFixed()
    {
        var (ctx, displays) = Context(
            new DisplayInfo(@"\\.\DISPLAY1", "Dell U2720Q", 60, 144),
            new DisplayInfo(@"\\.\DISPLAY2", "Laptop panel", 120, 120));
        new DisplayRefreshRule().Fix(ctx);
        Assert.Single(displays.SetCalls);
        Assert.Equal((@"\\.\DISPLAY1", 144), displays.SetCalls[0]);
    }

    [Fact]
    public void FixThenUndo_RestoresEachPriorRate()
    {
        var (ctx, displays) = Context(
            new DisplayInfo(@"\\.\DISPLAY1", "Dell U2720Q", 60, 144),
            new DisplayInfo(@"\\.\DISPLAY2", "BenQ XL2411", 75, 165));
        var rule = new DisplayRefreshRule();
        var prior = rule.Fix(ctx);
        rule.Undo(ctx, prior);
        Assert.Equal(60, displays.Attached.Find(d => d.DeviceName == @"\\.\DISPLAY1")!.CurrentHz);
        Assert.Equal(75, displays.Attached.Find(d => d.DeviceName == @"\\.\DISPLAY2")!.CurrentHz);
    }

    [Fact]
    public void NoDisplays_NoFinding()
    {
        Assert.Null(new DisplayRefreshRule().Detect(TestContext.Empty()));
    }
}
