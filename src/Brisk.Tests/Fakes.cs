using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Brisk.Services;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Logging;
using BriskEngine.Models;

namespace Brisk.Tests;

public sealed class FakeProcessRunner : BriskEngine.Cleaning.IProcessRunner
{
    public System.Collections.Generic.List<(string Exe, string Args)> Calls = new();
    public int NextExitCode;

    public (int ExitCode, string StdOut) Run(string exe, string args)
    {
        Calls.Add((exe, args));
        return (NextExitCode, "");
    }
}

/// Models schtasks closely enough for brisk's own autostart: /Query answers 0
/// only once a /Create has actually succeeded, so the two surfaces that read
/// the task (the Settings checkbox and the Startup page's brisk row) can be
/// tested against the same machine state.
public sealed class TaskStateRunner : BriskEngine.Cleaning.IProcessRunner
{
    public System.Collections.Generic.List<(string Exe, string Args)> Calls { get; } = new();
    public bool CreateSucceeds { get; set; } = true;
    public bool DeleteSucceeds { get; set; } = true;
    public bool TaskExists { get; set; }

    public (int ExitCode, string StdOut) Run(string exe, string args)
    {
        Calls.Add((exe, args));
        if (args.Contains("/Create"))
        {
            if (CreateSucceeds) TaskExists = true;
            return (CreateSucceeds ? 0 : 1, "");
        }
        if (args.Contains("/Delete"))
        {
            if (DeleteSucceeds) TaskExists = false;
            return (DeleteSucceeds ? 0 : 1, "");
        }
        return (TaskExists ? 0 : 1, "");                       // /Query
    }
}

