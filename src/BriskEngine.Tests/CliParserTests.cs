using Brisk.Cli;
using Xunit;

namespace BriskEngine.Tests;

public class CliParserTests
{
    [Fact]
    public void NoArgs_IsHelp() => Assert.Equal("help", CliParser.Parse(new string[0]).Verb);

    [Fact]
    public void ScanJson()
    {
        var cmd = CliParser.Parse(new[] { "scan", "--json" });
        Assert.Equal("scan", cmd.Verb);
        Assert.True(cmd.Json);
    }

    [Fact]
    public void FixRuleWithYes()
    {
        var cmd = CliParser.Parse(new[] { "fix", "--rule", "power-plan", "--yes" });
        Assert.Equal(("fix", "power-plan", true), (cmd.Verb, cmd.RuleId, cmd.Yes));
    }

    [Fact]
    public void FixUndo()
    {
        var cmd = CliParser.Parse(new[] { "fix", "--rule", "power-plan", "--undo", "--yes" });
        Assert.True(cmd.Undo);
    }

    [Fact]
    public void CleanLevel()
    {
        var cmd = CliParser.Parse(new[] { "clean", "--level", "developer" });
        Assert.Equal("developer", cmd.Level);
        Assert.False(cmd.Yes);
    }

    [Fact]
    public void BadLevel_IsError()
    {
        Assert.Equal("error", CliParser.Parse(new[] { "clean", "--level", "mega" }).Verb);
    }

    [Fact]
    public void MissingRuleArgument_IsError()
    {
        Assert.Equal("error", CliParser.Parse(new[] { "fix", "--rule" }).Verb);
    }

    [Fact]
    public void UnknownVerb_IsError()
    {
        Assert.Equal("error", CliParser.Parse(new[] { "explode" }).Verb);
    }

    [Fact]
    public void Clean_ParsesTarget()
    {
        var cmd = CliParser.Parse(new[] { "clean", "--target", "windows-temp", "--yes" });
        Assert.Equal("clean", cmd.Verb);
        Assert.Equal("windows-temp", cmd.Target);
        Assert.True(cmd.Yes);
    }

    [Fact]
    public void Clean_TargetWithoutValue_IsError()
    {
        Assert.Equal("error", CliParser.Parse(new[] { "clean", "--target" }).Verb);
    }

    /// FIX WAVE, Finding 1. The display fix is applied for this session only,
    /// so the console needs the same answer the GUI's overlay collects.
    [Fact]
    public void FixKeep_Parses()
    {
        var cmd = CliParser.Parse(
            new[] { "fix", "--rule", "display-refresh", "--keep", "--yes" });
        Assert.True(cmd.Keep);
        Assert.Equal("display-refresh", cmd.RuleId);
    }

    [Fact]
    public void Keep_DefaultsToFalse()
    {
        Assert.False(CliParser.Parse(new[] { "fix", "--all", "--yes" }).Keep);
    }
}
