using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using BriskEngine.Logging;
using BriskEngine.Models;
using BriskEngine.Safety;

namespace BriskEngine.Cleaning;

public sealed record CleanEntry(string TargetId, string Path, long Bytes, string Action);

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

    public CleanReport Clean(TargetScanResult scan, bool dryRun)
    {
        var entries = new List<CleanEntry>();
        void Record(string path, long bytes, string action)
        {
            var entry = new CleanEntry(scan.Target.Id, path, bytes, action);
            entries.Add(entry);
            _log.Append(new { ts = DateTime.UtcNow, targetId = entry.TargetId,
                path = entry.Path, bytes = entry.Bytes, action = entry.Action });
        }

        switch (scan.Target.Id)
        {
            case "docker-prune":
                if (!dryRun)
                {
                    _processRunner.Run("docker", "system prune -af");
                    Record("(docker)", 0, "external");
                }
                return new CleanReport(entries);
            case "empty-recycle-bin":
                if (!dryRun)
                {
                    SHEmptyRecycleBinW(IntPtr.Zero, null, SHERB_SILENT);
                    Record("(recycle bin)", 0, "external");
                }
                return new CleanReport(entries);
        }

        var blockedByElevation = scan.Target.RequiresElevation && !_isElevated();
        foreach (var item in scan.Items)
        {
            if (blockedByElevation) { Record(item.Path, 0, "refused"); continue; }

            var auth = _validator.Authorize(item.Path, scan.Target);
            if (!auth.Allowed) { Record(item.Path, 0, "refused"); continue; }
            if (dryRun) { Record(item.Path, item.Bytes, "dry-run"); continue; }

            try
            {
                _recycler.Recycle(item.Path);
                Record(item.Path, item.Bytes, "recycled");
            }
            catch (Exception)
            {
                Record(item.Path, 0, "error"); // one bad file never stops the run
            }
        }
        return new CleanReport(entries);
    }
}
