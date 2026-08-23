using System;
using System.IO;
using Brisk.Cli;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Logging;
using Xunit;

namespace BriskEngine.Tests;

public sealed class ProgramFixTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-pf-").FullName;

    private FixRunner Runner() => new(
        new FixJournal(Path.Combine(_root, "j.jsonl")),
        new ActionLog(Path.Combine(_root, "log.jsonl")));

    [Fact]
    public void FixUndo_WithoutYes_DoesNotMutate()
    {
        var power = new FakePowercfg { Active = (PowerPlanRule.Balanced, "Balanced") };
        power.Schemes.Add((PowerPlanRule.Balanced, "Balanced"));
        power.Schemes.Add((PowerPlanRule.HighPerformance, "High performance"));
        var ctx = TestContext.Empty() with { Powercfg = power };
        var runner = Runner();

        var applyResult = Program.Fix(new CliCommand("fix", RuleId: "power-plan", Yes: true), ctx, runner);
        Assert.Equal(0, applyResult);
        var callsAfterApply = power.SetCalls.Count;
        Assert.Equal(1, callsAfterApply);

        var undoResult = Program.Fix(new CliCommand("fix", RuleId: "power-plan", Undo: true), ctx, runner);
        Assert.Equal(0, undoResult);
        Assert.Equal(callsAfterApply, power.SetCalls.Count); // no --yes => no mutation
    }

    [Fact]
    public void FixRule_NoLiveFinding_DoesNotApply()
    {
        var power = new FakePowercfg { Active = (PowerPlanRule.HighPerformance, "High performance") };
        power.Schemes.Add((PowerPlanRule.HighPerformance, "High performance"));
        var ctx = TestContext.Empty() with { Powercfg = power };
        var runner = Runner();

        var result = Program.Fix(new CliCommand("fix", RuleId: "power-plan", Yes: true), ctx, runner);
        Assert.Equal(0, result);
        Assert.Empty(power.SetCalls);
    }

    /// FIX WAVE, Finding 1. The console has no "is the picture back?" overlay,
    /// so a CLI fix must not write the new mode to the registry either — a
    /// machine power-cycled through a black screen has to come back on the
    /// mode it booted with.
    [Fact]
    public void FixDisplayRefresh_AppliesTheRate_ButPersistsNothing()
    {
        var displays = new FakeDisplays();
        displays.Attached.Add(new DisplayInfo("DISPLAY1", "Dell U2720Q", 60, 144));
        var ctx = TestContext.Empty() with { Displays = displays };

        var result = Program.Fix(
            new CliCommand("fix", RuleId: "display-refresh", Yes: true), ctx, Runner());

        Assert.Equal(0, result);
        Assert.Equal(("DISPLAY1", 144), displays.SetCalls[0]);
        Assert.Equal(0, displays.PersistCalls);
    }

    /// ...and --keep is the console's version of the answer, which is the only
    /// thing that makes it permanent. It must not depend on a live finding:
    /// by the time the user can answer, the display is already at its best
    /// rate and nothing is left to detect.
    [Fact]
    public void FixKeep_PersistsTheModeOnScreen_EvenWithNoFindingLeft()
    {
        var displays = new FakeDisplays();
        displays.Attached.Add(new DisplayInfo("DISPLAY1", "Dell U2720Q", 144, 144));
        var ctx = TestContext.Empty() with { Displays = displays };

        var result = Program.Fix(
            new CliCommand("fix", RuleId: "display-refresh", Keep: true, Yes: true),
            ctx, Runner());

        Assert.Equal(0, result);
        Assert.Equal(1, displays.PersistCalls);
    }

    [Fact]
    public void FixKeep_WithoutYes_DoesNotPersist()
    {
        var displays = new FakeDisplays();
        var ctx = TestContext.Empty() with { Displays = displays };

        Assert.Equal(0, Program.Fix(
            new CliCommand("fix", RuleId: "display-refresh", Keep: true), ctx, Runner()));
        Assert.Equal(0, displays.PersistCalls);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
