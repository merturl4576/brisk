using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using BriskEngine.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace BriskEngine.Live;

/// One row of "Şu an en çok kullanan": an app's display name and its share
/// (CPU % or GPU % of the whole machine) or its memory in bytes.
public sealed record ProcLoad(string Name, double Value);

/// One second of the Home monitor. null = not read this time, and the GUI
/// shows "—": the first sample of every delta (CPU, network, per-app CPU),
/// a reader that failed, a sensor with nothing to say. RamUsedBytes is 0
/// whenever RamPercent is null. An empty top list is the same "not known"
/// (or nothing using that part at all), never a list padded with zeros.
public sealed record MonitorSample(double? CpuPercent, double? GpuPercent, double? RamPercent, long RamUsedBytes,
    double? CpuTempC, double? GpuTempC, long? UpBytesPerSec, long? DownBytesPerSec, BatteryInfo? Battery,
    IReadOnlyList<ProcLoad> TopCpu, IReadOnlyList<ProcLoad> TopGpu, IReadOnlyList<ProcLoad> TopRam);

/// GetSystemTimes, in 100 ns units. Kernel time includes idle time.
internal readonly record struct CpuTimes(long Idle, long Kernel, long User);

/// One adapter's byte counters since it came up.
internal readonly record struct AdapterBytes(string Id, long Sent, long Received);

/// One GPU engine instance of the `GPU Engine` counter set, named
/// "pid_1234_luid_..._eng_0_engtype_3D", and its utilization in percent.
internal readonly record struct EngineLoad(string Instance, double Percent);

/// One running process. Started and Cpu are null when the process cannot be
/// opened (access denied); RamBytes is null when its memory was not read.
internal readonly record struct ProcReading(int Pid, string Name, DateTime? Started, TimeSpan? Cpu, long? RamBytes);

internal interface ISystemTimesReader
{
    CpuTimes? Read();
}

internal interface IMemoryReader
{
    MemoryReading? Read();
}

internal interface IAdapterBytesReader
{
    /// The adapters that are up, loopback left out.
    IReadOnlyList<AdapterBytes>? Read();
}

internal interface IGpuEngineReader
{
    /// The 3D engines. null = no `GPU Engine` counters on this machine, or no
    /// reading yet.
    IReadOnlyList<EngineLoad>? Read();
}

internal interface IProcessListReader
{
    IReadOnlyList<ProcReading>? Read();

    /// The exe behind a pid, for its display name; null when it cannot be read.
    string? PathOf(int pid);
}

/// The live readings behind Home, sampled once a second from a background
/// thread. Every delta (machine CPU, network, per-app CPU) is primed by the
/// first call, so that call returns nulls and an empty TopCpu for them.
///
/// Each reader runs on its own: one that throws or cannot read leaves only
/// its own fields null (the top lists empty when the process list fails), and
/// Sample never throws. Sources, all read-only:
/// - CPU: GetSystemTimes, busy = 1 - idle / (kernel + user) between samples.
/// - GPU: PDH `GPU Engine(*engtype_3D)\Utilization Percentage`, summed and
///   clamped to 100; per app from the pid in each instance name.
/// - RAM: GlobalMemoryStatusEx, in use = total - available.
/// - Network: NetworkInterface byte counters of up, non-loopback adapters.
/// - Per app: Process.GetProcesses, CPU from TotalProcessorTime deltas keyed
///   by pid and start time, memory from PDH `Process(*)\Working Set - Private`
///   (Task Manager's memory column).
/// - Temperatures: ISensorProbe through SensorReading.IsReal. Battery:
///   IBatteryProbe, one probe for the monitor's lifetime.
///
/// Cost, measured live, unelevated, on the maintainer's laptop 2026-11-07
/// (~370 processes, 12 threads): the first call opens the PDH queries and
/// takes ~0.8 s; a warm call ~24 ms without temperatures, ~95 ms with
/// RealSensorProbe (LibreHardwareMonitor's update is most of it).
public sealed class SystemMonitor
{
    public const int TopCount = 3;

    private static readonly IReadOnlyList<ProcLoad> None = Array.Empty<ProcLoad>();

