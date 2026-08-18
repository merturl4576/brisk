using BriskEngine.Cleaning;

namespace BriskEngine.Diagnostics;

public sealed record DiagnosticContext(
    IPowercfgProbe Powercfg,
    IRegistryProbe Registry,
    IProcessInfoProbe Processes,
    ISensorProbe Sensors,
    IDisplayProbe Displays,
    IDiskInfoProbe Disk,
    IFileProbe Files,
    IProcessLister RunningApps,
    string DataDirectory);   // %LOCALAPPDATA%\brisk — history store, journals
