using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brisk.Services;
using BriskEngine.Diagnostics;
using Xunit;

namespace Brisk.Tests;

file sealed class MemRegistry : IRegistryProbe
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
    public int? GetInt(string k, string v) => Key(k).TryGetValue(v, out var o) ? o as int? : null;
    public void SetInt(string k, string v, int value) => Key(k)[v] = value;
    public IReadOnlyList<string> GetValueNames(string k) => Key(k).Keys.ToList();
    public IReadOnlyList<string> GetSubKeyNames(string k) => Array.Empty<string>();
}

public sealed class SettingsTests : IDisposable
{
    private const string RunKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-set-").FullName;

    [Fact]
    public void Load_MissingFile_GivesDefaults()
    {
        var s = Settings.Load(Path.Combine(_root, "nope", "settings.json"));
        Assert.Equal("system", s.Language);
        Assert.Equal("system", s.Theme);
        Assert.False(s.DryRun);
        Assert.False(s.StartWithWindows);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(_root, "sub", "settings.json");
        new Settings { Language = "tr", Theme = "dark", DryRun = true }.Save(path);
        var s = Settings.Load(path);
        Assert.Equal("tr", s.Language);
        Assert.Equal("dark", s.Theme);
        Assert.True(s.DryRun);
    }

    [Fact]
    public void Load_CorruptFile_GivesDefaults()
    {
        var path = Path.Combine(_root, "bad.json");
        File.WriteAllText(path, "{{{ nope");
        Assert.Equal("system", Settings.Load(path).Language);
    }

    [Fact]
    public void StartupLauncher_OnWritesQuotedCommand_OffRemoves()
    {
        var reg = new MemRegistry();
        var launcher = new StartupLauncher(reg, @"C:\Apps\brisk-app.exe");

        launcher.Apply(true);
        Assert.True(launcher.IsOn());
        Assert.Equal("\"C:\\Apps\\brisk-app.exe\" --tray", reg.GetString(RunKey, "brisk"));

        launcher.Apply(false);
        Assert.False(launcher.IsOn());
        Assert.Null(reg.GetString(RunKey, "brisk"));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
