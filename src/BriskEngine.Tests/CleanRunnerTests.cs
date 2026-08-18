using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Logging;
using BriskEngine.Models;
using BriskEngine.Safety;
using Xunit;

namespace BriskEngine.Tests;

/// Behaves like the shell: a successful recycle removes the path from disk,
/// a batch processes in order and aborts at the first failing path (so a
/// failed batch leaves real partial work behind, exactly like SHFileOperation).
/// SkipPaths mimics FOF_NOERRORUI's quiet skip: the call "succeeds" but the
/// path stays on disk — only observation can tell it was never recycled.
sealed class FakeRecycler : IRecycler
{
    public List<string> Recycled { get; } = new();
    public List<int> BatchCalls { get; } = new();
    /// EVERY trip to the shell, batch or single — the thing that costs
    /// ~200 ms apiece and made the 2026-08-18 run take 53 seconds.
    public int ShellCalls { get; private set; }
    public HashSet<string> FailPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SkipPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Recycle(string path)
    {
        ShellCalls++;
        RecycleOne(path);
    }

    public void Recycle(IReadOnlyList<string> paths)
    {
        ShellCalls++;
        BatchCalls.Add(paths.Count);
        foreach (var path in paths) RecycleOne(path);
    }

    private void RecycleOne(string path)
    {
        if (FailPaths.Contains(path)) throw new IOException("Fake recycler error");
        if (SkipPaths.Contains(path)) return;   // quiet shell skip
        Recycled.Add(path);
        if (File.Exists(path)) File.Delete(path);
        else if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}

sealed class FakeRunner : IProcessRunner
{
    public List<string> Commands { get; } = new();
    public (int ExitCode, string StdOut) Run(string exe, string args)
    { Commands.Add($"{exe} {args}"); return (0, ""); }
}

public sealed class CleanRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-clean-").FullName;
    private readonly FakeRecycler _recycler = new();
    private readonly FakeRunner _runner = new();
    private readonly ActionLog _log;
    private readonly string _logPath;

    public CleanRunnerTests()
    {
        _logPath = Path.Combine(_root, "log.jsonl");
        _log = new ActionLog(_logPath);
    }

    private CleanRunner Runner(bool elevated = false, ILockProbe? lockProbe = null) =>
        new(new SafetyValidator(), _recycler, _log, _runner, () => elevated, lockProbe);

    private (CleanupTarget, TargetScanResult) ScanOver(string dir, params string[] files)
    {
        Directory.CreateDirectory(dir);
        var items = new List<ResolvedItem>();
        foreach (var f in files)
        {
            var p = Path.Combine(dir, f);
            File.WriteAllBytes(p, new byte[10]);
            items.Add(new ResolvedItem("t", p, 10, DateTime.UtcNow));
        }
        var target = new CleanupTarget("t", "T", CleanupLevel.Safe,
            new List<string> { dir }, "Test", DeletesContentsNotDirectory: true);
        return (target, new TargetScanResult(target, items, null));
    }

    /// Round 11: the scanner sizes skipped (app-running) targets so the GUI
    /// can promise "+X when you close the app" — that sizing must NEVER leak
    /// into a clean. A skipped scan with items cleans nothing, logs nothing.
    [Fact]
    public void SkippedScan_CleansNothing_EvenWithItems()
    {
        var (target, scan) = ScanOver(Path.Combine(_root, "skipped"), "held.tmp");
        var skipped = scan with { SkippedReason = "WhatsApp is running — close it to include this target" };

        var report = Runner().Clean(skipped, dryRun: false);

        Assert.Empty(report.Entries);
        Assert.Empty(_recycler.Recycled);
        Assert.False(File.Exists(_logPath));   // nothing recorded — same as pre-round-11
        _ = target;
    }

    [Fact]
    public void RecyclesAuthorizedItems_AndLogs()
    {
        var (_, scan) = ScanOver(Path.Combine(_root, "cache"), "a.tmp", "b.tmp");
        var report = Runner().Clean(scan, dryRun: false);
        Assert.Equal(2, _recycler.Recycled.Count);
        Assert.Equal(20, report.RecycledBytes);
        Assert.Equal(2, File.ReadAllLines(_logPath).Length);
    }

