using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using BriskEngine.Diagnostics;

namespace Brisk.Services;

/// One sample of the machine's vitals for the overview dashboard. Null means
/// "this sensor has nothing trustworthy right now" and renders as "—".
public sealed record LiveReading(
    double? CpuPercent,
    double? RamPercent,
    double? TempC,
    string? TempSource,   // "CPU" / "GPU" — hardware label, not user text
    long FreeDiskBytes);

/// Live vitals for the overview tiles, plus the visibility-gated pulse that
/// refreshes them. The spec's no-background-work promise is enforced here:
/// the timer runs only between Start (window became visible) and Stop
/// (hidden/closed/minimized) — nothing ever ticks while brisk is tray-only.
public interface ILiveMetrics
{
    /// Reads all vitals once. Called off the UI thread; never throws for a
    /// missing sensor — it degrades to nulls instead.
    LiveReading Read();
    void Start(Action onTick);
    void Stop();
    bool IsTicking { get; }
}

/// Sensor sources (least code that is reliable, per spec):
/// - Temperatures: the engine's ISensorProbe (LibreHardwareMonitor), the same
///   instance the diagnostics use — no second hardware session.
/// - RAM: the engine's IProcessInfoProbe (GlobalMemoryStatusEx underneath).
/// - CPU: GetSystemTimes deltas between ticks. PerformanceCounter would need
///   a new NuGet package on .NET 8; this is two P/Invoke lines instead.
/// - Disk: the host's FreeDiskBytes, passed in as a delegate.
public sealed class LiveMetrics : ILiveMetrics
{
    private readonly ISensorProbe _sensors;
    private readonly IProcessInfoProbe _processes;
    private readonly Func<long> _freeDiskBytes;
    private readonly Func<(long Idle, long Total)?> _cpuTimes;
    private readonly DispatcherTimer _timer;
    private Action? _onTick;
    private (long Idle, long Total)? _previousCpu;

    public LiveMetrics(ISensorProbe sensors, IProcessInfoProbe processes,
        Func<long> freeDiskBytes, Func<(long Idle, long Total)?>? cpuTimes = null)
    {
        _sensors = sensors;
        _processes = processes;
        _freeDiskBytes = freeDiskBytes;
        _cpuTimes = cpuTimes ?? SystemCpuTimes;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        _timer.Tick += (_, _) => _onTick?.Invoke();
    }

    public bool IsTicking => _timer.IsEnabled;

    public void Start(Action onTick)
    {
        if (_timer.IsEnabled) return;
        _onTick = onTick;
        _timer.Start();
        onTick();   // fill the tiles the moment the window shows, not 1.6 s later
    }

    public void Stop() => _timer.Stop();

    /// Callers serialize Read (one tick in flight at a time), so the previous
    /// CPU sample needs no locking. The first read has no delta and reports
    /// a null CPU; the next tick fills it in.
    public LiveReading Read()
    {
        double? cpu = null;
        if (_cpuTimes() is { } now)
        {
            if (_previousCpu is { } prev && now.Total > prev.Total)
                cpu = Math.Clamp(
                    100.0 * (1.0 - (now.Idle - prev.Idle) / (double)(now.Total - prev.Total)),
                    0.0, 100.0);
            _previousCpu = now;
        }
        // GlobalMemoryStatusEx reports 0 only on failure — a running Windows
        // is never at a true 0% load, so 0 renders as "sensor unavailable".
        var ramLoad = _processes.MemoryLoadPercent();
        double? ram = ramLoad > 0 ? ramLoad : null;
        // Through the shared predicate, not a bare null check. NaN is what a
        // present-but-silent sensor reports, and it reports it every tick — so
        // a NaN CPU beside a quiet GPU took the CPU branch below and the tile
        // rendered "NaN°C" under the caption "Temperature · CPU", where a null
        // correctly renders "—". That is a reading claimed rather than a sensor
        // admitting it has nothing, and this class takes any ISensorProbe: it
        // cannot assume the one it was handed filters.
        var cpuTemp = Reading(_sensors.CpuTempC());
        var gpuTemp = Reading(_sensors.GpuTempC());
        double? temp = null;
        string? source = null;
        if (cpuTemp is not null && (gpuTemp is null || cpuTemp >= gpuTemp))
        {
            temp = cpuTemp;
            source = "CPU";
        }
        else if (gpuTemp is not null)
        {
            temp = gpuTemp;
            source = "GPU";
        }
        return new LiveReading(cpu, ram, temp, source, _freeDiskBytes());
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    private static double? Reading(double? celsius) =>
        SensorReading.IsReal(celsius) ? celsius : null;

    /// Kernel time includes idle time, so Total = kernel + user is the whole
    /// pie and Idle is the slice spent doing nothing.
    private static (long Idle, long Total)? SystemCpuTimes()
    {
        try
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
            return (idle, kernel + user);
        }
        catch
        {
            return null;
        }
    }
}
