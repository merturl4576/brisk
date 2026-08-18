using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace Brisk.Services;

/// One bin entry: the original path the item was deleted from, and the
/// physical payload ($RXXXXXX…) that uniquely identifies THIS entry — two
/// deletions of the same original path are two different payloads.
public readonly record struct BinEntry(string OriginalPath, string PayloadPath);

/// Recycle Bin session over the bin's own on-disk contract, not shell COM
/// (round-12 fix round 1): live verification on the dev machine showed
/// Shell.Application's System.Recycle.DeducedOriginalPath returns EMPTY for
/// every item on current Windows 11 builds — the COM identity matching
/// could never match anything. Windows has stored each recycled item as a
/// $RXXXXXX payload plus a $IXXXXXX metadata file (original path inside)
/// since Vista; reading $I directly gives exact full-fidelity identity,
/// enumerating is a materialized file listing (nothing is mutated under a
/// live enumerator), and purging deletes BOTH files so no ghost entries
/// linger. No COM round-trips per item, no apartment concerns — safe from
/// any thread, including the GUI's Task.Run.
public sealed class ShellRecycleBinSession : IRecycleBinSession
{
    /// Restore = move the payload back to its original path and retire the
    /// metadata. True only when EVERY wanted path came back; an original
    /// that already exists again (a regenerated cache file) is never
    /// overwritten — that counts as a failure and the GUI's ↗ fallback
    /// lets the user resolve it in Explorer.
    public bool Restore(IReadOnlyList<string> originalPaths)
    {
        var wanted = new HashSet<string>(originalPaths, StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return true;
        var restored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in CollectRecords(originalPaths))
        {
            try
            {
                if (File.Exists(record.Original) || Directory.Exists(record.Original))
                    continue;   // never overwrite regenerated content
                var parent = Path.GetDirectoryName(record.Original);
                if (parent is not null) Directory.CreateDirectory(parent);
                if (Directory.Exists(record.Payload))
                    Directory.Move(record.Payload, record.Original);
                else if (File.Exists(record.Payload))
                    File.Move(record.Payload, record.Original);
                else
                    continue;
                TryDelete(record.Index);
                restored.Add(record.Original);
            }
            catch (Exception) { /* count it unrestored */ }
        }
        return restored.Count == wanted.Count;
    }

    /// The payload identities of bin entries currently matching these
    /// original paths. The simple clean snapshots this BEFORE recycling and
    /// excludes the result from its auto-purge — so a file the USER deleted
    /// earlier at the same path is structurally out of the purge's reach.
    public IReadOnlyList<string> MatchingItemIds(IReadOnlyList<string> originalPaths) =>
        CollectRecords(originalPaths).Select(r => r.Payload).ToList();

    /// Identity-matched purge: only entries whose original path is wanted
    /// AND whose payload identity is not excluded are touched. The records
    /// are fully collected before anything is deleted, and every deletion
    /// takes the $I metadata with the $R payload — no ghost entries. Per-
    /// item failures are skipped; the returned originals are exactly what
    /// actually left the bin.
    public IReadOnlyList<string> Purge(IReadOnlyList<string> originalPaths,
        IReadOnlyList<string>? excludeItemIds = null)
    {
        var purged = new List<string>();
        if (originalPaths.Count == 0) return purged;
        var plan = PlanPurge(
            CollectRecords(originalPaths)
                .Select(r => new BinEntry(r.Original, r.Payload)).ToList(),
            originalPaths, excludeItemIds ?? Array.Empty<string>());
        foreach (var target in plan)
        {
            try
            {
                if (Directory.Exists(target.Payload))
                    Directory.Delete(target.Payload, recursive: true);
                else if (File.Exists(target.Payload))
                    File.Delete(target.Payload);
                else
                    continue;   // already gone — claim nothing
                if (target.Index is { } index) TryDelete(index);
                if (!purged.Contains(target.Original, StringComparer.OrdinalIgnoreCase))
                    purged.Add(target.Original);
            }
            catch (Exception) { /* one stubborn item never stops the rest */ }
        }
        return purged;
    }

