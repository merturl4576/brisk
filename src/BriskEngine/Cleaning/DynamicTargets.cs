using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace BriskEngine.Cleaning;

/// Targets found by walking the user's project folders, not by path
/// templates (monitor GUI wave). Find is the walk the scanner uses; Matches
/// is the same rule for one path, which SafetyValidator asks again on the
/// real path at delete time — the scan's verdict is never trusted alone.
///
/// stale-project-deps: a node_modules or .next folder beside a package.json,
/// or a Python venv (a folder holding pyvenv.cfg), in a project whose other
/// top-level entries were all last written 90 days or more ago.
/// archive-beside-extracted: a .7z/.zip/.rar of 1 GiB or more with a sibling
/// folder named as its stem that holds at least as many bytes. The archive
/// is the item; the extracted copy is never touched.
public static class DynamicTargets
{
    public const long MinArchiveBytes = 1L << 30;

    private const string StaleProjectDepsId = "stale-project-deps";
    private const string ArchiveBesideExtractedId = "archive-beside-extracted";
    private const int StaleDays = 90;

    /// How far below a root the walk looks: Desktop\group\project\node_modules
    /// is level 4.
    private const int MaxDepth = 4;

    private static readonly string[] RootNames =
        { "Desktop", "Documents", "Downloads", @"source\repos", "Projects", "dev", "code", "repos" };

    private static readonly string[] NodeFolderNames = { "node_modules", ".next" };
    private static readonly string[] ArchiveExtensions = { ".7z", ".zip", ".rar" };

    /// What `python -m venv`, virtualenv and uv put at a venv's top level.
    /// A folder holding pyvenv.cfg AND anything else is a project made into
    /// its own venv (`python -m venv .`): the user's code lives there.
    private static readonly HashSet<string> VenvEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        "pyvenv.cfg", "Lib", "lib64", "Scripts", "bin", "Include", "share", "etc",
        ".gitignore", "CACHEDIR.TAG", ".lock", ".project", "pip-selfcheck.json", "selfcheck.json",
    };

    /// The user's project folders that exist on this machine. A root that is
    /// a junction is left out: the walk never follows one.
    public static IReadOnlyList<string> DefaultRoots()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile)) return Array.Empty<string>();
        return RootNames.Select(name => Path.Combine(profile, name)).Where(IsRealDirectory).ToList();
    }

    /// Walks each root at most MaxDepth levels down. A junction or symlink is
    /// never followed, and a dependency folder is never entered, matched or
    /// not: npm unpacks packages with their tarball's 1985 timestamps, so
    /// anything inside one would look stale.
    public static IEnumerable<string> Find(string targetId, IReadOnlyList<string> roots, DateTime nowUtc,
        CancellationToken ct)
    {
        var found = new List<string>();
        if (targetId is not (StaleProjectDepsId or ArchiveBesideExtractedId)) return found;
        var deps = targetId == StaleProjectDepsId;

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsRealDirectory(root)) continue;
            var pending = new Stack<(string Dir, int Depth)>();
            pending.Push((root, 0));
            while (pending.Count > 0)
            {
                var (dir, depth) = pending.Pop();
                foreach (var entry in Entries(dir))
                {
                    ct.ThrowIfCancellationRequested();
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (entry is not DirectoryInfo folder)
                    {
                        if (!deps && Matches(targetId, entry.FullName, nowUtc, ct)) found.Add(entry.FullName);
                        continue;
                    }
                    if (IsDependencyFolder(folder))
                    {
                        if (deps && Matches(targetId, folder.FullName, nowUtc, ct)) found.Add(folder.FullName);
                        continue;
                    }
                    if (depth + 1 < MaxDepth) pending.Push((folder.FullName, depth + 1));
                }
            }
        }
        return found;
    }

    /// The rule for one path, read from the disk as it is now.
    public static bool Matches(string targetId, string realPath, DateTime nowUtc) =>
        Matches(targetId, realPath, nowUtc, CancellationToken.None);

    private static bool Matches(string targetId, string path, DateTime nowUtc, CancellationToken ct)
    {
        try
        {
            path = Path.TrimEndingDirectorySeparator(path);
            return targetId switch
            {
                StaleProjectDepsId => IsStaleDependencyFolder(path, nowUtc),
                ArchiveBesideExtractedId => IsArchiveBesideExtracted(path, ct),
                _ => false,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool IsStaleDependencyFolder(string dir, DateTime nowUtc)
    {
        if (!IsRealDirectory(dir)) return false;
        var project = Path.GetDirectoryName(dir);
        if (project is null || InsideNodeFolder(project)) return false;

        var name = Path.GetFileName(dir);
        var shaped = IsNodeFolderName(name)
            ? File.Exists(Path.Combine(project, "package.json"))
            : IsVenv(new DirectoryInfo(dir));
        return shaped && LastTouchedUtc(project, except: name) is { } touched
            && touched <= nowUtc.AddDays(-StaleDays);
    }

    /// The newest last write among the project's top-level entries, the
    /// dependency folder aside: installing packages is not working on the
    /// project. Null when nothing else is there — an unknown age is never stale.
    private static DateTime? LastTouchedUtc(string project, string except)
    {
        DateTime? newest = null;
        foreach (var entry in new DirectoryInfo(project).EnumerateFileSystemInfos())
        {
            if (string.Equals(entry.Name, except, StringComparison.OrdinalIgnoreCase)) continue;
            if (newest is null || entry.LastWriteTimeUtc > newest) newest = entry.LastWriteTimeUtc;
        }
        return newest;
    }

    private static bool IsArchiveBesideExtracted(string file, CancellationToken ct)
    {
        if (!ArchiveExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) return false;
        var archive = new FileInfo(file);
        if (!archive.Exists || (archive.Attributes & FileAttributes.ReparsePoint) != 0
            || archive.Length < MinArchiveBytes || archive.DirectoryName is not { } parent) return false;

        var extracted = Path.Combine(parent, Path.GetFileNameWithoutExtension(file));
        return IsRealDirectory(extracted) && SizeCalculator.SizeOf(extracted, ct) >= archive.Length;
    }

    /// The shape alone, stale or not: what the walk never enters.
    private static bool IsDependencyFolder(DirectoryInfo folder)
    {
        try { return IsNodeFolderName(folder.Name) || IsVenv(folder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool IsVenv(DirectoryInfo folder) =>
        File.Exists(Path.Combine(folder.FullName, "pyvenv.cfg"))
        && folder.EnumerateFileSystemInfos().All(e => VenvEntries.Contains(e.Name));

    private static bool IsNodeFolderName(string name) =>
        NodeFolderNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// A package's own node_modules sits inside another node_modules; the
    /// walk never goes there, and neither does the rule.
    private static bool InsideNodeFolder(string path) =>
        path.Split(Path.DirectorySeparatorChar).Any(IsNodeFolderName);

    private static FileSystemInfo[] Entries(string dir)
    {
        try { return new DirectoryInfo(dir).GetFileSystemInfos(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<FileSystemInfo>();
        }
    }

    private static bool IsRealDirectory(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) != 0
                && (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
