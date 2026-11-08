using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Live;
using Xunit;

namespace BriskEngine.Tests;

file sealed class Cpu : ISystemTimesReader
{
    public Func<CpuTimes?> Next = () => null;
    public CpuTimes? Read() => Next();
}

file sealed class Memory : IMemoryReader
{
    public Func<MemoryReading?> Next = () => null;
    public MemoryReading? Read() => Next();
}

file sealed class Net : IAdapterBytesReader
{
    public Func<IReadOnlyList<AdapterBytes>?> Next = () => null;
    public IReadOnlyList<AdapterBytes>? Read() => Next();
}

file sealed class Gpu : IGpuEngineReader
{
    public Func<IReadOnlyList<EngineLoad>?> Next = () => null;
    public IReadOnlyList<EngineLoad>? Read() => Next();
}

file sealed class Procs : IProcessListReader
{
    public Func<IReadOnlyList<ProcReading>?> Next = () => null;
    public Dictionary<int, string> Paths = new();
    public List<int> PathAsked = new();
    public bool PathThrows;
    public IReadOnlyList<ProcReading>? Read() => Next();
    public string? PathOf(int pid)
    {
        PathAsked.Add(pid);
        if (PathThrows) throw new InvalidOperationException("path unreadable");
        return Paths.GetValueOrDefault(pid);
    }
}

file sealed class Power : IBatteryProbe
{
    public Func<BatteryInfo?> Next = () => null;
    public BatteryInfo? Read() => Next();
}

file sealed class Sensors : ISensorProbe
{
    public Func<double?> Cpu = () => null;
    public Func<double?> Gpu = () => null;
    public double? CpuTempC() => Cpu();
    public double? GpuTempC() => Gpu();
    public int GpuCount() => throw new InvalidOperationException("the sampler never asks");
}

/// The process name with a capital first letter, or "app at <path>" when a
/// path came with it, so a test can see which one the sampler handed over.
file sealed class Namer : IAppNamer
{
    public string Name(string processOrExeName, string? fullPath) =>
        fullPath is null ? RealAppNamer.Pretty(RealAppNamer.Stem(processOrExeName)) : $"app at {fullPath}";
}

/// Every reader faked, a clock that moves one second per Sample, 4 cores.
file sealed class Rig
{
    public readonly Cpu Cpu = new();
    public readonly Memory Memory = new();
    public readonly Net Net = new();
    public readonly Gpu Gpu = new();
    public readonly Procs Procs = new();
    public readonly Power Power = new();
    public readonly Sensors Sensors = new();
    public TimeSpan Now;
    public readonly SystemMonitor Monitor;

    public Rig(int? selfPid = null)
    {
        Monitor = new SystemMonitor(Cpu, Procs, Net, Gpu, Memory, Sensors, Power, new Namer(),
            processorCount: 4, now: () => Now, selfPid: selfPid);
    }

    public MonitorSample Sample()
    {
        var sample = Monitor.Sample();
        Now += TimeSpan.FromSeconds(1);
        return sample;
    }
}

public sealed class SystemMonitorTests
{
    private static readonly DateTime Boot = new(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc);

    private static ProcReading P(int pid, string name, double cpuSeconds, long ramMb = 0, int startedMinute = 0) =>
        new(pid, name, Boot.AddMinutes(startedMinute), TimeSpan.FromSeconds(cpuSeconds), ramMb << 20);

    private static CpuTimes Times(long idle, long kernel, long user) => new(idle, kernel, user);

    private static IReadOnlyList<AdapterBytes> Adapters(params (string Id, long Sent, long Received)[] rows) =>
        rows.Select(r => new AdapterBytes(r.Id, r.Sent, r.Received)).ToList();

    private static EngineLoad Engine(int pid, double percent, int engine = 0, string luid = "0x00010622") =>
        new($"pid_{pid}_luid_0x00000000_{luid}_phys_0_eng_{engine}_engtype_3D", percent);

