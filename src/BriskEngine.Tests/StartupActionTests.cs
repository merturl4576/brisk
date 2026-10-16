using System;
using System.Collections.Generic;
using BriskEngine.Actions;
using BriskEngine.Actions.Rules;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.RealProbes;
using BriskEngine.Paths;
using Xunit;

namespace BriskEngine.Tests;

public sealed class StartupActionTests
{
    private const string Run = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";

    [Fact]
    public void List_CarriesTheCommand_AndIncludesStartupFolderShortcuts()
    {
        var reg = new FakeRegistry();
        reg.SetString(Run, "Discord", @"""C:\Users\x\AppData\Local\Discord\Update.exe"" --processStart Discord.exe");
        var files = new FakeFiles();
        var folder = PathExpander.Expand(StartupManager.Folders[0].Folder)!;
        files.FileLists[folder] = new List<string> { $@"{folder}\Wispr Flow.lnk", $@"{folder}\desktop.ini" };
        var mgr = new StartupManager(reg, null, null, files);

        var entries = mgr.List();

        Assert.Contains(entries, e => e.Name == "Discord" && e.Command!.Contains("Update.exe") && e.Enabled);
        Assert.Contains(entries, e => e.Hive == "Folder" && e.Name == "Wispr Flow.lnk" && e.Enabled);
        Assert.DoesNotContain(entries, e => e.Name == "desktop.ini");
    }

    [Fact]
    public void SetEnabled_StartupFolderShortcut_WritesTheApprovedValue()
    {
        var reg = new FakeRegistry();
        var files = new FakeFiles();
        var folder = PathExpander.Expand(StartupManager.Folders[0].Folder)!;
        files.FileLists[folder] = new List<string> { $@"{folder}\Wispr Flow.lnk" };
        var mgr = new StartupManager(reg, null, null, files);

        Assert.True(mgr.SetEnabled("Folder", "Wispr Flow.lnk", false));

        Assert.Equal(3, reg.GetBytes(StartupManager.Folders[0].Approved, "Wispr Flow.lnk")![0]);
        Assert.Contains(mgr.List(), e => e.Name == "Wispr Flow.lnk" && !e.Enabled);
    }

    [Fact]
    public void StartupEntryRule_DisablesAndReenables()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetString(Run, "Spotify", @"C:\Spotify\Spotify.exe /minimized");
        var mgr = new StartupManager(reg, null);
        var rule = new StartupEntryRule(mgr, "HKCU", "Spotify");

        Assert.Equal("startup:HKCU:Spotify", rule.Id);
        Assert.True(rule.IsOn(ctx));
        var prior = rule.Fix(ctx);
        Assert.False(rule.IsOn(ctx));
        Assert.Equal(ReadBackState.Held, ReadBack.StateOf(ctx, rule));
        rule.Undo(ctx, prior);
        Assert.True(rule.IsOn(ctx));
    }

    [Fact]
    public void StartupEntryRule_RefusedWrite_ThrowsUnauthorized()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetString(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Vendor", @"C:\v.exe");
        reg.DenyWriteKeys.Add(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
        var rule = new StartupEntryRule(new StartupManager(reg, null), "HKLM", "Vendor");
        Assert.Throws<UnauthorizedAccessException>(() => rule.Fix(ctx));
    }

    [Theory]
    [InlineData("startup:HKCU:Discord", "HKCU", "Discord")]
    [InlineData("startup:Folder:My: App.lnk", "Folder", "My: App.lnk")]
    public void TryParseId_RoundTrips(string id, string hive, string name)
    {
        Assert.True(StartupEntryRule.TryParseId(id, out var h, out var n));
        Assert.Equal(hive, h);
        Assert.Equal(name, n);
        Assert.Equal(id, StartupEntryRule.IdFor(h, n));
    }

    [Fact]
    public void TryParseId_RejectsOtherIds() =>
        Assert.False(StartupEntryRule.TryParseId("power-mode", out _, out _));

    [Theory]
    [InlineData("Discord", null, StartupProtectionKind.None)]
    [InlineData("RtkAudUService", null, StartupProtectionKind.Hardware)]
    [InlineData("OneDrive", null, StartupProtectionKind.Sync)]
    [InlineData("Bitwarden", null, StartupProtectionKind.Passwords)]
    [InlineData("NordVPN", null, StartupProtectionKind.Vpn)]
    [InlineData("PowerToys", null, StartupProtectionKind.Keyboard)]
    [InlineData("Time Tracker", null, StartupProtectionKind.None)]
    [InlineData("SecurityHealth", null, StartupProtectionKind.Security)]
    [InlineData("Tray", @"C:\Program Files\McAfee\mcuicnt.exe", StartupProtectionKind.Security)]
    public void Classify(string name, string? command, StartupProtectionKind expected) =>
        Assert.Equal(expected, StartupProtection.Classify(name, command, Array.Empty<string>()));

    [Fact]
    public void Classify_ARegisteredAntivirusNameIsSecurity() =>
        Assert.Equal(StartupProtectionKind.Security,
            StartupProtection.Classify("Acme Shield Tray", null, new[] { "Acme Shield Total" }));

    [Fact]
    public void Hibernation_OffAndOn()
    {
        var exists = true;
        var runner = new ScriptedRunner((_, args) => { exists = args.EndsWith("on"); return (0, ""); });
        var rule = new HibernationRule(runner, () => exists);
        Assert.True(rule.IsOn(TestContext.Empty()));
        var prior = rule.Fix(TestContext.Empty());
        Assert.False(rule.IsOn(TestContext.Empty()));
        rule.Undo(TestContext.Empty(), prior);
        Assert.True(rule.IsOn(TestContext.Empty()));
        Assert.Equal(new[] { "powercfg.exe /hibernate off", "powercfg.exe /hibernate on" }, runner.Commands);
    }

    [Theory]
    [InlineData((byte)128, false)]
    [InlineData((byte)8, true)]
    [InlineData((byte)1, true)]
    [InlineData((byte)255, true)]
    public void IsLaptop_UnknownCountsAsLaptop(byte flag, bool laptop) =>
        Assert.Equal(laptop, RealPowercfgProbe.IsLaptop(flag));
}
