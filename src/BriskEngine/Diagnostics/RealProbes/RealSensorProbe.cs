using System;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace BriskEngine.Diagnostics.RealProbes;

/// LibreHardwareMonitor is not thread-safe, and brisk-app shares one probe:
/// the Home monitor reads the temperatures once a second from its sampler
/// thread while a scan's thermals rule may read them from another. Every
/// touch of the Computer (open, update, read, close) holds one lock, so two
/// callers take turns instead of updating the same hardware at once. A
/// second Computer for the monitor would not be safer: both would drive the
/// same kernel driver and the same sensors.
public sealed class RealSensorProbe : ISensorProbe, IDisposable
{
    private readonly Computer _computer;
    private readonly object _gate = new();
    private bool _closed;

    public RealSensorProbe()
    {
        _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
        lock (_gate)
        {
            try
            {
                _computer.Open();
            }
            catch
            {
                // sensors unavailable (no admin / unsupported hardware) — CpuTempC/GpuTempC will return null
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            try
            {
                _computer.Close();
            }
            catch
            {
                // best-effort: never let teardown crash the process
            }
        }
    }

    public double? CpuTempC()
    {
        try
        {
            return MaxTemperature(h => h.HardwareType == HardwareType.Cpu);
        }
        catch
        {
            return null;
        }
    }

    public double? GpuTempC()
    {
        try
        {
            return MaxTemperature(h =>
                h.HardwareType == HardwareType.GpuNvidia ||
                h.HardwareType == HardwareType.GpuAmd ||
                h.HardwareType == HardwareType.GpuIntel);
        }
        catch
        {
            return null;
        }
    }

    public int GpuCount()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            using var results = searcher.Get();
            return results.Count;
        }
        catch
        {
            return 1;
        }
    }

    private double? MaxTemperature(Func<IHardware, bool> matches)
    {
        lock (_gate)
        {
            if (_closed) return null;
            double? max = null;
            foreach (var hardware in _computer.Hardware)
            {
                if (!matches(hardware)) continue;
                hardware.Update();
                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.SensorType != SensorType.Temperature || sensor.Value is null) continue;
                    var value = (double)sensor.Value.Value;
                    // A sensor that is present but has nothing to say reports NaN.
                    // Passing it on makes it a reading: it fails every threshold,
                    // so nothing calls it hot, but it is not null either — it would
                    // print "CPU NaN°C" and take the both-read template.
                    if (!double.IsFinite(value)) continue;
                    if (max is null || value > max) max = value;
                }
            }
            return max;
        }
    }
}
