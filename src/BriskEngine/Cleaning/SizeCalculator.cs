using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BriskEngine.Models;

namespace BriskEngine.Cleaning;

public static class SizeCalculator
{
    /// Tolerant recursive size. Skips unreadable entries and never traverses
    /// reparse points (a junction inside a cache must not count — or delete —
    /// what it points to).
    ///
    /// It delegates to StatsOf with a floor nothing can clear, so there is
    /// exactly ONE traversal in this file. The two used to be separate and
    /// the second one drifting from the first is precisely what a folder
    /// total that disagreed with the files named under it would look like.
    public static long SizeOf(string path, CancellationToken ct = default) =>
        StatsOf(path, minFileBytes: long.MaxValue, take: 0, ct).Bytes;

    /// The same walk SizeOf does, keeping the `take` largest files at or over
    /// `minFileBytes` on the way past — sorted biggest first, with each
    /// file's LastWriteTimeUtc. Same refusals as the size walk: a reparse
    /// point is never traversed, and a directory that refuses to enumerate
    /// costs that branch rather than the answer.
    public static DirectoryStats StatsOf(string path, long minFileBytes, int take,
        CancellationToken ct = default)
    {
        var largest = take > 0 ? new List<LargeFile>() : null;

        // Check the path's own attributes first — if it's a reparse point, never traverse it
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReparsePoint) != 0) return Empty;
        }
        catch { return Empty; }

        if (File.Exists(path))
        {
            try
            {
                var file = new FileInfo(path);
                Keep(largest, file, minFileBytes);
                return Done(file.Length, largest, take);
            }
            catch { return Empty; }
        }
        if (!Directory.Exists(path)) return Empty;
        var total = SizeOfDirectory(new DirectoryInfo(path), largest, minFileBytes, ct);
        return Done(total, largest, take);
    }

    private static readonly DirectoryStats Empty =
        new(0, Array.Empty<LargeFile>());

    /// The kept files, ordered and cut. Ordering here rather than as they
    /// arrive is what keeps the walk itself a straight sum: the list is
    /// bounded by how many files clear the floor, which on a real profile is
    /// a handful, not the thousands the walk passes.
    private static DirectoryStats Done(long total, List<LargeFile>? largest, int take)
    {
        if (largest is null || largest.Count == 0)
            return new DirectoryStats(total, Array.Empty<LargeFile>());
        largest.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        if (largest.Count > take) largest.RemoveRange(take, largest.Count - take);
        return new DirectoryStats(total, largest);
    }

    private static void Keep(List<LargeFile>? largest, FileInfo file, long minFileBytes)
    {
        if (largest is null || file.Length < minFileBytes) return;
        // A file whose write time will not read costs the ENTRY, not the
        // walk — the same trade every other refusal in this file makes.
        try { largest.Add(new LargeFile(file.FullName, file.Length, file.LastWriteTimeUtc)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static long SizeOfDirectory(DirectoryInfo dir, List<LargeFile>? largest,
        long minFileBytes, CancellationToken ct)
    {
        long total = 0;
        FileSystemInfo[] entries;
        try { entries = dir.GetFileSystemInfos(); }
        catch (UnauthorizedAccessException) { return 0; }
        catch (IOException) { return 0; }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            switch (entry)
            {
                case FileInfo f:
                    total += f.Length;
                    Keep(largest, f, minFileBytes);
                    break;
                case DirectoryInfo d:
                    total += SizeOfDirectory(d, largest, minFileBytes, ct);
                    break;
            }
        }
        return total;
    }
}