    [Fact]
    public void First_sample_has_no_cpu_delta_second_has()
    {
        var rig = new Rig();
        rig.Cpu.Next = () => Times(idle: 1_000, kernel: 2_000, user: 1_000);
        rig.Net.Next = () => Adapters(("eth", 5_000, 90_000));
        rig.Procs.Next = () => new[] { P(10, "chrome", 3), P(11, "code", 1) };

        var first = rig.Sample();

        Assert.Null(first.CpuPercent);
        Assert.Null(first.UpBytesPerSec);
        Assert.Null(first.DownBytesPerSec);
        Assert.Empty(first.TopCpu);

        // Kernel time includes idle: 200 of 400 ticks passed, 50 of them idle.
        rig.Cpu.Next = () => Times(idle: 1_050, kernel: 2_100, user: 1_100);
        rig.Net.Next = () => Adapters(("eth", 6_000, 150_000));
        // Chrome used 2 s of CPU in 1 s on 4 cores = 50 %; code 0.4 s = 10 %.
        rig.Procs.Next = () => new[] { P(10, "chrome", 5), P(11, "code", 1.4) };

        var second = rig.Sample();

        Assert.Equal(75, second.CpuPercent!.Value, precision: 6);
        Assert.Equal(1_000, second.UpBytesPerSec);
        Assert.Equal(60_000, second.DownBytesPerSec);
        Assert.Equal(new[] { "Chrome", "Code" }, second.TopCpu.Select(t => t.Name));
        Assert.Equal(50, second.TopCpu[0].Value, precision: 6);
        Assert.Equal(10, second.TopCpu[1].Value, precision: 6);
    }

    [Fact]
    public void Gpu_null_when_counters_missing()
    {
        var rig = new Rig();
        rig.Procs.Next = () => new[] { P(10, "game", 0) };

        // No GPU Engine category (a VM, a server without a display driver).
        rig.Gpu.Next = () => null;
        var missing = rig.Sample();
        Assert.Null(missing.GpuPercent);
        Assert.Empty(missing.TopGpu);

        // The category is there but no 3D engine has a reading: still no number.
        rig.Gpu.Next = () => Array.Empty<EngineLoad>();
        var empty = rig.Sample();
        Assert.Null(empty.GpuPercent);
        Assert.Empty(empty.TopGpu);
    }

    [Fact]
    public void Gpu_is_each_adapters_3d_engines_summed_then_the_busiest_adapter()
    {
        var rig = new Rig();
        rig.Procs.Next = () => new[] { P(10, "game", 0), P(11, "dwm", 0) };

        // One adapter: its 3D engines add up.
        rig.Gpu.Next = () => new[] { Engine(10, 30), Engine(10, 12.5, engine: 1), Engine(11, 5), Engine(99, 2) };
        var sample = rig.Sample();
        Assert.Equal(49.5, sample.GpuPercent!.Value, precision: 6);
        // pid 99 is not in the process list: counted in the total, named nowhere.
        Assert.Equal(new[] { ("Game", 42.5), ("Dwm", 5.0) }, sample.TopGpu.Select(t => (t.Name, t.Value)));

        // A hybrid laptop: the iGPU at 30 and the dGPU at 50 read 50, not 80.
        const string Igpu = "0x00010622", Dgpu = "0x0001A3F0";
        rig.Gpu.Next = () => new[]
        {
            Engine(11, 20, luid: Igpu), Engine(11, 10, engine: 1, luid: Igpu),
            Engine(10, 45, luid: Dgpu), Engine(11, 5, luid: Dgpu),
        };
        var hybrid = rig.Sample();
        Assert.Equal(50, hybrid.GpuPercent!.Value, precision: 6);

        // One adapter past 100 is clamped to 100; the other one adds nothing to it.
        rig.Gpu.Next = () => new[]
        {
            Engine(10, 80, luid: Dgpu), Engine(11, 70, luid: Dgpu), Engine(11, 60, engine: 1, luid: Igpu),
        };
        var busy = rig.Sample();
        Assert.Equal(100.0, busy.GpuPercent);
        Assert.Equal(new[] { ("Dwm", 100.0), ("Game", 80.0) }, busy.TopGpu.Select(t => (t.Name, t.Value)));
    }

