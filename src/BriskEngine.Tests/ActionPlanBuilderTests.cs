using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Actions;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

public sealed class ActionPlanBuilderTests
{
    private const string Run = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Kit(ActionPlanBuilder Builder, DiagnosticContext Ctx, FakeRegistry Reg,
        FakeFeatures Features, FakeSpace Space, FakeUi Ui);

    private static Kit Make(Func<string, string?>? describe = null)
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        var features = new FakeFeatures();
        var space = new FakeSpace();
        var ui = new FakeUi();
        var startup = new StartupManager(reg, null);
        var rules = new ActionRules(new ActionProbes(new FakeRunner(), ui, new FakeOverlay(), features,
            new FakeTasks(), startup, () => false));
        return new Kit(new ActionPlanBuilder(ctx, rules, startup, space, features, describe), ctx, reg, features, space, ui);
    }

    private static TargetScanResult T(string id, long bytes, string? skipped = null) => new(
        CleanupTargetRegistry.All.Single(t => t.Id == id),
        new[] { new ResolvedItem(id, $@"C:\fake\{id}", bytes, Now.AddDays(-40)) }, skipped);

    private static ScanResult Scan(params TargetScanResult[] targets) => new(targets);

    private static ActionItem? Row(ActionPlan plan, string id) => plan.Items.FirstOrDefault(i => i.Id == id);

    [Fact]
    public void StartupRow_TitleIsTheFriendlyName()
    {
        var kit = Make(p => p.EndsWith("msedge.exe", StringComparison.OrdinalIgnoreCase) ? "Microsoft Edge" : null);
        kit.Reg.SetString(Run, "MicrosoftEdgeAutoLaunch_F6C5A0B1C2D3E4F5", @"""C:\Edge\msedge.exe"" --auto");
        var row = kit.Builder.BuildFast().SelectMany(g => g.Items).Single(i => i.Id.Contains("MicrosoftEdgeAutoLaunch"));
        Assert.Equal("Microsoft Edge", row.Title);
    }

    [Fact]
    public void Build_IsComposeOfTheTwoHalves()
    {
        var kit = Make();
        kit.Space.Bin = 5_000;
        var scan = Scan(T("user-temp", 100), T("npm-cache", 300));
        var whole = kit.Builder.Build(scan, Now);
        var halves = ActionPlanBuilder.Compose(Now, kit.Builder.BuildSpace(scan), kit.Builder.BuildFast(), scan);

        Assert.Equal(whole.Groups.Select(g => g.Kind), halves.Groups.Select(g => g.Kind));
        Assert.Equal(whole.Items.Select(i => i.Id), halves.Items.Select(i => i.Id));
        Assert.Contains(halves.Groups, g => g.Kind == ActionGroupKind.Space);
    }

    [Fact]
    public void BuildFast_HasNoSpaceGroup_AndBuildSpaceIsNullForAnEmptyScan()
    {
        var kit = Make();
        Assert.DoesNotContain(kit.Builder.BuildFast(), g => g.Kind == ActionGroupKind.Space);
        Assert.Null(kit.Builder.BuildSpace(Scan()));
    }

    [Fact]
    public void Build_MachineWithoutOptionalFeatures_HidesThoseRowsAndDoesNotThrow()
    {
        var kit = Make();
        var plan = kit.Builder.Build(Scan(), Now);

        foreach (var id in new[] { "privacy.recall", "speed.gpu-scheduling", "speed.windowed-games",
                     "speed.power-mode", "speed.vm-platform", "space.docker", "space.hibernation",
                     "space.recycle-bin", "space.downloads", "space.caches", "privacy.firewall",
                     "privacy.defender-realtime" })
            Assert.Null(Row(plan, id));
        Assert.NotNull(Row(plan, "speed.animations"));   // the fake UI reports animations on
        Assert.NotNull(Row(plan, "privacy.copilot"));    // policy absent = Copilot allowed
    }

    [Fact]
    public void Build_GroupsAreSpaceSpeedPrivacy_AndEmptyGroupsAreLeftOut()
    {
        var kit = Make();
        var plan = kit.Builder.Build(Scan(T("user-temp", 100)), Now);
        Assert.Equal(new[] { ActionGroupKind.Space, ActionGroupKind.Speed, ActionGroupKind.Privacy },
            plan.Groups.Select(g => g.Kind));
    }

    [Fact]
    public void Build_CacheBucketCountsARunningAppsCacheAtFullSize()
    {
        var kit = Make();
        var plan = kit.Builder.Build(Scan(T("user-temp", 100), T("chrome-cache", 50, "chrome is running")), Now);
        Assert.Equal(150, Row(plan, "space.caches")!.Bytes);
    }

    [Fact]
    public void Build_ShaderCachesAreListedUnticked()
    {
        var kit = Make();
        var plan = kit.Builder.Build(Scan(T("nvidia-shader-cache", 2_300)), Now);
        var row = Row(plan, "space.shaders")!;
        Assert.False(row.DefaultOn);
        Assert.Equal("Games may stutter on first launch.", row.Cost);
    }

    /// MID since the monitor GUI wave, so unticked everywhere; a laptop
    /// still gets the battery warning as its cost.
    [Fact]
    public void Build_Hibernation_IsMid_AndALaptopGetsTheBatteryCost()
    {
        var desktop = Make();
        ((FakePowercfg)desktop.Ctx.Powercfg).Battery = false;
        var row = Row(desktop.Builder.Build(Scan(T("hibernation-file", 13_000)), Now), "space.hibernation")!;
        Assert.False(row.DefaultOn);
        Assert.Equal(SpaceLevel.Mid, row.Level);
        Assert.Equal("Fast startup off · can re-enable", row.Cost);

        var laptop = Make();
        ((FakePowercfg)laptop.Ctx.Powercfg).Battery = true;
        row = Row(laptop.Builder.Build(Scan(T("hibernation-file", 13_000)), Now), "space.hibernation")!;
        Assert.False(row.DefaultOn);
        Assert.Equal(ActionCatalog.HibernationLaptopCost, row.Cost);
    }

    [Fact]
    public void Build_DownloadsRowListsEachFile()
    {
        var kit = Make();
        var target = CleanupTargetRegistry.All.Single(t => t.Id == "old-installers");
        var scan = Scan(new TargetScanResult(target, new[]
        {
            new ResolvedItem("old-installers", @"C:\Users\x\Downloads\a.exe", 100, Now.AddDays(-60)),
            new ResolvedItem("old-installers", @"C:\Users\x\Downloads\b.msi", 900, Now.AddDays(-90)),
        }, null));

        var row = Row(kit.Builder.Build(scan, Now), "space.downloads")!;

        Assert.Equal(1000, row.Bytes);
        Assert.Equal(@"C:\Users\x\Downloads\b.msi", row.Files[0].Path);
        Assert.Equal(2, row.Files.Count);
    }

    [Fact]
    public void Build_DockerAndRecycleBinComeFromTheSpaceProbe()
    {
        var kit = Make();
        kit.Space.Docker = 5_000;
        kit.Space.Bin = 700;
        var plan = kit.Builder.Build(Scan(), Now);
        Assert.Equal(5_000, Row(plan, "space.docker")!.Bytes);
        Assert.False(Row(plan, "space.docker")!.DefaultOn);
        Assert.Equal(700, Row(plan, "space.recycle-bin")!.Bytes);
    }

    [Fact]
    public void Build_StartupRows_TickedUnlessProtected_AndNeverBrisk()
    {
        var kit = Make();
        kit.Reg.SetString(Run, "Discord", @"C:\Discord\Update.exe --processStart Discord.exe");
        kit.Reg.SetString(Run, "OneDrive", @"C:\OneDrive\OneDrive.exe /background");
        kit.Reg.SetString(Run, "brisk", @"C:\brisk\brisk-app.exe --tray");
        kit.Reg.SetString(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Vendor", @"C:\v\v.exe");

        var plan = kit.Builder.Build(Scan(), Now);

        var discord = Row(plan, "startup:HKCU:Discord")!;
        Assert.True(discord.DefaultOn);
        Assert.Null(discord.TitleKey);
        Assert.Equal("Discord", discord.Title);
        var onedrive = Row(plan, "startup:HKCU:OneDrive")!;
        Assert.False(onedrive.DefaultOn);
        Assert.Equal("File sync stops.", onedrive.Cost);
        Assert.Null(Row(plan, "startup:HKCU:brisk"));
        Assert.True(Row(plan, "startup:HKLM:Vendor")!.NeedsAdmin);
    }

    [Fact]
    public void Build_RecallRowNeedsTheFeature()
    {
        var kit = Make();
        Assert.Null(Row(kit.Builder.Build(Scan(), Now), "privacy.recall"));
        kit.Features.States["Recall"] = 2;
        Assert.NotNull(Row(kit.Builder.Build(Scan(), Now), "privacy.recall"));
    }

    [Fact]
    public void Build_ARuleThatIsAlreadyDone_HidesItsRow()
    {
        var kit = Make();
        kit.Ui.Minimize = false;
        kit.Ui.ClientArea = false;
        Assert.Null(Row(kit.Builder.Build(Scan(), Now), "speed.animations"));
    }

    // ---- Space levels (monitor GUI wave): MIN regenerates, MID costs a
    // little, MAX includes what cannot come back. Each level includes the
    // ones below it, and only MIN is ticked by default.

    [Fact]
    public void Every_space_row_has_a_level_and_no_other_row_does()
    {
        Assert.All(ActionCatalog.Rows.Where(r => r.Group == ActionGroupKind.Space),
            r => Assert.True(r.Level is not null, $"{r.Id} has no level"));
        Assert.All(ActionCatalog.Rows.Where(r => r.Group != ActionGroupKind.Space), r =>
        {
            Assert.True(r.Level is null, $"{r.Id} is not a Space row but has a level");
            Assert.False(r.Lost, $"{r.Id} is not a Space row but is marked lost");
        });
    }

    [Theory]
    [InlineData("space.caches", SpaceLevel.Min, false)]
    [InlineData("space.developer", SpaceLevel.Min, false)]
    [InlineData("space.hibernation", SpaceLevel.Mid, false)]
    [InlineData("space.shaders", SpaceLevel.Mid, false)]
    [InlineData("space.downloads", SpaceLevel.Mid, true)]
    [InlineData("space.recycle-bin", SpaceLevel.Mid, true)]
    [InlineData("space.windows-old", SpaceLevel.Max, true)]
    [InlineData("space.docker", SpaceLevel.Max, true)]
    [InlineData("space.crash-dumps", SpaceLevel.Min, false)]
    [InlineData("space.setup-logs", SpaceLevel.Min, false)]
    [InlineData("space.driver-leftovers", SpaceLevel.Min, false)]
    [InlineData("space.profile-git", SpaceLevel.Max, true)]
    [InlineData("space.project-deps", SpaceLevel.Mid, false)]
    [InlineData("space.archives", SpaceLevel.Max, true)]
    [InlineData("space.restore-points", SpaceLevel.Max, true)]
    public void Space_rows_carry_the_spec_level_and_only_min_is_on(string id, SpaceLevel level, bool lost)
    {
        var row = ActionCatalog.Find(id);
        Assert.Equal(level, row.Level);
        Assert.Equal(lost, row.Lost);
        Assert.Equal(level == SpaceLevel.Min, row.DefaultOn);
    }

    /// Spec, undo model: hibernation can be switched back on in Settings,
    /// but brisk does not offer it as undo.
    [Fact]
    public void Hibernation_is_not_offered_as_undo() =>
        Assert.False(ActionCatalog.Find("space.hibernation").Reversible);

    [Fact]
    public void Build_SpaceItemsCarryTheirRowsLevelAndLostFlag()
    {
        var kit = Make();
        kit.Space.Bin = 700;
        var plan = kit.Builder.Build(Scan(T("user-temp", 100), T("nvidia-shader-cache", 200)), Now);

        Assert.Equal(SpaceLevel.Min, Row(plan, "space.caches")!.Level);
        Assert.Equal(SpaceLevel.Mid, Row(plan, "space.shaders")!.Level);
        Assert.False(Row(plan, "space.shaders")!.Lost);
        Assert.True(Row(plan, "space.recycle-bin")!.Lost);
        Assert.Null(Row(plan, "speed.animations")!.Level);
    }

    [Fact]
    public void Build_NewSpaceRowsCountTheirTargets()
    {
        var plan = Make().Builder.Build(Scan(T("live-kernel-reports", 10_300), T("panther-logs", 600),
            T("driver-installer-leftovers", 700), T("profile-git", 6_540)), Now);

        Assert.Equal(10_300, Row(plan, "space.crash-dumps")!.Bytes);
        Assert.Equal(600, Row(plan, "space.setup-logs")!.Bytes);
        Assert.Equal(700, Row(plan, "space.driver-leftovers")!.Bytes);
        var git = Row(plan, "space.profile-git")!;
        Assert.Equal(6_540, git.Bytes);
        Assert.False(git.DefaultOn);
        Assert.Equal("Can't be undone.", git.Cost);
    }

    [Fact]
    public void Build_DynamicSpaceRowsCountTheirTargets()
    {
        var plan = Make().Builder.Build(Scan(T("stale-project-deps", 10_700),
            T("archive-beside-extracted", 3_740)), Now);

        var deps = Row(plan, "space.project-deps")!;
        Assert.Equal(10_700, deps.Bytes);
        Assert.False(deps.DefaultOn);
        Assert.Equal("npm install brings them back.", deps.Cost);
        var archives = Row(plan, "space.archives")!;
        Assert.Equal(3_740, archives.Bytes);
        Assert.False(archives.DefaultOn);
        Assert.True(archives.Lost);
    }

    // ---- Restore points (MAX): every one but the newest. Windows cannot say
    // what deleting them frees until it has, so the row carries no size.

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void No_row_with_zero_or_one_restore_point(int count)
    {
        var kit = Make();
        kit.Space.RestorePoints = () => count;
        Assert.Null(Row(kit.Builder.Build(Scan(), Now), "space.restore-points"));
    }

    [Fact]
    public void Row_has_unknown_size()
    {
        var kit = Make();
        kit.Space.RestorePoints = () => 3;
        var row = Row(kit.Builder.Build(Scan(), Now), "space.restore-points")!;
        Assert.Null(row.Bytes);   // "—" on screen: never a guess, never counted
        Assert.Equal(SpaceLevel.Max, row.Level);
        Assert.True(row.Lost);
        Assert.False(row.DefaultOn);
        Assert.True(row.NeedsAdmin);
        Assert.Equal(new[] { "restore-points" }, row.LinkIds);
    }

    /// System Restore unreadable (not an administrator, service off) reads as
    /// null in the real probe; a probe that throws is the same. Neither is
    /// "0 points".
    [Fact]
    public void Probe_failure_means_no_row()
    {
        var unknown = Make();
        unknown.Space.RestorePoints = () => null;
        Assert.Null(Row(unknown.Builder.Build(Scan(), Now), "space.restore-points"));

        var throwing = Make();
        throwing.Space.RestorePoints = () => throw new InvalidOperationException("WMI");
        var plan = throwing.Builder.Build(Scan(T("user-temp", 100)), Now);
        Assert.Null(Row(plan, "space.restore-points"));
        Assert.NotNull(Row(plan, "space.caches"));   // the other rows still come
    }

    private static ActionPlan PlanOf(params ActionItem[] items) => new(Now,
        items.GroupBy(i => i.Group).Select(g => new ActionGroup(g.Key, g.ToList())).ToList(), Scan());

    private static ActionItem Item(string id, long? bytes = null) =>
        ActionPlanBuilder.ItemFrom(ActionCatalog.Find(id), bytes);

    private static readonly IReadOnlySet<string> NoSkips = new HashSet<string>();

    [Fact]
    public void Default_selection_ticks_only_min_space_rows()
    {
        // Downloads (MID) and Windows.old (MAX) were ticked before levels came.
        var plan = PlanOf(Item("space.caches", 100), Item("space.downloads", 200), Item("space.windows-old", 300));
        Assert.Equal(new[] { "space.caches" }, ActionSelection.Default(plan).RowIds);
    }

    [Fact]
    public void ForLevel_is_cumulative_and_honours_skips()
    {
        var plan = PlanOf(Item("space.caches", 100), Item("space.developer", 100),
            Item("space.shaders", 200), Item("space.downloads", 200),
            Item("space.windows-old", 300), Item("space.docker", 300),
            Item("speed.animations"), Item("speed.vm-platform"), Item("privacy.ads"));

        var mid = ActionSelection.ForLevel(plan, SpaceLevel.Mid,
            new HashSet<string> { "space.developer" }).RowIds;
        Assert.Contains("space.caches", mid);
        Assert.DoesNotContain("space.developer", mid);   // skipped
        Assert.Contains("space.shaders", mid);
        Assert.Contains("space.downloads", mid);
        Assert.DoesNotContain("space.windows-old", mid); // MAX
        Assert.DoesNotContain("space.docker", mid);

        var max = ActionSelection.ForLevel(plan, SpaceLevel.Max, NoSkips).RowIds;
        Assert.Contains("space.docker", max);
        Assert.Contains("space.windows-old", max);
        Assert.Contains("space.caches", max);

        // Speed and Privacy ride along with their own defaults at every level.
        foreach (var level in new[] { SpaceLevel.Min, SpaceLevel.Mid, SpaceLevel.Max })
        {
            var ids = ActionSelection.ForLevel(plan, level, NoSkips).RowIds;
            Assert.Contains("speed.animations", ids);
            Assert.Contains("privacy.ads", ids);
            Assert.DoesNotContain("speed.vm-platform", ids);
        }
        Assert.Equal(ActionSelection.Default(plan).RowIds.OrderBy(i => i),
            ActionSelection.ForLevel(plan, SpaceLevel.Min, NoSkips).RowIds.OrderBy(i => i));
    }

    /// Ruling R2: a skip is a skip on every page, not only on Yer aç.
    [Fact]
    public void ForLevel_skips_apply_to_speed_and_privacy_rows_too()
    {
        var plan = PlanOf(Item("space.caches", 100), Item("speed.animations"), Item("privacy.ads"),
            Item("privacy.copilot"));
        var ids = ActionSelection.ForLevel(plan, SpaceLevel.Max,
            new HashSet<string> { "speed.animations", "privacy.ads" }).RowIds;
        Assert.Equal(new[] { "privacy.copilot", "space.caches" }, ids.OrderBy(i => i));
    }

    [Fact]
    public void ForLevel_keeps_every_listed_file_like_the_default()
    {
        var file = new ActionFile(@"C:\Users\x\Downloads\setup.exe", 10, null);
        var plan = PlanOf(ActionPlanBuilder.ItemFrom(ActionCatalog.Find("space.downloads"), 10,
            files: new[] { file }));
        Assert.Contains(file.Path, ActionSelection.ForLevel(plan, SpaceLevel.Mid, NoSkips).Files);
    }
}
