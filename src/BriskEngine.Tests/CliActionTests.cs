using System;
using System.IO;
using System.Linq;
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

    private static (ActionEngine Engine, string Data) PlanOnlyRun()
    {
        var data = Directory.CreateTempSubdirectory("brisk-cli-run-").FullName;
        var engine = ActionEngine.CreateReal(TestContext.Empty(data), new FakeRunner(), () => false);
        // An unknown id is dropped whenever the queue is processed, so a
        // plan-only run that processes it shows up as an empty queue.
        engine.Deferred.Add("no-such-target", DateTime.UtcNow);
        var scanner = new BriskEngine.Cleaning.Scanner(Array.Empty<CleanupTarget>(), new FakeRunningApps());
        Program.RunActions(new CliCommand("run"), scanner, engine, new StringWriter());
        return (engine, data);
    }

    [Fact]
    public void Run_WithoutYes_LeavesTheDeferredQueueAlone()
    {
        var (engine, data) = PlanOnlyRun();
        try { Assert.Single(engine.Deferred.Pending()); }
        finally { Directory.Delete(data, true); }
    }

    [Fact]
    public void Run_WithoutYes_CreatesNoRunOrReceiptFolders()
    {
        var (_, data) = PlanOnlyRun();
        try
        {
            Assert.False(Directory.Exists(Path.Combine(data, "runs")));
            Assert.False(Directory.Exists(Path.Combine(data, "receipts")));
        }
        finally { Directory.Delete(data, true); }
    }

    private static ActionPlan PlanOf(params ActionGroup[] groups) =>
        new(DateTime.UtcNow, groups, new ScanResult(Array.Empty<TargetScanResult>()));

    [Fact]
    public void PrintPlan_ListsEveryFile_NotOnlyTheFirstTen()
    {
        var files = Enumerable.Range(1, 12)
            .Select(i => new ActionFile($@"C:\Users\x\Downloads\setup{i}.exe", 1L << 20, null)).ToArray();
        var o = new StringWriter();
        Program.PrintPlan(PlanOf(new ActionGroup(ActionGroupKind.Space,
            new[] { Item("space.downloads", 12L << 20, files) })), o);
        for (var i = 1; i <= 12; i++) Assert.Contains($"setup{i}.exe", o.ToString());
    }

    [Fact]
    public void PrintPlan_TicksWhatTheSelectionRuns()
    {
        var plan = PlanOf(
            new ActionGroup(ActionGroupKind.Space, new[] { Item("space.caches", 1L << 30) }),
            new ActionGroup(ActionGroupKind.Privacy, new[] { Item("privacy.ads") }));
        var o = new StringWriter();
        Program.PrintPlan(plan, o, ActionSelection.Default(plan).Only(ActionGroupKind.Privacy, plan));
        Assert.Contains("[ ] Caches and temporary files", o.ToString());
        Assert.Contains("[x] Showing ads and suggestions for you", o.ToString());
    }

    [Fact]
    public void PrintPlan_SaysTheWindowsUpdateCleanupTakesMinutes()
    {
        var o = new StringWriter();
        Program.PrintPlan(PlanOf(new ActionGroup(ActionGroupKind.Space, new[] { Item("space.caches", 1L << 30) })), o);
        Assert.Contains("several minutes", o.ToString());
    }

    [Fact]
    public void Run_YesForAnotherGroup_LeavesTheDeferredQueueAlone_AndShowsThePlanFirst()
    {
        var data = Directory.CreateTempSubdirectory("brisk-cli-run-").FullName;
        try
        {
            var engine = ActionEngine.CreateReal(TestContext.Empty(data), new FakeRunner(), () => false);
            engine.Deferred.Add("no-such-target", DateTime.UtcNow);
            var scanner = new BriskEngine.Cleaning.Scanner(Array.Empty<CleanupTarget>(), new FakeRunningApps());
            var o = new StringWriter();

            Program.RunActions(new CliCommand("run", Yes: true, Group: "privacy"), scanner, engine, o);

            Assert.Single(engine.Deferred.Pending());
            Assert.Contains("One click", o.ToString());
        }
        finally { Directory.Delete(data, true); }
    }

    /// Spec open item: `brisk run --group speed` printed the whole plan.
    [Fact]
    public void Run_group_prints_only_that_group()
    {
        var plan = PlanOf(
            new ActionGroup(ActionGroupKind.Space, new[] { Item("space.caches", 1L << 30) }),
            new ActionGroup(ActionGroupKind.Speed, new[] { Item("speed.animations") }));
        var o = new StringWriter();

        Program.PrintPlan(plan, o, ActionSelection.Default(plan).Only(ActionGroupKind.Speed, plan),
            ActionGroupKind.Speed);

        var text = o.ToString();
        Assert.DoesNotContain("Free up space", text);
        Assert.DoesNotContain("Caches and temporary files", text);
        Assert.DoesNotContain("several minutes", text);
        Assert.Contains("Speed up", text);
        Assert.Contains("[x] Window opening animations", text);
    }

    /// `brisk run` without --yes over a scan of temp files posing as the
    /// named registry targets. Plan only: nothing is deleted.
    private static string RunPlanText(CliCommand cmd, params string[] targetIds)
    {
        var data = Directory.CreateTempSubdirectory("brisk-cli-run-").FullName;
        try
        {
            var targets = targetIds.Select(id =>
            {
                var dir = Directory.CreateDirectory(Path.Combine(data, "fake-" + id)).FullName;
                File.WriteAllBytes(Path.Combine(dir, "x.bin"), new byte[4096]);
                return BriskEngine.Cleaning.CleanupTargetRegistry.All.Single(t => t.Id == id)
                    with { PathTemplates = new[] { dir } };
            }).ToList();
            var engine = ActionEngine.CreateReal(TestContext.Empty(data), new FakeRunner(), () => false);
            var scanner = new BriskEngine.Cleaning.Scanner(targets, new FakeRunningApps());
            var o = new StringWriter();
            Assert.Equal(0, Program.RunActions(cmd, scanner, engine, o));
            Assert.Contains("nothing changed", o.ToString());
            return o.ToString();
        }
        finally { Directory.Delete(data, true); }
    }

    [Fact]
    public void Run_group_speed_leaves_the_space_rows_out_of_the_printed_plan()
    {
        var text = RunPlanText(CliParser.Parse(new[] { "run", "--group", "speed" }), "npm-cache");
        Assert.DoesNotContain("Free up space", text);
        Assert.DoesNotContain("Developer caches", text);

        Assert.Contains("[x] Developer caches", RunPlanText(CliParser.Parse(new[] { "run" }), "npm-cache"));
    }

    [Fact]
    public void Run_level_ticks_the_rows_up_to_that_level()
    {
        string Line(string text, string title) =>
            text.Split('\n').Single(l => l.Contains(title, StringComparison.Ordinal)).Trim();

        var min = RunPlanText(CliParser.Parse(new[] { "run" }), "npm-cache", "nvidia-shader-cache");
        Assert.StartsWith("[x] Developer caches", Line(min, "Developer caches"));
        Assert.StartsWith("[ ] Game shader caches", Line(min, "Game shader caches"));

        var mid = RunPlanText(CliParser.Parse(new[] { "run", "--level", "mid" }), "npm-cache", "nvidia-shader-cache");
        Assert.StartsWith("[x] Developer caches", Line(mid, "Developer caches"));
        Assert.StartsWith("[x] Game shader caches", Line(mid, "Game shader caches"));
    }
}
