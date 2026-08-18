using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    private int _running;                // 0 = idle, 1 = a surface owns it

    public SafeCleanRunner(CleanService cleanService, IRecycleBinSession bin)
    {
        _cleanService = cleanService;
        _bin = bin;
    }

    /// Round-13 review (I1): each view model's busy flag guards its OWN
    /// button, but all three share this ONE runner — so a tray clean could
    /// start while Depolama's was still mid-flight. Both would purge, both
    /// would count the same bytes as freed, and whichever lost the race
    /// would file a "still in the Recycle Bin" line for bytes already gone.
    ///
    /// The lease makes the sequence single-flight app-wide. Take it BEFORE
    /// touching any UI state: a surface that loses it must be a complete
    /// no-op — not a half-cleared report or a dismissed banner for a run
    /// that never starts — exactly like a re-press on the same surface.
    /// Null means someone else is cleaning; dispose to hand the runner back.
    /// The lease covers every bin mutation, not just this runner's own
    /// sequence — a level clean, an undo and a reclaim take it too, so the
    /// bin is only ever handed to one caller at a time.
    public IDisposable? TryBegin()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return null;
        RunningChanged?.Invoke(true);
        return new Lease(this);
    }

    /// Round-13 re-review (N1): the lease alone made a refused press SILENT
    /// — every clean button stayed enabled while another surface cleaned,
    /// and pressing one did nothing at all. This is the shared signal the
    /// buttons disable on, so the refusal is visible before it happens.
    /// Raised on whichever thread takes or releases the lease; in practice
    /// always the dispatcher, since every clean surface presses from it.
    public event Action<bool>? RunningChanged;

    private sealed class Lease : IDisposable
    {
        private SafeCleanRunner? _owner;

        public Lease(SafeCleanRunner owner) => _owner = owner;

        public bool IsHeldBy(SafeCleanRunner runner)
            => ReferenceEquals(Volatile.Read(ref _owner), runner);

        /// Idempotent — a second Dispose must never release a lease that a
        /// LATER clean is holding.
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null) return;
            Volatile.Write(ref owner._running, 0);
            owner.RunningChanged?.Invoke(false);
        }
    }

    /// onEntry streams every engine entry as it is recorded, on the worker
    /// thread (live progress); onPurging fires once between the recycle and
    /// the purge, for surfaces that show the freeing phase.
    /// The lease is the TOKEN, not a flag to check (round-13 re-review,
    /// minor 12): "somebody holds it" would pass exactly the case worth
    /// catching — a second surface running while the first holds the lease.
    public async Task<SafeCleanResult> RunAsync(IDisposable lease, ScanResult scan,
        Action<CleanEntry>? onEntry = null, Action? onPurging = null)
    {
        if (lease is not Lease held || !held.IsHeldBy(this))
            throw new InvalidOperationException(
                "SafeCleanRunner.RunAsync requires THIS runner's live lease from "
                + "TryBegin() — without it two surfaces can purge the bin at once.");
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
