using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BriskEngine.Cleaning;
using Xunit;

namespace BriskEngine.Tests;

/// Monitor GUI wave: the user's own big files (VM disks, ISOs, emulators),
/// which the Space page lists locked and never counts. Every fixture is a
/// sparse file in a temp folder, and the places the rules name (the WSL and
/// Docker stores, the AVD home, BlueStacks) are moved into that folder
/// through the seam: no test walks the real profile.
public sealed class PersonalFilesTests : IDisposable
{
    private const long GiB = PersonalFiles.MinBytes;

    private readonly string _root = Directory.CreateTempSubdirectory("brisk-personal-").FullName;

    private string At(params string[] parts) => Path.Combine(parts.Prepend(_root).ToArray());

    private PersonalFiles.Places Places => new(
        LocalAppData: At("LocalAppData"),
        AvdHome: At(".android", "avd"),
        BlueStacks: At("ProgramData", "BlueStacks_nxt"));

    private IReadOnlyList<PersonalFile> Find(params string[] roots) =>
        PersonalFiles.Find(roots, Places, CancellationToken.None);

    [SparseFact]
    public void Lists_vm_disk_and_iso_over_1_GiB()
    {
        var vdi = SparseFile.Create(At("VirtualBox VMs", "Kali", "Kali.vdi"), 24 * GiB);
        var vmdk = SparseFile.Create(At("Documents", "Virtual Machines", "Win11", "Win11.vmdk"), 3 * GiB);
        var vhdx = SparseFile.Create(At("Desktop", "lab", "Lab.VHDX"), 2 * GiB);   // extension in any case
        var iso = SparseFile.Create(At("Downloads", "Server.iso"), GiB);          // exactly 1 GiB is listed
        SparseFile.Create(At("Downloads", "film.mkv"), 5 * GiB);                   // big, but no disk image
        SparseFile.Create(At("Downloads", "Kali.7z"), 4 * GiB);                    // an archive is not his disk

        // The temp root holds every other root: each file is still listed once.
        var found = Find(At("VirtualBox VMs"), At("Documents"), At("Desktop"), At("Downloads"), _root);

        Assert.Equal(new[]
        {
            new PersonalFile("Kali.vdi", vdi, 24 * GiB),
            new PersonalFile("Win11.vmdk", vmdk, 3 * GiB),
            new PersonalFile("Lab.VHDX", vhdx, 2 * GiB),
            new PersonalFile("Server.iso", iso, GiB),
        }, found);
    }

    [SparseFact]
    public void Skips_wsl_and_docker_stores()
    {
        SparseFile.Create(At("LocalAppData", "Packages", "CanonicalGroupLimited.Ubuntu_79rhkp1fndgsc",
            "LocalState", "ext4.vhdx"), 8 * GiB);
        SparseFile.Create(At("LocalAppData", "Docker", "wsl", "disk", "docker_data.vhdx"), 20 * GiB);
        SparseFile.Create(At("LocalAppData", "Docker", "wsl", "main", "ext4.vhdx"), 2 * GiB);
        var own = SparseFile.Create(At("LocalAppData", "VMs", "dev.vhdx"), 2 * GiB);       // not a store
        var copy = SparseFile.Create(At("Documents", "backup", "ext4.vhdx"), 3 * GiB);    // a store's name, elsewhere

        Assert.Equal(new[] { copy, own }, Find(At("LocalAppData"), At("Documents")).Select(f => f.Path));
        Assert.Empty(Find(At("LocalAppData", "Docker", "wsl", "disk")));                  // a root inside a store
    }

    [SparseFact]
    public void Avd_folder_is_one_entry()
    {
        var pixel = At(".android", "avd", "Pixel_7_API_34.avd");
        SparseFile.Create(Path.Combine(pixel, "userdata-qemu.img"), 6 * GiB);
        SparseFile.Create(Path.Combine(pixel, "snapshots", "default_boot", "ram.img"), 2 * GiB);
        File.WriteAllText(At(".android", "avd", "Pixel_7_API_34.ini"), @"path.rel=avd\Pixel_7_API_34.avd");
        SparseFile.Create(At(".android", "avd", "Wear.avd", "userdata-qemu.img"), GiB - 1);   // under 1 GiB
        SparseFile.Create(At("Desktop", "copy.avd", "userdata-qemu.img"), 3 * GiB);          // not in the AVD home

        var expected = new[] { new PersonalFile("Pixel_7_API_34.avd", pixel, 8 * GiB) };
        Assert.Equal(expected, Find(At(".android", "avd"), At("Desktop")));
        Assert.Equal(expected, Find(_root));   // the AVD home reached by walking down to it
    }

