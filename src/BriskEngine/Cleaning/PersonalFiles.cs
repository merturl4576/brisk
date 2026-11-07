using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BriskEngine.Paths;

namespace BriskEngine.Cleaning;

/// One of the user's own big files: Name is the file or folder name, Bytes
/// its size as Explorer shows it (a folder's is the sum of its files).
public sealed record PersonalFile(string Name, string Path, long Bytes);

/// The user's own large files (monitor GUI wave): VM disks, ISOs, Android
/// emulators, BlueStacks. The Space page lists them under "Senin
/// dosyaların · brisk dokunmaz": locked, never counted toward any GB number,
/// never deletable. This is a read-only walk; nothing here writes.
///
/// Listed at MinBytes or more, largest first:
/// - .vdi / .vhdx / .vmdk anywhere under the roots, except the system's own
///   stores: WSL (%LOCALAPPDATA%\Packages\*\LocalState\ext4.vhdx) and Docker
///   (%LOCALAPPDATA%\Docker\wsl\**);
/// - .iso;
/// - each Android emulator, ~\.android\avd\*.avd, as one entry;
/// - BlueStacks, %ProgramData%\BlueStacks_nxt, as one entry.
public static class PersonalFiles
{
    /// 1 GiB, inclusive: a file of exactly this size is listed.
    public const long MinBytes = 1L << 30;

    private static readonly string[] DiskExtensions = { ".vdi", ".vhdx", ".vmdk", ".iso" };

    /// The places the rules name. Find(roots, ct) uses this machine's; tests
    /// hand in temp folders, so no test walks the real profile.
    internal sealed record Places(string? LocalAppData, string? AvdHome, string? BlueStacks)
    {
        internal static Places OfThisMachine()
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new Places(
                PathExpander.Expand("%LOCALAPPDATA%"),
                string.IsNullOrEmpty(profile) ? null : Path.Combine(profile, ".android", "avd"),
                PathExpander.Expand(@"%ProgramData%\BlueStacks_nxt"));
        }

        internal bool IsBlueStacks(string dir) => Same(dir, BlueStacks);

        internal bool IsAvdHome(string dir) => Same(dir, AvdHome);

        /// %LOCALAPPDATA%\Docker\wsl and everything under it.
        internal bool InDockerStore(string path) =>
            LocalAppData is not null && IsAtOrUnder(path, Path.Combine(LocalAppData, "Docker", "wsl"));

        /// %LOCALAPPDATA%\Packages\<package>\LocalState\ext4.vhdx: a WSL
        /// distro installed from the Store.
        internal bool IsWslDisk(string file)
        {
            if (LocalAppData is null
                || !string.Equals(Path.GetFileName(file), "ext4.vhdx", StringComparison.OrdinalIgnoreCase))
                return false;
            var localState = Path.GetDirectoryName(file);
            var package = Path.GetDirectoryName(localState);
            return string.Equals(Path.GetFileName(localState), "LocalState", StringComparison.OrdinalIgnoreCase)
                && Same(Path.GetDirectoryName(package), Path.Combine(LocalAppData, "Packages"));
        }
    }

    /// The folders the user keeps big files in, plus the emulator homes, as
    /// they exist on this machine. Downloads has no SpecialFolder; it is
    /// spelled %USERPROFILE%\Downloads as everywhere else in the engine. A
    /// root that is a junction is left out: the walk never follows one.
    public static IReadOnlyList<string> DefaultRoots()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var places = Places.OfThisMachine();
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            string.IsNullOrEmpty(profile) ? null : Path.Combine(profile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            string.IsNullOrEmpty(profile) ? null : Path.Combine(profile, "VirtualBox VMs"),
            places.AvdHome,
            places.BlueStacks,
        };
        return roots.Where(r => !string.IsNullOrEmpty(r)).Select(r => r!)
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(IsRealDirectory).ToList();
    }

    public static IReadOnlyList<PersonalFile> Find(IReadOnlyList<string> roots, CancellationToken ct) =>
        Find(roots, Places.OfThisMachine(), ct);

    /// Walks every root. A junction or symlink is never followed, a folder
    /// that refuses to list costs that branch only, and the Docker store is
    /// never entered. BlueStacks and each AVD are sized as a whole and never
    /// walked for files of their own. Roots may overlap: an entry is listed
    /// once per full path.
    ///
    /// A node_modules folder is never entered either, as in DynamicTargets: no
    /// VM disk or ISO lives in one, and on a developer's Desktop they are
    /// most of the walk (on the machine this was built on, Desktop took 11 s
    /// with them and 3.5 s without).
    internal static IReadOnlyList<PersonalFile> Find(IReadOnlyList<string> roots, Places places,
        CancellationToken ct)
    {
        var found = new Dictionary<string, PersonalFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Select(FullPath).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsRealDirectory(root)) continue;
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var dir = pending.Pop();
                if (places.InDockerStore(dir)) continue;
                if (places.IsBlueStacks(dir))
                {
                    AddFolder(found, dir, ct);
                    continue;
                }
                var avdHome = places.IsAvdHome(dir);
                foreach (var entry in Entries(dir))
                {
                    ct.ThrowIfCancellationRequested();
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (entry is DirectoryInfo folder)
                    {
                        if (avdHome && folder.Name.EndsWith(".avd", StringComparison.OrdinalIgnoreCase))
                            AddFolder(found, folder.FullName, ct);
                        else if (!IsNodeModules(folder.Name))
                            pending.Push(folder.FullName);
                    }
                    else if (entry is FileInfo file && IsDiskImage(file.Name) && !places.IsWslDisk(file.FullName))
                    {
                        Add(found, file.FullName, file.Length);
                    }
                }
            }
        }
        return found.Values
            .OrderByDescending(f => f.Bytes)
            .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddFolder(Dictionary<string, PersonalFile> found, string dir, CancellationToken ct) =>
        Add(found, dir, SizeCalculator.SizeOf(dir, ct));

    private static void Add(Dictionary<string, PersonalFile> found, string path, long bytes)
    {
        if (bytes >= MinBytes) found.TryAdd(path, new PersonalFile(Path.GetFileName(path), path, bytes));
    }

    private static bool IsNodeModules(string name) =>
        string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase);

    private static bool IsDiskImage(string name) =>
        DiskExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    private static string? FullPath(string root)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { return null; }
    }

    private static bool Same(string? path, string? other) =>
        path is not null && other is not null
        && string.Equals(Path.TrimEndingDirectorySeparator(path),
            Path.TrimEndingDirectorySeparator(other), StringComparison.OrdinalIgnoreCase);

    private static bool IsAtOrUnder(string path, string dir)
    {
        if (Same(path, dir)) return true;
        var prefix = Path.EndsInDirectorySeparator(dir) ? dir : dir + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

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
