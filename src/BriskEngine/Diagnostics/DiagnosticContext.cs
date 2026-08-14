namespace BriskEngine.Diagnostics;

public sealed record DiagnosticContext(
    IPowercfgProbe Powercfg,
    IRegistryProbe Registry,
    IProcessInfoProbe Processes,
    ISensorProbe Sensors,
    IDiskInfoProbe Disk,
    string DataDirectory);   // %LOCALAPPDATA%\brisk — history store, journals
