using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Services;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using BriskEngine.Models;
using BriskEngine.Safety;
using Xunit;

namespace Brisk.Tests;

file sealed class NullPowercfg : IPowercfgProbe
{
    public (Guid Id, string Name) GetActiveScheme() => (Guid.Empty, "High performance");
    public IReadOnlyList<(Guid Id, string Name)> ListSchemes() =>
        Array.Empty<(Guid, string)>();
    public void SetActive(Guid id) { }
}

file sealed class NullRegistry : IRegistryProbe
{
    public string? GetString(string k, string v) => null;
    public void SetString(string k, string v, string value) { }
    public void DeleteValue(string k, string v) { }
    public byte[]? GetBytes(string k, string v) => null;
    public void SetBytes(string k, string v, byte[] value) { }
    public int? GetInt(string k, string v) => null;
    public void SetInt(string k, string v, int value) { }
    public IReadOnlyList<string> GetValueNames(string k) => Array.Empty<string>();
    public IReadOnlyList<string> GetSubKeyNames(string k) => Array.Empty<string>();
}

file sealed class NullProcessInfo : IProcessInfoProbe
{
    public IReadOnlyList<(string Name, long WorkingSetBytes)> TopByMemory(int count) =>
        Array.Empty<(string, long)>();
    public double MemoryLoadPercent() => 10;
}

file sealed class NullSensors : ISensorProbe
{
    public double? CpuTempC() => null;
    public double? GpuTempC() => null;
    public int GpuCount() => 0;
}

/// The ordinary case, and the only one on an administrator account: the
/// process token and the signed-in user are the same account.
file sealed class SameUserSession : ISessionProbe
{
    public SessionIdentity Current() =>
        new(@"PC\alice", @"PC\alice", false);
}

file sealed class NullDisplays : IDisplayProbe
{
    public int PersistCalls { get; private set; }
    public IReadOnlyList<DisplayInfo> Displays() => System.Array.Empty<DisplayInfo>();
    public void SetRefreshRate(string deviceName, int hz) { }
    public void PersistCurrentModes() => PersistCalls++;
}

file sealed class NullEventLog : IEventLogProbe
{
    public IReadOnlyList<BootRecord> RecentBoots(int count) => System.Array.Empty<BootRecord>();
}

file sealed class NullHardware : IHardwareProbe
{
    public IReadOnlyList<MemoryModule> MemoryModules() => System.Array.Empty<MemoryModule>();
}

file sealed class NullMemoryIntegrity : IMemoryIntegrityProbe
{
    public bool? IsOn() => null;
}

file sealed class NullDisk : IDiskInfoProbe
{
    public long FreeBytes(string driveRoot) => 100L << 30;
    public long TotalBytes(string driveRoot) => 500L << 30;
}

file sealed class NullFiles : IFileProbe
{
    public bool FileExists(string path) => false;
    public string? ReadAllText(string path) => null;
    public void WriteAllText(string path, string content) { }
    public IReadOnlyList<string> ListFiles(string directory) => Array.Empty<string>();
    public long DirectorySizeBytes(string path) => 0;
    public DateTime? NewestWriteUtc(string path, int limit = 1500) => null;
}

file sealed class NothingRuns : IProcessLister
{
    public bool IsRunning(string processName) => false;
}

file sealed class FixedRule : IDiagnosticRule
{
    private readonly DiagnosticFinding? _finding;
    public FixedRule(string id, DiagnosticFinding? finding) { Id = id; _finding = finding; }
    public string Id { get; }
    public RuleCategory Category => RuleCategory.Auto;
    public DiagnosticFinding? Detect(DiagnosticContext ctx) => _finding;
    public string Fix(DiagnosticContext ctx) => "{}";
    public void Undo(DiagnosticContext ctx, string priorStateJson) { }
}

file sealed class BoomRule : IDiagnosticRule
{
    public string Id => "boom";
    public RuleCategory Category => RuleCategory.Auto;
    public DiagnosticFinding? Detect(DiagnosticContext ctx) =>
        throw new InvalidOperationException("probe exploded");
    public string Fix(DiagnosticContext ctx) => "{}";
    public void Undo(DiagnosticContext ctx, string priorStateJson) { }
}

