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
    private static readonly HashSet<string> PathlessIds = new() { "docker-prune", "empty-recycle-bin", "component-store" };

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
            if (PathlessIds.Contains(t.Id)) { Assert.Empty(t.PathTemplates); continue; }
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
}
