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
    private static readonly HashSet<string> PathlessIds = new() { "docker-prune", "empty-recycle-bin" };

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
}
