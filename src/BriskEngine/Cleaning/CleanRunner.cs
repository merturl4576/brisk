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
        var batch = new List<ResolvedItem>(BatchSize);
        void RecycleSingle(ResolvedItem item)
        {
            try
            {
                // A path already gone was taken by the failed batch's partial
                // shell work — that IS the recycle, not an error.
                if (File.Exists(item.Path) || Directory.Exists(item.Path))
                    _recycler.Recycle(item.Path);
                Record(item.Path, item.Bytes, "recycled");
            }
            catch (Exception ex)
            {
                Record(item.Path, 0, "error", ex.Message); // one bad file never stops the run
            }
        }
        void Flush()
        {
            if (batch.Count == 0) return;
            try
            {
                _recycler.Recycle(batch.Select(i => i.Path).ToList());
                foreach (var item in batch) Record(item.Path, item.Bytes, "recycled");
            }
            catch (Exception)
            {
                // The shell reports one failure for the whole batch; retry
                // per item to attribute the failure to the path that earned it.
                foreach (var item in batch) RecycleSingle(item);
            }
            batch.Clear();
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
}
