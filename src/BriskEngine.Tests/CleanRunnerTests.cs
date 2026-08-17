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
    public HashSet<string> FailPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SkipPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Recycle(string path)
    {
        if (FailPaths.Contains(path)) throw new IOException("Fake recycler error");
        if (SkipPaths.Contains(path)) return;   // quiet shell skip
        Recycled.Add(path);
        if (File.Exists(path)) File.Delete(path);
        else if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    public void Recycle(IReadOnlyList<string> paths)
    {
        BatchCalls.Add(paths.Count);
        foreach (var path in paths) Recycle(path);
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

    private CleanRunner Runner(bool elevated = false) =>
        new(new SafetyValidator(), _recycler, _log, _runner, () => elevated);

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
