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

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
