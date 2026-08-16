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
    private readonly string _actionLogPath;
    private readonly string _cliExePath;

    public EngineHost(DiagnosticContext ctx, IReadOnlyList<IDiagnosticRule> rules,
        Scanner scanner, FixRunner fixes, CleanRunner cleaner, FixJournal journal,
        StartupManager startup, string actionLogPath, string cliExePath)
    {
        _ctx = ctx;
        _rules = rules;
        _scanner = scanner;
        _fixes = fixes;
        _cleaner = cleaner;
        _journal = journal;
        _startup = startup;
        _actionLogPath = actionLogPath;
        _cliExePath = cliExePath;
    }

    public Task<ScanSnapshot> ScanAsync(IProgress<string>? progress = null,
        CancellationToken ct = default) => Task.Run(() =>
    {
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
            HealthScore.Compute(findings), DateTime.UtcNow);
    }, ct);

    private sealed class SyncProgressAdapter : IProgress<ScanProgress>
    {
        private readonly Action<ScanProgress> _handler;
        public SyncProgressAdapter(Action<ScanProgress> handler) { _handler = handler; }
        public void Report(ScanProgress value) => _handler(value);
    }

    public FixOutcome Fix(string ruleId) => WithRule(ruleId, r => _fixes.Apply(r, _ctx));
    public FixOutcome Undo(string ruleId) => WithRule(ruleId, r => _fixes.Undo(r, _ctx));

    private FixOutcome WithRule(string ruleId, Func<IDiagnosticRule, FixOutcome> action)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
        return rule is null
            ? new FixOutcome(false, $"unknown rule '{ruleId}'")
            : action(rule);
    }

    public CleanReport Clean(TargetScanResult scan, bool dryRun) =>
        _cleaner.Clean(scan, dryRun);

    public IReadOnlyList<UndoableFix> ListUndoable() => _journal.ListUndoable();
    public IReadOnlyList<ActionLogEntry> ReadLog(int max = 200) =>
        ActionLogReader.ReadTail(_actionLogPath, max);
    public IReadOnlyList<StartupEntry> ListStartup() => _startup.List();
    public bool SetStartupEnabled(string hive, string name, bool enabled) =>
        _startup.SetEnabled(hive, name, enabled);

    /// Per-action UAC: run the CLI elevated for exactly one consented action.
    public bool RunElevated(string cliArgs) => RunAs(_cliExePath, cliArgs);

    public bool CreateRestorePoint() => RunAs("powershell.exe",
        "-NoProfile -Command Checkpoint-Computer -Description brisk " +
        "-RestorePointType MODIFY_SETTINGS");

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
}
