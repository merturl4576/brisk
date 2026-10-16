using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BriskEngine.Live;

public interface IProcessCloser
{
    /// Asks each window to close, so the app can prompt to save. Returns how
    /// many were asked.
    int RequestClose(IReadOnlyList<int> pids);
    void Kill(IReadOnlyList<int> pids);
}

public sealed class RealProcessCloser : IProcessCloser
{
    public int RequestClose(IReadOnlyList<int> pids)
    {
        var asked = 0;
        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow()) asked++;
            }
            catch (Exception) { }
        }
        return asked;
    }

    public void Kill(IReadOnlyList<int> pids)
    {
        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(entireProcessTree: true);
            }
            catch (Exception) { }
        }
    }
}