    [SparseFact]
    public void Bluestacks_folder_is_one_entry()
    {
        var bluestacks = At("ProgramData", "BlueStacks_nxt");
        SparseFile.Create(Path.Combine(bluestacks, "Engine", "Pie64", "Data.vhdx"), 20 * GiB);
        SparseFile.Create(Path.Combine(bluestacks, "Engine", "Pie64", "Root.vhdx"), GiB);   // never its own row
        File.WriteAllBytes(Path.Combine(bluestacks, "bluestacks.conf"), new byte[64]);

        var expected = new[] { new PersonalFile("BlueStacks_nxt", bluestacks, 21 * GiB + 64) };
        Assert.Equal(expected, Find(bluestacks));
        Assert.Equal(expected, Find(At("ProgramData")));   // reached by walking down to it
    }

    [SparseFact]
    public void Below_1_GiB_is_ignored()
    {
        SparseFile.Create(At("Downloads", "netinst.iso"), GiB - 1);
        SparseFile.Create(At("VMs", "small.vdi"), GiB - 1);
        File.WriteAllBytes(At("VMs", "Win11.vmdk"), new byte[512]);   // a VMware descriptor
        SparseFile.Create(At("ProgramData", "BlueStacks_nxt", "Engine", "Data.vhdx"), GiB - 1);

        Assert.Empty(Find(_root));
    }

    /// Beyond the brief, for speed: node_modules is most of a developer's
    /// Desktop and never holds his disks.
    [SparseFact]
    public void Never_enters_node_modules()
    {
        SparseFile.Create(At("Desktop", "app", "node_modules", "fixture-pkg", "test.iso"), 2 * GiB);
        var iso = SparseFile.Create(At("Desktop", "app", "build", "app.iso"), 2 * GiB);

        Assert.Equal(new[] { iso }, Find(At("Desktop")).Select(f => f.Path));
    }

    [SparseJunctionFact]
    public void Never_follows_a_junction()
    {
        SparseFile.Create(At("elsewhere", "far.vdi"), 2 * GiB);
        var walked = Directory.CreateDirectory(At("walked")).FullName;
        var link = At("walked", "shortcut");
        Assert.True(DynamicTargetsTests.Junction(link, At("elsewhere")));
        try
        {
            Assert.Empty(Find(walked));
            Assert.Empty(Find(link));   // a root that is a junction is not walked either
        }
        finally
        {
            // The link on its own: a recursive delete of the temp root throws
            // on a junction and would leave the folder behind.
            Directory.Delete(link);
        }
    }

    [Fact]
    public void Stops_when_cancelled()
    {
        Directory.CreateDirectory(At("Downloads"));
        File.WriteAllBytes(At("Downloads", "a.iso"), new byte[64]);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => PersonalFiles.Find(new[] { _root }, Places, cancelled.Token));
    }

    /// Shape only, read-only: the roots exist, are real folders, and are the
    /// brief's folders; nothing under them is walked.
    [Fact]
    public void DefaultRoots_are_existing_user_and_emulator_folders()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var allowed = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(profile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Path.Combine(profile, "VirtualBox VMs"),
            Path.Combine(profile, ".android", "avd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BlueStacks_nxt"),
        };

        var roots = PersonalFiles.DefaultRoots();
        Assert.Equal(roots.Count, roots.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var root in roots)
        {
            Assert.Contains(root, allowed, StringComparer.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(root), root);
            Assert.Equal(0, (int)(File.GetAttributes(root) & FileAttributes.ReparsePoint));
        }
    }

    /// The places the public Find uses are the brief's: %LOCALAPPDATA%,
    /// ~\.android\avd and %ProgramData%\BlueStacks_nxt.
    [Fact]
    public void Real_places_are_the_named_locations()
    {
        var places = PersonalFiles.Places.OfThisMachine();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            places.LocalAppData, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(profile, ".android", "avd"), places.AvdHome, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "BlueStacks_nxt"), places.BlueStacks, StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
