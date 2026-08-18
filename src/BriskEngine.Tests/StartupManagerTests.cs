using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using Xunit;

namespace BriskEngine.Tests;

sealed class FakeStartupRegistry : IRegistryProbe
{
    public Dictionary<string, Dictionary<string, object>> Keys { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DeniedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, object> Key(string k) =>
        Keys.TryGetValue(k, out var d) ? d : Keys[k] = new(StringComparer.OrdinalIgnoreCase);

    public string? GetString(string k, string v) => Key(k).TryGetValue(v, out var o) ? o as string : null;
    public void SetString(string k, string v, string value) { Deny(k); Key(k)[v] = value; }
    public void DeleteValue(string k, string v) { Deny(k); Key(k).Remove(v); }
    public byte[]? GetBytes(string k, string v) => Key(k).TryGetValue(v, out var o) ? o as byte[] : null;
    public void SetBytes(string k, string v, byte[] value) { Deny(k); Key(k)[v] = value; }
    public int? GetInt(string k, string v) => Key(k).TryGetValue(v, out var o) ? o as int? : null;
    public void SetInt(string k, string v, int value) { Deny(k); Key(k)[v] = value; }
    public IReadOnlyList<string> GetValueNames(string k) => Key(k).Keys.ToList();
    public IReadOnlyList<string> GetSubKeyNames(string k) => Array.Empty<string>();
    private void Deny(string k) { if (DeniedKeys.Contains(k)) throw new UnauthorizedAccessException(k); }
}

public sealed class StartupManagerTests : IDisposable
{
    private const string HkcuRun = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string HkcuApproved =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string HklmRun = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string HklmApproved =
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly string _root = Directory.CreateTempSubdirectory("brisk-sm-").FullName;
    private readonly FakeStartupRegistry _reg = new();

    private StartupManager Manager() =>
        new(_reg, new ActionLog(Path.Combine(_root, "log.jsonl")));

