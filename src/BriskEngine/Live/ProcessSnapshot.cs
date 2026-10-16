using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BriskEngine.Live;

public sealed record ProcSample(int Pid, string Name, int SessionId, TimeSpan? Cpu,
    long PrivateBytes, bool HasWindow, string? ExePath);

public interface IProcessSnapshotProbe
{
    int ProcessorCount { get; }
    IReadOnlyList<ProcSample> Snapshot();
}

public sealed class RealProcessSnapshotProbe : IProcessSnapshotProbe
{
    public int ProcessorCount => Environment.ProcessorCount;

    public IReadOnlyList<ProcSample> Snapshot()
    {
        var list = new List<ProcSample>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    TimeSpan? cpu = null;
                    try { cpu = p.TotalProcessorTime; } catch (Exception) { }
                    long privateBytes = 0;
                    try { privateBytes = p.PrivateMemorySize64; } catch (Exception) { }
                    var window = false;
                    try { window = p.MainWindowHandle != IntPtr.Zero; } catch (Exception) { }
                    string? exe = null;
                    try { exe = p.MainModule?.FileName; } catch (Exception) { }
                    list.Add(new ProcSample(p.Id, p.ProcessName, p.SessionId, cpu, privateBytes, window, exe));
                }
                catch (Exception) { }   // the process exited while it was being read
            }
        }
        return list;
    }
}