    /// Never named in a top list (ruling R19): brisk itself, and the kernel's
    /// own entries, which are not apps anyone can close. The machine's totals
    /// still count them.
    private static readonly HashSet<string> NotApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "brisk", "brisk-app", "Idle", "System", "Secure System", "Registry", "Memory Compression",
    };

    private readonly ISystemTimesReader _cpu;
    private readonly IProcessListReader _processes;
    private readonly IAdapterBytesReader _network;
    private readonly IGpuEngineReader _gpu;
    private readonly IMemoryReader _memory;
    private readonly ISensorProbe _sensors;
    private readonly IBatteryProbe _battery;
    private readonly IAppNamer _namer;
    private readonly int _processorCount;
    private readonly Func<TimeSpan> _now;
    private readonly int _selfPid;
    private readonly object _gate = new();

    private CpuTimes? _cpuBefore;
    private (Dictionary<string, AdapterBytes> Adapters, TimeSpan At)? _netBefore;
    private Dictionary<int, Tracked> _tracked = new();
    private TimeSpan? _trackedAt;

    /// What the monitor remembers of a process between samples. Started and
    /// Name together tell a reused pid from the process it had before.
    private sealed class Tracked
    {
        public required string Name { get; init; }
        public required DateTime? Started { get; init; }
        public required TimeSpan? Cpu { get; init; }
        public bool PathRead { get; set; }
        public string? Path { get; set; }
    }

    public SystemMonitor(ISensorProbe sensors, IBatteryProbe battery, IAppNamer namer)
        : this(new SystemTimesReader(), new ProcessListReader(), new AdapterBytesReader(), new GpuEngineReader(),
            new MemoryStatusReader(), sensors, battery, namer, Environment.ProcessorCount, now: null)
    {
    }

    /// selfPid: the process the lists leave out as brisk whatever its exe is
    /// named (a download is often renamed); this process when null.
    internal SystemMonitor(ISystemTimesReader cpu, IProcessListReader processes, IAdapterBytesReader network,
        IGpuEngineReader gpu, IMemoryReader memory, ISensorProbe sensors, IBatteryProbe battery, IAppNamer namer,
        int processorCount, Func<TimeSpan>? now, int? selfPid = null)
    {
        _selfPid = selfPid ?? Environment.ProcessId;
        _cpu = cpu;
        _processes = processes;
        _network = network;
        _gpu = gpu;
        _memory = memory;
        _sensors = sensors;
        _battery = battery;
        _namer = namer;
        _processorCount = Math.Max(1, processorCount);
        if (now is null)
        {
            var clock = Stopwatch.StartNew();
            now = () => clock.Elapsed;
        }
        _now = now;
    }

    /// The processes remembered for the next CPU delta: the last list read,
    /// so an exited process is gone after one sample.
    internal int TrackedProcesses
    {
        get { lock (_gate) return _tracked.Count; }
    }

    public MonitorSample Sample()
    {
        lock (_gate)
        {
            try
            {
                return Take();
            }
            catch (Exception)
            {
                // Every reader is isolated below; this is the last word on
                // "Sample never throws" for a fault in the sampler itself.
                return new MonitorSample(null, null, null, 0, null, null, null, null, null, None, None, None);
            }
        }
    }

    private MonitorSample Take()
    {
        var cpuPercent = ReadCpu();
        var (up, down) = ReadNetwork();

        IReadOnlyList<ProcReading>? processes;
        try { processes = _processes.Read(); }
        catch (Exception) { processes = null; }
        var processesAt = _now();

        IReadOnlyList<EngineLoad>? engines;
        try { engines = _gpu.Read(); }
        catch (Exception) { engines = null; }
        var (gpuPercent, gpuByPid) = Gpu(engines);

        MemoryReading? memory;
        try { memory = _memory.Read(); }
        catch (Exception) { memory = null; }
        double? ramPercent = null;
        long ramUsed = 0;
        if (memory is { Total: > 0 } m)
        {
            ramUsed = Math.Clamp(m.Total - m.Available, 0, m.Total);
            ramPercent = 100.0 * ramUsed / m.Total;
        }

        var cpuTemp = Temperature(_sensors.CpuTempC);
        var gpuTemp = Temperature(_sensors.GpuTempC);

        BatteryInfo? battery;
        try { battery = _battery.Read(); }
        catch (Exception) { battery = null; }

        IReadOnlyList<ProcLoad> topCpu = None, topRam = None, topGpu = None;
        if (processes is not null)
        {
            var rows = Track(processes, processesAt);
            topCpu = Top(rows, r => r.Cpu, clampTo100: true);
            topRam = Top(rows, r => r.Ram, clampTo100: false);
            if (gpuByPid is not null)
                topGpu = Top(rows, r => gpuByPid.TryGetValue(r.Pid, out var g) ? g : null, clampTo100: true);
        }

        return new MonitorSample(cpuPercent, gpuPercent, ramPercent, ramUsed, cpuTemp, gpuTemp, up, down, battery,
            topCpu, topGpu, topRam);
    }

    private double? ReadCpu()
    {
        CpuTimes? now;
        try { now = _cpu.Read(); }
        catch (Exception) { now = null; }
        if (now is not { } times) return null;
        var before = _cpuBefore;
        _cpuBefore = times;
        return before is { } then ? Busy(then, times) : null;
    }

    /// The machine's busy share between two GetSystemTimes readings. null when
    /// no time passed or a counter went backwards.
    internal static double? Busy(CpuTimes before, CpuTimes now)
    {
        var idle = now.Idle - before.Idle;
        var kernel = now.Kernel - before.Kernel;
        var user = now.User - before.User;
        if (idle < 0 || kernel < 0 || user < 0) return null;
        var total = kernel + user;
        if (total <= 0) return null;
        return Math.Clamp(100.0 * (total - idle) / total, 0, 100);
    }

    /// Bytes per second over the adapters read both times. One that came up,
    /// went away or had its counters reset in between has no true difference
    /// and adds nothing.
    private (long? Up, long? Down) ReadNetwork()
    {
        IReadOnlyList<AdapterBytes>? read;
        try { read = _network.Read(); }
        catch (Exception) { read = null; }
        var at = _now();
        if (read is null) return (null, null);

        var adapters = new Dictionary<string, AdapterBytes>(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in read) adapters.TryAdd(adapter.Id, adapter);
        var before = _netBefore;
        _netBefore = (adapters, at);
        if (before is not { } then) return (null, null);

        var seconds = (at - then.At).TotalSeconds;
        if (seconds <= 0) return (null, null);
        long sent = 0, received = 0;
        foreach (var (id, now) in adapters)
        {
            if (!then.Adapters.TryGetValue(id, out var old)) continue;
            var sentDelta = now.Sent - old.Sent;
            var receivedDelta = now.Received - old.Received;
            if (sentDelta < 0 || receivedDelta < 0) continue;
            sent += sentDelta;
            received += receivedDelta;
        }
        return ((long)Math.Round(sent / seconds), (long)Math.Round(received / seconds));
    }

    /// The machine's GPU % (3D engines summed, clamped to 100) and each pid's
    /// sum. No engine with a reading = no number at all.
    private static (double? Percent, Dictionary<int, double>? ByPid) Gpu(IReadOnlyList<EngineLoad>? engines)
    {
        if (engines is null) return (null, null);
        double total = 0;
        var read = false;
        var byPid = new Dictionary<int, double>();
        foreach (var engine in engines)
        {
            if (!double.IsFinite(engine.Percent) || engine.Percent < 0) continue;
            read = true;
            total += engine.Percent;
            if (PidOf(engine.Instance) is { } pid)
                byPid[pid] = byPid.GetValueOrDefault(pid) + engine.Percent;
        }
        return read ? (Math.Min(total, 100), byPid) : (null, null);
    }

    /// "pid_14380_luid_..." → 14380; null for any other shape.
    internal static int? PidOf(string instance)
    {
        const string Prefix = "pid_";
        if (!instance.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var end = instance.IndexOf('_', Prefix.Length);
        var digits = end < 0 ? instance.AsSpan(Prefix.Length) : instance.AsSpan(Prefix.Length, end - Prefix.Length);
        return digits.Length > 0 && int.TryParse(digits, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var pid) ? pid : null;
    }

    private static double? Temperature(Func<double?> read)
    {
        try
        {
            var celsius = read();
            return SensorReading.IsReal(celsius) ? celsius : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private readonly record struct Row(int Pid, string Name, double? Cpu, double? Ram);

    /// Turns this list into rows with their CPU share since the last list, and
    /// remembers it — only it — for the next delta.
    private List<Row> Track(IReadOnlyList<ProcReading> processes, TimeSpan at)
    {
        var capacitySeconds = _trackedAt is { } then ? (at - then).TotalSeconds * _processorCount : 0;
        var tracked = new Dictionary<int, Tracked>(processes.Count);
        var rows = new List<Row>(processes.Count);
        foreach (var p in processes)
        {
            // Pid 0 is the Idle process: its "CPU" is the time nothing ran.
            if (p.Pid == 0 || tracked.ContainsKey(p.Pid)) continue;
            _tracked.TryGetValue(p.Pid, out var old);
            var same = old is not null && old.Started == p.Started && string.Equals(old.Name, p.Name, StringComparison.Ordinal);

            double? cpu = null;
            if (same && capacitySeconds > 0 && p.Started is not null
                && p.Cpu is { } now && old!.Cpu is { } before && now >= before)
                cpu = (now - before).TotalSeconds / capacitySeconds * 100;

            tracked[p.Pid] = new Tracked
            {
                Name = p.Name,
                Started = p.Started,
                Cpu = p.Cpu,
                PathRead = same && old!.PathRead,
                Path = same ? old!.Path : null,
            };
            rows.Add(new Row(p.Pid, p.Name, cpu, p.RamBytes));
        }
        _tracked = tracked;
        _trackedAt = at;
        return rows;
    }

    /// The three apps (processes grouped by name) with the largest known value
    /// above zero, brisk and the kernel's entries left out.
    private IReadOnlyList<ProcLoad> Top(List<Row> rows, Func<Row, double?> value, bool clampTo100)
    {
        var apps = new Dictionary<string, (double Sum, List<int> Pids, string Name)>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (row.Pid == _selfPid || NotApps.Contains(row.Name)) continue;
            if (value(row) is not { } v || !double.IsFinite(v) || v <= 0) continue;
            if (!apps.TryGetValue(row.Name, out var app)) app = (0, new List<int>(), row.Name);
            app.Pids.Add(row.Pid);
            apps[row.Name] = (app.Sum + v, app.Pids, app.Name);
        }
        return apps.Values
            .Select(a => (a.Name, a.Pids, Value: clampTo100 ? Math.Min(a.Sum, 100) : a.Sum))
            .OrderByDescending(a => a.Value)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Take(TopCount)
            .Select(a => new ProcLoad(DisplayName(a.Name, a.Pids), a.Value))
            .ToList();
    }

    /// The namer's name for the app, from the first of its processes whose exe
    /// can be read. Each process's path is asked once in its life.
    private string DisplayName(string name, List<int> pids)
    {
        string? path = null;
        foreach (var pid in pids)
        {
            if (!_tracked.TryGetValue(pid, out var t)) continue;
            if (!t.PathRead)
            {
                try { t.Path = _processes.PathOf(pid); }
                catch (Exception) { t.Path = null; }
                t.PathRead = true;
            }
            if (t.Path is not null) { path = t.Path; break; }
        }
        try
        {
            return _namer.Name(name, path);
        }
        catch (Exception)
        {
            return name;
        }
    }
}

internal sealed class SystemTimesReader : ISystemTimesReader
{
    public CpuTimes? Read() =>
        GetSystemTimes(out var idle, out var kernel, out var user) ? new CpuTimes(idle, kernel, user) : null;

    // FILETIME is two little-endian DWORDs: one 64-bit count of 100 ns.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
}

internal sealed class MemoryStatusReader : IMemoryReader
{
    public MemoryReading? Read() => MemoryStatus.Read();
}

internal sealed class AdapterBytesReader : IAdapterBytesReader
{
    public IReadOnlyList<AdapterBytes>? Read()
    {
        var list = new List<AdapterBytes>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var stats = adapter.GetIPStatistics();
                list.Add(new AdapterBytes(adapter.Id, stats.BytesSent, stats.BytesReceived));
            }
            catch (Exception)
            {
                // The adapter went away while it was read: it adds nothing.
            }
        }
        return list;
    }
}

