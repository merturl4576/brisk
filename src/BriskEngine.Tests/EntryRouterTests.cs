using Brisk.Cli;
using Xunit;

namespace BriskEngine.Tests;

/// One executable now answers to two audiences: a window when double-clicked,
/// a console when typed at. The router is the seam that decides, and it is
/// tested rather than trusted because getting it wrong has two loud failure
/// modes — brisk's own autostart ("--tray") opening a console instead of a
/// tray icon, and "brisk scan" opening a window instead of printing.
public class EntryRouterTests
{
    [Fact]
    public void NoArgs_IsTheWindow() =>
        Assert.False(EntryRouter.RoutesToConsole(new string[0]));

    /// The scheduled task brisk writes for its own autostart passes exactly
    /// this. Routing it to the console would make brisk fail to start with
    /// Windows while claiming, in its own settings, that it does.
    [Fact]
    public void TrayFlag_IsTheWindow() =>
        Assert.False(EntryRouter.RoutesToConsole(new[] { "--tray" }));

    [Theory]
    [InlineData("scan")]
    [InlineData("fix")]
    [InlineData("clean")]
    [InlineData("targets")]
    [InlineData("rules")]
    [InlineData("version")]
    public void EveryCliVerb_IsTheConsole(string verb) =>
        Assert.True(EntryRouter.RoutesToConsole(new[] { verb }));

    /// A mistyped verb belongs to the console, where it gets "unknown command
    /// 'scna'". Sending it to the window would answer a typo with a silent
    /// success — brisk telling the user nothing about what it just ignored.
    [Fact]
    public void MistypedVerb_IsTheConsole_SoItCanSayWhatWasWrong() =>
        Assert.True(EntryRouter.RoutesToConsole(new[] { "scna" }));

    /// Windows itself passes switches to GUI processes it restarts or embeds.
    /// Anything switch-shaped that brisk's console does not claim stays with
    /// the window, so a future GUI flag needs no edit here.
    [Theory]
    [InlineData("-Embedding")]
    [InlineData("/restart")]
    [InlineData("--some-future-window-flag")]
    public void UnknownSwitches_StayWithTheWindow(string arg) =>
        Assert.False(EntryRouter.RoutesToConsole(new[] { arg }));

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("/?")]
    [InlineData("--version")]
    public void HelpAndVersionSwitches_AreTheConsole(string arg) =>
        Assert.True(EntryRouter.RoutesToConsole(new[] { arg }));

    /// The parser knows verbs, not switches, so the router hands it the verb
    /// those switches mean. Without this, "brisk --help" reaches the parser as
    /// an unknown command and answers a help request with an error.
    [Theory]
    [InlineData("--help", new string[0])]
    [InlineData("-h", new string[0])]
    [InlineData("/?", new string[0])]
    [InlineData("--version", new[] { "version" })]
    [InlineData("-v", new[] { "version" })]
    public void SwitchesAreTranslatedToVerbs(string arg, string[] expected) =>
        Assert.Equal(expected, EntryRouter.Normalize(new[] { arg }));

    [Fact]
    public void RealArgumentsPassThroughUntouched()
    {
        var args = new[] { "clean", "--level", "safe", "--yes" };
        Assert.Equal(args, EntryRouter.Normalize(args));
    }
}
