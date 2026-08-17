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
            if (present.Count == 0) return;
            var batchOk = true;
            try { _recycler.Recycle(present.Select(i => i.Path).ToList()); }
            catch (Exception) { batchOk = false; }
            if (batchOk)
            {
                foreach (var item in present)
                {
                    if (Exists(item.Path))
                        Record(item.Path, 0, "error", "the shell skipped this path");
                    else
                        Record(item.Path, item.Bytes, "recycled");
                }
                return;
            }
            // The shell reports one failure for the whole batch; retry per
            // item to attribute the failure to the path that earned it.
            foreach (var item in present) RecycleSingle(item);
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