/// The 3D engines' utilization through PDH. A machine without the counter set
/// (a VM, a server with no display driver) is asked once, then never again.
/// The counter is a rate: the first read after the query opens has no value.
internal sealed class GpuEngineReader : IGpuEngineReader
{
    private const string Utilization = @"\GPU Engine(*engtype_3D)\Utilization Percentage";
    private PdhCounters? _counters;
    private bool _missing;

    public IReadOnlyList<EngineLoad>? Read()
    {
        if (_missing) return null;
        _counters ??= PdhCounters.Open(Utilization);
        if (_counters is null)
        {
            _missing = true;
            return null;
        }
        if (!_counters.Collect()) return null;
        return _counters.Values(0, PdhCounters.FormatDouble)?
            .Select(v => new EngineLoad(v.Instance, v.Value))
            .ToList();
    }
}

/// Every process from Process.GetProcesses (each one disposed), its CPU time
/// and start time where it can be opened, and its private working set — the
/// memory Task Manager shows — from PDH. Where PDH's process counters cannot
/// be read, every process falls back to its whole working set, so one sample
/// never mixes the two.
internal sealed class ProcessListReader : IProcessListReader
{
    private const string IdProcess = @"\Process(*)\ID Process";
    private const string PrivateWorkingSet = @"\Process(*)\Working Set - Private";
    private PdhCounters? _counters;
    private bool _missing;

