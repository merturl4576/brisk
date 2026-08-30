using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Models;

namespace Brisk.Services;

/// Skipped carries the refused/error entries verbatim (engine English) so
/// the GUI edge can recompose human-language reasons — the round-9 rule.
/// Recycled (round 12) carries the per-path recycled entries WITH their
/// bytes, so the auto-purge can account freed vs left-in-bin precisely.
/// ROUND 16 review (minor 3): there used to be a Problems list here — one
/// "path — reason" string per failure, in the engine's English, built on
/// every run and bound straight to the Depolama page. That binding IS the
/// 2026-08-18 screenshot. Skipped carries the same entries with structure
/// instead of prose, the GUI narrates from it, and every path is in the
/// action log with a timestamp. A field that exists to hold raw paths for
/// display is one {Binding} away from putting them back on the page.
/// Removed (2026-08-30, the heavy trio) carries the entries taken PAST the
/// recycle bin ("removed": Windows.old, hiberfil). They count toward what
/// was freed and must never enter RecycledPaths — a restore over a path
/// the bin never held reports a false "restore failed".
public sealed record CleanOutcome(
    IReadOnlyList<string> RecycledPaths, long RecycledBytes,
    bool WasDryRun,
    IReadOnlyList<CleanEntry> Skipped,
    IReadOnlyList<CleanEntry> Recycled,
    IReadOnlyList<CleanEntry> Removed)
{
    public long RemovedBytes => Removed.Sum(e => e.Bytes);
}

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

    /// The honest headline (round 11): over the SAME predicate the button
    /// cleans through, but counting only bytes the clean can take right now
    /// — delete-locked items stay on the shelf and out of the promise.
    /// Every reclaimable figure the GUI utters comes from here.
    public static long ReclaimableNowBytes(ScanResult scan) =>
        scan.Targets.Where(IsSafeDefault).Sum(t => t.ReclaimableBytes);

    /// A safe-level target sitting out because its app is running: the
    /// actionable half of the promise ("+310 MB when you close WhatsApp").
    /// Same shape as IsSafeDefault minus the skip, so the two can never
    /// overlap or leave a gap.
    public static bool IsAppHeld(TargetScanResult t) =>
        t.Target.Level == CleanupLevel.Safe
        && t.SkippedReason is not null
        && t.Target.AppDisplayName is not null
        && !t.Target.RequiresIndividualSelection
        && !t.Target.RequiresExplicitOptIn
        && t.TotalBytes > 0;

    /// The one definition of "safe to clean in one click" — shared by the
    /// flyout, the overview and the Depolama simple view. Deletion stays
    /// behind its own consented button; fix-all must never call this.
    /// onEntry (additive, round 10) streams every engine entry as it is
    /// recorded, on the worker thread — live progress for the GUI.
    public CleanOutcome CleanSafe(ScanResult scan, Action<CleanEntry>? onEntry = null) =>
        CleanTargets(scan.Targets.Where(IsSafeDefault), onEntry);

    public CleanOutcome CleanTargets(IEnumerable<TargetScanResult> scans,
        Action<CleanEntry>? onEntry = null)
    {
        var paths = new List<string>();
        long bytes = 0;
        var skipped = new List<CleanEntry>();
        var recycled = new List<CleanEntry>();
        var removed = new List<CleanEntry>();
        foreach (var scan in scans)
        {
            var report = _host.Clean(scan, _settings.DryRun, onEntry);
            foreach (var entry in report.Entries)
            {
                if (entry.Action == "recycled")
                {
                    paths.Add(entry.Path);
                    bytes += entry.Bytes;
                    recycled.Add(entry);
                }
                else if (entry.Action == "removed")
                {
                    removed.Add(entry);
                }
                else if (entry.Action is "refused" or "error")
                {
                    skipped.Add(entry);
                }
            }
        }
        return new CleanOutcome(paths, bytes, _settings.DryRun, skipped, recycled, removed);
    }
}
