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
    IDefenderProbe Defender,
    string DataDirectory)    // %LOCALAPPDATA%\brisk — history store, journals
{
    /// What this scan has already measured, so it does not measure it
    /// again. The table belongs to ONE scan and to no other, which is what
    /// makes it the right place for an answer that must not be paid for
    /// twice inside a scan and must not survive one.
    ///
    /// THE CONTEXT ITSELF IS NOT PER-SCAN, and that is the trap this member
    /// is written around. The CLI builds one per process, so a CLI scan gets
    /// a fresh table for free; brisk-app builds one in AppServices.Build()
    /// and reuses it for the life of the window. A record `with` copies the
    /// Memo REFERENCE, so a scan that merely copied the app's context would
    /// answer out of the first scan's table forever — the Defender read and
    /// the posture rows frozen at whatever the machine looked like when the
    /// app started, still saying "Windows does not ask" after the fix that
    /// made it ask again. ForScan() below is what replaces the table, and
    /// EngineHost.ScanAsync is the one caller that has to.
    ///
    /// Concurrent because nothing promises the rules stay sequential: they
    /// run in a foreach today, and a scan that parallelised them would
    /// corrupt a plain dictionary silently. Keyed by the caller's own
    /// prefix — FileStats writes "stats:" + path — so a second kind of
    /// memo can share the table without colliding with the first.
    public System.Collections.Concurrent.ConcurrentDictionary<string, object> Memo { get; init; } = new();

    /// This context with an EMPTY memo: what a caller that reuses a context
    /// across scans has to take before each one. Every other member is
    /// carried over, because the probes are the same probes and only the
    /// answers went stale.
    public DiagnosticContext ForScan() => this with { Memo = new() };
}
