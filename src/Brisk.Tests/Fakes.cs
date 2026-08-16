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

public static class TestData
{
    public static DiagnosticFinding Finding(string ruleId, Severity sev = Severity.Warning,
        RuleCategory cat = RuleCategory.Auto, int stars = 3, bool canFix = true) => new(
        ruleId, $"rule.{ruleId}.title", $"Title {ruleId}", $"Evidence {ruleId}",
        sev, cat, stars, canFix, canFix ? $"Fix {ruleId}" : null);

    public static TargetScanResult Target(string id, CleanupLevel level, long bytes,
        string? skipped = null, bool pick = false, bool optIn = false, bool admin = false)
    {
        var target = new CleanupTarget(id, id, level, new List<string> { @"C:\x\" + id },
            "Test", RequiresIndividualSelection: pick, RequiresExplicitOptIn: optIn,
            RequiresElevation: admin);
        var items = bytes == 0
            ? (IReadOnlyList<ResolvedItem>)Array.Empty<ResolvedItem>()
            : new[] { new ResolvedItem(id, @"C:\x\" + id + @"\item", bytes, null) };
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
    public FixOutcome Undo(string ruleId) { Undone.Add(ruleId); return new(true, ruleId); }

    public CleanReport Clean(TargetScanResult scan, bool dryRun)
    {
        Cleans.Add((scan.Target.Id, dryRun));
        if (OnClean is not null) return OnClean(scan, dryRun);
        var entries = scan.Items
            .Select(i => new CleanEntry(scan.Target.Id, i.Path, i.Bytes,
                dryRun ? "dry-run" : "recycled"))
            .ToList();
        return new CleanReport(entries);
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
    public long FreeDiskBytes() => 122L << 30;
    public long Lifetime { get; set; }
    public long LifetimeReclaimedBytes() => Lifetime;
    public bool IsElevated() => Elevated;
}
