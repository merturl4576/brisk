using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Brisk.Services;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

public class FixAllServiceTests
{
    [Fact]
    public void Run_FixesAutoAndConfirm_SkipsAdviseAndUnfixable()
    {
        var host = new FakeEngineHost();
        var snapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
            TestData.Finding("visual-effects", cat: RuleCategory.Confirm, canFix: true),
            TestData.Finding("no-fix", cat: RuleCategory.Confirm, canFix: false),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
        });

        var result = new FixAllService(host).Run(snapshot);

        Assert.Equal(new[] { "power-plan", "visual-effects" }, host.Fixed);
        Assert.Equal(2, result.Attempted);
        Assert.Equal(2, result.Applied);
        Assert.Equal(new[] { "power-plan", "visual-effects" },
            result.FixedRules.Select(f => f.RuleId).ToArray());
        Assert.Empty(result.DisabledStartup);
    }

    [Fact]
    public void Run_StartupBloat_ReportsDisabledHeavyItemsByName()
    {
        var inner = new FakeEngineHost();
        inner.Startup.Add(new StartupEntry("HKCU", "Discord", true, true));
        inner.Startup.Add(new StartupEntry("HKCU", "Steam", true, true));
        inner.Startup.Add(new StartupEntry("HKCU", "MyTool", true, false));
        var host = new StartupDisablingHost(inner);
        var snapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("startup-bloat", cat: RuleCategory.Confirm, canFix: true),
        });

        var result = new FixAllService(host).Run(snapshot);

        Assert.Equal(new[] { "startup-bloat" }, inner.Fixed);
        Assert.Equal(new[] { "Discord", "Steam" }, result.DisabledStartup);
        Assert.Empty(result.FixedRules);   // reported per item, not as a rule line
        Assert.Equal(1, result.Applied);
    }

    [Fact]
    public void Run_StartupBloat_NoObservableDiff_FallsBackToRuleLine()
    {
        // FakeEngineHost.Fix does not mutate the startup list, so no per-item
        // diff is observable — the fix must still be reported honestly.
        var host = new FakeEngineHost();
        host.Startup.Add(new StartupEntry("HKCU", "Discord", true, true));
        var snapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("startup-bloat", cat: RuleCategory.Confirm, canFix: true),
        });

        var result = new FixAllService(host).Run(snapshot);

        Assert.Empty(result.DisabledStartup);
        Assert.Equal("startup-bloat", Assert.Single(result.FixedRules).RuleId);
    }

    [Fact]
    public void Run_CountsFailedFixes_AsAttemptedNotApplied()
    {
        var inner = new FakeEngineHost();
        var host = new FailingFixHost(inner, "visual-effects");
        var snapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
            TestData.Finding("visual-effects", cat: RuleCategory.Confirm, canFix: true),
        });

        var result = new FixAllService(host).Run(snapshot);

        Assert.Equal(2, result.Attempted);
        Assert.Equal(1, result.Applied);
        Assert.Equal("power-plan", Assert.Single(result.FixedRules).RuleId);
    }

    /// Fakes.cs is a locked contract; startup-disable semantics are simulated
    /// with a decorator whose Fix("startup-bloat") disables the heavy items,
    /// exactly like the real StartupBloatRule.Fix does.
    private sealed class StartupDisablingHost : DelegatingHost
    {
        private readonly FakeEngineHost _inner;

        public StartupDisablingHost(FakeEngineHost inner) : base(inner)
        {
            _inner = inner;
        }

        public override FixOutcome Fix(string ruleId)
        {
            if (string.Equals(ruleId, "startup-bloat", StringComparison.OrdinalIgnoreCase))
                for (var i = 0; i < _inner.Startup.Count; i++)
                    if (_inner.Startup[i].KnownHeavy)
                        _inner.Startup[i] = _inner.Startup[i] with { Enabled = false };
            return base.Fix(ruleId);
        }
    }

    private sealed class FailingFixHost : DelegatingHost
    {
        private readonly string _failingRuleId;

        public FailingFixHost(FakeEngineHost inner, string failingRuleId) : base(inner)
        {
            _failingRuleId = failingRuleId;
        }

        public override FixOutcome Fix(string ruleId) =>
            string.Equals(ruleId, _failingRuleId, StringComparison.OrdinalIgnoreCase)
                ? new FixOutcome(false, ruleId)
                : base.Fix(ruleId);
    }

    private abstract class DelegatingHost : IEngineHost
    {
        private readonly FakeEngineHost _inner;

        protected DelegatingHost(FakeEngineHost inner) { _inner = inner; }

        public virtual FixOutcome Fix(string ruleId) => _inner.Fix(ruleId);
        public Task<ScanSnapshot> ScanAsync(IProgress<string>? progress = null,
            CancellationToken ct = default) => _inner.ScanAsync(progress, ct);
        public FixOutcome Undo(string ruleId) => _inner.Undo(ruleId);
        public CleanReport Clean(TargetScanResult scan, bool dryRun) =>
            _inner.Clean(scan, dryRun);
        public IReadOnlyList<UndoableFix> ListUndoable() => _inner.ListUndoable();
        public IReadOnlyList<ActionLogEntry> ReadLog(int max = 200) => _inner.ReadLog(max);
        public IReadOnlyList<StartupEntry> ListStartup() => _inner.ListStartup();
        public bool SetStartupEnabled(string hive, string name, bool enabled) =>
            _inner.SetStartupEnabled(hive, name, enabled);
        public bool RunElevated(string cliArgs) => _inner.RunElevated(cliArgs);
        public bool CreateRestorePoint() => _inner.CreateRestorePoint();
        public long FreeDiskBytes() => _inner.FreeDiskBytes();
        public long LifetimeReclaimedBytes() => _inner.LifetimeReclaimedBytes();
        public bool IsElevated() => _inner.IsElevated();
    }
}