    /// The pure purge decision (unit-tested; no filesystem): which payloads
    /// to delete, each paired with its $I metadata sibling.
    /// - only wanted original paths;
    /// - never an excluded payload identity (the pre-clean snapshot);
    /// - $RXXXXXX payload → $IXXXXXX sibling in the same directory; an
    ///   unrecognized payload name gets no sibling guess — better a stale
    ///   index than a wrong deletion.
    public static IReadOnlyList<(string Original, string Payload, string? Index)> PlanPurge(
        IReadOnlyList<BinEntry> entries, IReadOnlyList<string> wantedOriginals,
        IReadOnlyList<string> excludePayloadIds)
    {
        var wanted = new HashSet<string>(wantedOriginals, StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(excludePayloadIds, StringComparer.OrdinalIgnoreCase);
        var plan = new List<(string, string, string?)>();
        foreach (var entry in entries)
        {
            if (!wanted.Contains(entry.OriginalPath)) continue;
            if (excluded.Contains(entry.PayloadPath)) continue;
            plan.Add((entry.OriginalPath, entry.PayloadPath, IndexSiblingFor(entry.PayloadPath)));
        }
        return plan;
    }

    /// $R payload name → the paired $I index file next to it; null when the
    /// payload does not follow the $R convention.
    public static string? IndexSiblingFor(string payloadPath)
    {
        var name = Path.GetFileName(payloadPath);
        if (!name.StartsWith("$R", StringComparison.OrdinalIgnoreCase)) return null;
        var dir = Path.GetDirectoryName(payloadPath);
        if (string.IsNullOrEmpty(dir)) return null;
        return Path.Combine(dir, "$I" + name[2..]);
    }

    /// The original path stored in a $I metadata file, or null when the
    /// bytes are not a $I record we understand. Format (stable since
    /// Vista): int64 version (1 or 2), int64 size, int64 delete-FILETIME,
    /// then the original path — version 1 as a fixed 260-char block,
    /// version 2 as int32 char-count (terminator included) + chars.
    public static string? ParseIndexFile(byte[] bytes)
    {
        try
        {
            if (bytes.Length < 24) return null;
            var version = BitConverter.ToInt64(bytes, 0);
            if (version == 1)
            {
                if (bytes.Length < 24 + 520) return null;
                var raw = Encoding.Unicode.GetString(bytes, 24, 520);
                var end = raw.IndexOf('\0');
                var path = end >= 0 ? raw[..end] : raw;
                return path.Length == 0 ? null : path;
            }
            if (version == 2)
            {
                if (bytes.Length < 28) return null;
                var chars = BitConverter.ToInt32(bytes, 24);
                if (chars <= 0 || chars > 32_768) return null;
                if (bytes.Length < 28 + chars * 2) return null;
                var path = Encoding.Unicode.GetString(bytes, 28, chars * 2).TrimEnd('\0');
                return path.Length == 0 ? null : path;
            }
            return null;
        }
        catch (Exception) { return null; }
    }

    public void OpenRecycleBinUi()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder")
            { UseShellExecute = true });
        }
        catch (Exception) { /* UI nicety only */ }
    }

    private readonly record struct BinRecord(string Original, string Payload, string Index);

    /// Every bin entry matching the wanted originals, read straight from
    /// the per-volume, per-user bin directories' $I metadata. Materialized
    /// before the caller mutates anything.
    private static List<BinRecord> CollectRecords(IReadOnlyList<string> originalPaths)
    {
        var records = new List<BinRecord>();
        var wanted = new HashSet<string>(originalPaths, StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return records;
        foreach (var binDir in BinDirsFor(originalPaths))
        {
            IEnumerable<string> indexFiles;
            try
            {
                indexFiles = Directory.EnumerateFiles(binDir, "$I*").ToList();
            }
            catch (Exception) { continue; }
            // Round 14: every clean reads EVERY $I record in the bin, twice
            // (once for the pre-clean snapshot, once for the purge — they
            // observe different bin states, so neither can be skipped). At
            // the 5,732 entries on the 2026-08-18 machine that is ~500 ms of
            // pure small-file reads per pass, spent inside the window the
            // user sees as frozen. The reads are independent, so they go
            // wide; matching stays exact and order was never guaranteed
            // (same-path duplicates have always been arbitrary).
            var found = new ConcurrentBag<BinRecord>();
            Parallel.ForEach(indexFiles, indexFile =>
            {
                try
                {
                    var original = ParseIndexFile(File.ReadAllBytes(indexFile));
                    if (original is null || !wanted.Contains(original)) return;
                    var payload = Path.Combine(Path.GetDirectoryName(indexFile)!,
                        "$R" + Path.GetFileName(indexFile)[2..]);
                    found.Add(new BinRecord(original, payload, indexFile));
                }
                catch (Exception) { /* unreadable record — not a match */ }
            });
            records.AddRange(found);
        }
        return records;
    }

    /// The $Recycle.Bin\<user SID> directory of every volume the wanted
    /// paths live on (deletions land in the bin of their own volume).
    private static IEnumerable<string> BinDirsFor(IReadOnlyList<string> originalPaths)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (sid is null) yield break;
        var roots = originalPaths
            .Select(Path.GetPathRoot)
            .Where(r => !string.IsNullOrEmpty(r))
            .Select(r => r!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var binDir = Path.Combine(root, "$Recycle.Bin", sid);
            if (Directory.Exists(binDir)) yield return binDir;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception) { /* a stale index is cosmetic */ }
    }
}
