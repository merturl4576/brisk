using System;
using System.Collections.Generic;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;

namespace BriskEngine.Tests;

public sealed class FakePowercfg : IPowercfgProbe
{
    public (Guid Id, string Name) Active;
    public List<(Guid Id, string Name)> Schemes = new();
    public List<Guid> SetCalls = new();
    public (Guid Id, string Name) GetActiveScheme() => Active;
    public IReadOnlyList<(Guid Id, string Name)> ListSchemes() => Schemes;
    public void SetActive(Guid id)
    {
        SetCalls.Add(id);
        Active = Schemes.Find(s => s.Id == id);
    }
}

public sealed class FakeRegistry : IRegistryProbe
{
    // key = $"{keyPath}::{valueName}"
    public Dictionary<string, object> Values = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> SubKeys = new(StringComparer.OrdinalIgnoreCase);
    private static string K(string k, string v) => $"{k}::{v}";
    public string? GetString(string k, string v) => Values.TryGetValue(K(k, v), out var o) ? o as string : null;
    public void SetString(string k, string v, string value) => Values[K(k, v)] = value;
    public void DeleteValue(string k, string v) => Values.Remove(K(k, v));
    public byte[]? GetBytes(string k, string v) => Values.TryGetValue(K(k, v), out var o) ? o as byte[] : null;
    public void SetBytes(string k, string v, byte[] value) => Values[K(k, v)] = value;
    public int? GetInt(string k, string v) => Values.TryGetValue(K(k, v), out var o) ? o as int? : null;
    public void SetInt(string k, string v, int value) => Values[K(k, v)] = value;
    public IReadOnlyList<string> GetValueNames(string keyPath)
    {
        var names = new List<string>();
        foreach (var key in Values.Keys)
            if (key.StartsWith(keyPath + "::", StringComparison.OrdinalIgnoreCase))
                names.Add(key[(keyPath.Length + 2)..]);
        return names;
    }
    public IReadOnlyList<string> GetSubKeyNames(string keyPath) =>
        SubKeys.TryGetValue(keyPath, out var s) ? s : new List<string>();
}

public sealed class FakeProcessInfo : IProcessInfoProbe
{
    public List<(string Name, long WorkingSetBytes)> Top = new();
    public double MemoryLoad = 40;
    // Tuple element names MUST match the interface exactly (CS8141).
    public IReadOnlyList<(string Name, long WorkingSetBytes)> TopByMemory(int count) =>
        Top.GetRange(0, Math.Min(count, Top.Count));
    public double MemoryLoadPercent() => MemoryLoad;
}

public sealed class FakeSensors : ISensorProbe
{
    public double? CpuTemp; public double? GpuTemp; public int Gpus = 1;
    public double? CpuTempC() => CpuTemp;
    public double? GpuTempC() => GpuTemp;
    public int GpuCount() => Gpus;
}

public sealed class FakeDisk : IDiskInfoProbe
{
    public long Free = 500L << 30; public long Total = 1000L << 30;
    public long FreeBytes(string driveRoot) => Free;
    public long TotalBytes(string driveRoot) => Total;
}

public sealed class FakeFiles : IFileProbe
{
    public Dictionary<string, string> Texts = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> FileLists = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> Sizes = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DateTime?> NewestWrites = new(StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => Texts.ContainsKey(path);
    public string? ReadAllText(string path) =>
        Texts.TryGetValue(path, out var text) ? text : null;
    public void WriteAllText(string path, string content) => Texts[path] = content;
    public IReadOnlyList<string> ListFiles(string directory) =>
        FileLists.TryGetValue(directory, out var files) ? files : new List<string>();
    public long DirectorySizeBytes(string path) =>
        Sizes.TryGetValue(path, out var size) ? size : 0;
    public DateTime? NewestWriteUtc(string path, int limit = 1500) =>
        NewestWrites.TryGetValue(path, out var dt) ? dt : null;
}

public sealed class FakeRunningApps : IProcessLister
{
    public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsRunning(string processName) => Running.Contains(processName);
}

public static class TestContext
{
    public static DiagnosticContext Empty(string? dataDir = null) => new(
        new FakePowercfg(), new FakeRegistry(), new FakeProcessInfo(),
        new FakeSensors(), new FakeDisk(), new FakeFiles(), new FakeRunningApps(),
        dataDir ?? System.IO.Directory.CreateTempSubdirectory("brisk-ctx-").FullName);
}