/// An in-memory registry, enough for the one thing the app layer reads and
/// writes directly: brisk's own legacy HKCU\\Run autostart value.
public sealed class FakeRegistry : BriskEngine.Diagnostics.IRegistryProbe
{
    public System.Collections.Generic.Dictionary<string, string> Strings { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// StartupApproved records are binary, and swallowing them left a test
    /// unable to see whether brisk clears its own orphaned one.
    public System.Collections.Generic.Dictionary<string, byte[]> Blobs { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// Store startup tasks are DWORDs under per-package subkeys, and
    /// StartupManager reads all three shapes. A fake that answered null and
    /// empty to those would let an app-layer test of the Store rows pass while
    /// seeing no Store rows at all.
    public System.Collections.Generic.Dictionary<string, int> Ints { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> SubKeys { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    private static string Key(string keyPath, string valueName) =>
        keyPath + "\\" + valueName;

    public string? GetString(string keyPath, string valueName) =>
        Strings.TryGetValue(Key(keyPath, valueName), out var v) ? v : null;
    public void SetString(string keyPath, string valueName, string value) =>
        Strings[Key(keyPath, valueName)] = value;
    public void DeleteValue(string keyPath, string valueName)
    {
        Strings.Remove(Key(keyPath, valueName));
        Blobs.Remove(Key(keyPath, valueName));
        // DWORDs too. Nothing in this project writes an int through the double
        // today, so the divergence costs nothing yet — but every rule that
        // deletes a DWORD on undo is tested against the OTHER double, and the
        // next app-layer test of that path would go green on behaviour the
        // real registry does not have.
        Ints.Remove(Key(keyPath, valueName));
    }
    public byte[]? GetBytes(string keyPath, string valueName) =>
        Blobs.TryGetValue(Key(keyPath, valueName), out var v) ? v : null;
    public void SetBytes(string keyPath, string valueName, byte[] value) =>
        Blobs[Key(keyPath, valueName)] = value;
    public int? GetInt(string keyPath, string valueName) =>
        Ints.TryGetValue(Key(keyPath, valueName), out var v) ? v : null;
    public void SetInt(string keyPath, string valueName, int value) =>
        Ints[Key(keyPath, valueName)] = value;
    /// Run values are strings, so listing only Strings is what the real key
    /// holds — the binaries and DWORDs above live under different keys and
    /// are read by name, never enumerated.
    public System.Collections.Generic.IReadOnlyList<string> GetValueNames(string keyPath) =>
        Strings.Keys
            .Where(k => k.StartsWith(keyPath + "\\", StringComparison.OrdinalIgnoreCase))
            .Select(k => k.Substring(keyPath.Length + 1))
            .ToList();
    public System.Collections.Generic.IReadOnlyList<string> GetSubKeyNames(string keyPath) =>
        SubKeys.TryGetValue(keyPath, out var s) ? s : (System.Collections.Generic.IReadOnlyList<string>)Array.Empty<string>();
}

/// The other eleven probes a DiagnosticContext takes, for a test that runs a
/// rule reading the registry and nothing else.
///
/// EVERY MEMBER THROWS. The tempting alternative — answer null, empty, zero —
/// would let a rule that reached one of these run to completion over a reading
/// nobody arranged, and the test built on it would pass while measuring a
/// machine the fixture never described. A NotSupportedException naming the
/// member is the loud version of the same answer.
public sealed class NoOtherProbes
    : IPowercfgProbe, IProcessInfoProbe, ISensorProbe, IDisplayProbe,
        IEventLogProbe, IHardwareProbe, IDiskInfoProbe, IFileProbe,
        IProcessLister, IMemoryIntegrityProbe, IDeliveryOptimizationProbe
{
    private static T No<T>([CallerMemberName] string member = "") =>
        throw new NotSupportedException(
            $"{member} was asked of a fixture that answers the registry and " +
            "nothing else — see NoOtherProbes");

    public (Guid Id, string Name) GetActiveScheme() => No<(Guid, string)>();
    public IReadOnlyList<(Guid Id, string Name)> ListSchemes() =>
        No<IReadOnlyList<(Guid, string)>>();
    public void SetActive(Guid id) => No<bool>();

    public IReadOnlyList<(string Name, long WorkingSetBytes)> TopByMemory(int count) =>
        No<IReadOnlyList<(string, long)>>();
    public double MemoryLoadPercent() => No<double>();

    public double? CpuTempC() => No<double?>();
    public double? GpuTempC() => No<double?>();
    public int GpuCount() => No<int>();

    public IReadOnlyList<DisplayInfo> Displays() => No<IReadOnlyList<DisplayInfo>>();
    public void SetRefreshRate(string deviceName, int hz) => No<bool>();
    public void PersistCurrentModes() => No<bool>();

    public IReadOnlyList<BootRecord> RecentBoots(int count) =>
        No<IReadOnlyList<BootRecord>>();

    public IReadOnlyList<MemoryModule> MemoryModules() => No<IReadOnlyList<MemoryModule>>();

    public long FreeBytes(string driveRoot) => No<long>();
    public long TotalBytes(string driveRoot) => No<long>();

    public bool FileExists(string path) => No<bool>();
    public string? ReadAllText(string path) => No<string?>();
    public void WriteAllText(string path, string content) => No<bool>();
    public IReadOnlyList<string> ListFiles(string directory) => No<IReadOnlyList<string>>();
    public long DirectorySizeBytes(string path) => No<long>();
    public DateTime? NewestWriteUtc(string path, int limit = 1500) => No<DateTime?>();

    public bool IsRunning(string processName) => No<bool>();

    public bool? IsOn() => No<bool?>();

    public PeerUpload? UploadedToPeers() => No<PeerUpload?>();
}

public static class TestData
{
    /// A context for a rule that reads the registry and nothing else. The data
    /// directory names a path under the system temp directory that nothing
    /// here creates, so a rule that went looking for the history store would
    /// find no store — the same "not arranged for" the probes throw about,
    /// said in the one place a string cannot throw. Nothing is written there:
    /// the suite must not litter the machine it tests on.
    public static DiagnosticContext RegistryContext(IRegistryProbe registry)
    {
        var none = new NoOtherProbes();
        return new DiagnosticContext(none, registry, none, none, none, none, none,
            none, none, none, none, none,
            System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "brisk-registry-only-context"));
    }

    public static DiagnosticFinding Finding(string ruleId, Severity sev = Severity.Warning,
        RuleCategory cat = RuleCategory.Auto, int stars = 3, bool canFix = true,
        string? evidenceKey = null, IReadOnlyList<string>? evidenceArgs = null,
        Headline? headline = null, FindingKind kind = FindingKind.Problem) => new(
        ruleId, $"rule.{ruleId}.title", $"Title {ruleId}", $"Evidence {ruleId}",
        sev, cat, stars, canFix, canFix ? $"Fix {ruleId}" : null,
        evidenceKey, evidenceArgs, headline, kind);

    public static TargetScanResult Target(string id, CleanupLevel level, long bytes,
        string? skipped = null, bool pick = false, bool optIn = false, bool admin = false,
        string? app = null, string category = "Test", long lockedBytes = 0)
    {
        var target = new CleanupTarget(id, id, level, new List<string> { @"C:\x\" + id },
            category, RequiresAppClosedProcess: app, RequiresIndividualSelection: pick,
            RequiresExplicitOptIn: optIn, RequiresElevation: admin);
        var items = new List<ResolvedItem>();
        if (bytes > 0)
            items.Add(new ResolvedItem(id, @"C:\x\" + id + @"\item", bytes, null));
        if (lockedBytes > 0)
            items.Add(new ResolvedItem(id, @"C:\x\" + id + @"\held", lockedBytes, null,
                Locked: true));
        return new TargetScanResult(target, items, skipped);
    }

    /// The fixture's sensors, stated rather than defaulted. ScanSnapshot has
    /// no optional Sensors any more — the optional one was filled in with
    /// "both answered", which put a measurement claim on a card built from a
    /// snapshot that had measured nothing. A fixture whose probes are all
    /// nulls read no temperature, so this is what it says: nothing answered.
    /// Every test that cares passes its own.
    private static readonly SensorStatus NothingAnswered = new(false, false, null);

    /// The fixture journals no fix, so there is nothing for a read-back to
    /// re-read — stated the same way and for the same reason as the sensors
    /// above. A test about the read-back passes its own rows; this default is
    /// what the fixture's own machine would produce, not a shrug.
    private static readonly ReadBackResult[] NothingReRead = Array.Empty<ReadBackResult>();

    /// The fixture's registry holds no USB record, so the fixture's snapshot
    /// holds no device — stated, like the two above, rather than defaulted.
    /// A test about the records passes its own list; ReportCardModelTests'
    /// planted fixture passes what the SHIPPED rule read out of a planted
    /// registry, which is the only way to ask whether a name a real read had
    /// in its hands can reach the card.
    private static readonly UsbDeviceRecord[] NoDevicesRead = Array.Empty<UsbDeviceRecord>();

    public static ScanSnapshot Snapshot(IReadOnlyList<DiagnosticFinding>? findings = null,
        params TargetScanResult[] targets) => Snapshot(findings, NothingAnswered, targets);

    public static ScanSnapshot Snapshot(IReadOnlyList<DiagnosticFinding>? findings,
        SensorStatus sensors, params TargetScanResult[] targets) =>
        Snapshot(findings, sensors, NothingReRead, targets);

    public static ScanSnapshot Snapshot(IReadOnlyList<DiagnosticFinding>? findings,
        SensorStatus sensors, IReadOnlyList<ReadBackResult> readBack,
        params TargetScanResult[] targets) =>
        Snapshot(findings, sensors, readBack, NoDevicesRead, targets);

    /// The whole shape, for the one fixture that plants device records: the
    /// card must print no name off a snapshot that CARRIES one, which is a
    /// stronger question than the same card built over an empty list.
    public static ScanSnapshot Snapshot(IReadOnlyList<DiagnosticFinding>? findings,
        SensorStatus sensors, IReadOnlyList<ReadBackResult> readBack,
        IReadOnlyList<UsbDeviceRecord> usbDevices,
        params TargetScanResult[] targets) => new(
        findings ?? Array.Empty<DiagnosticFinding>(),
        new ScanResult(targets), 72, new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc),
        sensors, readBack, usbDevices);

    /// A snapshot whose only distinguishing feature is what brisk found when
    /// it looked again — the shape most read-back tests want.
    public static ScanSnapshot Snapshot(IReadOnlyList<DiagnosticFinding>? findings,
        IReadOnlyList<ReadBackResult> readBack) =>
        Snapshot(findings, NothingAnswered, readBack);
}

public sealed class FakeEngineHost : IEngineHost
{
    public ScanSnapshot NextSnapshot { get; set; } = TestData.Snapshot();
    public int ScanCalls { get; private set; }
    public List<string> Fixed { get; } = new();
    public List<string> Undone { get; } = new();
    public List<(string TargetId, bool DryRun)> Cleans { get; } = new();
    public Func<TargetScanResult, bool, CleanReport>? OnClean { get; set; }
    public List<UndoableFix> Undoable { get; } = new();
    public List<ActionLogEntry> LogEntries { get; } = new();
    public List<StartupEntry> Startup { get; } = new();
    public List<(string Hive, string Name, bool Enabled)> StartupToggles { get; } = new();
    public bool StartupToggleResult { get; set; } = true;
    public List<string> ElevatedRuns { get; } = new();
    public bool ElevatedResult { get; set; } = true;
    public bool RestorePointResult { get; set; } = true;
    public int RestorePointCalls { get; private set; }
    public bool Elevated { get; set; }

    public Task<ScanSnapshot> ScanAsync(IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        ScanCalls++;
        progress?.Report("fake");
        return Task.FromResult(NextSnapshot);
    }

    /// Mirrors OnUndo below: lets a test model a fix that REFUSES —
    /// diagnostic-level writes under HKLM and fails cleanly through FixRunner
    /// on an unelevated machine, which is the ordinary outcome of the privacy
    /// page's one button on a standard account. Runs after the record, so a
    /// refused fix is still an attempted one.
    public Func<string, FixOutcome>? OnFix { get; set; }

    public FixOutcome Fix(string ruleId)
    {
        Fixed.Add(ruleId);
        return OnFix?.Invoke(ruleId) ?? new FixOutcome(true, ruleId);
    }
    /// Mirrors OnClean: lets a test model the display rescue's unhappy paths —
    /// an undo that throws, or one whose effect changes what the next scan
    /// sees — without another whole decorator host. Runs before the record.
    public Action<string>? OnUndo { get; set; }

    public FixOutcome Undo(string ruleId)
    {
        OnUndo?.Invoke(ruleId);
        Undone.Add(ruleId);
        return new(true, ruleId);
    }

    /// Counts the one call that writes a display mode to the registry, so a
    /// test can prove the raise stayed session-only until a Keep.
    public int KeepDisplayCalls { get; private set; }
    public bool KeepDisplayResult { get; set; } = true;

    public FixOutcome KeepDisplayFix()
    {
        KeepDisplayCalls++;
        return new(KeepDisplayResult, "keep");
    }

    public CleanReport Clean(TargetScanResult scan, bool dryRun,
        Action<CleanEntry>? onEntry = null)
    {
        Cleans.Add((scan.Target.Id, dryRun));
        var report = OnClean is not null
            ? OnClean(scan, dryRun)
            : new CleanReport(scan.Items
                .Select(i => new CleanEntry(scan.Target.Id, i.Path, i.Bytes,
                    dryRun ? "dry-run" : "recycled"))
                .ToList());
        if (onEntry is not null)
            foreach (var entry in report.Entries) onEntry(entry);
        return report;
    }

    public IReadOnlyList<UndoableFix> ListUndoable() => Undoable;
    public IReadOnlyList<ActionLogEntry> ReadLog(int max = 200) => LogEntries;
    public IReadOnlyList<StartupEntry> ListStartup() => Startup;

    public bool SetStartupEnabled(string hive, string name, bool enabled)
    {
        StartupToggles.Add((hive, name, enabled));
        return StartupToggleResult;
    }

    public bool RunElevated(string cliArgs) { ElevatedRuns.Add(cliArgs); return ElevatedResult; }
    public bool CreateRestorePoint() { RestorePointCalls++; return RestorePointResult; }
    public long FreeDisk { get; set; } = 122L << 30;
    public long FreeDiskBytes() => FreeDisk;
    public long Lifetime { get; set; }
    public long LifetimeReclaimedBytes() => Lifetime;
    public bool IsElevated() => Elevated;

    /// Same account by default — the ordinary case, and on an administrator
    /// account the only one. Set it to model over-the-shoulder elevation.
    public SessionIdentity SessionIdentity { get; set; } =
        new(@"PC\alice", @"PC\alice", false);
    public SessionIdentity Session() => SessionIdentity;
}
