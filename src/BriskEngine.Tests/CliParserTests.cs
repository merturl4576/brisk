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
}
