using System;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace BriskEngine.Diagnostics.RealProbes;

public sealed class RealSensorProbe : ISensorProbe
{
    private readonly Computer _computer;

    public RealSensorProbe()
    {
        _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
        try
        {
            _computer.Open();
        }
        catch
        {
            // sensors unavailable (no admin / unsupported hardware) — CpuTempC/GpuTempC will return null
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
        double? max = null;
        foreach (var hardware in _computer.Hardware)
        {
            if (!matches(hardware)) continue;
            hardware.Update();
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.SensorType != SensorType.Temperature || sensor.Value is null) continue;
                var value = (double)sensor.Value.Value;
                if (max is null || value > max) max = value;
            }
        }
        return max;
    }
}
