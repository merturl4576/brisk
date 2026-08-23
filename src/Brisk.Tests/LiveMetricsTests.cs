using System;
using System.Collections.Generic;
using Brisk.Services;
using BriskEngine.Diagnostics;
using Xunit;

namespace Brisk.Tests;

public class LiveMetricsTests
{
    private sealed class FakeSensors : ISensorProbe
    {
        public double? Cpu { get; set; }
        public double? Gpu { get; set; }
        public double? CpuTempC() => Cpu;
        public double? GpuTempC() => Gpu;
        public int GpuCount() => 1;
    }

    private sealed class FakeProcessInfo : IProcessInfoProbe
    {
        public double Load { get; set; }
        public IReadOnlyList<(string Name, long WorkingSetBytes)> TopByMemory(int count) =>
            Array.Empty<(string, long)>();
        public double MemoryLoadPercent() => Load;
    }

    private static LiveMetrics Build(FakeSensors? sensors = null,
        FakeProcessInfo? processes = null, long freeDisk = 5L << 30,
        params (long Idle, long Total)?[] cpuSamples)
    {
        var queue = new Queue<(long Idle, long Total)?>(cpuSamples);
        return new LiveMetrics(sensors ?? new FakeSensors(),
            processes ?? new FakeProcessInfo(), () => freeDisk,
            () => queue.Count > 0 ? queue.Dequeue() : null);
    }

    [Fact]
    public void Cpu_FirstReadHasNoDelta_SecondComputesPercent()
    {
        var live = Build(cpuSamples: new (long, long)?[] { (100, 200), (150, 300) });

        Assert.Null(live.Read().CpuPercent);           // no previous sample yet
        // idle went 100→150 (Δ50) of total 200→300 (Δ100) → 50% busy
        Assert.Equal(50.0, live.Read().CpuPercent);
    }

    [Fact]
    public void Cpu_UnavailableTimes_ReportNull()
    {
        var live = Build(cpuSamples: new (long, long)?[] { null, null });
        Assert.Null(live.Read().CpuPercent);
        Assert.Null(live.Read().CpuPercent);
    }

    [Fact]
    public void Temp_PicksTheHighestSensor_AndLabelsIt()
    {
        var live = Build(new FakeSensors { Cpu = 60, Gpu = 72 });
        var reading = live.Read();
        Assert.Equal(72, reading.TempC);
        Assert.Equal("GPU", reading.TempSource);

        live = Build(new FakeSensors { Cpu = 80, Gpu = 70 });
        reading = live.Read();
        Assert.Equal(80, reading.TempC);
        Assert.Equal("CPU", reading.TempSource);

        live = Build(new FakeSensors { Cpu = 65 });   // GPU sensor missing
        reading = live.Read();
        Assert.Equal(65, reading.TempC);
        Assert.Equal("CPU", reading.TempSource);
    }

    /// NaN is not a temperature, and the live tile is where that bit the
    /// hardest: a present-but-silent CPU sensor reports NaN on EVERY tick, and
    /// `is not null` sent it down the CPU branch — so the overview rendered
    /// "NaN°C" under "Temperature · CPU" for as long as the window was open,
    /// while a sensor that answered null correctly rendered "—". Same predicate
    /// as the scan snapshot, the CLI's notice and the thermals rule.
    [Theory]
    [InlineData(double.NaN, null)]
    [InlineData(double.PositiveInfinity, null)]
    [InlineData(double.NaN, double.NaN)]
    public void Temp_NonFiniteReadings_AreNotTemperatures(double cpu, double? gpu)
    {
        var reading = Build(new FakeSensors { Cpu = cpu, Gpu = gpu }).Read();

        Assert.Null(reading.TempC);
        Assert.Null(reading.TempSource);
    }

    /// The half that must still work: a silent CPU must not hide a GPU that
    /// answered. Before the fix this reported the NaN; a fix that simply
    /// dropped both readings whenever either was NaN would be just as wrong.
    [Fact]
    public void Temp_NaNCpu_LetsTheGpuReadingThrough()
    {
        var reading = Build(new FakeSensors { Cpu = double.NaN, Gpu = 71 }).Read();

        Assert.Equal(71, reading.TempC);
        Assert.Equal("GPU", reading.TempSource);
    }

    [Fact]
    public void Temp_NoSensors_ReportsNull()
    {
        var reading = Build(new FakeSensors()).Read();
        Assert.Null(reading.TempC);
        Assert.Null(reading.TempSource);
    }

    [Fact]
    public void Ram_ZeroMeansUnavailable_NotAZeroPercent()
    {
        Assert.Null(Build(processes: new FakeProcessInfo { Load = 0 }).Read().RamPercent);
        Assert.Equal(55.5,
            Build(processes: new FakeProcessInfo { Load = 55.5 }).Read().RamPercent);
    }

    [Fact]
    public void FreeDisk_PassesThrough()
    {
        Assert.Equal(5L << 30, Build().Read().FreeDiskBytes);
    }

    [Fact]
    public void StartStop_TicksOnlyWhileStarted_StartIsIdempotent()
    {
        var live = Build();
        var ticks = 0;
        Assert.False(live.IsTicking);   // constructed = silent (tray-only state)

        live.Start(() => ticks++);
        Assert.True(live.IsTicking);
        Assert.Equal(1, ticks);         // one immediate refresh on show

        live.Start(() => ticks++);      // window re-shown while already ticking
        Assert.Equal(1, ticks);

        live.Stop();
        Assert.False(live.IsTicking);   // hidden: nothing may tick any more

        live.Start(() => ticks++);      // shown again → fresh immediate refresh
        Assert.True(live.IsTicking);
        Assert.Equal(2, ticks);
        live.Stop();
    }
}