    [Fact]
    public void List_ReportsEnabledStateAndHeavyFlag()
    {
        _reg.SetString(HkcuRun, "Discord", "x.exe");
        _reg.SetString(HkcuRun, "MyTool", "y.exe");
        _reg.SetBytes(HkcuApproved, "MyTool",
            new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        var items = Manager().List();
        var discord = items.Single(i => i.Name == "Discord");
        var mytool = items.Single(i => i.Name == "MyTool");
        Assert.True(discord.Enabled);
        Assert.True(discord.KnownHeavy);
        Assert.False(mytool.Enabled);
        Assert.False(mytool.KnownHeavy);
        Assert.Equal("HKCU", discord.Hive);
    }

    [Fact]
    public void SetEnabled_False_WritesDisabledBytes_AndLogs()
    {
        _reg.SetString(HkcuRun, "Spotify", "s.exe");
        Assert.True(Manager().SetEnabled("HKCU", "Spotify", false));
        var bytes = _reg.GetBytes(HkcuApproved, "Spotify")!;
        Assert.Equal(12, bytes.Length);
        Assert.Equal(1, bytes[0] & 1);
        var log = File.ReadAllText(Path.Combine(_root, "log.jsonl"));
        Assert.Contains("Spotify", log);
        Assert.Contains("disable", log);
    }

    [Fact]
    public void SetEnabled_True_WritesEnabledBytes()
    {
        _reg.SetString(HkcuRun, "Spotify", "s.exe");
        var mgr = Manager();
        mgr.SetEnabled("HKCU", "Spotify", false);
        Assert.True(mgr.SetEnabled("HKCU", "Spotify", true));
        Assert.Equal(0, _reg.GetBytes(HkcuApproved, "Spotify")![0] & 1);
    }

    [Fact]
    public void SetEnabled_DeniedHive_ReturnsFalse()
    {
        _reg.SetString(HklmRun, "Svc", "s.exe");
        _reg.DeniedKeys.Add(HklmApproved);
        Assert.False(Manager().SetEnabled("HKLM", "Svc", false));
    }

    private static void StoreTask(FakeRegistry reg, string pfn, string task, int state) =>
        StoreRegistry.Task(reg, pfn, task, state);

    [Fact]
    public void StoreApps_AreListed_WithTheirEnabledState()
    {
        var reg = new FakeRegistry();
        StoreTask(reg, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "Spotify", 2);
        StoreTask(reg, "Microsoft.Copilot_8wekyb3d8bbwe", "Copilot.StartupTaskId", 0);

        var items = new StartupManager(reg, null).List();

        var spotify = Assert.Single(items, i => i.Name.Contains("Spotify", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Store", spotify.Hive);
        Assert.True(spotify.Enabled);
        Assert.True(spotify.KnownHeavy);          // the heavy table already lists Spotify

        var copilot = Assert.Single(items, i => i.Name.Contains("Copilot", StringComparison.OrdinalIgnoreCase));
        Assert.False(copilot.Enabled);
    }

    // The package family name is publisher-qualified and hash-suffixed. A user
    // recognises "SpotifyMusic", not "SpotifyAB.SpotifyMusic_zpdnekdrzrea0".
    [Fact]
    public void StoreAppName_DropsThePublisherAndTheHash()
    {
        var reg = new FakeRegistry();
        StoreTask(reg, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "Spotify", 2);
        StoreTask(reg, "MSTeams_8wekyb3d8bbwe", "TeamsTfwStartupTask", 2);

        var names = new StartupManager(reg, null).List().Select(i => i.Name).ToArray();

        Assert.Contains("SpotifyMusic", names);
        Assert.Contains("MSTeams", names);
    }

    [Fact]
    public void DisablingAStoreApp_WritesStateZero_AndEnablingWritesTwo()
    {
        var reg = new FakeRegistry();
        StoreTask(reg, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "Spotify", 2);
        var mgr = new StartupManager(reg, null);

        Assert.True(mgr.SetEnabled("Store", "SpotifyMusic", enabled: false));
        Assert.Equal(0, reg.GetInt($@"{StartupManager.StoreRoot}\SpotifyAB.SpotifyMusic_zpdnekdrzrea0\Spotify", "State"));

        Assert.True(mgr.SetEnabled("Store", "SpotifyMusic", enabled: true));
        Assert.Equal(2, reg.GetInt($@"{StartupManager.StoreRoot}\SpotifyAB.SpotifyMusic_zpdnekdrzrea0\Spotify", "State"));
    }

    // One package can register several startup tasks — Spotify registers two.
    // Toggling the app must move all of them, or the app still starts.
    [Fact]
    public void APackageWithSeveralTasks_TogglesAllOfThem()
    {
        var reg = new FakeRegistry();
        StoreTask(reg, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "Spotify", 2);
        StoreTask(reg, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "SpotifyLauncher", 2);

        var items = new StartupManager(reg, null).List();
        Assert.Single(items, i => i.Hive == "Store");        // one row, not two

        new StartupManager(reg, null).SetEnabled("Store", "SpotifyMusic", enabled: false);
        Assert.Equal(0, reg.GetInt($@"{StartupManager.StoreRoot}\SpotifyAB.SpotifyMusic_zpdnekdrzrea0\Spotify", "State"));
        Assert.Equal(0, reg.GetInt($@"{StartupManager.StoreRoot}\SpotifyAB.SpotifyMusic_zpdnekdrzrea0\SpotifyLauncher", "State"));
    }

    [Fact]
    public void NoStoreApps_ChangesNothingAboutTheRunEntries()
    {
        var reg = new FakeRegistry();
        reg.SetString(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "OneDrive", "x");

        var items = new StartupManager(reg, null).List();

        Assert.Single(items);
        Assert.Equal("HKCU", items[0].Hive);
    }

    // The State value mirrors WinRT's StartupTaskState: 0 Disabled,
    // 1 DisabledByUser, 2 Enabled, 3 DisabledByPolicy, 4 EnabledByPolicy.
    // Three of the five mean the task does not start, so reading "anything but
    // 0" as enabled had brisk claiming two disabled states start with Windows.
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void StateIsReadAsTheStartupTaskStateEnum_NotAsNonZero(int state, bool starts)
    {
        var reg = new FakeRegistry();
        StoreTask(reg, "Contoso.Widget_abcdefghijklm", "WidgetStartup", state);

        var row = Assert.Single(new StartupManager(reg, null).List());

        Assert.Equal(starts, row.Enabled);
    }

    // The real WhatsApp record on the maintainer's machine: a GUID-named task
    // enabled, the named one disabled. The app starts, so the row says so --
    // and the toggle writes every task, including the one already at the
    // target, or enabling would leave half the package behind.
    [Fact]
    public void APackageWithOneTaskOnAndOneOff_ReadsEnabled_AndTogglesBoth()
    {
        var reg = new FakeRegistry();
        const string Pfn = "5319275A.WhatsAppDesktop_cv1g1gvanyjgm";
        StoreTask(reg, Pfn, "2defd21c-0b9e-4e4e-873a-2a68c47d7da5", 2);
        StoreTask(reg, Pfn, "WhatsAppStartupTask", 0);
        var guidTask = $@"{StartupManager.StoreRoot}\{Pfn}\2defd21c-0b9e-4e4e-873a-2a68c47d7da5";
        var namedTask = $@"{StartupManager.StoreRoot}\{Pfn}\WhatsAppStartupTask";

        var row = Assert.Single(new StartupManager(reg, null).List(), i => i.Hive == "Store");
        Assert.True(row.Enabled);

        Assert.True(new StartupManager(reg, null).SetEnabled("Store", "WhatsAppDesktop", enabled: false));
        Assert.Equal(0, reg.GetInt(guidTask, "State"));
        Assert.Equal(0, reg.GetInt(namedTask, "State"));

        Assert.True(new StartupManager(reg, null).SetEnabled("Store", "WhatsAppDesktop", enabled: true));
        Assert.Equal(2, reg.GetInt(guidTask, "State"));
        Assert.Equal(2, reg.GetInt(namedTask, "State"));
    }

    // The package key also holds Schemas, SplashScreen and friends. Nothing
    // there starts with Windows, and this guard is the only thing keeping them
    // off the user's startup page.
    [Fact]
    public void SubkeysWithoutAState_AreNotStartupTasks()
    {
        var reg = new FakeRegistry();
        var root = StartupManager.StoreRoot;
        StoreTask(reg, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "Spotify", 2);
        reg.SubKeys[$@"{root}\SpotifyAB.SpotifyMusic_zpdnekdrzrea0"].Add("Schemas");
        // ...and a package whose subkeys are ALL of that kind gets no row.
        reg.SubKeys[root].Add("Contoso.NoStartupTasks_abcdefghijklm");
        reg.SubKeys[$@"{root}\Contoso.NoStartupTasks_abcdefghijklm"] =
            new List<string> { "Schemas", "PersistedStorageItemTable", "SplashScreen" };

        var items = new StartupManager(reg, null).List();

        Assert.Equal(new[] { "SpotifyMusic" }, items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public void SetEnabled_ForAStorePackageThatIsNotThere_ReturnsFalse()
    {
        var reg = new FakeRegistry();
        StoreTask(reg, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "Spotify", 2);

        Assert.False(new StartupManager(reg, null).SetEnabled("Store", "NoSuchApp", enabled: false));
        // ...and it touched nothing on the way to saying so.
        Assert.Equal(2, reg.GetInt($@"{StartupManager.StoreRoot}\SpotifyAB.SpotifyMusic_zpdnekdrzrea0\Spotify", "State"));
    }

    [Fact]
    public void SetEnabled_WhenTheStoreTaskDeniesTheWrite_ReturnsFalse()
    {
        var reg = new FakeRegistry();
        StoreTask(reg, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "Spotify", 2);
        reg.DenyWriteKeys.Add(
            $@"{StartupManager.StoreRoot}\SpotifyAB.SpotifyMusic_zpdnekdrzrea0\Spotify");

        Assert.False(new StartupManager(reg, null).SetEnabled("Store", "SpotifyMusic", enabled: false));
    }

    // Two publishers can ship packages that shorten to the same name. The name
    // is also the handle SetEnabled resolves, so an ambiguous label would put
    // two identical rows on the page and make either one write to both.
    [Fact]
    public void PackagesThatShortenAlike_AreLabelledApart_AndToggleIndependently()
    {
        var reg = new FakeRegistry();
        StoreTask(reg, "Microsoft.Copilot_8wekyb3d8bbwe", "Copilot.StartupTaskId", 2);
        StoreTask(reg, "Contoso.Copilot_zzzzzzzzzzzzz", "ContosoStartup", 2);
        var microsoft = $@"{StartupManager.StoreRoot}\Microsoft.Copilot_8wekyb3d8bbwe\Copilot.StartupTaskId";
        var contoso = $@"{StartupManager.StoreRoot}\Contoso.Copilot_zzzzzzzzzzzzz\ContosoStartup";

        var names = new StartupManager(reg, null).List().Select(i => i.Name).ToArray();
        Assert.Equal(2, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("Microsoft.Copilot", names);
        Assert.Contains("Contoso.Copilot", names);

        Assert.True(new StartupManager(reg, null).SetEnabled("Store", "Microsoft.Copilot", enabled: false));
        Assert.Equal(0, reg.GetInt(microsoft, "State"));
        Assert.Equal(2, reg.GetInt(contoso, "State"));   // the neighbour is untouched
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