public sealed class EngineHostTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-eh-").FullName;

    private EngineHost Host(params IDiagnosticRule[] rules)
    {
        var ctx = new DiagnosticContext(new NullPowercfg(), new NullRegistry(),
            new NullProcessInfo(), new NullSensors(), new NullDisplays(), new NullEventLog(),
            new NullHardware(), new NullDisk(), new NullFiles(),
            new NothingRuns(), new NullMemoryIntegrity(), _root);
        var logPath = Path.Combine(_root, "action-log.jsonl");
        var log = new ActionLog(logPath);
        var journal = new FixJournal(Path.Combine(_root, "fix-journal.jsonl"));
        var scanDir = Path.Combine(_root, "scan-me");
        Directory.CreateDirectory(scanDir);
        File.WriteAllBytes(Path.Combine(scanDir, "x.bin"), new byte[64]);
        var targets = new[] { new CleanupTarget("t1", "T1", CleanupLevel.Safe,
            new List<string> { scanDir }, "Test") };
        return new EngineHost(ctx, rules, new Scanner(targets, new NothingRuns()),
            new FixRunner(journal, log),
            new CleanRunner(new SafetyValidator(), new NullRecycler(), log,
                new RealProcessRunner(), () => false),
            journal, new StartupManager(new NullRegistry(), log), logPath,
            Path.Combine(_root, "Brisk.Cli.exe"), new SameUserSession());
    }

    private sealed class NullRecycler : IRecycler
    {
        public void Recycle(string path) { }
        public void Recycle(IReadOnlyList<string> paths) { }
    }

    [Fact]
    public async Task ScanAsync_CollectsFindings_SkipsThrowingRule_ComputesHealth()
    {
        var finding = TestData.Finding("power-plan", Severity.Warning, stars: 4);
        var host = Host(new FixedRule("power-plan", finding),
            new FixedRule("quiet", null), new BoomRule());
        var progress = new ConcurrentBag<string>();

        var snapshot = await host.ScanAsync(
            new SyncProgress(progress.Add));

        Assert.Equal("power-plan", Assert.Single(snapshot.Findings).RuleId);
        Assert.Equal(88, snapshot.Health);
        Assert.Equal(64, snapshot.Cleaner.TotalBytes);
        Assert.NotEmpty(progress);
    }

    [Fact]
    public void Fix_UnknownRule_Fails()
    {
        var outcome = Host().Fix("nope");
        Assert.False(outcome.Ok);
        Assert.Contains("unknown", outcome.Message);
    }

    [Fact]
    public void FixThenUndo_RoundTrips_ThroughJournal()
    {
        var host = Host(new FixedRule("power-plan",
            TestData.Finding("power-plan")));
        Assert.True(host.Fix("power-plan").Ok);
        Assert.Equal("power-plan", Assert.Single(host.ListUndoable()).RuleId);
        Assert.True(host.Undo("power-plan").Ok);
        Assert.Empty(host.ListUndoable());
        Assert.Equal(2, host.ReadLog().Count);
    }

    /// The card's "what brisk could not read" section is built from the
    /// snapshot, so the scan records what the sensors answered at scan time.
    [Fact]
    public async Task ScanAsync_RecordsSensorStatus()
    {
        var host = Host(Array.Empty<IDiagnosticRule>());

        var snapshot = await host.ScanAsync();

        Assert.NotNull(snapshot.Sensors);
        Assert.False(snapshot.Sensors!.CpuRead);
        Assert.False(snapshot.Sensors.GpuRead);
        Assert.Null(snapshot.Sensors.MemoryIntegrityOn);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}

/// IProgress that reports synchronously — Progress<T> posts to a sync context
/// and races with test assertions.
public sealed class SyncProgress : IProgress<string>
{
    private readonly Action<string> _handler;
    public SyncProgress(Action<string> handler) { _handler = handler; }
    public void Report(string value) => _handler(value);
}
