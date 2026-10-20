using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BriskEngine.Cleaning;
using BriskEngine.Models;

namespace BriskEngine.Actions;

/// Cache targets that were skipped because their app was open. They are
/// cleaned when the app is closed; work older than a week drops out.
public sealed class DeferredCleanQueue
{
    public sealed record Entry(string TargetId, DateTime QueuedUtc);

    public static readonly TimeSpan Expiry = TimeSpan.FromDays(7);
    private readonly string _path;

    public DeferredCleanQueue(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public IReadOnlyList<Entry> Pending()
    {
        if (!File.Exists(_path)) return Array.Empty<Entry>();
        try { return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path)) ?? new List<Entry>(); }
        catch (JsonException) { return Array.Empty<Entry>(); }
    }

    public void Add(string targetId, DateTime nowUtc)
    {
        var list = Pending().Where(e => e.TargetId != targetId).ToList();
        list.Add(new Entry(targetId, nowUtc));
        Save(list);
    }

    public IReadOnlyList<CleanReport> ProcessDue(DateTime nowUtc, IProcessLister apps,
        Func<CleanupTarget, TargetScanResult> scanOne, Func<TargetScanResult, CleanReport> clean)
    {
        var reports = new List<CleanReport>();
        var keep = new List<Entry>();
        foreach (var entry in Pending())
        {
            if (nowUtc - entry.QueuedUtc > Expiry) continue;
            var target = CleanupTargetRegistry.All.FirstOrDefault(t => t.Id == entry.TargetId);
            if (target is null) continue;
            if (target.AppProcessCandidates.Any(apps.IsRunning)) { keep.Add(entry); continue; }
            var scan = scanOne(target);
            if (scan.SkippedReason is not null) { keep.Add(entry); continue; }
            reports.Add(clean(scan));
        }
        Save(keep);
        return reports;
    }

    private void Save(List<Entry> list) =>
        File.WriteAllText(_path, JsonSerializer.Serialize(list));
}
