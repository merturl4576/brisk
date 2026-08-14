using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BriskEngine.Safety;

public static class ProtectedPaths
{
    /// Folders brisk must never delete from, not even via a covering template.
    public static IReadOnlyList<string> Roots()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var roots = new List<string?>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetEnvironmentVariable("OneDrive"),
            Path.Combine(windows, "System32"),
            Path.Combine(windows, "WinSxS"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };
        return roots.Where(r => !string.IsNullOrEmpty(r)).Select(r => r!).ToList();
    }

    public static bool IsProtected(string realPath)
    {
        foreach (var root in Roots())
        {
            var rootReal = RealPath.Resolve(root);
            if (string.Equals(realPath, rootReal, StringComparison.OrdinalIgnoreCase)) return true;
            if (realPath.StartsWith(rootReal + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
