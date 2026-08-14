using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace BriskEngine.Diagnostics.RealProbes;

public sealed class RealProcessInfoProbe : IProcessInfoProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public IReadOnlyList<(string Name, long WorkingSetBytes)> TopByMemory(int count)
    {
        try
        {
            var processes = Process.GetProcesses();
            try
            {
                return processes
                    .GroupBy(p => p.ProcessName)
                    .Select(g => (Name: g.Key, WorkingSetBytes: SafeSum(g)))
                    .OrderByDescending(p => p.WorkingSetBytes)
                    .Take(count)
                    .ToList();
            }
            finally
            {
                foreach (var p in processes) p.Dispose();
            }
        }
        catch
        {
            return Array.Empty<(string, long)>();
        }
    }

    private static long SafeSum(IEnumerable<Process> group)
    {
        long total = 0;
        foreach (var p in group)
        {
            try { total += p.WorkingSet64; }
            catch { /* process may have exited, or access denied — skip it */ }
        }
        return total;
    }

    public double MemoryLoadPercent()
    {
        try
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref status)) return 0;
            return status.dwMemoryLoad;
        }
        catch
        {
            return 0;
        }
    }
}