    [Fact]
    public void Top_lists_hold_three_largest()
    {
        var rig = new Rig();
        rig.Procs.Next = () => new[]
        {
            P(1, "chrome", 0, ramMb: 300), P(2, "chrome", 0, ramMb: 200), P(3, "code", 0, ramMb: 400),
            P(4, "spotify", 0, ramMb: 100), P(5, "discord", 0, ramMb: 450), P(6, "notepad", 0, ramMb: 20),
        };
        rig.Sample();

        rig.Procs.Next = () => new[]
        {
            // CPU seconds used over the 1 s on 4 cores: chrome 0.8 + 0.4, code 1.6, spotify 0.2,
            // discord 0.4, notepad 0.
            P(1, "chrome", 0.8, ramMb: 300), P(2, "chrome", 0.4, ramMb: 200), P(3, "code", 1.6, ramMb: 400),
            P(4, "spotify", 0.2, ramMb: 100), P(5, "discord", 0.4, ramMb: 450), P(6, "notepad", 0, ramMb: 20),
        };
        rig.Gpu.Next = () => new[] { Engine(4, 1), Engine(1, 9), Engine(2, 3), Engine(6, 2), Engine(3, 0) };
        var sample = rig.Sample();

        Assert.Equal(new[] { ("Code", 40.0), ("Chrome", 30.0), ("Discord", 10.0) },
            sample.TopCpu.Select(t => (t.Name, Math.Round(t.Value, 6))));
        // RAM in bytes, an app's processes together.
        Assert.Equal(new[] { ("Chrome", 500.0 * (1 << 20)), ("Discord", 450.0 * (1 << 20)), ("Code", 400.0 * (1 << 20)) },
            sample.TopRam.Select(t => (t.Name, t.Value)));
        Assert.Equal(new[] { ("Chrome", 12.0), ("Notepad", 2.0), ("Spotify", 1.0) },
            sample.TopGpu.Select(t => (t.Name, t.Value)));
    }

    [Fact]
    public void Top_lists_leave_out_idle_and_whatever_used_nothing()
    {
        var rig = new Rig();
        rig.Procs.Next = () => new[] { P(0, "Idle", 0, ramMb: 0), P(1, "code", 0, ramMb: 400), P(2, "notepad", 0) };
        rig.Sample();
        rig.Procs.Next = () => new[] { P(0, "Idle", 3.5, ramMb: 0), P(1, "code", 0.4, ramMb: 400), P(2, "notepad", 0) };
        rig.Gpu.Next = () => new[] { Engine(1, 0), Engine(2, 0) };

        var sample = rig.Sample();

        Assert.Equal(new[] { "Code" }, sample.TopCpu.Select(t => t.Name));
        Assert.Equal(new[] { "Code" }, sample.TopRam.Select(t => t.Name));
        Assert.Empty(sample.TopGpu);
        Assert.Equal(0.0, sample.GpuPercent);
    }

    [Fact]
    public void Top_names_come_from_the_exe_of_the_winners_only()
    {
        var rig = new Rig();
        rig.Procs.Paths[3] = @"C:\Apps\Code\Code.exe";
        rig.Procs.Next = () => Enumerable.Range(1, 6).Select(i => P(i, $"app{i}", 0, ramMb: i * 10)).ToList();

        var first = rig.Sample();
        var second = rig.Sample();

        Assert.Equal(new[] { "App6", "App5", "App4" }, first.TopRam.Select(t => t.Name));
        Assert.DoesNotContain(1, rig.Procs.PathAsked);
        Assert.DoesNotContain(2, rig.Procs.PathAsked);
        Assert.DoesNotContain(3, rig.Procs.PathAsked);
        // A running process's path is asked once, not once a second.
        Assert.Equal(3, rig.Procs.PathAsked.Count);
        Assert.Equal(first.TopRam, second.TopRam);

        rig.Procs.Next = () => new[] { P(3, "code", 0, ramMb: 900) };
        Assert.Equal(@"app at C:\Apps\Code\Code.exe", rig.Sample().TopRam.Single().Name);
    }

    [Fact]
    public void A_reused_pid_is_a_new_process()
    {
        var rig = new Rig();
        rig.Procs.Next = () => new[] { P(42, "setup", 120, startedMinute: 0) };
        rig.Sample();

        // Pid 42 now belongs to a process started later, with its own CPU clock.
        rig.Procs.Next = () => new[] { P(42, "setup", 121, startedMinute: 5) };
        Assert.Empty(rig.Sample().TopCpu);

        rig.Procs.Next = () => new[] { P(42, "setup", 122, startedMinute: 5) };
        Assert.Equal(25, rig.Sample().TopCpu.Single().Value, precision: 6);
    }

