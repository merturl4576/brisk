using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Actions;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Logging;
using BriskEngine.Models;
using BriskEngine.Safety;
using Xunit;

namespace BriskEngine.Tests;

public sealed class ActionRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-run-").FullName;
    private readonly DiagnosticContext _ctx;
    private readonly FakeRegistry _reg;
    private readonly FakeRecycler _recycler = new();
    private readonly FixJournal _journal;
    private readonly DeferredCleanQueue _deferred;
    private readonly RunStore _runs;
    private readonly FakeUi _ui = new();
    private bool _elevated = true;
    private Func<string, (long, DateTime?)?> _stat = ActionRunner.StatFile;

    public ActionRunnerTests()
    {
        _ctx = TestContext.Empty(Path.Combine(_root, "data"));
        _reg = (FakeRegistry)_ctx.Registry;
        _journal = new FixJournal(Path.Combine(_root, "journal.jsonl"));
        _deferred = new DeferredCleanQueue(Path.Combine(_root, "deferred.json"));
        _runs = new RunStore(Path.Combine(_root, "runs"));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private ActionRunner Runner()
    {
        var rules = new ActionRules(new ActionProbes(new FakeRunner(), _ui, new FakeOverlay(), new FakeFeatures(),
            new FakeTasks(), new StartupManager(_reg, null), () => false));
        var cleaner = new CleanRunner(new SafetyValidator(), _recycler, new ActionLog(Path.Combine(_root, "log.jsonl")),
            new FakeRunner(), () => _elevated, null, () => 0, new WindowsDeleter());
        return new ActionRunner(_ctx, rules, _journal, cleaner, _deferred, _runs, () => _elevated, s => _stat(s));
    }

    private static ActionItem Row(string id, long? bytes = null, IReadOnlyList<ActionFile>? files = null) =>
        ActionPlanBuilder.ItemFrom(ActionCatalog.Find(id), bytes, files: files);

    private static ActionPlan Plan(ScanResult scan, params ActionItem[] items) => new(DateTime.UtcNow,
        items.GroupBy(i => i.Group).Select(g => new ActionGroup(g.Key, g.ToList())).ToList(), scan);

    private static ActionSelection Select(params string[] ids) => new(
        ids.ToHashSet(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static readonly ScanResult NoScan = new(Array.Empty<TargetScanResult>());

    [Fact]
    public void Run_AppliesTickedRows_AndLeavesUntickedOnesAlone()
    {
        var plan = Plan(NoScan, Row("privacy.ads"), Row("privacy.copilot"));
        var run = Runner().Run(plan, Select("privacy.ads"));

        var row = Assert.Single(run.Rows);
        Assert.Equal(RowOutcome.Done, row.Outcome);
        Assert.Equal(ReadBackState.Held, row.ReadBack);
        Assert.Equal(0, _reg.GetInt(@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled"));
        Assert.Null(_reg.GetInt(@"HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot"));
        Assert.Contains("advertising-id", run.UndoableRuleIds);
    }

    [Fact]
    public void Run_RowNeedingAdmin_IsSkippedWhenNotElevated()
    {
        _elevated = false;
        var plan = Plan(NoScan, Row("privacy.error-reports"));
        var row = Assert.Single(Runner().Run(plan, Select("privacy.error-reports")).Rows);

        Assert.Equal(RowOutcome.Skipped, row.Outcome);
        Assert.Equal(ActionRunner.NeedsAdmin, row.Reason);
        Assert.Null(_reg.GetInt(@"HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled"));
    }

    [Fact]
    public void Run_ARuleRefusedHalfway_IsRolledBack()
    {
        // search-web-results writes a policy value, then a second key; the
        // second key refuses, so the first write must not survive.
        _reg.DenyWriteKeys.Add(SearchWebResultsRule.LegacyKey);
        var plan = Plan(NoScan, Row("privacy.start-web"));

        var row = Assert.Single(Runner().Run(plan, Select("privacy.start-web")).Rows);

        Assert.Equal(RowOutcome.Skipped, row.Outcome);
        Assert.Null(_reg.GetInt(SearchWebResultsRule.PolicyKey, SearchWebResultsRule.PolicyValue));
        Assert.Null(_journal.LastUndoablePriorState("search-web-results"));
    }

    [Fact]
    public void Run_RecoversAPendingWriteAheadLogFirst()
    {
        const string key = @"HKCU\Software\BriskTest";
        _reg.SetInt(key, "A", 1);
        var crashed = new WriteAheadRegistry(_reg, Path.Combine(_ctx.DataDirectory, "registry-wal.jsonl"));
        crashed.SetInt(key, "A", 0);   // a previous run died here

        Runner().Run(Plan(NoScan), Select());

        Assert.Equal(1, _reg.GetInt(key, "A"));
    }

    [Fact]
    public void Run_RestartRows_ArePendingRestart()
    {
        _reg.SetInt(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 1);
        var row = Assert.Single(Runner().Run(Plan(NoScan, Row("speed.gpu-scheduling")), Select("speed.gpu-scheduling")).Rows);
        Assert.Equal(RowOutcome.PendingRestart, row.Outcome);
        Assert.Equal(2, _reg.GetInt(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"));
    }

    [Fact]
    public void Run_DeletesACacheForReal()
    {
        var dir = Directory.CreateTempSubdirectory("brisk-run-cache-").FullName;   // under %TEMP%
        File.WriteAllBytes(Path.Combine(dir, "x.bin"), new byte[64]);
        var target = CleanupTargetRegistry.All.Single(t => t.Id == "user-temp");
        var scan = new ScanResult(new[] { new TargetScanResult(target,
            new[] { new ResolvedItem("user-temp", dir, 64, DateTime.UtcNow) }, null) });

        var row = Assert.Single(Runner().Run(Plan(scan, Row("space.caches", 64)), Select("space.caches")).Rows);

        Assert.Equal(RowOutcome.Done, row.Outcome);
        Assert.Equal(64, row.BytesFreed);
        Assert.False(Directory.Exists(dir));
        Assert.Empty(_recycler.Recycled);
    }

    [Fact]
    public void Run_TargetSkippedForRunningApp_IsDeferredNotCleaned()
    {
        var target = CleanupTargetRegistry.All.Single(t => t.Id == "chrome-cache");
        var scan = new ScanResult(new[] { new TargetScanResult(target,
            new[] { new ResolvedItem("chrome-cache", @"C:\fake\chrome\cache", 500, DateTime.UtcNow) },
            "chrome is running — close it to include this target") });

        var row = Assert.Single(Runner().Run(Plan(scan, Row("space.caches", 500)), Select("space.caches")).Rows);

        Assert.Equal(RowOutcome.Done, row.Outcome);
        Assert.Equal(500, row.BytesDeferred);
        Assert.Equal("chrome-cache", Assert.Single(_deferred.Pending()).TargetId);
    }

    [Fact]
    public void Run_AnAppThatOpenedAfterThePlan_IsDeferredToo()
    {
        ((FakeRunningApps)_ctx.RunningApps).Running.Add("chrome");
        var target = CleanupTargetRegistry.All.Single(t => t.Id == "chrome-cache");
        var scan = new ScanResult(new[] { new TargetScanResult(target,
            new[] { new ResolvedItem("chrome-cache", @"C:\fake\chrome\cache", 500, DateTime.UtcNow) }, null) });

        var row = Assert.Single(Runner().Run(Plan(scan, Row("space.caches", 500)), Select("space.caches")).Rows);

        Assert.Equal(500, row.BytesDeferred);
        Assert.Single(_deferred.Pending());
    }

    [Fact]
    public void Run_DownloadsFileChangedSincePlan_IsSkipped()
    {
        const string path = @"C:\Users\x\Downloads\setup.exe";
        var written = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var target = CleanupTargetRegistry.All.Single(t => t.Id == "old-installers");
        var scan = new ScanResult(new[] { new TargetScanResult(target,
            new[] { new ResolvedItem("old-installers", path, 100, written) }, null) });
        _stat = _ => (200, written);   // grew since the plan
        var plan = Plan(scan, Row("space.downloads", 100, new[] { new ActionFile(path, 100, written) }));
        var selection = new ActionSelection(new HashSet<string> { "space.downloads" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path });

        var row = Assert.Single(Runner().Run(plan, selection).Rows);

        Assert.Equal(RowOutcome.Skipped, row.Outcome);
        Assert.Contains("changed", row.Reason);
        Assert.Equal(0, row.BytesFreed);
    }

    [Fact]
    public void Run_IsSaved_AndUndoReversesItInReverse()
    {
        var runner = Runner();
        var run = runner.Run(Plan(NoScan, Row("privacy.ads"), Row("speed.animations")),
            Select("privacy.ads", "speed.animations"));
        Assert.False(_ui.Minimize);
        Assert.NotNull(_runs.Load(run.RunId));
        Assert.Equal(run.RunId, _runs.Latest()!.RunId);

        var undone = runner.Undo(run.RunId);

        Assert.All(undone, r => Assert.Equal(RowOutcome.Done, r.Outcome));
        Assert.Equal("window-animations", undone[0].RowId);   // last applied, first undone
        Assert.True(_ui.Minimize);
        Assert.Null(_reg.GetInt(@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled"));
    }

    [Fact]
    public void Undo_UnknownRun_ReturnsNothing() =>
        Assert.Empty(Runner().Undo("no-such-run"));
}
