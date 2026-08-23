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

        var findings = new List<DiagnosticFinding>();
        foreach (var rule in _rules)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(rule.Id);
            try
            {
                if (rule.Detect(_ctx) is { } finding) findings.Add(finding);
            }
            catch
            {
                // A broken probe must never kill the scan (spec: degrade gracefully).
            }
        }
        var cleaner = _scanner.Scan(ct, new SyncProgressAdapter(p =>
            progress?.Report(p.TargetId)));
        return new ScanSnapshot(findings, cleaner,
            HealthScore.Compute(findings), DateTime.UtcNow,
            new SensorStatus(CpuRead: cpuRead, GpuRead: gpuRead,
                MemoryIntegrityOn: integrityOn));
    }, ct);

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
