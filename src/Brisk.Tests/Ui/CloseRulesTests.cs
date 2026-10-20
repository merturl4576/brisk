using Brisk.Localization;
using Brisk.Tray;
using Brisk.Ui.Shell;

namespace Brisk.Tests.Ui;

public sealed class CloseRulesTests
{
    [Theory]
    [InlineData(false, false, false, CloseAction.Exit)]
    [InlineData(true, false, false, CloseAction.Hide)]
    [InlineData(false, true, false, CloseAction.Hide)]
    [InlineData(false, false, true, CloseAction.Hide)]
    [InlineData(true, true, true, CloseAction.Hide)]
    public void OnClose_HidesOnlyWhenSomethingStillNeedsBrisk(bool deferred, bool running, bool autostart, CloseAction expected) =>
        Assert.Equal(expected, CloseRules.OnClose(deferred, running, autostart));

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void ShouldExitWhileHidden_OnceNothingKeepsItAlive(bool deferred, bool running, bool autostart, bool expected) =>
        Assert.Equal(expected, CloseRules.ShouldExitWhileHidden(deferred, running, autostart));

    [Fact]
    public void TrayMenu_IsOpenAndExit()
    {
        var loc = new Loc();
        loc.SetLanguage("tr");
        Assert.Equal(new[] { loc["tray.open"], loc["tray.exit"] }, TrayIcon.MenuLabels(loc));
    }
}
