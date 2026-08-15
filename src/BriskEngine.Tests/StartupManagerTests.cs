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

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
