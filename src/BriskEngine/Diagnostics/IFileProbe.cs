using System;
using System.Collections.Generic;

namespace BriskEngine.Diagnostics;

public interface IFileProbe
{
    bool FileExists(string path);
    string? ReadAllText(string path);                  // null when missing/unreadable
    void WriteAllText(string path, string content);
    IReadOnlyList<string> ListFiles(string directory); // empty when missing
    long DirectorySizeBytes(string path);              // delegates to SizeCalculator
    DateTime? NewestWriteUtc(string path, int limit = 1500); // bounded deep walk
}