    [Fact]
    public void Exited_processes_are_forgotten()
    {
        var rig = new Rig();
        rig.Procs.Next = () => Enumerable.Range(1, 50).Select(i => P(i, $"tmp{i}", 0)).ToList();
        rig.Sample();
        Assert.Equal(50, rig.Monitor.TrackedProcesses);

        rig.Procs.Next = () => new[] { P(7, "tmp7", 0) };
        rig.Sample();
        Assert.Equal(1, rig.Monitor.TrackedProcesses);
    }

    [Fact]
    public void A_process_without_cpu_time_is_left_out_of_cpu_only()
    {
        var rig = new Rig();
        // Access denied (a protected process): no start time, no CPU time,
        // but its memory is known.
        var locked = new ProcReading(4100, "MsMpEng", null, null, 300L << 20);
        rig.Procs.Next = () => new[] { locked, P(9, "code", 0, ramMb: 100) };
        rig.Sample();
        rig.Procs.Next = () => new[] { locked, P(9, "code", 0.4, ramMb: 100) };

        var sample = rig.Sample();

        Assert.Equal(new[] { "Code" }, sample.TopCpu.Select(t => t.Name));
        Assert.Equal(new[] { "MsMpEng", "Code" }, sample.TopRam.Select(t => t.Name));
    }

    /// Ruling R19: brisk itself and the kernel's own entries are not apps the
    /// user can close; the top lists name neither. The machine totals still
    /// count them.
    [Fact]
    public void Top_lists_leave_out_brisk_and_the_kernel_entries()
    {
        var rig = new Rig(selfPid: 77);
        IReadOnlyList<ProcReading> List(double cpu) => new[]
        {
            P(4, "System", cpu, ramMb: 900), P(88, "Registry", cpu, ramMb: 800),
            P(90, "Secure System", cpu, ramMb: 700), P(91, "Memory Compression", cpu, ramMb: 1_500),
            P(92, "Idle", cpu, ramMb: 1), P(50, "brisk-app", cpu, ramMb: 600), P(51, "BRISK", cpu, ramMb: 600),
            // brisk under whatever name its exe was saved as
            P(77, "brisk-0.12.0", cpu, ramMb: 650),
            P(10, "code", cpu / 4, ramMb: 100),
        };
        rig.Procs.Next = () => List(0);
        rig.Sample();
        rig.Procs.Next = () => List(0.4);
        rig.Gpu.Next = () => new[] { Engine(4, 5), Engine(50, 5), Engine(77, 5), Engine(91, 5), Engine(10, 1) };

        var sample = rig.Sample();

        Assert.Equal(new[] { "Code" }, sample.TopCpu.Select(t => t.Name));
        Assert.Equal(new[] { "Code" }, sample.TopRam.Select(t => t.Name));
        Assert.Equal(new[] { "Code" }, sample.TopGpu.Select(t => t.Name));
        Assert.Equal(21, sample.GpuPercent!.Value, precision: 6);
    }

    [Fact]
    public void Network_counts_adapters_read_both_times()
    {
        var rig = new Rig();
        rig.Net.Next = () => Adapters(("eth", 1_000, 1_000), ("wifi", 50_000, 50_000));
        rig.Sample();

        // wifi went away, a VPN came up, eth's counters reset (a driver restart):
        // none of them has a true difference to give.
        rig.Net.Next = () => Adapters(("eth", 10, 10), ("vpn", 9_999, 9_999));
        var reset = rig.Sample();
        Assert.Equal(0, reset.UpBytesPerSec);
        Assert.Equal(0, reset.DownBytesPerSec);

        rig.Net.Next = () => Adapters(("eth", 2_010, 4_010), ("vpn", 10_999, 19_999));
        var both = rig.Sample();
        Assert.Equal(3_000, both.UpBytesPerSec);
        Assert.Equal(14_000, both.DownBytesPerSec);
    }

