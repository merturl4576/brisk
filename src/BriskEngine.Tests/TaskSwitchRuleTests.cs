using System.Collections.Generic;
using BriskEngine.Actions.Switches;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

sealed class FakeTasks : IScheduledTaskProbe
{
    public Dictionary<string, bool> Tasks = new(System.StringComparer.OrdinalIgnoreCase);
    public bool? IsEnabled(string taskPath) => Tasks.TryGetValue(taskPath, out var on) ? on : null;
    public void SetEnabled(string taskPath, bool enabled) => Tasks[taskPath] = enabled;
}

public sealed class TaskSwitchRuleTests
{
    private const string Consolidator =
        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator";
    private const string UsbCeip =
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip";

    [Fact]
    public void ParseEnabled_ReadsTheTaskSetting_NotATriggerSetting()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers><TimeTrigger><Enabled>false</Enabled></TimeTrigger></Triggers>
              <Settings><Enabled>true</Enabled></Settings>
            </Task>
            """;
        Assert.True(RealScheduledTaskProbe.ParseEnabled(xml));
    }

    [Fact]
    public void ParseEnabled_DisabledTask()
    {
        const string xml = """
            <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Settings><Enabled>false</Enabled></Settings>
            </Task>
            """;
        Assert.False(RealScheduledTaskProbe.ParseEnabled(xml));
    }

    [Fact]
    public void ParseEnabled_NoEnabledElement_MeansEnabled()
    {
        const string xml = """
            <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task"><Settings /></Task>
            """;
        Assert.True(RealScheduledTaskProbe.ParseEnabled(xml));
    }

    [Fact]
    public void ParseEnabled_Garbage_IsNull()
    {
        Assert.Null(RealScheduledTaskProbe.ParseEnabled("ERROR: The system cannot find the file specified."));
    }

    [Fact]
    public void IsOn_WhenAnyListedTaskExistsAndIsEnabled()
    {
        var tasks = new FakeTasks();
        var rule = new TaskSwitchRule(tasks);
        var ctx = TestContext.Empty();
        Assert.False(rule.IsOn(ctx));   // none exist on this machine
        tasks.Tasks[UsbCeip] = false;
        Assert.False(rule.IsOn(ctx));
        tasks.Tasks[Consolidator] = true;
        Assert.True(rule.IsOn(ctx));
        Assert.NotNull(rule.Detect(ctx));
    }

    [Fact]
    public void Fix_DisablesOnlyEnabledTasks_UndoReenablesExactlyThose()
    {
        var tasks = new FakeTasks();
        tasks.Tasks[Consolidator] = true;
        tasks.Tasks[UsbCeip] = false;
        var rule = new TaskSwitchRule(tasks);
        var ctx = TestContext.Empty();

        var prior = rule.Fix(ctx);
        Assert.False(tasks.Tasks[Consolidator]);
        Assert.Equal(ReadBackState.Held, ReadBack.StateOf(ctx, rule));

        rule.Undo(ctx, prior);
        Assert.True(tasks.Tasks[Consolidator]);
        Assert.False(tasks.Tasks[UsbCeip]);
    }

    [Fact]
    public void RealProbe_SetEnabledThrows_AndIsEnabledIsNull_WhenSchtasksFails()
    {
        var probe = new RealScheduledTaskProbe(new ScriptedRunner((_, _) => (1, "")));
        Assert.Throws<System.InvalidOperationException>(() => probe.SetEnabled(Consolidator, false));
        Assert.Null(probe.IsEnabled(Consolidator));
    }
}
