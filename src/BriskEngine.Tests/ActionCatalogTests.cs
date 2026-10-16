using System;
using System.Linq;
using BriskEngine.Actions;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

public sealed class ActionCatalogTests
{
    internal static ActionRules Rules(DiagnosticContext ctx) => new(new ActionProbes(
        new FakeRunner(), new FakeUi(), new FakeOverlay(), new FakeFeatures(), new FakeTasks(),
        new StartupManager(ctx.Registry, null), () => false));

    [Fact]
    public void RowIdsAndKeysAreUnique()
    {
        var ids = ActionCatalog.Rows.Select(r => r.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        var keys = ActionCatalog.AllKeys().ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void EveryRuleLinkResolves()
    {
        var rules = Rules(TestContext.Empty());
        foreach (var row in ActionCatalog.Rows.Where(r => r.LinkKind == ActionLinkKind.Rules))
        foreach (var id in row.LinkIds)
            Assert.True(rules.Find(id) is not null, $"{row.Id} links to unknown rule '{id}'");
    }

    [Fact]
    public void EveryCleanupLinkIsARegisteredTarget()
    {
        var targets = CleanupTargetRegistry.All.Select(t => t.Id).ToHashSet();
        foreach (var row in ActionCatalog.Rows.Where(r => r.LinkKind == ActionLinkKind.Cleanup))
        foreach (var id in row.LinkIds)
            Assert.Contains(id, targets);
    }

    [Fact]
    public void CostLinesAreSixWordsAtMost()
    {
        var costs = ActionCatalog.Rows.Select(r => r.Cost).Where(c => c is not null)
            .Append(ActionCatalog.HibernationLaptopCost)
            .Concat(Enum.GetValues<StartupProtectionKind>()
                .Where(k => k != StartupProtectionKind.None)
                .Select(k => ActionCatalog.StartupCost(k).English));
        foreach (var cost in costs)
            Assert.True(cost!.Split(' ').Length <= 6, $"too long: {cost}");
    }

    [Fact]
    public void SpecDefaults()
    {
        Assert.False(ActionCatalog.Find("space.shaders").DefaultOn);
        Assert.False(ActionCatalog.Find("space.docker").DefaultOn);
        Assert.False(ActionCatalog.Find("speed.memory-integrity").DefaultOn);
        Assert.False(ActionCatalog.Find("speed.vm-platform").DefaultOn);
        Assert.False(ActionCatalog.Find("privacy.location").DefaultOn);
        Assert.True(ActionCatalog.Find("space.caches").DefaultOn);
        Assert.Equal("component-store", ActionCatalog.Find("space.caches").LinkIds[^1]);
    }

    [Fact]
    public void Find_BuildsAStartupRuleFromItsId()
    {
        var rules = Rules(TestContext.Empty());
        Assert.Equal("startup:HKCU:Discord", rules.Find("startup:HKCU:Discord")!.Id);
        Assert.Null(rules.Find("no-such-rule"));
    }

    [Fact]
    public void Selection_DefaultTicksDefaultOnRowsAndEveryListedFile()
    {
        var file = new ActionFile(@"C:\Users\x\Downloads\setup.exe", 10, null);
        ActionItem Item(string id, bool on, params ActionFile[] files) => new(id, ActionGroupKind.Space,
            "k", "t", null, null, null, null, 1, on, false, false, false, ActionLinkKind.Cleanup,
            new[] { "user-temp" }, files);
        var plan = new ActionPlan(DateTime.UtcNow, new[]
        {
            new ActionGroup(ActionGroupKind.Space, new[] { Item("space.caches", true), Item("space.shaders", false), Item("space.downloads", true, file) }),
        }, new BriskEngine.Models.ScanResult(Array.Empty<BriskEngine.Models.TargetScanResult>()));

        var selection = ActionSelection.Default(plan);

        Assert.Contains("space.caches", selection.RowIds);
        Assert.DoesNotContain("space.shaders", selection.RowIds);
        Assert.Contains(file.Path, selection.Files);
        Assert.Empty(selection.Only(ActionGroupKind.Privacy, plan).RowIds);
    }

    [Theory]
    [InlineData("privacy.firewall")]
    [InlineData("privacy.defender-realtime")]
    public void UndoNeverTurnsProtectionOff(string id) => Assert.False(ActionCatalog.Find(id).Reversible);
}
