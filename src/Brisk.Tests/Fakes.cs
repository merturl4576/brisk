using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Brisk.Services;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
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

/// An in-memory registry, enough for the one thing the app layer reads and
/// writes directly: brisk's own legacy HKCU\\Run autostart value.
public sealed class FakeRegistry : BriskEngine.Diagnostics.IRegistryProbe
{
    public System.Collections.Generic.Dictionary<string, string> Strings { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    private static string Key(string keyPath, string valueName) =>
        keyPath + "\\" + valueName;

    public string? GetString(string keyPath, string valueName) =>
        Strings.TryGetValue(Key(keyPath, valueName), out var v) ? v : null;
    public void SetString(string keyPath, string valueName, string value) =>
        Strings[Key(keyPath, valueName)] = value;
    public void DeleteValue(string keyPath, string valueName) =>
        Strings.Remove(Key(keyPath, valueName));
    public byte[]? GetBytes(string keyPath, string valueName) => null;
    public void SetBytes(string keyPath, string valueName, byte[] value) { }
    public int? GetInt(string keyPath, string valueName) => null;
    public void SetInt(string keyPath, string valueName, int value) { }
    /// Real enough for StartupManager.List(), so a test can watch brisk's own
    /// stale row disappear from the startup list the GUI actually renders.
    public System.Collections.Generic.IReadOnlyList<string> GetValueNames(string keyPath) =>
        Strings.Keys
            .Where(k => k.StartsWith(keyPath + "\\", StringComparison.OrdinalIgnoreCase))
            .Select(k => k.Substring(keyPath.Length + 1))
            .ToList();
    public System.Collections.Generic.IReadOnlyList<string> GetSubKeyNames(string keyPath) =>
        Array.Empty<string>();
}

public static class TestData
{
    public static DiagnosticFinding Finding(string ruleId, Severity sev = Severity.Warning,
        RuleCategory cat = RuleCategory.Auto, int stars = 3, bool canFix = true,
        string? evidenceKey = null, IReadOnlyList<string>? evidenceArgs = null) => new(
        ruleId, $"rule.{ruleId}.title", $"Title {ruleId}", $"Evidence {ruleId}",
        sev, cat, stars, canFix, canFix ? $"Fix {ruleId}" : null,
        evidenceKey, evidenceArgs);

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

    public static ScanSnapshot Snapshot(IReadOnlyList<DiagnosticFinding>? findings = null,
        params TargetScanResult[] targets) => new(
        findings ?? Array.Empty<DiagnosticFinding>(),
        new ScanResult(targets), 72, new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc));
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

    public FixOutcome Fix(string ruleId) { Fixed.Add(ruleId); return new(true, ruleId); }
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
}
