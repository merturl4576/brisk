using BriskEngine.Cleaning;

namespace BriskEngine.Diagnostics;

public sealed record DiagnosticContext(
    IPowercfgProbe Powercfg,
    IRegistryProbe Registry,
    IProcessInfoProbe Processes,
    ISensorProbe Sensors,
    IDisplayProbe Displays,
    IEventLogProbe EventLog,
    IHardwareProbe Hardware,
    IDiskInfoProbe Disk,
    IFileProbe Files,
    IProcessLister RunningApps,
    IMemoryIntegrityProbe MemoryIntegrity,
    IDeliveryOptimizationProbe DeliveryOptimization,
    string DataDirectory);   // %LOCALAPPDATA%\brisk — history store, journals
