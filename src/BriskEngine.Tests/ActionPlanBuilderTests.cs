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

    private static Kit Make()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        var features = new FakeFeatures();
        var space = new FakeSpace();
        var ui = new FakeUi();
        var startup = new StartupManager(reg, null);
        var rules = new ActionRules(new ActionProbes(new FakeRunner(), ui, new FakeOverlay(), features,
            new FakeTasks(), startup, () => false));
        return new Kit(new ActionPlanBuilder(ctx, rules, startup, space, features), ctx, reg, features, space, ui);
    }

    private static TargetScanResult T(string id, long bytes, string? skipped = null) => new(
        CleanupTargetRegistry.All.Single(t => t.Id == id),
        new[] { new ResolvedItem(id, $@"C:\fake\{id}", bytes, Now.AddDays(-40)) }, skipped);

    private static ScanResult Scan(params TargetScanResult[] targets) => new(targets);

    private static ActionItem? Row(ActionPlan plan, string id) => plan.Items.FirstOrDefault(i => i.Id == id);

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

    [Fact]
    public void Build_Hibernation_TickedOnADesktop_UntickedOnALaptop()
    {
        var desktop = Make();
        ((FakePowercfg)desktop.Ctx.Powercfg).Battery = false;
        var row = Row(desktop.Builder.Build(Scan(T("hibernation-file", 13_000)), Now), "space.hibernation")!;
        Assert.True(row.DefaultOn);
        Assert.Equal("Fast startup turns off.", row.Cost);

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
            new ResolvedItem("old-installers", @"C:\Users\x\Downloads\b.iso", 900, Now.AddDays(-90)),
        }, null));

        var row = Row(kit.Builder.Build(scan, Now), "space.downloads")!;

        Assert.Equal(1000, row.Bytes);
        Assert.Equal(@"C:\Users\x\Downloads\b.iso", row.Files[0].Path);
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
}
