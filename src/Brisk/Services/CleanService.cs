using System.Collections.Generic;
using System.Linq;
using BriskEngine.Models;

namespace Brisk.Services;

public sealed record CleanOutcome(
    IReadOnlyList<string> RecycledPaths, long RecycledBytes,
    IReadOnlyList<string> Problems, bool WasDryRun);

/// One clean pass over a set of scanned targets, shared by flyout and window.
public sealed class CleanService
{
    private readonly IEngineHost _host;
    private readonly Settings _settings;

    public CleanService(IEngineHost host, Settings settings)
    {
        _host = host;
        _settings = settings;
    }

    /// The one predicate behind "safe to clean in one click": safe level,
    /// not skipped, no per-item picking, no explicit opt-in, something to
    /// take. The Depolama page's simple view aggregates over the SAME
    /// predicate, so the number it shows is exactly what CleanSafe takes.
    public static bool IsSafeDefault(TargetScanResult t) =>
        t.Target.Level == CleanupLevel.Safe
        && t.SkippedReason is null
        && !t.Target.RequiresIndividualSelection
        && !t.Target.RequiresExplicitOptIn
        && t.Items.Count > 0;

    /// The one definition of "safe to clean in one click" — shared by the
    /// flyout, the overview and the Depolama simple view. Deletion stays
    /// behind its own consented button; fix-all must never call this.
    public CleanOutcome CleanSafe(ScanResult scan) =>
        CleanTargets(scan.Targets.Where(IsSafeDefault));

    public CleanOutcome CleanTargets(IEnumerable<TargetScanResult> scans)
    {
        var paths = new List<string>();
        long bytes = 0;
        var problems = new List<string>();
        foreach (var scan in scans)
        {
            var report = _host.Clean(scan, _settings.DryRun);
            foreach (var entry in report.Entries)
            {
                if (entry.Action == "recycled") { paths.Add(entry.Path); bytes += entry.Bytes; }
                else if (entry.Action is "refused" or "error")
                    problems.Add($"{entry.Path} — {entry.Reason}");
            }
        }
        return new CleanOutcome(paths, bytes, problems, _settings.DryRun);
    }
}
