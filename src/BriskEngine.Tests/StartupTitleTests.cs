using System;
using BriskEngine.Actions;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

public sealed class StartupTitleTests
{
    private static StartupEntry E(string name, string? cmd) => new("HKCU", name, true, false, cmd);

    [Fact]
    public void For_UsesTheExesDescription()
    {
        var e = E("MicrosoftEdgeAutoLaunch_F6C5A0B1C2D3E4F5",
            @"""C:\Program Files\Edge\msedge.exe"" --no-startup-window");
        Assert.Equal("Microsoft Edge", StartupTitle.For(e, p => p.EndsWith("msedge.exe") ? "Microsoft Edge" : null));
    }

    [Theory]
    [InlineData("Update")]
    [InlineData("Electron")]
    [InlineData("  ")]
    public void For_GenericOrBlankDescription_FallsBackToTheCleanedName(string description)
    {
        var e = E("Discord", @"C:\Users\m\AppData\Local\Discord\Update.exe --processStart Discord.exe");
        Assert.Equal("Discord", StartupTitle.For(e, _ => description));
    }

    [Theory]
    [InlineData("MicrosoftEdgeAutoLaunch_F6C5A0B1C2D3E4F5", "MicrosoftEdgeAutoLaunch")]
    [InlineData("Teams_{8E1F2A3B-1111-2222-3333-444455556666}", "Teams")]
    [InlineData("Spotify.lnk", "Spotify")]
    [InlineData("Steam", "Steam")]
    [InlineData("DeadBeefCafe", "DeadBeefCafe")]
    public void For_NoDescription_StripsIdTailsAndLnk(string name, string expected) =>
        Assert.Equal(expected, StartupTitle.For(E(name, null), _ => null));

    [Fact]
    public void ExecutablePath_ExpandsVariablesAndDropsQuotesAndSwitches()
    {
        Environment.SetEnvironmentVariable("BRISK_T", @"C:\X");
        Assert.Equal(@"C:\X\a b\app.exe", StartupManager.ExecutablePath(@"""%BRISK_T%\a b\app.exe"" /min"));
        Assert.Null(StartupManager.ExecutablePath("rundll32 shell32,Control_RunDLL"));
    }
}
