using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Models;
using BriskEngine.Safety;
using Xunit;

namespace BriskEngine.Tests;

public sealed class ScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-scan-").FullName;
    private readonly FakeRunningApps _processes = new();

    private CleanupTarget Target(string id, string template, string? app = null,
        bool pick = false) => new(
        id, id, CleanupLevel.Safe, new List<string> { template }, "Test",
        RequiresAppClosedProcess: app, RequiresIndividualSelection: pick);

    private static CleanupTarget ContentsOnlyTarget(string id, string template) => new(
        id, id, CleanupLevel.Safe, new List<string> { template }, "Test",
        DeletesContentsNotDirectory: true);

    [Fact]
    public void ResolvesItemsWithSizes()
    {
        var dir = Path.Combine(_root, "cache");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "x.bin"), new byte[42]);
        var result = new Scanner(new[] { Target("t1", dir) }, _processes).Scan();
        var t = result.Targets.Single();
        Assert.Null(t.SkippedReason);
        Assert.Equal(42, t.TotalBytes);
    }

    [Fact]
    public void RunningApp_SkipsTarget_ButStillSizesIt()
    {
        var dir = Path.Combine(_root, "appcache");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "held.bin"), new byte[64]);
        _processes.Running.Add("chrome");
        var result = new Scanner(new[] { Target("t2", dir, app: "chrome") }, _processes).Scan();
        var t = result.Targets.Single();
        Assert.NotNull(t.SkippedReason);
        // Round 11: the skipped target is still SIZED (so the GUI can say
        // "+64 B when you close chrome") but promises NOTHING.
        Assert.Equal(64, t.TotalBytes);
        Assert.Equal(0, t.ReclaimableBytes);
        Assert.Equal(64, t.BlockedBytes);
    }

    /// REGRESSION PIN — the 2026-08-17 promise bug: modern WhatsApp Desktop
    /// runs as "WhatsApp.Root", the registry excluded on "WhatsApp" only, so
    /// the exclusion never fired and a locked 310 MB cache entered the
    /// promise. ANY '|'-separated candidate must count as running, and the
    /// skip reason must name the app in its human form.
    [Fact]
    public void RunningApp_SecondaryProcessName_AlsoSkips()
    {
        var dir = Path.Combine(_root, "whatsapp");
        Directory.CreateDirectory(dir);
        _processes.Running.Add("WhatsApp.Root");
        var result = new Scanner(
            new[] { Target("t-wa", dir, app: "WhatsApp|WhatsApp.Root") }, _processes).Scan();
        var t = result.Targets.Single();
        Assert.NotNull(t.SkippedReason);
        Assert.StartsWith("WhatsApp is running", t.SkippedReason);
        Assert.Equal(0, t.ReclaimableBytes);
    }

    /// ROUND 15 review (I1): the allowance is finite, so WHAT it is spent
    /// on decides the promise's error bound. Spent in walk order the
    /// unverified tail is arbitrary — and unverified counts as free, so the
    /// arbitrary tail IS the risk. Spent largest-first, whatever goes
    /// unverified is the smallest it can be in the only unit the promise is
    /// made in: bytes.
    [Fact]
    public void TheAllowance_IsSpentOnTheBiggestItemsFirst()
    {
        var dir = Path.Combine(_root, "order");
        Directory.CreateDirectory(dir);
        // Written smallest-first, and named so walk order is the OPPOSITE
        // of size order — walk order would probe 'a' first.
        File.WriteAllBytes(Path.Combine(dir, "a-small.bin"), new byte[10]);
        File.WriteAllBytes(Path.Combine(dir, "b-medium.bin"), new byte[500]);
        File.WriteAllBytes(Path.Combine(dir, "c-largest.bin"), new byte[9000]);
        var probe = new FakeLockProbe();

        new Scanner(new[] { ContentsOnlyTarget("t-order", dir) }, _processes, probe).Scan();

        Assert.Equal(
            new[] { "c-largest.bin", "b-medium.bin", "a-small.bin" },
            probe.Calls.Select(c => Path.GetFileName(c.Path)).ToArray());
    }

    /// Re-review minor 10: the "a skipped target is never probed" guard
    /// moved into the largest-first pass, and the round-11 test that looks
    /// like it covers this builds its Scanner WITHOUT a probe — so it tests
    /// the ScanModels short-circuit, not the guard. This tests the guard.
    [Fact]
    public void ASkippedTarget_IsNeverProbedAtAll()
    {
        var dir = Path.Combine(_root, "skipped-probe");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "held.bin"), new byte[64]);
        _processes.Running.Add("chrome");
        var probe = new FakeLockProbe();

        var result = new Scanner(
            new[] { Target("t-skip", dir, app: "chrome") }, _processes, probe).Scan();

        Assert.NotNull(result.Targets.Single().SkippedReason);
        Assert.Empty(probe.Calls);   // the whole target is already outside the promise
    }

    /// Round 11 honest total: a delete-locked item stays on the shelf (the
    /// clean still attempts it) but leaves the promise.
    [Fact]
    public void LockedItem_StaysInItems_ButLeavesThePromise()
    {
        var dir = Path.Combine(_root, "lockedcache");
        Directory.CreateDirectory(dir);
        var free = Path.Combine(dir, "free.bin");
        var held = Path.Combine(dir, "held.bin");
        File.WriteAllBytes(free, new byte[10]);
        File.WriteAllBytes(held, new byte[30]);
        var probe = new FakeLockProbe();
        probe.LockedPaths.Add(held);

        var result = new Scanner(
            new[] { ContentsOnlyTarget("t-lock", dir) }, _processes, probe).Scan();
        var t = result.Targets.Single();

        Assert.Equal(2, t.Items.Count);
        Assert.Equal(40, t.TotalBytes);
        Assert.Equal(10, t.ReclaimableBytes);
        Assert.Equal(30, t.BlockedBytes);
        Assert.True(t.Items.Single(i => i.Path == held).Locked);
        Assert.False(t.Items.Single(i => i.Path == free).Locked);
    }

    /// REVIEW ROUND 1 (I2): the probe budget's SCOPE is the target — every
    /// item of one target draws from the same allowance, and each target
    /// starts fresh. (A per-item budget let a 500-child temp dir spend
    /// 500 × 256 handle opens per scan.)
    [Fact]
    public void LockProbeBudget_IsSharedAcrossOneTargetsItems_FreshPerTarget()
    {
        var dir1 = Path.Combine(_root, "budget1");
        var dir2 = Path.Combine(_root, "budget2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        File.WriteAllBytes(Path.Combine(dir1, "a.bin"), new byte[1]);
        File.WriteAllBytes(Path.Combine(dir1, "b.bin"), new byte[1]);
        File.WriteAllBytes(Path.Combine(dir2, "c.bin"), new byte[1]);
        var probe = new FakeLockProbe();

        new Scanner(new[]
        {
            ContentsOnlyTarget("t-b1", dir1),
            ContentsOnlyTarget("t-b2", dir2),
        }, _processes, probe).Scan();

        var b1 = probe.Calls.Where(c => c.Path.StartsWith(dir1)).Select(c => c.Budget)
            .Distinct().ToList();
        var b2 = probe.Calls.Where(c => c.Path.StartsWith(dir2)).Select(c => c.Budget)
            .Distinct().ToList();
        Assert.Equal(2, probe.Calls.Count(c => c.Path.StartsWith(dir1)));
        Assert.Single(b1);            // one budget for both of target 1's items
        Assert.Single(b2);
        Assert.NotSame(b1[0], b2[0]); // and a fresh one for target 2
    }

    [Fact]
    public void OldInstallers_FiltersByAge()
    {
        var downloads = Path.Combine(_root, "Downloads");
        Directory.CreateDirectory(downloads);
        var old = Path.Combine(downloads, "old-setup.exe");
        var fresh = Path.Combine(downloads, "fresh-setup.exe");
        File.WriteAllBytes(old, new byte[10]);
        File.WriteAllBytes(fresh, new byte[10]);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-45));
        var target = Registered("old-installers") with
            { PathTemplates = new[] { Path.Combine(downloads, "*.exe") } };
        var result = new Scanner(new[] { target }, _processes).Scan();
        var item = result.Targets.Single().Items.Single();
        Assert.EndsWith("old-setup.exe", item.Path);
    }

    /// Monitor GUI wave: the age rule belongs to the target, not to
    /// old-installers alone — kernel dumps and setup logs carry 7 days.
    [Fact]
    public void Min_age_skips_young_files()
    {
        var dir = Path.Combine(_root, "dumps");
        Directory.CreateDirectory(dir);
        var young = Path.Combine(dir, "young.dmp");
        var old = Path.Combine(dir, "old.dmp");
        File.WriteAllBytes(young, new byte[10]);
        File.WriteAllBytes(old, new byte[20]);
        File.SetLastWriteTimeUtc(young, DateTime.UtcNow.AddDays(-1));
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-10));
        var target = new CleanupTarget("dumps", "Dumps", CleanupLevel.Deep,
            new List<string> { Path.Combine(dir, "*.dmp") }, "System", MinAgeDays: 7);

        var item = new Scanner(new[] { target }, _processes).Scan().Targets.Single().Items.Single();

        Assert.Equal(old, item.Path);
        Assert.Equal(20, item.Bytes);

        // the rule holds for a contents-only target's children too
        var contents = ContentsOnlyTarget("dump-dir", dir) with { MinAgeDays = 7 };
        var child = new Scanner(new[] { contents }, _processes).Scan().Targets.Single().Items.Single();
        Assert.Equal(old, child.Path);
    }

    /// While $WINDOWS.~BT exists a feature upgrade is under way and its
    /// Panther logs are still being written: the target yields nothing, and
    /// says nothing (a skip reason would read as "close an app").
    [Fact]
    public void Panther_target_is_empty_during_an_upgrade()
    {
        var panther = Path.Combine(_root, "Panther");
        Directory.CreateDirectory(panther);
        var log = Path.Combine(panther, "setupact.log");
        File.WriteAllBytes(log, new byte[50]);
        File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddDays(-30));
        var target = Registered("panther-logs") with
            { PathTemplates = new[] { Path.Combine(panther, "*.log") } };

        var upgrading = new Scanner(new[] { target }, _processes, upgradeInProgress: () => true)
            .Scan().Targets.Single();
        Assert.Empty(upgrading.Items);
        Assert.Null(upgrading.SkippedReason);

        var finished = new Scanner(new[] { target }, _processes, upgradeInProgress: () => false)
            .Scan().Targets.Single();
        Assert.Equal(log, finished.Items.Single().Path);
    }

    /// ~\.git goes as one folder. A .git FILE (a worktree's pointer) is not
    /// a repository, and a junction named .git frees nothing when removed.
    [Fact]
    public void Profile_git_is_offered_only_as_a_real_folder()
    {
        var home = Path.Combine(_root, "home");
        var git = Path.Combine(home, ".git");
        Directory.CreateDirectory(Path.Combine(git, "objects"));
        File.WriteAllBytes(Path.Combine(git, "objects", "pack"), new byte[70]);
        var item = new Scanner(new[] { ProfileGitAt(git) }, _processes).Scan().Targets.Single().Items.Single();
        Assert.Equal(git, item.Path);   // the folder itself, not its contents
        Assert.Equal(70, item.Bytes);

        var worktree = Path.Combine(_root, "home-file", ".git");
        Directory.CreateDirectory(Path.GetDirectoryName(worktree)!);
        File.WriteAllText(worktree, "gitdir: D:/elsewhere/.git/worktrees/x");
        Assert.Empty(new Scanner(new[] { ProfileGitAt(worktree) }, _processes).Scan().Targets.Single().Items);

        var linked = Path.Combine(_root, "home-link", ".git");
        Directory.CreateDirectory(Path.GetDirectoryName(linked)!);
        var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linked}\" \"{git}\"")
        { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        Assert.Empty(new Scanner(new[] { ProfileGitAt(linked) }, _processes).Scan().Targets.Single().Items);
    }

    /// A dynamic target has no templates: its items come from its rule's
    /// walk over the roots the scanner was given (the real ones are the
    /// user's Desktop, Documents and project folders).
    [Fact]
    public void Dynamic_target_items_come_from_the_walk()
    {
        var projects = Path.Combine(_root, "projects");
        var modules = Directory.CreateDirectory(Path.Combine(projects, "app", "node_modules")).FullName;
        File.WriteAllBytes(Path.Combine(modules, "index.js"), new byte[30]);
        var manifest = Path.Combine(projects, "app", "package.json");
        File.WriteAllText(manifest, "{}");
        File.SetLastWriteTimeUtc(manifest, DateTime.UtcNow.AddDays(-200));

        var item = new Scanner(new[] { Registered("stale-project-deps") }, _processes,
            dynamicRoots: new[] { projects }).Scan().Targets.Single().Items.Single();

        Assert.Equal(modules, item.Path);
        Assert.Equal(30, item.Bytes);
    }

    private static CleanupTarget Registered(string id) => CleanupTargetRegistry.All.Single(t => t.Id == id);

    /// The registered rule with "~" moved to a temp home: the real ~\.git is
    /// a live repository and no test may touch it.
    private static CleanupTarget ProfileGitAt(string git) =>
        Registered("profile-git") with { PathTemplates = new[] { git } };

    [Fact]
    public void VanishedFile_DoesNotKillScan()
    {
        var testdir = Path.Combine(_root, "testdir");
        Directory.CreateDirectory(testdir);
        File.WriteAllBytes(Path.Combine(testdir, "good.bin"), new byte[42]);

        // Create a dangling junction: point to a directory, then delete it
        var tempdir = Path.Combine(_root, "tempdir");
        Directory.CreateDirectory(tempdir);
        var junction = Path.Combine(testdir, "dangling");
        var p = Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c mklink /J \"{junction}\" \"{tempdir}\"")
        { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        Directory.Delete(tempdir);

        // Scan should complete without throwing, counting only the good file
        var result = new Scanner(new[] { Target("t1", testdir) }, _processes).Scan();
        var t = result.Targets.Single();
        Assert.Null(t.SkippedReason);
        Assert.Equal(42, t.TotalBytes);
    }

    [Fact]
    public void ContentsOnlyTarget_EmitsChildrenNotTheDirectory()
    {
        var dir = Path.Combine(_root, "usertemp");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "a.tmp"), new byte[10]);
        File.WriteAllBytes(Path.Combine(dir, "b.tmp"), new byte[20]);
        var subdir = Path.Combine(dir, "subdir");
        Directory.CreateDirectory(subdir);
        File.WriteAllBytes(Path.Combine(subdir, "c.tmp"), new byte[30]);

        var result = new Scanner(new[] { ContentsOnlyTarget("t-contents", dir) }, _processes).Scan();
        var t = result.Targets.Single();

        Assert.Null(t.SkippedReason);
        Assert.Equal(3, t.Items.Count);
        Assert.All(t.Items, item => Assert.NotEqual(dir, item.Path, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(60, t.TotalBytes);
    }

    [Fact]
    public void ContentsOnlyItems_PassTheValidator()
    {
        var dir = Path.Combine(_root, "usertemp2");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "a.tmp"), new byte[10]);

        var target = ContentsOnlyTarget("t-contents2", dir);
        var result = new Scanner(new[] { target }, _processes).Scan();
        var t = result.Targets.Single();
        Assert.NotEmpty(t.Items);

        var validator = new SafetyValidator();
        foreach (var item in t.Items)
        {
            var authorization = validator.Authorize(item.Path, target);
            Assert.True(authorization.Allowed, $"'{item.Path}' should be allowed: {authorization.Reason}");
        }
    }

    /// 2026-08-30 review, CONFIRMED on the live machine: hiberfil.sys is
    /// kernel-held its whole life, so the delete-lock probe marked it Locked
    /// and ReclaimableBytes read 0 — hiding from the deep reveal the exact
    /// gigabytes the feature exists to announce. A past-the-bin target is
    /// never given to the shell, so the shell's question must not be asked:
    /// no probe call, nothing marked locked, the bytes stay in the promise.
    [Fact]
    public void PastTheBinTarget_IsNeverLockProbed_SoItsBytesStayInThePromise()
    {
        var file = Path.Combine(_root, "hiberfil.sys");
        File.WriteAllBytes(file, new byte[128]);
        var probe = new FakeLockProbe();
        probe.LockedPaths.Add(file);   // the kernel would say exactly this

        var noBin = new CleanupTarget("hibernation-file", "Hibernation",
            CleanupLevel.Deep, new List<string> { file }, "System",
            RequiresExplicitOptIn: true, BypassesRecycleBin: true,
            RequiresElevation: true);
        var normal = Target("t-normal", file);

        var result = new Scanner(new[] { noBin, normal }, _processes, probe).Scan();

        var heavy = result.Targets.Single(t => t.Target.Id == "hibernation-file");
        Assert.False(heavy.Items.Single().Locked);
        Assert.Equal(128, heavy.ReclaimableBytes);
        // the ordinary target IS probed and honestly zeroed — the exemption
        // is the noBin flag, not the path
        var ordinary = result.Targets.Single(t => t.Target.Id == "t-normal");
        Assert.True(ordinary.Items.Single().Locked);
        Assert.Equal(0, ordinary.ReclaimableBytes);
        Assert.All(probe.Calls, c => Assert.Equal(file, c.Path));
        Assert.Single(probe.Calls);   // one probe: the ordinary target's
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}

public sealed class FakeLockProbe : ILockProbe
{
    private readonly object _gate = new();
    public HashSet<string> LockedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// Every call with the budget it was handed — the scanner scans targets
    /// in parallel, so recording is gated.
    public List<(string Path, LockProbeBudget Budget)> Calls { get; } = new();

    public bool IsLockedForDelete(string path, LockProbeBudget budget,
        System.Threading.CancellationToken ct = default)
    {
        lock (_gate) Calls.Add((path, budget));
        return LockedPaths.Contains(path);
    }
}