    public IReadOnlyList<ProcReading>? Read()
    {
        var privateBytes = PrivateWorkingSets();
        var all = Process.GetProcesses();
        var list = new List<ProcReading>(all.Length);
        foreach (var process in all)
        {
            using (process)
            {
                try
                {
                    var pid = process.Id;
                    var name = process.ProcessName;
                    TimeSpan? cpu = null;
                    DateTime? started = null;
                    try
                    {
                        cpu = process.TotalProcessorTime;
                        started = process.StartTime;
                    }
                    catch (Exception)
                    {
                        // Access denied: neither is known.
                        cpu = null;
                        started = null;
                    }
                    long? ram = privateBytes is null
                        ? process.WorkingSet64
                        : privateBytes.TryGetValue(pid, out var bytes) ? bytes : null;
                    list.Add(new ProcReading(pid, name, started, cpu, ram));
                }
                catch (Exception)
                {
                    // It exited while it was being read.
                }
            }
        }
        return list;
    }

    /// pid → private working set from one collection. null when the counters
    /// cannot be read.
    private Dictionary<int, long>? PrivateWorkingSets()
    {
        try
        {
            if (_missing) return null;
            _counters ??= PdhCounters.Open(IdProcess, PrivateWorkingSet);
            if (_counters is null)
            {
                _missing = true;
                return null;
            }
            if (!_counters.Collect()) return null;
            var ids = _counters.Values(0, PdhCounters.FormatLarge);
            var sizes = _counters.Values(1, PdhCounters.FormatLarge);
            return ids is null || sizes is null ? null : Join(ids, sizes);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// PDH names the twenty Chrome processes "chrome" twenty times (the "#3"
    /// suffix exists only in the paths it expands), so an instance is its name
    /// and which occurrence of that name it is; both counters of one
    /// collection list the instances in the same order. Pid 0 is _Total and
    /// Idle. null when nothing joins.
    internal static Dictionary<int, long>? Join(IReadOnlyList<PdhValue> ids, IReadOnlyList<PdhValue> sizes)
    {
        var pidOf = new Dictionary<(string, int), int>(ids.Count);
        foreach (var id in ids)
            if (id.Value is > 0 and <= int.MaxValue) pidOf.TryAdd((id.Instance, id.Occurrence), (int)id.Value);
        var bytes = new Dictionary<int, long>(sizes.Count);
        foreach (var size in sizes)
            if (size.Value is >= 0 and < long.MaxValue && pidOf.TryGetValue((size.Instance, size.Occurrence), out var pid))
                bytes.TryAdd(pid, (long)size.Value);
        return bytes.Count > 0 ? bytes : null;
    }

    public string? PathOf(int pid)
    {
        using var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle.IsInvalid) return null;
        var buffer = new char[1024];
        var size = buffer.Length;
        return QueryFullProcessImageNameW(handle, 0, buffer, ref size) && size > 0 ? new string(buffer, 0, size) : null;
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags,
        [Out] char[] exeName, ref int size);
}

/// One instance's value from a PDH counter array. Occurrence counts the earlier
/// items of the same name in that array (0 for the first), invalid ones
/// included, so two counters of one collection agree on it.
internal readonly record struct PdhValue(string Instance, int Occurrence, double Value);

/// One PDH query over wildcard counters, added by their English paths: on a
/// Turkish Windows the counter names are Turkish, and PdhAddEnglishCounter is
/// what finds them anyway. Collect and Values are not thread-safe; the
/// monitor calls them under its lock.
internal sealed class PdhCounters
{
    internal const uint FormatDouble = 0x00000200;
    internal const uint FormatLarge = 0x00000400;
    private const uint MoreData = 0x800007D2;

