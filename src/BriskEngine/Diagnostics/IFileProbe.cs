using System;
using System.Collections.Generic;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics;

public interface IFileProbe
{
    bool FileExists(string path);
    string? ReadAllText(string path);                  // null when missing/unreadable
    void WriteAllText(string path, string content);
    IReadOnlyList<string> ListFiles(string directory); // empty when missing
    long DirectorySizeBytes(string path);              // delegates to SizeCalculator
    /// The same walk, keeping the `take` largest files at or over
    /// `minFileBytes` as well as the total. Callers go through
    /// FileStats.Of rather than here, so one scan walks a folder once.
    DirectoryStats DirectoryStats(string path, long minFileBytes, int take);
    DateTime? NewestWriteUtc(string path, int limit = 1500); // bounded deep walk
}
