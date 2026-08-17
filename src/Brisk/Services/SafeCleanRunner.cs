using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BriskEngine.Cleaning;
using BriskEngine.Models;

namespace Brisk.Services;

/// What one safe clean actually did. FreedBytes is POST-purge truth — the
/// bytes that really left the disk — never the bytes-moved-to-the-bin
/// figure; anything recycled the purge could not take is reported as
/// LeftInBinBytes rather than folded into the good news.
public sealed record SafeCleanResult(
    CleanOutcome Outcome,
    IReadOnlyList<string> PurgedPaths,
    long FreedBytes)
{
    public int CleanedCount => Outcome.RecycledPaths.Count;
    public long LeftInBinBytes => Outcome.RecycledBytes - FreedBytes;
}

/// The ONE-STEP safe clean, shared by every surface that offers it: the
/// Depolama card's Temizle, the overview's "Free up 1.2 GB" button and the
/// tray flyout's Clean. Round 13 exists because they disagreed — round 12
/// gave Depolama a real purge while the other two left the bytes sitting in
/// the Recycle Bin under the same "free up space" promise.
///
/// The sequence is the round-12 mechanism verbatim:
///   1. snapshot the payload identities ALREADY in the bin at the paths this
///      run is about to recycle — they belong to the USER's own earlier
///      deletions, and excluding them makes it structurally impossible to
///      destroy one;
///   2. recycle through the engine's unchanged batched path;
///   3. purge exactly THIS run's recycled originals, minus that snapshot;
///   4. account freed bytes per item, from what the purge confirmed.
///
/// A dry run, or a run that recycled nothing, never touches the bin at all.
/// The surfaces keep their own reporting — this owns only the sequence.
public sealed class SafeCleanRunner
{
    private readonly CleanService _cleanService;
    private readonly IRecycleBinSession _bin;

    public SafeCleanRunner(CleanService cleanService, IRecycleBinSession bin)
    {
        _cleanService = cleanService;
        _bin = bin;
    }

    /// onEntry streams every engine entry as it is recorded, on the worker
    /// thread (live progress); onPurging fires once between the recycle and
    /// the purge, for surfaces that show the freeing phase.
    public async Task<SafeCleanResult> RunAsync(ScanResult scan,
        Action<CleanEntry>? onEntry = null, Action? onPurging = null)
    {
        var plannedPaths = scan.Targets.Where(CleanService.IsSafeDefault)
            .SelectMany(t => t.Items).Select(i => i.Path).ToList();
        var preExisting = plannedPaths.Count == 0
            ? (IReadOnlyList<string>)Array.Empty<string>()
            : await Task.Run(() => _bin.MatchingItemIds(plannedPaths));
        var outcome = await Task.Run(() => _cleanService.CleanSafe(scan, onEntry));
        if (outcome.WasDryRun || outcome.RecycledPaths.Count == 0)
            return new SafeCleanResult(outcome, Array.Empty<string>(), 0);
        onPurging?.Invoke();
        var purged = await Task.Run(() =>
            _bin.Purge(outcome.RecycledPaths, preExisting));
        var purgedSet = new HashSet<string>(purged, StringComparer.OrdinalIgnoreCase);
        return new SafeCleanResult(outcome, purged,
            outcome.Recycled.Where(e => purgedSet.Contains(e.Path)).Sum(e => e.Bytes));
    }
}
