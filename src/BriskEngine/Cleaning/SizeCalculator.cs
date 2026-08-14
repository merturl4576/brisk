using System;
using System.IO;
using System.Threading;

namespace BriskEngine.Cleaning;

public static class SizeCalculator
{
    /// Tolerant recursive size. Skips unreadable entries and never traverses
    /// reparse points (a junction inside a cache must not count — or delete —
    /// what it points to).
    public static long SizeOf(string path, CancellationToken ct = default)
    {
        // Check the path's own attributes first — if it's a reparse point, never traverse it
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReparsePoint) != 0) return 0;
        }
        catch { return 0; }

        if (File.Exists(path))
        {
            try { return new FileInfo(path).Length; }
            catch { return 0; }
        }
        if (!Directory.Exists(path)) return 0;
        return SizeOfDirectory(new DirectoryInfo(path), ct);
    }

    private static long SizeOfDirectory(DirectoryInfo dir, CancellationToken ct)
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
            total += entry switch
            {
                FileInfo f => f.Length,
                DirectoryInfo d => SizeOfDirectory(d, ct),
                _ => 0
            };
        }
        return total;
    }
}
