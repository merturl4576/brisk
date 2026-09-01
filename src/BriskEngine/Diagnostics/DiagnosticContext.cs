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
    string DataDirectory)    // %LOCALAPPDATA%\brisk — history store, journals
{
    /// What this scan has already measured, so it does not measure it
    /// again. The context is built ONCE per scan (AppServices for the app,
    /// Program for the CLI) and the rules run against it in turn, which
    /// makes its lifetime exactly the right one for an answer that must
    /// not go stale between scans and must not be paid for twice inside
    /// one.
    ///
    /// Concurrent because nothing promises the rules stay sequential: they
    /// run in a foreach today, and a scan that parallelised them would
    /// corrupt a plain dictionary silently. Keyed by the caller's own
    /// prefix — FileStats writes "stats:" + path — so a second kind of
    /// memo can share the table without colliding with the first.
    public System.Collections.Concurrent.ConcurrentDictionary<string, object> Memo { get; } = new();
}
