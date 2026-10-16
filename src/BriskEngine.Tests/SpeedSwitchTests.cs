using BriskEngine.Actions.Rules;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

sealed class FakeUi : IUiEffectsProbe
{
    public bool Minimize = true, ClientArea = true;
    public bool MinimizeAnimation() => Minimize;
    public bool ClientAreaAnimation() => ClientArea;
    public void Set(bool minimize, bool clientArea) { Minimize = minimize; ClientArea = clientArea; }
}

public sealed class SpeedSwitchTests
{
    [Fact]
    public void Animations_FixTurnsBothOff_UndoRestoresEach()
    {
        var ui = new FakeUi { Minimize = true, ClientArea = false };
        var rule = new WindowAnimationsRule(ui);
        var ctx = TestContext.Empty();
        Assert.True(rule.IsOn(ctx));

        var prior = rule.Fix(ctx);
        Assert.False(ui.Minimize);
        Assert.False(ui.ClientArea);
        Assert.Equal(ReadBackState.Held, ReadBack.StateOf(ctx, rule));

        rule.Undo(ctx, prior);
        Assert.True(ui.Minimize);
        Assert.False(ui.ClientArea);
    }

    [Fact]
    public void Animations_AlreadyOff_NoFinding()
    {
        var rule = new WindowAnimationsRule(new FakeUi { Minimize = false, ClientArea = false });
        Assert.Null(rule.Detect(TestContext.Empty()));
    }

    [Theory]
    [InlineData(null, "SwapEffectUpgradeEnable", null)]
    [InlineData("VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;", "SwapEffectUpgradeEnable", "1")]
    [InlineData("SwapEffectUpgradeEnable=0;", "SwapEffectUpgradeEnable", "0")]
    public void ReadSetting(string? all, string key, string? expected) =>
        Assert.Equal(expected, WindowedGamesRule.ReadSetting(all, key));

    [Theory]
    [InlineData(null, "SwapEffectUpgradeEnable=1;")]
    [InlineData("VRROptimizeEnable=0;", "VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;")]
    [InlineData("SwapEffectUpgradeEnable=0;VRROptimizeEnable=0;", "SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;")]
    public void WithSetting_KeepsTheOtherSettings(string? all, string expected) =>
        Assert.Equal(expected, WindowedGamesRule.WithSetting(all, "SwapEffectUpgradeEnable", "1"));

    [Fact]
    public void WindowedGames_OnlyOnWindows11()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        var rule = new WindowedGamesRule();

        reg.SetString(WindowedGamesRule.BuildKey, "CurrentBuildNumber", "19045");
        Assert.False(rule.IsOn(ctx));

        reg.SetString(WindowedGamesRule.BuildKey, "CurrentBuildNumber", "26200");
        Assert.True(rule.IsOn(ctx));
    }

    [Fact]
    public void WindowedGames_FixAndUndo()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetString(WindowedGamesRule.BuildKey, "CurrentBuildNumber", "26200");
        reg.SetString(WindowedGamesRule.Key, WindowedGamesRule.Value, "VRROptimizeEnable=0;");
        var rule = new WindowedGamesRule();

        var prior = rule.Fix(ctx);
        Assert.Equal("VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;", reg.GetString(WindowedGamesRule.Key, WindowedGamesRule.Value));
        Assert.False(rule.IsOn(ctx));

        rule.Undo(ctx, prior);
        Assert.Equal("VRROptimizeEnable=0;", reg.GetString(WindowedGamesRule.Key, WindowedGamesRule.Value));
    }

    [Fact]
    public void WindowedGames_UndoDeletesAValueThatWasAbsent()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetString(WindowedGamesRule.BuildKey, "CurrentBuildNumber", "22631");
        var rule = new WindowedGamesRule();
        var prior = rule.Fix(ctx);
        rule.Undo(ctx, prior);
        Assert.Null(reg.GetString(WindowedGamesRule.Key, WindowedGamesRule.Value));
    }
}
