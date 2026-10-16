using System;
using System.IO;
using BriskEngine.Actions;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Brisk.Cli;
using Xunit;

namespace BriskEngine.Tests;

public sealed class CliActionTests
{
    [Fact]
    public void Parser_RunWithGroupAndYes()
    {
        var cmd = CliParser.Parse(new[] { "run", "--group", "speed", "--yes" });
        Assert.Equal("run", cmd.Verb);
        Assert.Equal("speed", cmd.Group);
        Assert.True(cmd.Yes);
    }

    [Theory]
    [InlineData("undo")]
    [InlineData("receipt")]
    [InlineData("live")]
    public void Parser_NewVerbs(string verb) => Assert.Equal(verb, CliParser.Parse(new[] { verb }).Verb);

    [Fact]
    public void Parser_UnknownGroupIsAnError() =>
        Assert.Equal("error", CliParser.Parse(new[] { "run", "--group", "games" }).Verb);

    private static ActionItem Item(string id, long? bytes = null, params ActionFile[] files) =>
        ActionPlanBuilder.ItemFrom(ActionCatalog.Find(id), bytes, files: files);

    [Fact]
    public void PrintPlan_ShowsTicksSizesCostsAndFiles()
    {
        var plan = new ActionPlan(DateTime.UtcNow, new[]
        {
            new ActionGroup(ActionGroupKind.Space, new[]
            {
                Item("space.caches", 6L << 30),
                Item("space.shaders", 2L << 30),
                Item("space.downloads", 100L << 20, new ActionFile(@"C:\Users\x\Downloads\setup.exe", 100L << 20, null)),
            }),
        }, new ScanResult(Array.Empty<TargetScanResult>()));
        var o = new StringWriter();

        Program.PrintPlan(plan, o);

        var text = o.ToString();
        Assert.Contains("Free up space", text);
        Assert.Contains("[x] Caches and temporary files  6.0 GB", text);
        Assert.Contains("[ ] Game shader caches  2.0 GB  — Games may stutter on first launch.", text);
        Assert.Contains("setup.exe  100 MB", text);
    }

    [Theory]
    [InlineData("privacy.ads", RowOutcome.Done, ReadBackState.Held, "Off ✓")]
    [InlineData("speed.animations", RowOutcome.Done, ReadBackState.Held, "Done ✓")]
    [InlineData("privacy.error-reports", RowOutcome.Done, ReadBackState.WrittenButIgnored, "Windows ignored it")]
    [InlineData("privacy.copilot", RowOutcome.Done, ReadBackState.WrittenButUnverified, "Written")]
    [InlineData("speed.animations", RowOutcome.Done, ReadBackState.Reverted, "Turned back on")]
    [InlineData("speed.gpu-scheduling", RowOutcome.PendingRestart, null, "After restart")]
    public void ResultWord(string id, RowOutcome outcome, ReadBackState? readBack, string expected)
    {
        var item = Item(id);
        Assert.StartsWith(expected, Program.ResultWord(item, new RowResult(id, outcome, null, readBack, 0, 0)));
    }

    [Fact]
    public void ResultWord_SkippedAndFailedCarryTheReason()
    {
        Assert.Equal("Needs administrator", Program.ResultWord(null,
            new RowResult("x", RowOutcome.Skipped, ActionRunner.NeedsAdmin, null, 0, 0)));
        Assert.Equal("Didn't work: boom", Program.ResultWord(null,
            new RowResult("x", RowOutcome.Failed, "boom", null, 0, 0)));
    }

    [Fact]
    public void PrintReceiptLines_PrintsOnlyWhatIsKnown()
    {
        var r = new Receipt("r", DateTime.UtcNow, DateTime.UtcNow, 0, 20L << 30, 2L << 30, 14, 3,
            Array.Empty<StartupRam>(), Array.Empty<string>(), false, 48_000, null, 9, 1, null, null);
        var o = new StringWriter();

        Program.PrintReceiptLines(r, new[] { "Chrome" }, o);

        var text = o.ToString();
        Assert.Contains("20.0 GB freed", text);
        Assert.Contains("2.0 GB more when Chrome closes", text);
        Assert.Contains("Startup apps 14 → 3", text);
        Assert.Contains("9 settings off · Windows ignored 1", text);
        Assert.DoesNotContain("Startup 48", text);   // no boot after the run yet
        Assert.DoesNotContain("RAM freed", text);
    }

    [Fact]
    public void ActionEngine_CreateReal_Wires()
    {
        var engine = ActionEngine.CreateReal(TestContext.Empty(), new FakeRunner(), () => false);
        Assert.NotNull(engine.Runner);
        Assert.NotNull(engine.Builder);
    }
}
