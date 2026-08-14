using System.Diagnostics;

namespace BriskEngine.Cleaning;

public interface IProcessLister
{
    bool IsRunning(string processName);
}

public sealed class RealProcessLister : IProcessLister
{
    public bool IsRunning(string processName) =>
        Process.GetProcessesByName(processName).Length > 0;
}
