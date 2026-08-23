using System;
using System.IO;
using Brisk.Cli;
using Xunit;

namespace BriskEngine.Tests;

/// "--help" is what people type before they read anything, and answering it
/// with "unknown command '--help'" is brisk being unhelpful about the one
/// question it exists to answer. The translation lives in the console entry
/// point rather than beside the window, so both executables inherit it.
public class CliHelpSwitchTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("/?")]
    public void HelpSwitches_PrintHelp_AndSucceed(string arg)
    {
        var (code, output) = Capture(() => Program.Run(new[] { arg }));

        Assert.Equal(0, code);
        Assert.DoesNotContain("unknown command", output);
        Assert.Contains("scan", output);
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    public void VersionSwitches_PrintTheVersion(string arg)
    {
        var (code, output) = Capture(() => Program.Run(new[] { arg }));

        Assert.Equal(0, code);
        Assert.Equal(BriskEngine.EngineInfo.Version, output.Trim());
    }

    /// The typo still has to be reported. A router that swallowed everything
    /// switch-shaped would be the same silence in a different place.
    [Fact]
    public void UnknownCommand_IsStillReported()
    {
        var (code, _) = Capture(() => Program.Run(new[] { "scna" }));

        Assert.Equal(2, code);
    }

    /// brisk.exe cannot render the card (no WPF) and says exactly why and
    /// where to go — an unknown-command error would be a lie about the
    /// reason for refusing.
    [Fact]
    public void Report_InTheStandaloneCli_RefusesWithThePreciseMessage()
    {
        var (code, output) = Capture(() => Program.Run(new[] { "report" }));

        Assert.Equal(2, code);
        Assert.Contains("brisk-app.exe report", output);
    }

    /// The flags do not change the answer. Letting the parser see the line
    /// first turned 'report --out card.png' into "bad argument '--out'" —
    /// true about the flag, and the same lie about WHY brisk.exe refuses.
    [Fact]
    public void Report_WithFlags_RefusesForTheSameReason()
    {
        var (code, output) = Capture(
            () => Program.Run(new[] { "report", "--out", "card.png" }));

        Assert.Equal(2, code);
        Assert.Contains("brisk-app.exe report", output);
        Assert.DoesNotContain("bad argument", output);
    }

    private static (int Code, string Output) Capture(Func<int> run)
    {
        var stdout = Console.Out;
        var stderr = Console.Error;
        var buffer = new StringWriter();
        try
        {
            Console.SetOut(buffer);
            Console.SetError(buffer);
            return (run(), buffer.ToString());
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }
    }
}
