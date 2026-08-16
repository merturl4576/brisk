using System;
using System.Collections.Generic;
using Brisk.Cli;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

public class CleanSelectionTests
{
    private static TargetScanResult Scan(string id, CleanupLevel level,
        bool pick = false, bool optIn = false) => new(
        new CleanupTarget(id, id, level, new List<string> { @"C:\x" }, "Test",
            RequiresIndividualSelection: pick, RequiresExplicitOptIn: optIn),
        Array.Empty<ResolvedItem>(), null);

    private static ScanResult Result(params TargetScanResult[] targets) => new(targets);

    [Fact]
    public void NoTarget_FiltersByLevel_ExcludesPickAndOptIn()
    {
        var scan = Result(
            Scan("user-temp", CleanupLevel.Safe),
            Scan("docker-prune", CleanupLevel.Developer, optIn: true),
            Scan("old-installers", CleanupLevel.Deep, pick: true),
            Scan("windows-temp", CleanupLevel.Deep));
        var (selected, error) = Program.SelectTargets(scan, null, CleanupLevel.Deep);
        Assert.Null(error);
        Assert.Equal("windows-temp", Assert.Single(selected).Target.Id);
    }

    [Fact]
    public void Target_SelectsRegardlessOfLevel_AllowsOptIn()
    {
        var scan = Result(Scan("docker-prune", CleanupLevel.Developer, optIn: true));
        var (selected, error) = Program.SelectTargets(scan, "docker-prune", CleanupLevel.Safe);
        Assert.Null(error);
        Assert.Equal("docker-prune", Assert.Single(selected).Target.Id);
    }

    [Fact]
    public void Target_IndividualSelection_IsRefused()
    {
        var scan = Result(Scan("old-installers", CleanupLevel.Deep, pick: true));
        var (selected, error) = Program.SelectTargets(scan, "old-installers", CleanupLevel.Deep);
        Assert.Empty(selected);
        Assert.Contains("per-item", error);
    }

    [Fact]
    public void Target_Unknown_IsError()
    {
        var (selected, error) = Program.SelectTargets(Result(), "nope", CleanupLevel.Safe);
        Assert.Empty(selected);
        Assert.Contains("unknown target", error);
    }
}