    [Fact]
    public void Deltas_span_a_failed_read()
    {
        var rig = new Rig();
        rig.Net.Next = () => Adapters(("eth", 0, 0));
        rig.Cpu.Next = () => Times(0, 0, 0);
        rig.Sample();

        rig.Net.Next = () => throw new InvalidOperationException("adapter list unreadable");
        rig.Cpu.Next = () => null;
        var failed = rig.Sample();
        Assert.Null(failed.UpBytesPerSec);
        Assert.Null(failed.CpuPercent);

        // Two seconds since the last good read.
        rig.Net.Next = () => Adapters(("eth", 4_000, 8_000));
        rig.Cpu.Next = () => Times(idle: 100, kernel: 300, user: 100);
        var later = rig.Sample();
        Assert.Equal(2_000, later.UpBytesPerSec);
        Assert.Equal(4_000, later.DownBytesPerSec);
        Assert.Equal(75, later.CpuPercent!.Value, precision: 6);
    }

    [Fact]
    public void Memory_temps_and_battery_are_read_every_sample()
    {
        var rig = new Rig();
        rig.Memory.Next = () => new MemoryReading(Total: 32L << 30, Available: 8L << 30);
        rig.Sensors.Cpu = () => 71.5;
        rig.Sensors.Gpu = () => double.NaN;
        var battery = new BatteryInfo(64, true, 29, null);
        rig.Power.Next = () => battery;

        var sample = rig.Sample();

        Assert.Equal(75, sample.RamPercent!.Value, precision: 6);
        Assert.Equal(24L << 30, sample.RamUsedBytes);
        Assert.Equal(71.5, sample.CpuTempC);
        // NaN is a sensor with nothing to say: no reading.
        Assert.Null(sample.GpuTempC);
        Assert.Same(battery, sample.Battery);

        rig.Sensors.Gpu = () => double.PositiveInfinity;
        rig.Memory.Next = () => new MemoryReading(0, 0);
        var odd = rig.Sample();
        Assert.Null(odd.GpuTempC);
        Assert.Null(odd.RamPercent);
        Assert.Equal(0, odd.RamUsedBytes);
    }

    [Fact]
    public void A_throwing_reader_nulls_only_its_fields()
    {
        var boom = new InvalidOperationException("reader failed");
        foreach (var broken in new[] { "cpu", "procs", "net", "gpu", "memory", "cputemp", "gputemp", "battery", "path" })
        {
            var rig = new Rig();
            rig.Cpu.Next = () => broken == "cpu" ? throw boom : Times(1_000, 2_000, 1_000);
            rig.Net.Next = () => broken == "net" ? throw boom : Adapters(("eth", 0, 0));
            rig.Procs.Next = () => broken == "procs" ? throw boom : new[] { P(10, "game", 0, ramMb: 300) };
            rig.Gpu.Next = () => broken == "gpu" ? throw boom : new[] { Engine(10, 40) };
            rig.Memory.Next = () => broken == "memory" ? throw boom : new MemoryReading(16L << 30, 4L << 30);
            rig.Sensors.Cpu = () => broken == "cputemp" ? throw boom : 60;
            rig.Sensors.Gpu = () => broken == "gputemp" ? throw boom : 55;
            rig.Power.Next = () => broken == "battery" ? throw boom : new BatteryInfo(80, false, null, null);
            rig.Procs.Paths[10] = @"C:\Games\game.exe";
            rig.Procs.PathThrows = broken == "path";
            rig.Sample();

            rig.Cpu.Next = () => broken == "cpu" ? throw boom : Times(1_050, 2_100, 1_100);
            rig.Net.Next = () => broken == "net" ? throw boom : Adapters(("eth", 1_000, 2_000));
            rig.Procs.Next = () => broken == "procs" ? throw boom : new[] { P(10, "game", 2, ramMb: 300) };
            var sample = rig.Sample();

            Assert.Equal(broken == "cpu" ? (double?)null : 75.0, sample.CpuPercent);
            Assert.Equal(broken == "net" ? (long?)null : 1_000L, sample.UpBytesPerSec);
            Assert.Equal(broken == "net" ? (long?)null : 2_000L, sample.DownBytesPerSec);
            Assert.Equal(broken == "gpu" ? (double?)null : 40.0, sample.GpuPercent);
            Assert.Equal(broken == "memory" ? (double?)null : 75.0, sample.RamPercent);
            Assert.Equal(broken == "memory" ? 0L : 12L << 30, sample.RamUsedBytes);
            Assert.Equal(broken == "cputemp" ? (double?)null : 60.0, sample.CpuTempC);
            Assert.Equal(broken == "gputemp" ? (double?)null : 55.0, sample.GpuTempC);
            Assert.Equal(broken == "battery" ? null : new BatteryInfo(80, false, null, null), sample.Battery);
            // The top lists need the process list for their names.
            Assert.Equal(broken == "procs" ? 0 : 1, sample.TopCpu.Count);
            Assert.Equal(broken == "procs" ? 0 : 1, sample.TopRam.Count);
            Assert.Equal(broken is "procs" or "gpu" ? 0 : 1, sample.TopGpu.Count);
            // An exe path that cannot be read leaves the process name.
            if (broken != "procs")
                Assert.Equal(broken == "path" ? "Game" : @"app at C:\Games\game.exe", sample.TopRam[0].Name);
        }
    }

