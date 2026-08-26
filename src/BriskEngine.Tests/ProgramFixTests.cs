using System;
using System.IO;
using System.Linq;
using Brisk.Cli;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Diagnostics.Rules.Privacy;
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

    /// `brisk fix --all` is a SECOND fix-all, and it does not go through the
    /// GUI's FixAllService — that one lives in the Brisk project, excludes the
    /// whole privacy topic by rule id, and Brisk.Cli does not reference it.
    /// The CLI selects on RuleCategory alone, which is why the four
    /// consequence-free switches ARE reached by `brisk fix --all --yes` and
    /// are meant to be: nothing a user relies on stops working when an
    /// advertising ID goes off.
    ///
    /// These two are the line. `--all` may never take Find my device or
    /// Timeline away from somebody who typed --all and was shown no
    /// consequence. Today they are outside the selection because they ship as
    /// Confirm; this test is what makes that a guarantee rather than a
    /// coincidence, and it fails the moment either rule is made Auto or the
    /// selection stops filtering on the category.
    [Theory]
    [InlineData("location")]
    [InlineData("activity-history")]
    public void CliFixAll_NeverReachesASwitchThatCostsTheUserSomething(string ruleId)
    {
        // Not vacuous: the rule has to BE in the registry for its absence from
        // the selection to mean anything. A typo'd id would otherwise pass
        // this test by naming a rule that does not exist.
        Assert.True(DiagnosticRuleRegistry.All.Any(r => r.Id == ruleId),
            $"no rule with id '{ruleId}' is registered, so its absence from " +
            "`fix --all` proves nothing");

        var selected = Program.FixAllRules().Select(r => r.Id).ToArray();
        Assert.False(selected.Contains(ruleId),
            $"`brisk fix --all` reaches '{ruleId}' and would apply it on any " +
            "machine it fires on, costing the user something the command " +
            "never named");
    }

    /// The other half, so the guard above cannot pass by the selection being
    /// empty: `fix --all` still reaches the switches that cost nothing.
    [Fact]
    public void CliFixAll_StillReachesTheSwitchesThatCostNothing()
    {
        var selected = Program.FixAllRules().Select(r => r.Id).ToArray();
        foreach (var id in new[] { "advertising-id", "diagnostic-level",
                                   "tailored-experiences", "speech-typing" })
            Assert.True(selected.Contains(id),
                $"`brisk fix --all` stopped reaching '{id}'");
    }

    /// `fix --rule <id> --yes` ACTED HAVING NAMED NO CONSEQUENCE. Without
    /// --yes this path prints the finding's title and its evidence — which is
    /// where the loss lives, in both languages, put there so that no CLI path
    /// takes Find my device or Timeline away unwarned — and then asks for
    /// --yes. With --yes it applied and printed "location: fixed" and nothing
    /// else, so the one flag that makes the command act was the one that
    /// removed the warning.
    ///
    /// location is the sharpest case and the one the ledger carried, but the
    /// print is not conditional on the rule: the preview path it mirrors is
    /// not either, and the finding is already in hand.
    ///
    /// The strings come off the SHIPPED rule rather than being quoted here,
    /// and the order is asserted: a consequence printed after the write is
    /// not a warning.
    [Fact]
    public void FixRule_WithYes_PrintsTheConsequence_BeforeItActs()
    {
        var registry = new FakeRegistry();
        var ctx = TestContext.Empty() with { Registry = registry };
        var finding = new LocationRule().Detect(ctx);
        Assert.True(finding is not null,
            "location reports nothing on an empty registry, so this test never " +
            "reached the path that prints a finding");

        var (code, output) = Capture(() => Program.Fix(
            new CliCommand("fix", RuleId: "location", Yes: true), ctx, Runner()));

        Assert.Equal(0, code);
        Assert.Equal(LocationRule.Denied,
            registry.GetString(LocationRule.KeyPath, LocationRule.ValueName));
        Assert.Contains(finding!.Title, output);
        Assert.Contains(finding.Evidence, output);
        Assert.True(output.IndexOf(finding.Evidence, StringComparison.Ordinal)
                < output.IndexOf("location: fixed", StringComparison.Ordinal),
            "the consequence was printed after the write went in, which is a " +
            $"record rather than a warning:{Environment.NewLine}{output}");
    }

    private static (int Code, string Output) Capture(Func<int> run)
    {
        var stdout = Console.Out;
        var buffer = new StringWriter();
        try
        {
            Console.SetOut(buffer);
            return (run(), buffer.ToString());
        }
        finally
        {
            Console.SetOut(stdout);
        }
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
