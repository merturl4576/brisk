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

    // FIX WAVE, Finding 1. The registry is what a reboot reads, so writing the
    // new mode there is what turns "the screen went black and I held the power
    // button" into a machine that boots black forever. The raise is applied
    // dynamically and NOTHING is persisted until something confirms it.
    [Fact]
    public void Fix_RaisesTheRate_ButWritesNothingToTheRegistry()
    {
        var (ctx, displays) = Context(
            new DisplayInfo(@"\\.\DISPLAY1", "Dell U2720Q", 60, 144));

        new DisplayRefreshRule().Fix(ctx);

        Assert.Equal((@"\\.\DISPLAY1", 144), displays.SetCalls[0]);
        Assert.Equal(0, displays.PersistCalls);
    }

    // FIX WAVE, Finding 1. The undo is the other half: after a confirmed
    // raise the registry IS carrying the new mode, so a session-only restore
    // would hand the rejected rate straight back at the next reboot.
    [Fact]
    public void Undo_PutsTheRestoredModeInTheRegistryToo()
    {
        var (ctx, displays) = Context(
            new DisplayInfo(@"\\.\DISPLAY1", "Dell U2720Q", 60, 144));
        var rule = new DisplayRefreshRule();

        rule.Undo(ctx, rule.Fix(ctx));

        Assert.Equal(1, displays.PersistCalls);
    }

    // FIX WAVE, Finding 2. ChangeDisplaySettingsEx answers DISP_CHANGE_BADMODE
    // when the driver will not take the rate — the exact case this rule exists
    // for, since it is the cable or adapter refusing. Swallowing it made the
    // fix report a refresh rate the screen never ran at.
    [Fact]
    public void RefusedMode_Throws_SoTheFixCannotClaimSuccess()
    {
        var (ctx, displays) = Context(
            new DisplayInfo(@"\\.\DISPLAY1", "Dell U2720Q", 60, 144));
        displays.RefusedRates.Add(144);

        Assert.Throws<DisplayChangeException>(() => new DisplayRefreshRule().Fix(ctx));
    }

    // FIX WAVE, Finding 2. Half a fix is worse than none: FixRunner journals
    // nothing when Fix throws, so a display left raised would be a change with
    // no undo behind it. Whatever was raised goes back before the failure is
    // reported.
    [Fact]
    public void OneDisplayRefusing_PutsTheOtherBack()
    {
        var (ctx, displays) = Context(
            new DisplayInfo(@"\\.\DISPLAY1", "Dell U2720Q", 60, 144),
            new DisplayInfo(@"\\.\DISPLAY2", "BenQ XL2411", 75, 165));
        displays.RefusedRates.Add(165);

        Assert.Throws<DisplayChangeException>(() => new DisplayRefreshRule().Fix(ctx));

        Assert.Equal(60,
            displays.Attached.Find(d => d.DeviceName == @"\\.\DISPLAY1")!.CurrentHz);
        Assert.Equal(0, displays.PersistCalls);
    }
}
