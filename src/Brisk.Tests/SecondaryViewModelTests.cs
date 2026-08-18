using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Diagnostics;
using Xunit;

namespace Brisk.Tests;

public sealed class SecondaryViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-vm2-").FullName;

    private static Brisk.Localization.Loc EnglishLoc()
    {
        var loc = new Brisk.Localization.Loc();
        loc.SetLanguage("en");
        return loc;
    }

    [Fact]
    public async Task Startup_ListsHeavyFirst_TogglesThroughHost()
    {
        var host = new FakeEngineHost();
        host.Startup.Add(new StartupEntry("HKCU", "MyTool", true, false));
        host.Startup.Add(new StartupEntry("HKCU", "Discord", true, true));
        var state = new AppState(host);
        var vm = new StartupViewModel(state, host, EnglishLoc(), () => false);
        await state.ScanAsync();

        Assert.Equal(new[] { "Discord", "MyTool" },
            vm.Items.Select(i => i.Name).ToArray());

        vm.Items[0].IsEnabled = false;
        Assert.Equal(("HKCU", "Discord", false), Assert.Single(host.StartupToggles));
        Assert.False(vm.ToggleFailed);
    }

    [Fact]
    public async Task Startup_FailedToggle_RevertsAndFlags()
    {
        var host = new FakeEngineHost { StartupToggleResult = false };
        host.Startup.Add(new StartupEntry("HKLM", "Svc", true, false));
        var state = new AppState(host);
        var vm = new StartupViewModel(state, host, EnglishLoc(), () => false);
        await state.ScanAsync();

        vm.Items[0].IsEnabled = false;
        Assert.True(vm.Items[0].IsEnabled);   // reverted
        Assert.True(vm.ToggleFailed);
    }

    [Fact]
    public async Task Startup_DryRun_TogglesLikeFailedToggle_NeverCallsHost()
    {
        var host = new FakeEngineHost();
        host.Startup.Add(new StartupEntry("HKCU", "MyTool", true, false));
        var state = new AppState(host);
        var vm = new StartupViewModel(state, host, EnglishLoc(), () => true);
        await state.ScanAsync();

        vm.Items[0].IsEnabled = false;

        Assert.Empty(host.StartupToggles);
        Assert.True(vm.Items[0].IsEnabled);   // reverted
        Assert.True(vm.ToggleFailed);
    }

    [Fact]
    public async Task Startup_DescribesKnownApps_AndFlagsSystemEntries()
    {
        var loc = EnglishLoc();
        var host = new FakeEngineHost();
        host.Startup.Add(new StartupEntry("HKCU", "com.squirrel.Teams.Teams", true, true));
        host.Startup.Add(new StartupEntry("HKCU", "OneDrive", true, false));
        host.Startup.Add(new StartupEntry("HKCU", "SecurityHealth", true, false));
        host.Startup.Add(new StartupEntry("HKLM", "RandomOemService", true, false));
        host.Startup.Add(new StartupEntry("HKCU", "MyTool", true, false));
        var state = new AppState(host);
        var vm = new StartupViewModel(state, host, loc, () => false);
        await state.ScanAsync();

        string Desc(string name) => vm.Items.Single(i => i.Name == name).Description;
        // known apps match by contained name, OrdinalIgnoreCase
        Assert.Equal(loc["startup.app.teams"], Desc("com.squirrel.Teams.Teams"));
        Assert.Equal(loc["startup.app.onedrive"], Desc("OneDrive"));
        // system-ish: known system names, and HKLM outside the heavy table
        Assert.Equal(loc["startup.system.hint"], Desc("SecurityHealth"));
        Assert.Equal(loc["startup.system.hint"], Desc("RandomOemService"));
        // unknown user apps get no invented description
        Assert.Equal("", Desc("MyTool"));
    }

    [Fact]
    public void Settings_SettersPersistAndApply()
    {
        var path = Path.Combine(_root, "settings.json");
        var settings = new Settings();
        var runner = new FakeProcessRunner();
        var applied = new List<string>();
        var vm = new SettingsViewModel(settings, path,
            new StartupLauncher(runner, @"C:\x\brisk-app.exe"),
            theme => applied.Add("theme:" + theme),
            lang => applied.Add("lang:" + lang));

        // "light" — a real change from the dark default, so the setter's
        // no-change guard doesn't swallow the apply callback.
        vm.Theme = "light";
        vm.Language = "tr";
        vm.DryRun = true;
        vm.StartWithWindows = true;

        Assert.Equal(new[] { "theme:light", "lang:tr" }, applied);
        var reloaded = Settings.Load(path);
        Assert.Equal("light", reloaded.Theme);
        Assert.Equal("tr", reloaded.Language);
        Assert.True(reloaded.DryRun);
        Assert.True(reloaded.StartWithWindows);
        Assert.Contains(runner.Calls,
            c => c.Exe == "schtasks.exe" && c.Args.Contains("/Create"));

        vm.StartWithWindows = false;
        Assert.Contains(runner.Calls,
            c => c.Exe == "schtasks.exe" && c.Args.Contains("/Delete"));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
