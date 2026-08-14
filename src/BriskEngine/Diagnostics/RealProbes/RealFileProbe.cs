using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Cleaning;

namespace BriskEngine.Diagnostics.RealProbes;

public sealed class RealFileProbe : IFileProbe
{
    public bool FileExists(string path) => File.Exists(path);

    public string? ReadAllText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    public void WriteAllText(string path, string content)
    {
        // Ensure directory exists
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, content);
    }

    public IReadOnlyList<string> ListFiles(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return Array.Empty<string>();
            return Directory.GetFiles(directory);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public long DirectorySizeBytes(string path) => SizeCalculator.SizeOf(path);

    public DateTime? NewestWriteUtc(string path, int limit = 1500)
    {
        try
        {
            if (!Directory.Exists(path))
                return null;

            var di = new DirectoryInfo(path);
            DateTime? newest = null;
            int count = 0;

            foreach (var entry in di.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            {
                if (count >= limit)
                    break;

                // Skip reparse points (junctions, symlinks)
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                try
                {
                    var writeTime = entry.LastWriteTimeUtc;
                    if (newest is null || writeTime > newest)
                        newest = writeTime;
                    count++;
                }
                catch
                {
                    // Tolerate individual entry errors
                }
            }

            return newest;
        }
        catch
        {
            return null;
        }
    }
}