    private readonly PdhQueryHandle _query;
    private readonly IntPtr[] _counters;

    private PdhCounters(PdhQueryHandle query, IntPtr[] counters)
    {
        _query = query;
        _counters = counters;
    }

    /// null when the query cannot be opened or a counter set is not on this
    /// machine (PDH_CSTATUS_NO_OBJECT).
    internal static PdhCounters? Open(params string[] englishPaths)
    {
        try
        {
            if (PdhOpenQueryW(null, IntPtr.Zero, out var query) != 0 || query.IsInvalid)
            {
                query.Dispose();
                return null;
            }
            var counters = new IntPtr[englishPaths.Length];
            for (var i = 0; i < englishPaths.Length; i++)
            {
                if (PdhAddEnglishCounterW(query, englishPaths[i], IntPtr.Zero, out counters[i]) != 0)
                {
                    query.Dispose();
                    return null;
                }
            }
            return new PdhCounters(query, counters);
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal bool Collect() => PdhCollectQueryData(_query) == 0;

    /// Every instance whose value is valid this collection. null when the
    /// counter has no data (the first collection of a rate counter), empty
    /// when it has no instances.
    internal IReadOnlyList<PdhValue>? Values(int counter, uint format)
    {
        uint size = 0;
        var status = PdhGetFormattedCounterArrayW(_counters[counter], format, ref size, out var count, IntPtr.Zero);
        if (status == 0) return Array.Empty<PdhValue>();
        if (status != MoreData || size == 0) return null;

        // The size can only be what the first call said for one collection;
        // a second "more data" is treated as no reading rather than looped on.
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(_counters[counter], format, ref size, out count, buffer) != 0) return null;
            var itemSize = Marshal.SizeOf<PdhFmtCounterValueItem>();
            var values = new List<PdhValue>((int)count);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<PdhFmtCounterValueItem>(buffer + i * itemSize);
                var name = Marshal.PtrToStringUni(item.Name) ?? "";
                var occurrence = seen.GetValueOrDefault(name);
                seen[name] = occurrence + 1;
                // PDH_CSTATUS_VALID_DATA 0, PDH_CSTATUS_NEW_DATA 1.
                if (item.CStatus > 1) continue;
                var value = format == FormatDouble ? BitConverter.Int64BitsToDouble(item.Value) : item.Value;
                values.Add(new PdhValue(name, occurrence, value));
            }
            return values;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// PDH_FMT_COUNTERVALUE_ITEM_W: LPWSTR szName; PDH_FMT_COUNTERVALUE
    /// { DWORD CStatus; union { LONG; double; LONGLONG; LPCSTR; LPCWSTR } }.
    /// The union is 8-aligned, so Value sits after 4 bytes of padding.
    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueItem
    {
        public IntPtr Name;
        public uint CStatus;
        public long Value;
    }

    /// Closes the query, and with it its counters, when the reader is collected.
    private sealed class PdhQueryHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public PdhQueryHandle() : base(ownsHandle: true) { }

        protected override bool ReleaseHandle() => PdhCloseQuery(handle) == 0;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out PdhQueryHandle query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(PdhQueryHandle query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(PdhQueryHandle query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize,
        out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