    [Fact]
    public void DryRun_TouchesNothing_ButLogs()
    {
        var (_, scan) = ScanOver(Path.Combine(_root, "cache2"), "a.tmp");
        var report = Runner().Clean(scan, dryRun: true);
        Assert.Empty(_recycler.Recycled);
        Assert.Equal("dry-run", report.Entries.Single().Action);
        Assert.Single(File.ReadAllLines(_logPath));
    }

    [Fact]
    public void UnauthorizedItem_IsRefused()
    {
        var (target, _) = ScanOver(Path.Combine(_root, "cache3"), "a.tmp");
        var outside = Path.Combine(_root, "outside.txt");
        File.WriteAllText(outside, "x");
        var scan = new TargetScanResult(target,
            new[] { new ResolvedItem("t", outside, 1, DateTime.UtcNow) }, null);
        var report = Runner().Clean(scan, dryRun: false);
        Assert.Equal("refused", report.Entries.Single().Action);
        Assert.Empty(_recycler.Recycled);
    }

    [Fact]
    public void ElevationRequired_WithoutElevation_RefusesAll()
    {
        var dir = Path.Combine(_root, "admin");
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, "a.tmp");
        File.WriteAllBytes(p, new byte[10]);
        var target = new CleanupTarget("adm", "Adm", CleanupLevel.Deep,
            new List<string> { dir }, "Test", RequiresElevation: true);
        var scan = new TargetScanResult(target,
            new[] { new ResolvedItem("adm", p, 10, DateTime.UtcNow) }, null);
        var report = Runner(elevated: false).Clean(scan, dryRun: false);
        Assert.Equal("refused", report.Entries.Single().Action);
    }

    [Fact]
    public void DockerPrune_RunsExternalCommand()
    {
        var target = new CleanupTarget("docker-prune", "Docker", CleanupLevel.Developer,
            new List<string>(), "Container", RequiresExplicitOptIn: true);
        var scan = new TargetScanResult(target, Array.Empty<ResolvedItem>(), null);
        Runner().Clean(scan, dryRun: false);
        Assert.Contains("docker system prune -af", _runner.Commands);
    }

    /// ROUND 14 — the 2026-08-18 live run: 332 items took 53 SECONDS. The
    /// shell aborts a batch at the first path it cannot take, and the old
    /// fallback then retried EVERY survivor with its own ~200 ms call — so
    /// one locked file put the whole rest of its batch back on the per-file
    /// path that batching exists to escape. One lock must cost a couple of
    /// extra calls, never a call per file behind it.
    [Fact]
    public void OneLockedFile_CostsTwoExtraShellCalls_NotOnePerSurvivor()
    {
        var files = Enumerable.Range(0, 40).Select(i => $"f{i}.tmp").ToArray();
        var dir = Path.Combine(_root, "locked");
        var (_, scan) = ScanOver(dir, files);
        _recycler.FailPaths.Add(Path.Combine(dir, "f20.tmp"));

        var report = Runner().Clean(scan, dryRun: false);

        // The accounting is exactly what it was: the 20 the failed call took
        // before it aborted, plus the 19 behind the lock, and the lock named.
        Assert.Equal(39, report.Entries.Count(e => e.Action == "recycled"));
        var error = Assert.Single(report.Entries, e => e.Action == "error");
        Assert.Equal(Path.Combine(dir, "f20.tmp"), error.Path);
        Assert.DoesNotContain(report.Entries,
            e => e.Action == "recycled" && e.Path == error.Path);

        // The cost is what changed: the failed batch, the head that earned
        // the failure, and the remainder as ONE batch. The old fallback
        // spent 21 calls here — one per survivor.
        Assert.Equal(3, _recycler.ShellCalls);
        Assert.Equal(new[] { 40, 19 }, _recycler.BatchCalls);
    }

    /// The pathological end of the same fix: when EVERY path is locked no
    /// split can help, so it must still terminate, still attribute each
    /// failure to the path that earned it, and — since head-splitting alone
    /// would cost 2n-1 against the old fallback's n+1 — bail out to
    /// item-by-item after two calls that harvest nothing. n+2 is the cost of
    /// THIS shape, not a bound on every shape: see the interleaved test.
    [Fact]
    public void EveryFileLocked_StillTerminates_AndNamesEveryFailure()
    {
        var files = Enumerable.Range(0, 5).Select(i => $"f{i}.tmp").ToArray();
        var dir = Path.Combine(_root, "alllocked");
        var (_, scan) = ScanOver(dir, files);
        foreach (var f in files) _recycler.FailPaths.Add(Path.Combine(dir, f));

        var report = Runner().Clean(scan, dryRun: false);

        Assert.Equal(5, report.Entries.Count(e => e.Action == "error"));
        Assert.Empty(report.Entries.Where(e => e.Action == "recycled"));
        Assert.Equal(0, report.RecycledBytes);
        foreach (var f in files)
            Assert.True(File.Exists(Path.Combine(dir, f)), $"{f} must survive");
        // The two barren spans that prove the locks are dense, then one
        // call per path — within one call of the old fallback's n + 1,
        // where head-splitting alone would have spent 2n - 1.
        Assert.Equal(files.Length + 2, _recycler.ShellCalls);
    }

    /// ROUND 15: a DELETE-access probe costs 0.058 ms; letting the shell
    /// discover the same lock costs ~1.02 SECONDS, and the runner pays it
    /// TWICE per held file — once for the batch it aborts, once for the
    /// retry that abort earns. On the 2026-08-18 run that was ~60 s of a
    /// 92 s clean. A path the probe already knows is held must never reach
    /// the shell, and must still be named in the report.
    [Fact]
    public void ProbeKnownHeldPaths_NeverReachTheShell()
    {
        var dir = Path.Combine(_root, "probed");
        var (_, scan) = ScanOver(dir, "a.tmp", "b.tmp", "c.tmp");
        var probe = new FakeLockProbe();
        probe.LockedPaths.Add(Path.Combine(dir, "b.tmp"));

        var report = Runner(lockProbe: probe).Clean(scan, dryRun: false);

        // ONE batch over the two takeable paths: no aborted call, no retry.
        Assert.Equal(new[] { 2 }, _recycler.BatchCalls);
        Assert.Equal(1, _recycler.ShellCalls);
        Assert.DoesNotContain(Path.Combine(dir, "b.tmp"), _recycler.Recycled);
        Assert.True(File.Exists(Path.Combine(dir, "b.tmp")));

        // …and the held path is still named, with the reason the GUI reads
        // as "in use by a running app".
        var held = Assert.Single(report.Entries, e => e.Action == "error");
        Assert.Equal(Path.Combine(dir, "b.tmp"), held.Path);
        Assert.Equal(CleanRunner.HeldReason, held.Reason);
        Assert.Equal(0, held.Bytes);
        Assert.Equal(2, report.Entries.Count(e => e.Action == "recycled"));
    }

    /// A dry run promises what a real one WOULD take, so it must not start
    /// reporting held paths as errors — the probe sits after that branch.
    [Fact]
    public void DryRun_IsUnchangedByTheProbe()
    {
        var dir = Path.Combine(_root, "probed-dry");
        var (_, scan) = ScanOver(dir, "a.tmp", "b.tmp");
        var probe = new FakeLockProbe();
        probe.LockedPaths.Add(Path.Combine(dir, "b.tmp"));

        var report = Runner(lockProbe: probe).Clean(scan, dryRun: true);

        Assert.Equal(2, report.Entries.Count(e => e.Action == "dry-run"));
        Assert.Empty(probe.Calls);
        Assert.Empty(_recycler.BatchCalls);
    }

    /// The shape the drain CANNOT catch, and the honest cost of it
    /// (round-14 re-review, N1 and N3): every failed call harvests exactly
    /// one item, so `barren` is reset before it ever reaches two and the
    /// span keeps paying for a batch attempt it will not finish. PHASE
    /// matters and is the whole point — a sweep over every period 2-8 and
    /// every phase at n=128 peaks at exactly 171 calls for period 3 with
    /// the free item at index 1, which is this shape. A CHARACTERIZATION
    /// pin, not a target: it exists so the ~4n/3 regime cannot drift
    /// silently, and so nobody reads the all-locked test's n+2 as a bound.
    [Fact]
    public void InterleavedLocks_AreTheShapeTheDrainCannotCatch()
    {
        const int n = 12;
        var files = Enumerable.Range(0, n).Select(i => $"f{i}.tmp").ToArray();
        var dir = Path.Combine(_root, "interleaved");
        var (_, scan) = ScanOver(dir, files);
        // Locked, FREE, locked — repeating. The span never opens on two
        // adjacent locks, so every failed call harvests exactly one item
        // and the drain's counter is reset before it can fire.
        foreach (var f in files.Where((_, i) => i % 3 != 1))
            _recycler.FailPaths.Add(Path.Combine(dir, f));

        var report = Runner().Clean(scan, dryRun: false);

        // Correctness is untouched, and is phase-invariant: every path gets
        // exactly one entry, and it is the right one.
        Assert.Equal(n, report.Entries.Count);
        Assert.Equal(n, report.Entries.Select(e => e.Path).Distinct().Count());
        Assert.Equal(8, report.Entries.Count(e => e.Action == "error"));
        Assert.Equal(4, report.Entries.Count(e => e.Action == "recycled"));
        foreach (var f in files.Where((_, i) => i % 3 != 1))
            Assert.True(File.Exists(Path.Combine(dir, f)), $"{f} must survive");

        // The cost: MORE than the old per-survivor fallback, which would
        // have spent 1 + 12 = 13 here. Sparse locks still settle in three
        // calls and a dense block is at parity; this is where the heuristic
        // loses, and it loses by a constant factor, never an order — the
        // same shape at n=128 costs 171 against the old fallback's 129.
        Assert.Equal(16, _recycler.ShellCalls);
    }

    /// The other half of why phase matters (round-14 re-review, N3): shift
    /// the same period-3 pattern so the span OPENS on two adjacent locks
    /// and the drain fires on the first two iterations, putting the cost
    /// back at n+2. This is the case my first interleave pin accidentally
    /// used while claiming to test the one above.
    [Fact]
    public void InterleavedLocks_OpeningOnTwoAdjacentLocks_AreCaught()
    {
        const int n = 12;
        var files = Enumerable.Range(0, n).Select(i => $"f{i}.tmp").ToArray();
        var dir = Path.Combine(_root, "interleaved-caught");
        var (_, scan) = ScanOver(dir, files);
        foreach (var f in files.Where((_, i) => i % 3 != 2))
            _recycler.FailPaths.Add(Path.Combine(dir, f));

        var report = Runner().Clean(scan, dryRun: false);

        Assert.Equal(n, report.Entries.Count);
        Assert.Equal(8, report.Entries.Count(e => e.Action == "error"));
        Assert.Equal(4, report.Entries.Count(e => e.Action == "recycled"));
        Assert.Equal(n + 2, _recycler.ShellCalls);
    }

    /// ROOT-CAUSE REGRESSION (round 10): one shell call per file cost
    /// ~200 ms each and turned ~1900 small temp items into a six-minute
    /// silent grind on 2026-08-17. Many items must reach the shell as
    /// ⌈n / BatchSize⌉ batch operations, never one call per file.
    [Fact]
    public void ManyItems_ReachTheShell_InBatches_NeverOneCallPerFile()
    {
        var files = Enumerable.Range(0, CleanRunner.BatchSize + 2)
            .Select(i => $"f{i}.tmp").ToArray();
        var (_, scan) = ScanOver(Path.Combine(_root, "big"), files);

        var report = Runner().Clean(scan, dryRun: false);

        Assert.Equal(new[] { CleanRunner.BatchSize, 2 }, _recycler.BatchCalls);
        Assert.Equal(files.Length,
            report.Entries.Count(e => e.Action == "recycled"));
        Assert.Equal(files.Length * 10, report.RecycledBytes);
    }

    /// A failed batch is retried per item: the bad path gets the error, the
    /// rest still get recycled, and paths the batch's partial shell work
    /// already took are honestly recorded as recycled — never re-attempted.
    [Fact]
    public void BatchFailure_FallsBackPerItem_AttributingEachPath()
    {
        var (_, scan) = ScanOver(Path.Combine(_root, "cache4"),
            "a.tmp", "b.tmp", "c.tmp");
        var bad = Path.Combine(_root, "cache4", "b.tmp");
        _recycler.FailPaths.Add(bad); // batch aborts here after taking a.tmp

        var report = Runner().Clean(scan, dryRun: false);

        Assert.Equal(3, report.Entries.Count);
        var error = Assert.Single(report.Entries, e => e.Action == "error");
        Assert.Equal(bad, error.Path);
        Assert.NotNull(error.Reason);
        Assert.NotEmpty(error.Reason);
        Assert.Equal(2, report.Entries.Count(e => e.Action == "recycled"));
        Assert.Equal(20, report.RecycledBytes);
        // a.tmp went in the batch's partial work and was NOT recycled twice
        Assert.Equal(2, _recycler.Recycled.Count);
        Assert.Equal(3, File.ReadAllLines(_logPath).Length);
    }

    /// The additive onEntry callback (round 10) reports every entry as it
    /// is recorded — the GUI's live progress ticks off this stream.
    [Fact]
    public void OnEntry_ReportsEveryEntry_InRecordOrder()
    {
        var (_, scan) = ScanOver(Path.Combine(_root, "cache5"), "a.tmp", "b.tmp");
        var seen = new List<CleanEntry>();

        var report = Runner().Clean(scan, dryRun: false, seen.Add);

        Assert.Equal(report.Entries, seen);
    }

    /// CRITICAL REGRESSION (round-10 review): a scanned path its owning app
    /// already took back (routine in %TEMP%) must never be recorded as OUR
    /// recycle — a phantom RecycledPaths entry poisons undo (restore counts
    /// paths the bin never held → false "restore failed") and inflates the
    /// freed bytes. Layered defense: the fail-closed validator refuses a
    /// path whose real path no longer resolves (this test), and Flush's
    /// before/after observation covers the authorize→flush and quiet-skip
    /// windows (the tests around this one). Either way: 0 bytes, never
    /// recycled, never sent to the shell.
    [Fact]
    public void PathGoneBeforeClean_NeverBecomesAPhantomRecycle()
    {
        var (_, scan) = ScanOver(Path.Combine(_root, "cache6"),
            "a.tmp", "b.tmp", "c.tmp");
        var gone = Path.Combine(_root, "cache6", "b.tmp");
        File.Delete(gone);                    // the app took its file back

        var report = Runner().Clean(scan, dryRun: false);

        var refused = Assert.Single(report.Entries, e => e.Action == "refused");
        Assert.Equal(gone, refused.Path);
        Assert.Equal(0, refused.Bytes);
        Assert.Equal(2, report.Entries.Count(e => e.Action == "recycled"));
        Assert.DoesNotContain(report.Entries,
            e => e.Action == "recycled" && e.Path == gone);
        Assert.Equal(20, report.RecycledBytes);
        Assert.Equal(2, Assert.Single(_recycler.BatchCalls)); // never sent to the shell
    }

    /// CRITICAL REGRESSION (round-10 review): FOF_NOERRORUI lets the shell
    /// quietly skip a path while the batch call still "succeeds" — recycled
    /// is recorded only when the path is OBSERVED gone afterwards.
    [Fact]
    public void ShellQuietSkip_IsAnError_NotARecordedRecycle()
    {
        var (_, scan) = ScanOver(Path.Combine(_root, "cache7"),
            "a.tmp", "b.tmp", "c.tmp");
        var skipped = Path.Combine(_root, "cache7", "b.tmp");
        _recycler.SkipPaths.Add(skipped);     // "success", file still on disk

        var report = Runner().Clean(scan, dryRun: false);

        var error = Assert.Single(report.Entries, e => e.Action == "error");
        Assert.Equal(skipped, error.Path);
        Assert.Equal(0, error.Bytes);
        Assert.Contains("skipped", error.Reason);
        Assert.Equal(2, report.Entries.Count(e => e.Action == "recycled"));
        Assert.Equal(20, report.RecycledBytes);
        Assert.True(File.Exists(skipped));    // and the file is honestly still there
    }

    /// Round-10 review: only the shell call sits inside the try — a log
    /// write or progress callback that throws mid-recording must surface,
    /// never re-run the loop and record the same items twice.
    [Fact]
    public void RecordingFailure_Surfaces_AndNeverDoubleRecords()
    {
        var (_, scan) = ScanOver(Path.Combine(_root, "cache8"), "a.tmp", "b.tmp");

        Assert.Throws<InvalidOperationException>(() => Runner().Clean(
            scan, dryRun: false,
            _ => throw new InvalidOperationException("progress sink died")));

        // exactly ONE journal line: the entry recorded before the callback
        // threw — the old catch-around-the-loop would have re-recorded it
        Assert.Single(File.ReadAllLines(_logPath));
    }

    [Fact]
    public void DryRun_ExternalTarget_IsVisible()
    {
        var target = new CleanupTarget("docker-prune", "Docker", CleanupLevel.Developer,
            new List<string>(), "Container", RequiresExplicitOptIn: true);
        var scan = new TargetScanResult(target, Array.Empty<ResolvedItem>(), null);
        var report = Runner().Clean(scan, dryRun: true);

        Assert.Single(report.Entries);
        Assert.Equal("dry-run", report.Entries.Single().Action);
        Assert.Empty(_runner.Commands); // no docker command executed
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
