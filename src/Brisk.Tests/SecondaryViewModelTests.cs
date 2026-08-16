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

file sealed class RegFake : IRegistryProbe
{
    public Dictionary<string, Dictionary<string, object>> Keys { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, object> Key(string k) =>
        Keys.TryGetValue(k, out var d) ? d : Keys[k] = new(StringComparer.OrdinalIgnoreCase);
    public string? GetString(string k, string v) => Key(k).TryGetValue(v, out var o) ? o as string : null;
    public void SetString(string k, string v, string value) => Key(k)[v] = value;
    public void DeleteValue(string k, string v) => Key(k).Remove(v);
    public byte[]? GetBytes(string k, string v) => Key(k).TryGetValue(v, out var o) ? o as byte[] : null;
    public void SetBytes(string k, string v, byte[] value) => Key(k)[v] = value;
    public int? GetInt(string k, string v) => null;
    public void SetInt(string k, string v, int value) { }
    public IReadOnlyList<string> GetValueNames(string k) => Key(k).Keys.ToList();
    public IReadOnlyList<string> GetSubKeyNames(string k) => Array.Empty<string>();
}

public sealed class SecondaryViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-vm2-").FullName;

    [Fact]
    public async Task Startup_ListsHeavyFirst_TogglesThroughHost()
    {
        var host = new FakeEngineHost();
        host.Startup.Add(new StartupEntry("HKCU", "MyTool", true, false));
        host.Startup.Add(new StartupEntry("HKCU", "Discord", true, true));
        var state = new AppState(host);
        var vm = new StartupViewModel(state, host, () => false);
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
        var vm = new StartupViewModel(state, host, () => false);
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
        var vm = new StartupViewModel(state, host, () => true);
        await state.ScanAsync();

        vm.Items[0].IsEnabled = false;

        Assert.Empty(host.StartupToggles);
        Assert.True(vm.Items[0].IsEnabled);   // reverted
        Assert.True(vm.ToggleFailed);
    }

    [Fact]
    public void Settings_SettersPersistAndApply()
    {
        var path = Path.Combine(_root, "settings.json");
        var settings = new Settings();
        var reg = new RegFake();
        var applied = new List<string>();
        var vm = new SettingsViewModel(settings, path,
            new StartupLauncher(reg, @"C:\x\brisk-app.exe"),
            theme => applied.Add("theme:" + theme),
            lang => applied.Add("lang:" + lang));

        vm.Theme = "dark";
        vm.Language = "tr";
        vm.DryRun = true;
        vm.StartWithWindows = true;

        Assert.Equal(new[] { "theme:dark", "lang:tr" }, applied);
        var reloaded = Settings.Load(path);
        Assert.Equal("dark", reloaded.Theme);
        Assert.Equal("tr", reloaded.Language);
        Assert.True(reloaded.DryRun);
        Assert.True(reloaded.StartWithWindows);
        Assert.NotNull(reg.GetString(
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "brisk"));

        vm.StartWithWindows = false;
        Assert.Null(reg.GetString(
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "brisk"));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
