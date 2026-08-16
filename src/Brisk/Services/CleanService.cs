using System.Collections.Generic;
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