    [Fact]
    public void A_namer_that_throws_falls_back_to_the_process_name()
    {
        var monitor = new SystemMonitor(new Cpu(), new Procs { Next = () => new[] { P(1, "code", 0, ramMb: 5) } },
            new Net(), new Gpu(), new Memory(), new Sensors(), new Power(), new ThrowingNamer(),
            processorCount: 4, now: () => TimeSpan.Zero);

        Assert.Equal("code", monitor.Sample().TopRam.Single().Name);
    }

    [Fact]
    public void Gpu_engine_instance_names_carry_the_pid()
    {
        Assert.Equal(14380, SystemMonitor.PidOf("pid_14380_luid_0x00000000_0x00010622_phys_0_eng_0_engtype_3D"));
        Assert.Null(SystemMonitor.PidOf("luid_0x00000000_0x00010622_phys_0_eng_0_engtype_3D"));
        Assert.Null(SystemMonitor.PidOf("pid__luid"));
        Assert.Null(SystemMonitor.PidOf("pid_99999999999_luid"));
        Assert.Equal("0x00000000_0x00010622",
            SystemMonitor.AdapterOf("pid_14380_luid_0x00000000_0x00010622_phys_0_eng_0_engtype_3D"));
        Assert.Equal("", SystemMonitor.AdapterOf("pid_14380_eng_0_engtype_3D"));
    }

    [Fact]
    public void Private_working_sets_join_repeated_instance_names_in_order()
    {
        // PDH lists twenty "chrome" instances under one name: the n-th chrome
        // of one counter is the n-th chrome of the other.
        var ids = new[]
        {
            new PdhValue("_Total", 0, 0), new PdhValue("chrome", 0, 30852), new PdhValue("chrome", 1, 22816),
            new PdhValue("code", 0, 400), new PdhValue("chrome", 2, 28276),
        };
        var sizes = new[]
        {
            new PdhValue("_Total", 0, 9e9), new PdhValue("chrome", 0, 100), new PdhValue("chrome", 1, 200),
            new PdhValue("code", 0, 300), new PdhValue("chrome", 2, 400),
            // An instance the id counter did not list joins nothing.
            new PdhValue("chrome", 3, 999),
        };

        var bytes = ProcessListReader.Join(ids, sizes)!;

        Assert.Equal(new Dictionary<int, long> { [30852] = 100, [22816] = 200, [400] = 300, [28276] = 400 }, bytes);
        Assert.Null(ProcessListReader.Join(ids, Array.Empty<PdhValue>()));
    }

    /// The one test that runs the real readers on this machine: read-only, and
    /// it asserts only that sampling does not throw.
    [Fact]
    public void Real_sampler_never_throws()
    {
        var monitor = new SystemMonitor(new FakeSensors(), new Power(), new RealAppNamer());
        var first = monitor.Sample();
        var second = monitor.Sample();
        Assert.NotNull(first.TopRam);
        Assert.NotNull(second.TopCpu);
    }
}

file sealed class ThrowingNamer : IAppNamer
{
    public string Name(string processOrExeName, string? fullPath) => throw new InvalidOperationException();
}
