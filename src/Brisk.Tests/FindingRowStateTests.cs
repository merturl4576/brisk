using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

/// The per-row visual fix lifecycle (UX round 5): Normal → Fixing → Fixed,
/// back to Normal on failure, and the same states driven by fix-all's
/// per-rule progress — regardless of which surface launched the run.
public class FindingRowStateTests
{
    private static Loc EnglishLoc()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        return loc;
    }

    private static HealthViewModel Vm(IEngineHost host, AppState state,
        FixAllService? fixAll = null, Func<DiagnosticFinding, bool>? filter = null) =>
        new(state, host, EnglishLoc(), () => false, fixAll ?? new FixAllService(host),
            filter, morphPause: () => Task.CompletedTask);

    [Fact]
    public async Task FixRow_EntersFixingBeforeTheFixLands_ThenMorphsToFixed()
    {
        var inner = new FakeEngineHost();
        inner.NextSnapshot = TestData.Snapshot(new[]
            { TestData.Finding("power-plan", canFix: true) });
        using var host = new GatedFixHost(inner);
        var state = new AppState(host);
        var vm = Vm(host, state);
        await state.ScanAsync();
        var row = vm.Rows.Single();
        Assert.False(row.IsWorking);
        Assert.False(row.IsFixed);

        var fixTask = vm.FixAsync(row);

        // Fixing is entered synchronously, before any await — the row reacts
        // the instant the button is pressed, not when the fix lands.
        Assert.True(row.IsFixing);
        Assert.True(row.IsWorking);
        Assert.False(row.IsFixed);

        host.Release();
        await fixTask;

        Assert.False(row.IsFixing);
        Assert.True(row.IsFixed);
        Assert.Equal("", vm.Message);
        Assert.Equal(2, inner.ScanCalls);   // the rescan still followed
    }

    [Fact]
    public async Task FixRow_Failure_ReturnsToNormal_AndSurfacesTheMessage()
    {
        var inner = new FakeEngineHost();
        inner.NextSnapshot = TestData.Snapshot(new[]
            { TestData.Finding("power-plan", canFix: true) });
        var host = new FailingFixHost(inner, "power-plan", "power-plan: fix failed");
        var state = new AppState(host);
        var vm = Vm(host, state);
        await state.ScanAsync();
        var row = vm.Rows.Single();

        await vm.FixAsync(row);

        Assert.False(row.IsFixing);
        Assert.False(row.IsFixed);          // back to Normal, not stuck
        Assert.Equal("power-plan: fix failed", vm.Message);
    }

    [Fact]
    public async Task FixRow_DryRun_NeverEntersFixing()
    {
        var loc = EnglishLoc();
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
            { TestData.Finding("power-plan", canFix: true) });
        var state = new AppState(host);
        var vm = new HealthViewModel(state, host, loc, () => true,
            new FixAllService(host), morphPause: () => Task.CompletedTask);
        await state.ScanAsync();
        var row = vm.Rows.Single();

        await vm.FixAsync(row);

        Assert.False(row.IsFixing);
        Assert.False(row.IsFixed);
        Assert.Equal(loc["dryrun.blocked"], vm.Message);
    }

    [Fact]
    public async Task UndoRow_WorksThroughTheUndoingState_ThenBackToNormal()
    {
        var inner = new FakeEngineHost();
        inner.NextSnapshot = TestData.Snapshot(new[]
            { TestData.Finding("visual-effects", cat: RuleCategory.Confirm) });
        inner.Undoable.Add(new UndoableFix("visual-effects", DateTime.UtcNow));
        using var host = new GatedFixHost(inner);
        var state = new AppState(host);
        var vm = Vm(host, state);
        await state.ScanAsync();
        var row = vm.Rows.Single();

        var undoTask = vm.UndoAsync(row);

        Assert.True(row.IsUndoing);
        Assert.True(row.IsWorking);
        Assert.False(row.IsFixing);         // undo wears its own label

        host.Release();
        await undoTask;

        Assert.False(row.IsUndoing);
        Assert.False(row.IsFixed);          // an undone row is Normal again
    }

    [Fact]
    public async Task FixAll_DrivesRowStates_OnEveryPageVm()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
            TestData.Finding("storage-sense", cat: RuleCategory.Confirm, canFix: true),
        });
        var state = new AppState(host);
        var fixAll = new FixAllService(host);   // shared, like the app wires it
        var health = Vm(host, state, fixAll, FindingSections.IsHealth);
        var perf = Vm(host, state, fixAll, FindingSections.IsPerformance);
        await state.ScanAsync();
        var healthRow = health.Rows.Single();   // storage-sense
        var perfRow = perf.Rows.Single();       // power-plan

        // Fix-all launched from the Sağlık page still animates the
        // Performans page's row — progress flows through the shared service.
        await health.FixAllAsync();

        Assert.True(healthRow.IsFixed);
        Assert.False(healthRow.IsWorking);
        Assert.True(perfRow.IsFixed);
        Assert.False(perfRow.IsWorking);
    }

    [Fact]
    public async Task FixAll_FailedRule_LeavesItsRowNormal()
    {
        var inner = new FakeEngineHost();
        inner.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", canFix: true),
            TestData.Finding("visual-effects", canFix: true),
        });
        var host = new FailingFixHost(inner, "visual-effects", "boom");
        var state = new AppState(host);
        var fixAll = new FixAllService(host);
        var vm = Vm(host, state, fixAll);
        await state.ScanAsync();
        var okRow = vm.Rows.Single(r => r.RuleId == "power-plan");
        var failRow = vm.Rows.Single(r => r.RuleId == "visual-effects");

        await vm.FixAllAsync();

        Assert.True(okRow.IsFixed);
        Assert.False(failRow.IsFixed);
        Assert.False(failRow.IsWorking);
    }

    [Fact]
    public void FixAllService_PublishesPerRuleProgress_InOrder()
    {
        var host = new FakeEngineHost();
        var snapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
            TestData.Finding("visual-effects", cat: RuleCategory.Confirm, canFix: true),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
        });
        var svc = new FixAllService(host);
        var events = new List<string>();
        svc.FixingRule += f => events.Add("fixing:" + f.RuleId);
        svc.FixedRule += (f, ok) => events.Add($"fixed:{f.RuleId}:{ok}");

        svc.Run(snapshot);

        Assert.Equal(new[]
        {
            "fixing:power-plan", "fixed:power-plan:True",
            "fixing:visual-effects", "fixed:visual-effects:True",
        }, events);
    }

    [Fact]
    public async Task DoneTitle_PrefersDoneKey_FallsBackToFixedComposition()
    {
        var loc = EnglishLoc();
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", canFix: true),
            TestData.Finding("custom-x", canFix: true),
        });
        var state = new AppState(host);
        var vm = Vm(host, state);
        await state.ScanAsync();

        Assert.Equal(loc["rule.power-plan.done"],
            vm.Rows.Single(r => r.RuleId == "power-plan").DoneTitle);
        Assert.Equal(loc.F("overview.report.fixed", "Title custom-x"),
            vm.Rows.Single(r => r.RuleId == "custom-x").DoneTitle);
    }

    /// Fakes.cs is a locked contract; blocking and failing fixes are
    /// simulated with thin decorators that only override Fix/Undo.
    private class DelegatingHost : IEngineHost
    {
        protected readonly FakeEngineHost Inner;

        protected DelegatingHost(FakeEngineHost inner) { Inner = inner; }

        public virtual FixOutcome Fix(string ruleId) => Inner.Fix(ruleId);
        public virtual FixOutcome Undo(string ruleId) => Inner.Undo(ruleId);
        public Task<ScanSnapshot> ScanAsync(IProgress<string>? progress = null,
            CancellationToken ct = default) => Inner.ScanAsync(progress, ct);
        public CleanReport Clean(TargetScanResult scan, bool dryRun,
                Action<CleanEntry>? onEntry = null) =>
            Inner.Clean(scan, dryRun, onEntry);
        public IReadOnlyList<UndoableFix> ListUndoable() => Inner.ListUndoable();
        public IReadOnlyList<ActionLogEntry> ReadLog(int max = 200) => Inner.ReadLog(max);
        public IReadOnlyList<StartupEntry> ListStartup() => Inner.ListStartup();
        public bool SetStartupEnabled(string hive, string name, bool enabled) =>
            Inner.SetStartupEnabled(hive, name, enabled);
        public bool RunElevated(string cliArgs) => Inner.RunElevated(cliArgs);
        public bool CreateRestorePoint() => Inner.CreateRestorePoint();
        public long FreeDiskBytes() => Inner.FreeDiskBytes();
        public long LifetimeReclaimedBytes() => Inner.LifetimeReclaimedBytes();
        public bool IsElevated() => Inner.IsElevated();
    }

    /// Fix/Undo block on a gate so the test can observe the in-flight state
    /// deterministically instead of racing the thread pool.
    private sealed class GatedFixHost : DelegatingHost, IDisposable
    {
        private readonly ManualResetEventSlim _gate = new(false);

        public GatedFixHost(FakeEngineHost inner) : base(inner) { }

        public void Release() => _gate.Set();
        public void Dispose() => _gate.Dispose();

        public override FixOutcome Fix(string ruleId)
        {
            _gate.Wait();
            return base.Fix(ruleId);
        }

        public override FixOutcome Undo(string ruleId)
        {
            _gate.Wait();
            return base.Undo(ruleId);
        }
    }

    private sealed class FailingFixHost : DelegatingHost
    {
        private readonly string _failingRuleId;
        private readonly string _message;

        public FailingFixHost(FakeEngineHost inner, string failingRuleId,
            string message) : base(inner)
        {
            _failingRuleId = failingRuleId;
            _message = message;
        }

        public override FixOutcome Fix(string ruleId) =>
            string.Equals(ruleId, _failingRuleId, StringComparison.OrdinalIgnoreCase)
                ? new FixOutcome(false, _message)
                : base.Fix(ruleId);
    }
}
