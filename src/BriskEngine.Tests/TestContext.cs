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
    public HashSet<string> DenyWriteKeys = new(StringComparer.OrdinalIgnoreCase);
    private static string K(string k, string v) => $"{k}::{v}";
    public string? GetString(string k, string v) => Values.TryGetValue(K(k, v), out var o) ? o as string : null;
    public void SetString(string k, string v, string value) => Values[K(k, v)] = value;
    public void DeleteValue(string k, string v)
    {
        if (DenyWriteKeys.Contains(k)) throw new UnauthorizedAccessException();
        Values.Remove(K(k, v));
    }
    public byte[]? GetBytes(string k, string v) => Values.TryGetValue(K(k, v), out var o) ? o as byte[] : null;
    public void SetBytes(string k, string v, byte[] value)
    {
        if (DenyWriteKeys.Contains(k)) throw new UnauthorizedAccessException();
        Values[K(k, v)] = value;
    }
    public int? GetInt(string k, string v) => Values.TryGetValue(K(k, v), out var o) ? o as int? : null;
    public void SetInt(string k, string v, int value)
    {
        if (DenyWriteKeys.Contains(k)) throw new UnauthorizedAccessException();
        Values[K(k, v)] = value;
    }
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

/// Plants a Store startup task the way Windows records one: the package family
/// name under SystemAppData, the task id under the package, the State value
/// under the task. Shared so the StartupManager tests and the StartupBloatRule
/// tests cannot drift into describing two different registries.
public static class StoreRegistry
{
    public static void Task(FakeRegistry reg, string packageFamilyName, string task, int state)
    {
        var apps = StartupManager.StoreRoot;
        if (!reg.SubKeys.TryGetValue(apps, out var pfns)) reg.SubKeys[apps] = pfns = new List<string>();
        if (!pfns.Contains(packageFamilyName)) pfns.Add(packageFamilyName);
        var appKey = $@"{apps}\{packageFamilyName}";
        if (!reg.SubKeys.TryGetValue(appKey, out var tasks)) reg.SubKeys[appKey] = tasks = new List<string>();
        if (!tasks.Contains(task)) tasks.Add(task);
        reg.SetInt($@"{appKey}\{task}", "State", state);
    }
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

public sealed class FakeDisplays : IDisplayProbe
{
    public List<DisplayInfo> Attached = new();
    public List<(string Device, int Hz)> SetCalls = new();

    /// Counts the writes to the registry, so a test can prove the mode change
    /// stayed session-only until something actually confirmed it.
    public int PersistCalls;

    /// Rates the driver will refuse, as a real one refuses a mode the cable
    /// cannot carry (DISP_CHANGE_BADMODE).
    public HashSet<int> RefusedRates = new();

    public IReadOnlyList<DisplayInfo> Displays() => Attached;

    public void SetRefreshRate(string deviceName, int hz)
    {
        if (RefusedRates.Contains(hz))
            throw new DisplayChangeException($"{deviceName}: refused {hz} Hz");
        SetCalls.Add((deviceName, hz));
        var i = Attached.FindIndex(d => d.DeviceName == deviceName);
        if (i >= 0) Attached[i] = Attached[i] with { CurrentHz = hz };
    }

    public void PersistCurrentModes() => PersistCalls++;
}

public sealed class FakeEventLog : IEventLogProbe
{
    public List<BootRecord> Boots = new();
    public IReadOnlyList<BootRecord> RecentBoots(int count) =>
        Boots.GetRange(0, Math.Min(count, Boots.Count));
}

public sealed class FakeHardware : IHardwareProbe
{
    public List<MemoryModule> Modules = new();
    public IReadOnlyList<MemoryModule> MemoryModules() => Modules;
}

/// Unknown by default, so a test that does not speak to memory integrity
/// exercises the hedged copy rather than silently picking a side.
public sealed class FakeMemoryIntegrity : IMemoryIntegrityProbe
{
    public bool? On;
    public bool? IsOn() => On;
}

/// Null by default: a test that never speaks to Delivery Optimization says
/// nothing about what this machine uploaded, and null is how the probe says
/// it could not ask. Zero would be a reading, and a test that never
/// mentioned this probe has not taken one.
public sealed class FakeDeliveryOptimization : IDeliveryOptimizationProbe
{
    public long? Bytes;
    public long? BytesUploadedToPeers() => Bytes;
}

public static class TestContext
{
    /// All context data dirs live under ONE per-run root that the next run
    /// deletes first. The old CreateTempSubdirectory("brisk-ctx-") leaked a
    /// loose directory into %TEMP% on every test, and the round-10 incident
    /// found the app's own cleaner grinding through thousands of them for
    /// minutes — the suite must never litter the machine it tests on.
    internal static readonly string CtxRoot = InitCtxRoot();

    private static string InitCtxRoot()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "brisk-test-ctx");
        try { System.IO.Directory.Delete(root, recursive: true); }
        catch (System.IO.IOException) { }             // first run, or a file in use
        catch (UnauthorizedAccessException) { }
        return System.IO.Directory.CreateDirectory(root).FullName;
    }

    public static DiagnosticContext Empty(string? dataDir = null) => new(
        new FakePowercfg(), new FakeRegistry(), new FakeProcessInfo(),
        new FakeSensors(), new FakeDisplays(), new FakeEventLog(), new FakeHardware(),
        new FakeDisk(), new FakeFiles(), new FakeRunningApps(),
        new FakeMemoryIntegrity(), new FakeDeliveryOptimization(),
        dataDir ?? System.IO.Directory.CreateDirectory(System.IO.Path.Combine(
            CtxRoot, System.IO.Path.GetRandomFileName())).FullName);
}
