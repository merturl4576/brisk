using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Brisk.Services;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
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

/// A registry that keeps what is written to it and can be told, afterwards,
/// to refuse every read under one key path — SecurityException, which is what
/// RegistryKey.OpenSubKey throws for a key this process may not open and what
/// RealRegistryProbe passes straight out. Armed after the fixes are applied,
/// so the test plants a switch brisk really turned off and only then takes the
/// read away from it.
file sealed class ArmableRegistry : IRegistryProbe
{
    private readonly Dictionary<string, object> _values =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _refused;

    public void RefuseReadsUnder(string keyPath) => _refused = keyPath;

    private static string K(string k, string v) => $"{k}::{v}";

    private void Refuse(string keyPath)
    {
        if (_refused is not null && keyPath.StartsWith(
                _refused, StringComparison.OrdinalIgnoreCase))
            throw new SecurityException($"this process may not read '{keyPath}'");
    }

    private T? Get<T>(string k, string v) where T : class
    {
        Refuse(k);
        return _values.TryGetValue(K(k, v), out var o) ? o as T : null;
    }

    public string? GetString(string k, string v) => Get<string>(k, v);
    public byte[]? GetBytes(string k, string v) => Get<byte[]>(k, v);
    public int? GetInt(string k, string v)
    {
        Refuse(k);
        return _values.TryGetValue(K(k, v), out var o) ? o as int? : null;
    }

    public void SetString(string k, string v, string value) => _values[K(k, v)] = value;
    public void SetBytes(string k, string v, byte[] value) => _values[K(k, v)] = value;
    public void SetInt(string k, string v, int value) => _values[K(k, v)] = value;
    public void DeleteValue(string k, string v) => _values.Remove(K(k, v));
    public IReadOnlyList<string> GetValueNames(string k) { Refuse(k); return Array.Empty<string>(); }
    public IReadOnlyList<string> GetSubKeyNames(string k) { Refuse(k); return Array.Empty<string>(); }
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

/// A probe that answers exactly what a test hands it, including the answers
/// that are not numbers. NullSensors returns null to everything, so a suite
/// built only on it can never tell a scan that read two temperatures from one
/// that read none — the three booleans on SensorStatus drive the whole
/// signature section of the shareable card.
file sealed class FixedSensors : ISensorProbe
{
    private readonly double? _cpu;
    private readonly double? _gpu;
    public FixedSensors(double? cpu, double? gpu) { _cpu = cpu; _gpu = gpu; }
    public double? CpuTempC() => _cpu;
    public double? GpuTempC() => _gpu;
    public int GpuCount() => _gpu is null ? 0 : 1;
}

/// A shared stopwatch with no clock: it only answers "which happened first".
/// Enough to pin an ordering guarantee, and it cannot go flaky the way a
/// timestamp comparison can on a machine that scans in under a millisecond.
file sealed class CallOrder
{
    private int _next;
    public int Next() => System.Threading.Interlocked.Increment(ref _next);
}

/// Records WHEN the scan asked for a CPU temperature, not what it answered.
file sealed class SequencedSensors : ISensorProbe
{
    private readonly CallOrder _order;
    public SequencedSensors(CallOrder order) { _order = order; }
    public int? CpuIndex { get; private set; }
    public double? CpuTempC() { CpuIndex ??= _order.Next(); return null; }
    public double? GpuTempC() => null;
    public int GpuCount() => 0;
}

/// Records WHEN the rule loop reached it. Detects nothing: the finding is
/// beside the point, the moment is the point.
file sealed class SequencedRule : IDiagnosticRule
{
    private readonly CallOrder _order;
    public SequencedRule(CallOrder order) { _order = order; }
    public string Id => "sequenced";
    public RuleCategory Category => RuleCategory.Auto;
    public int? DetectIndex { get; private set; }
    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        DetectIndex ??= _order.Next();
        return null;
    }
    public string Fix(DiagnosticContext ctx) => "{}";
    public void Undo(DiagnosticContext ctx, string priorStateJson) { }
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

/// Answers nothing, which is what an unread counter looks like. Not a
/// PeerUpload of zeroes: these host tests say nothing about what this
/// machine uploaded.
file sealed class NullDeliveryOptimization : IDeliveryOptimizationProbe
{
    public PeerUpload? UploadedToPeers() => null;
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
    public BriskEngine.Models.DirectoryStats DirectoryStats(
        string path, long minFileBytes, int take) =>
        new(0, Array.Empty<BriskEngine.Models.LargeFile>());
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

    private EngineHost Host(params IDiagnosticRule[] rules) =>
        Host(new NullSensors(), new NullRegistry(), rules);

    /// Same fixture with the sensor probe swapped — the context is built here,
    /// so a test that needs to watch the probes has to come in through this
    /// door rather than assemble a second one.
    private EngineHost Host(ISensorProbe sensors, params IDiagnosticRule[] rules) =>
        Host(sensors, new NullRegistry(), rules);

    /// The same door for the registry, which the read-back reads a second
    /// time after the rule loop has read it once.
    private EngineHost Host(IRegistryProbe registry, params IDiagnosticRule[] rules) =>
        Host(new NullSensors(), registry, rules);

    private EngineHost Host(ISensorProbe sensors, IRegistryProbe registry,
        params IDiagnosticRule[] rules)
    {
        var ctx = new DiagnosticContext(new NullPowercfg(), registry,
            new NullProcessInfo(), sensors, new NullDisplays(), new NullEventLog(),
            new NullHardware(), new NullDisk(), new NullFiles(),
            new NothingRuns(), new NullMemoryIntegrity(),
            new NullDeliveryOptimization(), _root);
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

    /// THE READ-BACK IS PART OF THE SCAN, and it re-runs the same registry
    /// reads the rule loop just ran — but it sat outside the try above, so
    /// one key this process may not open threw out of ScanAsync itself: no
    /// snapshot, no Changed, and a window still showing the previous scan
    /// with nothing said. The rule loop's own comment is the standard this
    /// pins for the second read of the same registry.
    ///
    /// The switch that could not be re-read gets no row — an absence, which
    /// is what ReadBack already gives a journal entry it cannot match — and
    /// the scan comes back whole: the other switch's row, and the cleaner's
    /// bytes off the same pass.
    [Fact]
    public async Task ScanAsync_AReadBackReadThatRefuses_DoesNotKillTheScan()
    {
        var registry = new ArmableRegistry();
        var host = Host(registry, new AdvertisingIdRule(), new SpeechTypingRule());
        Assert.True(host.Fix("advertising-id").Ok);
        Assert.True(host.Fix("speech-typing").Ok);
        registry.RefuseReadsUnder(AdvertisingIdRule.KeyPath);

        var snapshot = await host.ScanAsync();

        Assert.Equal("speech-typing", Assert.Single(snapshot.ReadBack).RuleId);
        Assert.Equal(64, snapshot.Cleaner.TotalBytes);
    }

    /// The other end of the same defect, and the one a lock inside FixJournal
    /// cannot close: the journal FILE, held by something outside this process
    /// — the CLI mid-fix, a backup tool, an antivirus scanner — so
    /// File.ReadAllLines cannot open it at all. ListUndoable throws before
    /// ReadBack.For is reached, and the whole scan went with it.
    ///
    /// An empty read-back is the honest answer: brisk could not read the
    /// journal, so it has nothing to say about what it turned off, and the
    /// page shows no read-back rows. It is not "nothing was ever fixed" being
    /// claimed — no row is a row nobody prints.
    [Fact]
    public async Task ScanAsync_TheJournalIsHeldByAnotherProcess_StillCompletes()
    {
        var host = Host(new AdvertisingIdRule());
        Assert.True(host.Fix("advertising-id").Ok);
        using var held = new FileStream(
            Path.Combine(_root, "fix-journal.jsonl"),
            FileMode.Open, FileAccess.Read, FileShare.None);

        var snapshot = await host.ScanAsync();

        Assert.Empty(snapshot.ReadBack);
        Assert.Equal(64, snapshot.Cleaner.TotalBytes);
    }

    /// THE DEVICE RECORDS RIDE THE SCAN, in the same pass over the same
    /// context that produced the findings — the argument ScanSnapshot already
    /// makes about the read-back, for the same reason. The Gizlilik page shows
    /// the COUNT off the finding and the RECORDS off this list, and two
    /// channels for one reading is how two surfaces come to disagree about one
    /// machine. A page that asked the host for the devices after the scan
    /// would be exactly that second channel.
    ///
    /// The count and the records are asserted together, off one registry: two
    /// instances under one model is a finding that says 2 and a list of two
    /// records naming that model twice.
    [Fact]
    public async Task ScanAsync_CarriesTheUsbDeviceRecords_FromTheSamePass()
    {
        var registry = new FakeRegistry();
        PlantUsb(registry, "Ven_Kingston&Prod_DataTraveler", "0123456789ABCD");
        PlantUsb(registry, "Ven_Kingston&Prod_DataTraveler", "SECONDSTICK");
        var host = Host(registry, new UsbHistoryRule());

        var snapshot = await host.ScanAsync();

        Assert.Equal("2", Assert.Single(snapshot.Findings).Headline!.Value);
        Assert.Equal(
            new[] { "Ven_Kingston&Prod_DataTraveler", "Ven_Kingston&Prod_DataTraveler" },
            snapshot.UsbDevices.Select(d => d.Model));
    }

    /// A machine with nothing to read there gets an empty list, which is a
    /// claim and the honest one: the record holds no device brisk could read.
    /// It is not "no device was ever attached" — the rule's own finding is
    /// what says brisk could not establish a count, and the page renders no
    /// fold at all over an empty list.
    [Fact]
    public async Task ScanAsync_NoUsbRecordToRead_CarriesAnEmptyList()
    {
        var snapshot = await Host(new UsbHistoryRule()).ScanAsync();

        Assert.Empty(snapshot.UsbDevices);
    }

    /// One instance of one USB storage device, the two levels deep Windows
    /// records it at. No property store: this fixture is about the channel,
    /// and the dates behind it are PrivacyDisclosureRuleTests' business.
    private static void PlantUsb(FakeRegistry reg, string model, string instance)
    {
        Add(reg, UsbHistoryRule.KeyPath, model);
        Add(reg, $@"{UsbHistoryRule.KeyPath}\{model}", instance);
    }

    private static void Add(FakeRegistry reg, string parent, string child)
    {
        if (!reg.SubKeys.TryGetValue(parent, out var children))
            reg.SubKeys[parent] = children = new List<string>();
        if (!children.Contains(child)) children.Add(child);
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
    ///
    /// This asserted false/false/null against an all-null fixture and nothing
    /// else, so `new SensorStatus(false, false, null)` hardcoded into ScanAsync
    /// would have passed it: the test could not fail. The two theories below
    /// are what make it a measurement — one machine whose sensors answer, one
    /// whose sensors are present and silent.
    [Fact]
    public async Task ScanAsync_RecordsSensorStatus()
    {
        var host = Host(Array.Empty<IDiagnosticRule>());

        var snapshot = await host.ScanAsync();

        Assert.False(snapshot.Sensors.CpuRead);
        Assert.False(snapshot.Sensors.GpuRead);
        Assert.Null(snapshot.Sensors.MemoryIntegrityOn);
    }

    /// Real temperatures, recorded as read. Without this the whole section is
    /// pinned only against a machine that answered nothing.
    [Fact]
    public async Task ScanAsync_RealTemperatures_AreRecordedAsRead()
    {
        var host = Host(new FixedSensors(55, 65), Array.Empty<IDiagnosticRule>());

        var snapshot = await host.ScanAsync();

        Assert.True(snapshot.Sensors.CpuRead);
        Assert.True(snapshot.Sensors.GpuRead);
    }

    /// NaN is what a present-but-silent sensor reports, and it is exactly the
    /// case `is not null` gets wrong: the snapshot would record "answered" and
    /// the card would print "Everything brisk tried to read, answered." over a
    /// scan that read no temperature at all. This is the whole reason the
    /// shared predicate is double.IsFinite and not a null check.
    [Theory]
    [InlineData(double.NaN, double.NaN)]
    [InlineData(double.PositiveInfinity, double.NegativeInfinity)]
    public async Task ScanAsync_NonFiniteTemperatures_AreNotReadings(
        double cpu, double gpu)
    {
        var host = Host(new FixedSensors(cpu, gpu), Array.Empty<IDiagnosticRule>());

        var snapshot = await host.ScanAsync();

        Assert.False(snapshot.Sensors.CpuRead);
        Assert.False(snapshot.Sensors.GpuRead);
    }

    /// One sensor each way, so a scan that simply copied one flag onto both
    /// cannot pass the three tests above by luck.
    [Fact]
    public async Task ScanAsync_OneSensorAnswering_RecordsOnlyThatOne()
    {
        var host = Host(new FixedSensors(double.NaN, 65), Array.Empty<IDiagnosticRule>());

        var snapshot = await host.ScanAsync();

        Assert.False(snapshot.Sensors.CpuRead);
        Assert.True(snapshot.Sensors.GpuRead);
    }

    /// The status must describe the same moment as the findings beside it in
    /// the snapshot, because the report card prints them together. Reading the
    /// probes after _scanner.Scan — a filesystem walk that can take seconds —
    /// let a card say "CPU temperature — not read" directly above a thermals
    /// finding quoting a CPU temperature.
    ///
    /// So this pins the ORDER, not the values: ScanAsync_RecordsSensorStatus
    /// passes either way, which is exactly why the defect survived a rewrite
    /// of this method and had to be corrected a second time.
    [Fact]
    public async Task ScanAsync_ReadsTheSensors_BeforeTheRulesRun()
    {
        var order = new CallOrder();
        var sensors = new SequencedSensors(order);
        var rule = new SequencedRule(order);

        await Host(sensors, rule).ScanAsync();

        Assert.NotNull(sensors.CpuIndex);
        Assert.NotNull(rule.DetectIndex);
        Assert.True(sensors.CpuIndex < rule.DetectIndex,
            $"sensors read at #{sensors.CpuIndex}, rules ran at #{rule.DetectIndex} — "
            + "the probes must be read before the rule loop, not after the disk walk");
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
