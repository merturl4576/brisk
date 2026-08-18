using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BriskEngine.Logging;
using BriskEngine.Models;
using BriskEngine.Safety;

namespace BriskEngine.Cleaning;

public sealed record CleanEntry(string TargetId, string Path, long Bytes, string Action, string? Reason = null);

public sealed record CleanReport(IReadOnlyList<CleanEntry> Entries)
{
    public long RecycledBytes =>
        Entries.Where(e => e.Action == "recycled").Sum(e => e.Bytes);
}

public sealed class CleanRunner
{
    [DllImport("shell32.dll")]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? root, uint flags);
    private const uint SHERB_SILENT = 0x7; // no confirm, no progress UI, no sound

    private readonly SafetyValidator _validator;
    private readonly IRecycler _recycler;
    private readonly ActionLog _log;
    private readonly IProcessRunner _processRunner;
    private readonly Func<bool> _isElevated;

    public CleanRunner(SafetyValidator validator, IRecycler recycler, ActionLog log,
        IProcessRunner processRunner, Func<bool> isElevated)
    {
        _validator = validator;
        _recycler = recycler;
        _log = log;
        _processRunner = processRunner;
        _isElevated = isElevated;
    }

    /// How many paths go into one shell recycle operation. The shell charges
    /// ~200 ms of overhead per SHFileOperation CALL, so one-call-per-file
    /// turned ~1900 small temp items into a six-minute silent grind (the
    /// 2026-08-17 live incident); batches of this size keep the same clean
    /// at seconds while progress callbacks still tick per chunk.
    public const int BatchSize = 128;

    /// onEntry (additive, round 10) fires for every recorded entry as it
    /// happens, on the calling thread — the GUI's live progress. Semantics
    /// of what gets cleaned are unchanged.
    public CleanReport Clean(TargetScanResult scan, bool dryRun,
        Action<CleanEntry>? onEntry = null)
    {
        // A skipped scan (its app is running) cleans NOTHING — since round
        // 11 the scanner sizes skipped targets so the GUI can promise "+X
        // when you close the app", and that sizing must never leak into a
        // clean. Empty report = exactly the pre-round-11 outcome.
        if (scan.SkippedReason is not null)
            return new CleanReport(new List<CleanEntry>());

        var entries = new List<CleanEntry>();
        void Record(string path, long bytes, string action, string? reason = null)
        {
            var entry = new CleanEntry(scan.Target.Id, path, bytes, action, reason);
            entries.Add(entry);
            var logObj = new { ts = DateTime.UtcNow, targetId = entry.TargetId,
                path = entry.Path, bytes = entry.Bytes, action = entry.Action, reason = entry.Reason };
            _log.Append(logObj);
            onEntry?.Invoke(entry);
        }

        switch (scan.Target.Id)
        {
            case "docker-prune":
                if (dryRun)
                {
                    Record("(docker)", 0, "dry-run");
                }
                else
                {
                    _processRunner.Run("docker", "system prune -af");
                    Record("(docker)", 0, "external");
                }
                return new CleanReport(entries);
            case "empty-recycle-bin":
                if (dryRun)
                {
                    Record("(recycle bin)", 0, "dry-run");
                }
                else
                {
                    SHEmptyRecycleBinW(IntPtr.Zero, null, SHERB_SILENT);
                    Record("(recycle bin)", 0, "external");
                }
                return new CleanReport(entries);
        }

        // Authorized items are recycled in shell batches; a failed batch is
        // retried item-by-item so every path still gets an accurate action
        // and reason ("one bad file never stops the run" holds either way).
        //
        // Attribution is OBSERVATION-based (round-10 review): "recycled" is
        // recorded only for a path that existed before the shell attempt
        // AND is gone after it. FOF_NOERRORUI lets the shell skip a path
        // without failing the call, and the scan-to-clean gap lets an app
        // take its own temp file back — neither may be claimed as our work
        // (a phantom recycle poisons undo and inflates the freed bytes).
        // Only the shell call sits in a try: recording must never run twice
        // because a log write or progress callback threw mid-loop.
        var batch = new List<ResolvedItem>(BatchSize);
        void RecycleSingle(ResolvedItem item)
        {
            if (!Exists(item.Path))
            {
                // Existed when this flush began, gone now: the failed
                // batch's partial shell work took it — that IS the recycle.
                Record(item.Path, item.Bytes, "recycled");
                return;
            }
            var ok = true;
            Exception? failure = null;
            try { _recycler.Recycle(item.Path); }
            catch (Exception ex) { ok = false; failure = ex; } // one bad file never stops the run
            if (!ok)
                Record(item.Path, 0, "error", failure!.Message);
            else if (Exists(item.Path))
                Record(item.Path, 0, "error", "the shell skipped this path");
            else
                Record(item.Path, item.Bytes, "recycled");
        }
        // ROUND 14, from the 2026-08-18 live run: 332 items took 53 SECONDS
        // because 15 of them were locked. The shell aborts the WHOLE call at
        // the first path it cannot take, so a failure says nothing about the
        // rest of the span — and retrying every survivor individually put
        // 171 of the 332 back on the ~200 ms-per-file path that batching was
        // introduced to escape. Only the HEAD earned the failure, so only it
        // is retried alone; the remainder goes back as a batch. Each locked
        // file now costs two extra calls instead of one call per file after
        // it, and the per-item attribution is unchanged.
        void RecycleSpan(List<ResolvedItem> span)
        {
            // Consecutive spans whose failed call took NOTHING. A loop, not
            // recursion (round-14 review): the tail call would have made
            // BatchSize double as a stack-depth constant.
            var barren = 0;
            while (span.Count > 0)
            {
                if (span.Count == 1) { RecycleSingle(span[0]); return; }
                var spanOk = true;
                try { _recycler.Recycle(span.Select(i => i.Path).ToList()); }
                catch (Exception) { spanOk = false; }
                if (spanOk)
                {
                    foreach (var item in span)
                    {
                        if (Exists(item.Path))
                            Record(item.Path, 0, "error", "the shell skipped this path");
                        else
                            Record(item.Path, item.Bytes, "recycled");
                    }
                    return;
                }
                // Harvest the partial work before retrying anything: a path
                // that was there when the span began and is gone now WAS
                // recycled by the failed call, and must never be sent to the
                // shell twice.
                var left = new List<ResolvedItem>(span.Count);
                foreach (var item in span)
                {
                    if (Exists(item.Path)) left.Add(item);
                    else Record(item.Path, item.Bytes, "recycled");
                }
                if (left.Count == 0) return;
                barren = left.Count == span.Count ? barren + 1 : 0;
                RecycleSingle(left[0]);
                if (left.Count == 1) return;
                // Two failed calls in a row that took nothing means the locks
                // are DENSE, not sparse, and re-sending the tail as a batch is
                // pure overhead — the old per-survivor fallback cost n+1 here,
                // and head-splitting alone would have cost 2n-1. Finishing
                // item by item keeps the worst case at n+2 while sparse locks
                // still settle in three calls.
                if (barren >= 2)
                {
                    for (var i = 1; i < left.Count; i++) RecycleSingle(left[i]);
                    return;
                }
                span = left.GetRange(1, left.Count - 1);
            }
        }
        void Flush()
        {
            if (batch.Count == 0) return;
            var present = new List<ResolvedItem>(batch.Count);
            foreach (var item in batch)
            {
                if (Exists(item.Path)) present.Add(item);
                else Record(item.Path, 0, "error",
                    "no longer exists (nothing to recycle)");
            }
            batch.Clear();
            RecycleSpan(present);
        }

        var blockedByElevation = scan.Target.RequiresElevation && !_isElevated();
        foreach (var item in scan.Items)
        {
            if (blockedByElevation) { Record(item.Path, 0, "refused", "requires administrator"); continue; }

            var auth = _validator.Authorize(item.Path, scan.Target);
            if (!auth.Allowed) { Record(item.Path, 0, "refused", auth.Reason); continue; }
            if (dryRun) { Record(item.Path, item.Bytes, "dry-run"); continue; }

            batch.Add(item);
            if (batch.Count >= BatchSize) Flush();
        }
        Flush();
        return new CleanReport(entries);
    }

    private static bool Exists(string path) =>
        File.Exists(path) || Directory.Exists(path);
}
