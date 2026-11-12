using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Models;
using BriskEngine.Paths;
using BriskEngine.Safety;
using Xunit;

namespace BriskEngine.Tests;

public class CleanupTargetRegistryTests
{
    private static readonly HashSet<string> PathlessIds =
        new() { "docker-prune", "empty-recycle-bin", "component-store", "restore-points" };

    [Fact]
    public void Ids_AreUnique()
    {
        var ids = CleanupTargetRegistry.All.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void EveryTarget_HasTemplates_OrIsKnownPathless()
    {
        foreach (var t in CleanupTargetRegistry.All)
        {
            if (PathlessIds.Contains(t.Id) || t.Dynamic) { Assert.Empty(t.PathTemplates); continue; }
            Assert.NotEmpty(t.PathTemplates);
        }
    }

    [Fact]
    public void NoTemplate_PointsInsideAProtectedRoot()
    {
        // old-installers points at Downloads, which is NOT a protected root; everything
        // must stay out of Documents/Desktop/Pictures/... and system roots.
        foreach (var t in CleanupTargetRegistry.All)
        foreach (var template in t.PathTemplates)
        {
            var expanded = PathExpander.Expand(template);
            if (expanded is null) continue; // env var absent on this machine
            var probe = expanded.Split('*')[0].TrimEnd('\\');
            Assert.False(ProtectedPaths.IsProtected(Path.GetFullPath(probe)),
                $"{t.Id}: {template} resolves into a protected root");
        }
    }

    [Fact]
    public void NoTemplate_IsTheUserProfileRootItself()
    {
        var profileRoot = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        foreach (var t in CleanupTargetRegistry.All)
        foreach (var template in t.PathTemplates)
        {
            var expanded = PathExpander.Expand(template);
            if (expanded is null) continue; // env var absent on this machine
            var probe = expanded.Split('*')[0].TrimEnd('\\');
            Assert.NotEqual(profileRoot, Path.GetFullPath(probe),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SafeLevel_NeverRequiresElevation()
    {
        foreach (var t in CleanupTargetRegistry.All.Where(t => t.Level == CleanupLevel.Safe))
            Assert.False(t.RequiresElevation, $"{t.Id} is Safe but requires elevation");
    }

    [Fact]
    public void OldInstallers_IsIndividualSelectionOnly()
    {
        var t = CleanupTargetRegistry.All.Single(t => t.Id == "old-installers");
        Assert.True(t.RequiresIndividualSelection);
        Assert.Equal(CleanupLevel.Deep, t.Level);
    }

    /// An .iso is a personal file (monitor GUI wave): never counted, never
    /// deletable. Before the wave old-installers swept them out of Downloads.
    [Fact]
    public void Old_installers_never_takes_an_iso()
    {
        var t = CleanupTargetRegistry.All.Single(t => t.Id == "old-installers");
        Assert.DoesNotContain(t.PathTemplates, p => p.EndsWith(".iso", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(CleanupTargetRegistry.All.SelectMany(x => x.PathTemplates),
            p => p.EndsWith(".iso", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DockerPrune_IsExplicitOptIn()
    {
        var t = CleanupTargetRegistry.All.Single(t => t.Id == "docker-prune");
        Assert.True(t.RequiresExplicitOptIn);
    }

    /// The heavy system trio (2026-08-30 deep-visible-cleanup wave): each
    /// one frees gigabytes, each one is a decision, so none may ever ride a
    /// one-click clean — explicit opt-in, Deep shelf, administrator. The
    /// two with a real path bypass the recycle bin (a 30 GB Windows.old
    /// does not fit in it, and pretending it does poisons undo).
    [Fact]
    public void HeavySystemTrio_IsDeepOptInAdmin()
    {
        foreach (var id in new[] { "windows-old", "hibernation-file", "component-store" })
        {
            var t = CleanupTargetRegistry.All.Single(t => t.Id == id);
            Assert.Equal(CleanupLevel.Deep, t.Level);
            Assert.True(t.RequiresExplicitOptIn, $"{id} must be explicit opt-in");
            Assert.True(t.RequiresElevation, $"{id} must require administrator");
        }
        Assert.True(CleanupTargetRegistry.All.Single(t => t.Id == "windows-old").BypassesRecycleBin);
        Assert.True(CleanupTargetRegistry.All.Single(t => t.Id == "hibernation-file").BypassesRecycleBin);
        Assert.Equal(new[] { @"%SystemDrive%\Windows.old" },
            CleanupTargetRegistry.All.Single(t => t.Id == "windows-old").PathTemplates);
        Assert.Equal(new[] { @"%SystemDrive%\hiberfil.sys" },
            CleanupTargetRegistry.All.Single(t => t.Id == "hibernation-file").PathTemplates);
    }

    /// 2026-09-01 live workbench: the Delivery Optimization cache sits under
    /// the NetworkService profile, so all 14 folders came back
    /// DE_ACCESSDENIEDSRC from the shell and a promised 7.5 GB freed 0 B.
    /// The target must declare past-the-bin removal — CleanRunner has the
    /// matching case, and the backstop refuses any noBin target that lacks one.
    [Fact]
    public void DeliveryOptimizationCache_GoesPastTheBin()
    {
        var t = CleanupTargetRegistry.All.Single(x => x.Id == "delivery-optimization");
        Assert.True(t.BypassesRecycleBin);
        Assert.True(t.RequiresElevation);
        Assert.Equal(CleanupLevel.Deep, t.Level);
    }

    /// REGRESSION PIN (2026-08-17): modern WhatsApp Desktop's process is
    /// "WhatsApp.Root"; with only "WhatsApp" registered, the running-app
    /// exclusion silently never fired and a locked 310 MB cache entered the
    /// Depolama promise. Both names must stay registered, and the display
    /// name must stay the human one.
    [Fact]
    public void WhatsAppCache_CoversTheRootProcessName()
    {
        var t = CleanupTargetRegistry.All.Single(t => t.Id == "whatsapp-cache");
        Assert.Contains("WhatsApp", t.AppProcessCandidates);
        Assert.Contains("WhatsApp.Root", t.AppProcessCandidates);
        Assert.Equal("WhatsApp", t.AppDisplayName);
    }

    /// REVIEW ROUND 1 (I3) — registry invariant: every process-name
    /// candidate of every registered target must be non-empty and trimmed,
    /// and an app-gated target must yield at least one candidate plus a
    /// display name. A candidate with a stray space is one that
    /// Process.GetProcessesByName never matches — the exclusion silently
    /// never fires, which is exactly the 310 MB WhatsApp bug reborn.
    [Fact]
    public void AppProcessCandidates_AreAlwaysNonEmptyAndTrimmed()
    {
        foreach (var t in CleanupTargetRegistry.All
                     .Where(t => t.RequiresAppClosedProcess is not null))
        {
            Assert.NotEmpty(t.AppProcessCandidates);
            Assert.NotNull(t.AppDisplayName);
            foreach (var candidate in t.AppProcessCandidates)
            {
                Assert.False(string.IsNullOrWhiteSpace(candidate),
                    $"{t.Id}: empty process-name candidate");
                Assert.True(candidate.Trim() == candidate,
                    $"{t.Id}: candidate '{candidate}' is not trimmed");
            }
        }
    }

    /// REVIEW ROUND 1 (I3) — the parser itself defends against the
    /// malformed shapes a future registry edit could take: spaces around
    /// the separator and a trailing '|' must still yield exact, matchable
    /// process names (never "" into IsRunning).
    [Fact]
    public void AppCandidates_SurviveSpacesAndTrailingSeparators()
    {
        var t = new CleanupTarget("x", "X", CleanupLevel.Safe,
            new List<string> { @"C:\x" }, "Test",
            RequiresAppClosedProcess: " WhatsApp | WhatsApp.Root |");

        Assert.Equal(new[] { "WhatsApp", "WhatsApp.Root" }, t.AppProcessCandidates);
        Assert.Equal("WhatsApp", t.AppDisplayName);

        var degenerate = t with { RequiresAppClosedProcess = " | " };
        Assert.Empty(degenerate.AppProcessCandidates);
        Assert.Null(degenerate.AppDisplayName);
    }

    [Theory]
    [InlineData("nvidia-shader-cache")]
    [InlineData("amd-shader-cache")]
    [InlineData("d3d-shader-cache")]
    public void ShaderCaches_AreDeepOptIn_AndNeedNoElevation(string id)
    {
        var t = CleanupTargetRegistry.All.Single(x => x.Id == id);
        Assert.Equal(CleanupLevel.Deep, t.Level);
        Assert.True(t.RequiresExplicitOptIn);
        Assert.False(t.RequiresElevation);
        Assert.True(t.Regenerates);
        Assert.True(t.DeletesContentsNotDirectory);
    }

    [Fact]
    public void WerSystem_IsDeepAndAdmin()
    {
        var t = CleanupTargetRegistry.All.Single(x => x.Id == "wer-system");
        Assert.Equal(CleanupLevel.Deep, t.Level);
        Assert.True(t.RequiresElevation);
    }

    private static CleanupTarget Get(string id) => CleanupTargetRegistry.All.Single(t => t.Id == id);

    /// Monitor GUI wave: four path-template targets, each a decision about
    /// one kind of leftover. Kernel dumps, setup logs and driver installers
    /// are a week old before they count; ~\.git never rides a plain
    /// `clean --level deep` (its loss is the user's whole home history).
    [Fact]
    public void New_targets_are_registered_with_rules()
    {
        var dumps = Get("live-kernel-reports");
        Assert.Equal(new[]
        {
            @"%SystemRoot%\LiveKernelReports\*.dmp", @"%SystemRoot%\LiveKernelReports\*\*.dmp",
            @"%SystemRoot%\Minidump\*.dmp", @"%SystemRoot%\MEMORY.DMP",
        }, dumps.PathTemplates);
        Assert.Equal(7, dumps.MinAgeDays);

        var panther = Get("panther-logs");
        Assert.Equal(7, panther.MinAgeDays);
        Assert.All(panther.PathTemplates, p => Assert.True(
            p.EndsWith(@"\*.log", StringComparison.Ordinal) || p.EndsWith(@"\*.etl", StringComparison.Ordinal), p));
        Assert.DoesNotContain(panther.PathTemplates, p => p.Contains(".xml", StringComparison.OrdinalIgnoreCase));

        var drivers = Get("driver-installer-leftovers");
        Assert.Contains(@"%SystemDrive%\NVIDIA\DisplayDriver", drivers.PathTemplates);
        Assert.Contains(@"%SystemDrive%\AMD\*", drivers.PathTemplates);
        Assert.Contains(drivers.PathTemplates, p => p.Contains(@"\UpdateFramework\ota-artifacts\"));
        Assert.Equal(7, drivers.MinAgeDays);

        var git = Get("profile-git");
        Assert.Equal(new[] { @"~\.git" }, git.PathTemplates);
        Assert.Null(git.MinAgeDays);
        Assert.False(git.RequiresElevation);
        Assert.True(git.RequiresExplicitOptIn);

        foreach (var t in new[] { dumps, panther, drivers, git })
        {
            Assert.Equal(CleanupLevel.Deep, t.Level);
            Assert.False(t.DeletesContentsNotDirectory, $"{t.Id} deletes whole paths");
            Assert.False(t.BypassesRecycleBin, $"{t.Id}: Space runs already delete for good");
        }
        foreach (var t in new[] { dumps, panther, drivers })
            Assert.True(t.RequiresElevation, $"{t.Id} lives in a system folder");

        Assert.Equal(30, Scanner.OldInstallerMinAgeDays);
        Assert.Equal(Scanner.OldInstallerMinAgeDays, Get("old-installers").MinAgeDays);
    }

    /// Monitor GUI wave: two targets with no templates, found by walking the
    /// user's project folders (DynamicTargets). Opt-in, like profile-git: a
    /// plain `clean --level deep` never walks Desktop and Documents.
    [Fact]
    public void Dynamic_targets_are_registered()
    {
        var deps = Get("stale-project-deps");
        var archives = Get("archive-beside-extracted");
        Assert.Equal(new[] { deps.Id, archives.Id },
            CleanupTargetRegistry.All.Where(t => t.Dynamic).Select(t => t.Id));
        foreach (var t in new[] { deps, archives })
        {
            Assert.Empty(t.PathTemplates);
            Assert.Equal(CleanupLevel.Deep, t.Level);
            Assert.True(t.RequiresExplicitOptIn, $"{t.Id} is opt-in");
            Assert.False(t.RequiresElevation, $"{t.Id} lives in the user's own folders");
            Assert.False(t.DeletesContentsNotDirectory, $"{t.Id} deletes whole paths");
            Assert.False(t.BypassesRecycleBin, $"{t.Id}: Space runs already delete for good");
            Assert.Null(t.MinAgeDays);
        }
        Assert.True(deps.Regenerates);
        Assert.False(archives.Regenerates);
    }

    /// Restore points (monitor GUI wave): System Restore removes them, so there are
    /// no templates; nothing goes near the shell (past the bin); and opt-in,
    /// so a plain `clean --level deep` never deletes them.
    [Fact]
    public void Restore_points_target_is_pathless_admin_opt_in()
    {
        var t = Get("restore-points");
        Assert.Empty(t.PathTemplates);
        Assert.False(t.Dynamic);
        Assert.Equal(CleanupLevel.Deep, t.Level);
        Assert.True(t.RequiresElevation);
        Assert.True(t.BypassesRecycleBin);
        Assert.True(t.RequiresExplicitOptIn);
    }

    /// Ruling R1: a byte is counted once. No new target's path sits inside,
    /// on, or above any other target's path — crash-dumps (app dumps under
    /// %LOCALAPPDATA%) and the WER queues stay with space.caches, and the
    /// kernel's Minidump/MEMORY.DMP are nobody else's.
    [Theory]
    [InlineData("live-kernel-reports")]
    [InlineData("panther-logs")]
    [InlineData("driver-installer-leftovers")]
    [InlineData("profile-git")]
    public void New_target_paths_overlap_no_other_target(string id)
    {
        foreach (var mine in FixedRoots(Get(id)))
        foreach (var other in CleanupTargetRegistry.All.Where(t => t.Id != id))
        foreach (var theirs in FixedRoots(other))
            Assert.False(mine.Equals(theirs, StringComparison.OrdinalIgnoreCase)
                || mine.StartsWith(theirs + '\\', StringComparison.OrdinalIgnoreCase)
                || theirs.StartsWith(mine + '\\', StringComparison.OrdinalIgnoreCase),
                $"{id} ({mine}) overlaps {other.Id} ({theirs})");
    }

    /// The part of each template no wildcard can move: the folder above the
    /// first '*' segment, or the whole path when there is no '*'.
    private static IEnumerable<string> FixedRoots(CleanupTarget t) =>
        t.PathTemplates.Select(PathExpander.Expand).OfType<string>().Select(p =>
        {
            var star = p.IndexOf('*');
            return Path.GetFullPath(star < 0 ? p : p[..p.LastIndexOf('\\', star)]);
        });

    [Theory]
    [InlineData("chrome-cache")]
    [InlineData("edge-cache")]
    [InlineData("brave-cache")]
    public void BrowserCaches_CoverEveryProfile(string id)
    {
        var t = CleanupTargetRegistry.All.Single(x => x.Id == id);
        Assert.Contains(t.PathTemplates, p => p.Contains(@"\Default\Cache"));
        Assert.Contains(t.PathTemplates, p => p.Contains(@"\Profile *\Cache"));
    }
}
