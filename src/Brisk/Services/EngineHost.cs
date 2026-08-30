using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Logging;
using BriskEngine.Models;

namespace Brisk.Services;

public sealed class EngineHost : IEngineHost
{
    private readonly DiagnosticContext _ctx;
    private readonly IReadOnlyList<IDiagnosticRule> _rules;
    private readonly Scanner _scanner;
    private readonly FixRunner _fixes;
    private readonly CleanRunner _cleaner;
    private readonly FixJournal _journal;
    private readonly StartupManager _startup;
    private readonly ISessionProbe _session;
    private readonly string _actionLogPath;
    private readonly string _cliExePath;

    public EngineHost(DiagnosticContext ctx, IReadOnlyList<IDiagnosticRule> rules,
        Scanner scanner, FixRunner fixes, CleanRunner cleaner, FixJournal journal,
        StartupManager startup, string actionLogPath, string cliExePath,
        ISessionProbe session)
    {
        _ctx = ctx;
        _rules = rules;
        _scanner = scanner;
        _fixes = fixes;
        _cleaner = cleaner;
        _journal = journal;
        _startup = startup;
        _session = session;
        _actionLogPath = actionLogPath;
        _cliExePath = cliExePath;
    }

    public Task<ScanSnapshot> ScanAsync(IProgress<string>? progress = null,
        CancellationToken ct = default) => Task.Run(() =>
    {
        // Read the sensors BEFORE the rules run, not after the cleaner walk.
        // What the card prints must describe the same moment the findings
        // describe, and the filesystem scan below can take seconds.
        // SensorReading, not a local copy of the rule: the CLI's notice and
        // this snapshot describe the same machine at the same moment, and
        // they used to disagree about NaN.
        var cpuRead = SensorReading.IsReal(_ctx.Sensors.CpuTempC());
        var gpuRead = SensorReading.IsReal(_ctx.Sensors.GpuTempC());
        var integrityOn = _ctx.MemoryIntegrity.IsOn();

        // One event-log walk per scan, not two: the boot trend below asks for
        // 20 boots and BootDegradationRule asks for 8, and each walk opens
        // readers and parses every record's XML. The trend runs FIRST so its
        // deeper fetch seeds the cache and the rule's 8 is served as a prefix
        // of the same newest-first list (2026-08-30 review).
        var ctx = _ctx with { EventLog = new PrefixCachingEventLog(_ctx.EventLog) };

        BootTrend? bootTrend = null;
        try
        {
            DateTime? firstChange = null, lastChange = null;
            foreach (var fix in _journal.ListUndoable())
            {
                if (firstChange is null || fix.FixedAtUtc < firstChange) firstChange = fix.FixedAtUtc;
                if (lastChange is null || fix.FixedAtUtc > lastChange) lastChange = fix.FixedAtUtc;
            }
            var (startupFirst, startupLast) = ActionLogReader.StartupChangeBoundsUtc(_actionLogPath);
            if (startupFirst is not null && (firstChange is null || startupFirst < firstChange))
                firstChange = startupFirst;
            if (startupLast is not null && (lastChange is null || startupLast > lastChange))
                lastChange = startupLast;
            bootTrend = BootTrendCalculator.Compute(
                ctx.EventLog.RecentBoots(BootTrendCalculator.SampledBoots),
                firstChange, lastChange);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            bootTrend = null;
        }

        var findings = new List<DiagnosticFinding>();
        foreach (var rule in _rules)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(rule.Id);
            try
            {
                if (rule.Detect(ctx) is { } finding) findings.Add(finding);
            }
            catch
            {
                // A broken probe must never kill the scan (spec: degrade gracefully).
            }
        }
        // The read-back rides the scan, in the same pass over the same
        // context that produced the findings above — see ScanSnapshot for why
        // it is not a second call the page makes for itself. It reads the
        // journal here rather than taking ListUndoable()'s answer from
        // somewhere else for the same reason.
        //
        // The catch is for the journal FILE and nothing else: ReadBack.For
        // drops a switch whose own read throws, and FixJournal takes its gate
        // around the read, so what is left is a handle this process does not
        // control — the CLI mid-fix, a backup tool, a scanner — refusing
        // File.ReadAllLines. No rows is the honest answer to that: the page
        // shows no read-back lines, which is an absence rather than a claim
        // that brisk has turned nothing off. What it replaces is a scan that
        // died on the spot, leaving the window on the previous snapshot with
        // nothing said — the same failure the rule loop's catch above exists
        // to prevent.
        IReadOnlyList<ReadBackResult> readBack;
        try
        {
            readBack = ReadBack.For(_ctx, _journal.ListUndoable(), _rules);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            readBack = Array.Empty<ReadBackResult>();
        }
        // The USB records, on the same pass and for the same reason the
        // read-back is here: the Gizlilik page shows the COUNT off the
        // finding above and the RECORDS off this list, and a page that asked
        // for the records separately would be a second channel for one
        // reading of one machine.
        //
        // WHAT THIS CATCH COVERS, said exactly. ReadDevices guards every read
        // it makes — a refused model key costs that branch, a refused
        // property read costs that field — so nothing known reaches here
        // today. It is written anyway, because of where it sits rather than
        // what it expects: this call is OUTSIDE the rule loop's catch, which
        // is precisely where ReadBack.For sat when a SecurityException out of
        // it took down the entire scan (d083da1). The failure mode at this
        // seam is total — no snapshot, no Changed, a window left on the
        // previous scan with nothing said — and the cost of a record nobody
        // could read is a fold that does not open.
        IReadOnlyList<UsbDeviceRecord> usbDevices;
        try
        {
            usbDevices = UsbHistoryRule.ReadDevices(_ctx);
        }
        catch (Exception)
        {
            usbDevices = Array.Empty<UsbDeviceRecord>();
        }
        var cleaner = _scanner.Scan(ct, new SyncProgressAdapter(p =>
            progress?.Report(p.TargetId)));
        return new ScanSnapshot(findings, cleaner,
            HealthScore.Compute(findings), DateTime.UtcNow,
            new SensorStatus(CpuRead: cpuRead, GpuRead: gpuRead,
                MemoryIntegrityOn: integrityOn),
            readBack, usbDevices, bootTrend);
    }, ct);

    /// Serves RecentBoots(n) as a prefix of the deepest fetch made so far —
    /// per-scan, single-threaded, discarded with the lambda that made it, so
    /// it can never go stale across scans.
    private sealed class PrefixCachingEventLog : IEventLogProbe
    {
        private readonly IEventLogProbe _inner;
        private IReadOnlyList<BootRecord>? _cached;
        private int _cachedCount;
        public PrefixCachingEventLog(IEventLogProbe inner) => _inner = inner;
        public IReadOnlyList<BootRecord> RecentBoots(int count)
        {
            if (_cached is null || count > _cachedCount)
            {
                _cached = _inner.RecentBoots(count);
                _cachedCount = count;
            }
            return _cached.Count <= count ? _cached : _cached.Take(count).ToList();
        }
    }

    private sealed class SyncProgressAdapter : IProgress<ScanProgress>
    {
        private readonly Action<ScanProgress> _handler;
        public SyncProgressAdapter(Action<ScanProgress> handler) { _handler = handler; }
        public void Report(ScanProgress value) => _handler(value);
    }

    public FixOutcome Fix(string ruleId) => WithRule(ruleId, r => _fixes.Apply(r, _ctx));
    public FixOutcome Undo(string ruleId) => WithRule(ruleId, r => _fixes.Undo(r, _ctx));

    /// The one write to the registry in the whole display path. Everything
    /// before it is session-only, so a machine that was power-cycled through a
    /// black screen comes back on the mode it booted with.
    public FixOutcome KeepDisplayFix()
    {
        try
        {
            _ctx.Displays.PersistCurrentModes();
            return new FixOutcome(true, $"{DisplayRefreshRule.RuleId}: kept");
        }
        catch (DisplayChangeException ex)
        {
            return new FixOutcome(false,
                $"{DisplayRefreshRule.RuleId}: could not be made permanent — {ex.Message}");
        }
    }

    private FixOutcome WithRule(string ruleId, Func<IDiagnosticRule, FixOutcome> action)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
        return rule is null
            ? new FixOutcome(false, $"unknown rule '{ruleId}'")
            : action(rule);
    }

    public CleanReport Clean(TargetScanResult scan, bool dryRun,
        Action<CleanEntry>? onEntry = null) =>
        _cleaner.Clean(scan, dryRun, onEntry);

    public IReadOnlyList<UndoableFix> ListUndoable() => _journal.ListUndoable();
    public IReadOnlyList<ActionLogEntry> ReadLog(int max = 200) =>
        ActionLogReader.ReadTail(_actionLogPath, max);
    public IReadOnlyList<StartupEntry> ListStartup() => _startup.List();
    public bool SetStartupEnabled(string hive, string name, bool enabled) =>
        _startup.SetEnabled(hive, name, enabled);

    /// Per-action UAC: run the CLI elevated for exactly one consented action.
    public bool RunElevated(string cliArgs) => RunAs(_cliExePath, cliArgs);

    public bool CreateRestorePoint() => RunAs("powershell.exe",
        "-NoProfile -Command \"try { Checkpoint-Computer -Description brisk " +
        "-RestorePointType MODIFY_SETTINGS -ErrorAction Stop } catch { exit 1 }\"");

    private static bool RunAs(string exe, string args)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, args)
            {
                Verb = "runas",
                UseShellExecute = true,
            });
            if (process is null) return false;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Win32Exception) { return false; }  // user cancelled the UAC prompt
        catch (FileNotFoundException) { return false; }
    }

    public long FreeDiskBytes() =>
        _ctx.Disk.FreeBytes(Path.GetPathRoot(Environment.SystemDirectory)!);

    public long LifetimeReclaimedBytes() =>
        ActionLogReader.TotalRecycledBytes(_actionLogPath);

    public bool IsElevated() => new WindowsPrincipal(WindowsIdentity.GetCurrent())
        .IsInRole(WindowsBuiltInRole.Administrator);

    public SessionIdentity Session() => _session.Current();
}
