using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using BriskEngine.Cleaning;
using Xunit;

namespace BriskEngine.Tests;

/// Monitor GUI wave: two targets found by walking project folders, not by
/// path templates. Every fixture sits in a temp folder handed in as the only
/// root, with nowUtc injected: no test walks the real profile.
public sealed class DynamicTargetsTests : IDisposable
{
    private const string Deps = "stale-project-deps";
    private const string Archives = "archive-beside-extracted";
    private const long GiB = DynamicTargets.MinArchiveBytes;

    private readonly string _root = Directory.CreateTempSubdirectory("brisk-dynamic-").FullName;
    private readonly DateTime _now = DateTime.UtcNow;

    private List<string> Find(string id, params string[] roots) =>
        DynamicTargets.Find(id, roots.Length == 0 ? new[] { _root } : roots, _now, CancellationToken.None)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

    private string At(params string[] parts) => Path.Combine(parts.Prepend(_root).ToArray());

    private string FileAt(params string[] parts)
    {
        var path = At(parts);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[64]);
        return path;
    }

    private void Age(string path, int days)
    {
        if (Directory.Exists(path)) Directory.SetLastWriteTimeUtc(path, _now.AddDays(-days));
        else File.SetLastWriteTimeUtc(path, _now.AddDays(-days));
    }

    /// A Node project whose package.json was last written `days` ago. Its
    /// node_modules is created first and stays "just now": installing
    /// packages is not working on the project.
    private string NodeProject(int days, params string[] parts)
    {
        FileAt(parts.Append("node_modules").Append("left-pad").Append("index.js").ToArray());
        Age(FileAt(parts.Append("package.json").ToArray()), days);
        return At(parts.Append("node_modules").ToArray());
    }

    [Fact]
    public void Finds_stale_node_modules_with_package_json()
    {
        var modules = NodeProject(200, "app");
        FileAt("site", ".next", "cache", "build.bin");
        Age(FileAt("site", "package.json"), 200);

        Assert.Equal(new[] { modules, At("site", ".next") }, Find(Deps));
        Assert.True(DynamicTargets.Matches(Deps, modules, _now));
    }

    [Fact]
    public void Ignores_project_touched_within_90_days()
    {
        NodeProject(200, "readme-edited");
        Age(FileAt("readme-edited", "README.md"), 30);   // one recent top-level entry is enough
        NodeProject(89, "eighty-nine");
        var ninety = NodeProject(90, "ninety");          // 90 days is old enough

        Assert.Equal(new[] { ninety }, Find(Deps));
    }

    [Fact]
    public void Ignores_node_modules_without_package_json()
    {
        FileAt("loose", "node_modules", "x", "index.js");
        Age(FileAt("loose", "notes.txt"), 200);

        Assert.Empty(Find(Deps));
        Assert.False(DynamicTargets.Matches(Deps, At("loose", "node_modules"), _now));
    }

    [Fact]
    public void Finds_venv_by_pyvenv_cfg()
    {
        FileAt("tool", "env-3.12", "pyvenv.cfg");
        FileAt("tool", "env-3.12", "Lib", "site-packages", "requests", "__init__.py");
        FileAt("tool", "env-3.12", "Scripts", "python.exe");
        FileAt("tool", "env-3.12", ".gitignore");
        Age(FileAt("tool", "main.py"), 120);

        Assert.Equal(new[] { At("tool", "env-3.12") }, Find(Deps));
    }

    /// `python -m venv .` inside a project puts pyvenv.cfg next to the
    /// user's own code: that folder is the project, never a dependency folder.
    [Fact]
    public void Ignores_a_project_that_is_its_own_venv()
    {
        FileAt("courses", "thesis", "pyvenv.cfg");
        FileAt("courses", "thesis", "Lib", "site-packages", "numpy", "__init__.py");
        FileAt("courses", "thesis", "Scripts", "python.exe");
        FileAt("courses", "thesis", "thesis.py");
        Age(FileAt("courses", "notes.txt"), 200);

        Assert.Empty(Find(Deps));
        Assert.False(DynamicTargets.Matches(Deps, At("courses", "thesis"), _now));
    }

    [JunctionFact]
    public void Never_follows_a_junction()
    {
        var walked = Directory.CreateDirectory(At("walked")).FullName;
        var outsideProject = Path.GetDirectoryName(NodeProject(200, "outside", "proj"))!;
        Assert.True(Junction(At("walked", "shortcut"), outsideProject));

        // A stale project whose node_modules is itself a junction.
        Age(FileAt("walked", "app", "package.json"), 200);
        FileAt("outside", "modules", "x", "index.js");
        Assert.True(Junction(At("walked", "app", "node_modules"), At("outside", "modules")));

        Assert.Empty(Find(Deps, walked));
        Assert.False(DynamicTargets.Matches(Deps, At("walked", "app", "node_modules"), _now));
    }

    /// npm unpacks every package with its tarball's 1985 timestamps, so a
    /// package inside node_modules looks ancient even in a project used today.
    [Fact]
    public void Never_descends_into_a_dependency_folder()
    {
        NodeProject(1, "live");
        Age(FileAt("live", "node_modules", "pkg", "package.json"), 365 * 40);
        FileAt("live", "node_modules", "pkg", "node_modules", "dep", "index.js");
        var stale = NodeProject(200, "old");
        Age(FileAt("old", "node_modules", "pkg", "package.json"), 365 * 40);
        FileAt("old", "node_modules", "pkg", "node_modules", "dep", "index.js");

        Assert.Equal(new[] { stale }, Find(Deps));
        Assert.False(DynamicTargets.Matches(Deps, At("live", "node_modules", "pkg", "node_modules"), _now));
    }

    [Fact]
    public void Walks_four_levels_below_a_root()
    {
        var fourth = NodeProject(200, "a", "b", "proj");   // node_modules is level 4
        NodeProject(200, "a", "b", "c", "proj");           // level 5: out of reach

        Assert.Equal(new[] { fourth }, Find(Deps));
    }

    [SparseFact]
    public void Finds_archive_only_with_bigger_sibling_folder()
    {
        var kali = Sparse(At("Kali.7z"), GiB + 10);
        Sparse(At("kali", "Kali.vdi"), 3 * GiB);           // extracted beside it, name differs in case
        Sparse(At("tiny.zip"), GiB - 1);                   // under 1 GiB
        Sparse(At("tiny", "data.bin"), 2 * GiB);
        Sparse(At("partial.rar"), GiB);                    // sibling holds less than the archive
        FileAt("partial", "readme.txt");
        Sparse(At("alone.zip"), 2 * GiB);                  // no sibling folder
        Sparse(At("disk.iso"), 2 * GiB);                   // not an archive
        Sparse(At("disk", "disk.bin"), 3 * GiB);

        Assert.Equal(new[] { kali }, Find(Archives));
        Assert.True(DynamicTargets.Matches(Archives, kali, _now));
        Assert.False(DynamicTargets.Matches(Archives, At("kali"), _now));   // the copy is never the item
    }

    /// Only the folders that exist, all under the user profile, none of
    /// them a junction. Read-only: nothing is walked.
    [Fact]
    public void DefaultRoots_are_existing_project_folders_in_the_profile()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var names = new[] { "Desktop", "Documents", "Downloads", @"source\repos", "Projects", "dev", "code", "repos" };
        foreach (var root in DynamicTargets.DefaultRoots())
        {
            Assert.Contains(Path.GetRelativePath(profile, root), names, StringComparer.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(root), root);
            Assert.Equal(0, (int)(File.GetAttributes(root) & FileAttributes.ReparsePoint));
        }
    }

    /// A file of `bytes` logical size that takes (almost) no disk. SparseFile
    /// checks the sparse mark and refuses to write real gigabytes without it.
    internal static string Sparse(string path, long bytes) => SparseFile.Create(path, bytes);

    /// Junctions need no admin rights; mklink is a cmd builtin.
    internal static bool Junction(string link, string target)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        return p.ExitCode == 0;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}

/// A test that needs a junction. xunit 2 decides skips at discovery, so the
/// attribute tries one in a scratch folder and skips, with the reason, on a
/// machine where `mklink /J` cannot make one.
public sealed class JunctionFactAttribute : FactAttribute
{
    public JunctionFactAttribute()
    {
        var scratch = Directory.CreateTempSubdirectory("brisk-junction-probe-").FullName;
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(scratch, "target")).FullName;
            if (!DynamicTargetsTests.Junction(Path.Combine(scratch, "link"), target))
                Skip = "mklink /J could not create a junction on this machine";
        }
        catch (Exception e)
        {
            Skip = $"mklink /J could not run: {e.Message}";
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }
    }
}
