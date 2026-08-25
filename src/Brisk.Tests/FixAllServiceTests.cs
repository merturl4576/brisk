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
    public void HasWork_TrueWithFixableNonAdvise_FalseWithAdviseOnlyOrEmpty()
    {
        var svc = new FixAllService(new FakeEngineHost());

        Assert.True(svc.HasWork(TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
        })));
        Assert.False(svc.HasWork(TestData.Snapshot(new[]
        {
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
            TestData.Finding("no-fix", cat: RuleCategory.Confirm, canFix: false),
        })));
        Assert.False(svc.HasWork(TestData.Snapshot()));
    }

    [Fact]
    public void HasWork_StartupBloat_CountsOnlyWhileHeavyItemsAreStillEnabled()
    {
        var host = new FakeEngineHost();
        var svc = new FixAllService(host);
        var snapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("startup-bloat", cat: RuleCategory.Confirm, canFix: true),
        });

        // Every heavy item already off — the rule fix would be a no-op.
        host.Startup.Add(new StartupEntry("HKCU", "Discord", Enabled: false, KnownHeavy: true));
        host.Startup.Add(new StartupEntry("HKCU", "MyTool", Enabled: true, KnownHeavy: false));
        Assert.False(svc.HasWork(snapshot));

        host.Startup.Add(new StartupEntry("HKCU", "Steam", Enabled: true, KnownHeavy: true));
        Assert.True(svc.HasWork(snapshot));
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

    /// THE GENERIC BUTTON DOES NOT REACH A PRIVACY SETTING. "Fix all (safe)"
    /// is about speed and hygiene; the only thing allowed to turn a privacy
    /// switch off is the Privacy page's own button, or — for the two that
    /// cost the user something — that switch's own control. The predicate
    /// here was `Category != Advise && CanFix`, which is category-blind: the
    /// four telemetry switches are Auto and fixable, so they rode the button
    /// the moment they entered the registry, and `location` and
    /// `activity-history` are Confirm and fixable, so they would have ridden
    /// it too and taken Find my device and Timeline with them.
    [Fact]
    public void Run_LeavesEveryPrivacyFinding_ToThePrivacyPage()
    {
        var host = new FakeEngineHost();
        var snapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
            TestData.Finding("advertising-id", cat: RuleCategory.Auto, canFix: true,
                kind: FindingKind.Notice),
            TestData.Finding("location", cat: RuleCategory.Confirm, canFix: true,
                kind: FindingKind.Notice),
        });

        var result = new FixAllService(host).Run(snapshot);

        Assert.Equal(new[] { "power-plan" }, host.Fixed);
        Assert.Equal(1, result.Attempted);
        Assert.Equal(1, result.Applied);
        Assert.Equal(new[] { "power-plan" },
            result.FixedRules.Select(f => f.RuleId).ToArray());
    }

    /// And the button greys out rather than standing lit over work it will
    /// not do. Without this, HasWork is true on virtually every machine,
    /// because a machine nobody has touched has all four switches on.
    [Fact]
    public void HasWork_IsFalse_WhenOnlyPrivacyFindingsAreFixable()
    {
        var svc = new FixAllService(new FakeEngineHost());

        Assert.False(svc.HasWork(TestData.Snapshot(new[]
        {
            TestData.Finding("advertising-id", cat: RuleCategory.Auto, canFix: true,
                kind: FindingKind.Notice),
            TestData.Finding("speech-typing", cat: RuleCategory.Auto, canFix: true,
                kind: FindingKind.Notice),
            TestData.Finding("activity-history", cat: RuleCategory.Confirm, canFix: true,
                kind: FindingKind.Notice),
        })), "the fix-all button is lit over privacy findings it must not touch");

        Assert.True(svc.HasWork(TestData.Snapshot(new[]
        {
            TestData.Finding("advertising-id", cat: RuleCategory.Auto, canFix: true,
                kind: FindingKind.Notice),
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
        })), "one real fixable finding beside the privacy ones and the button is dark");
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
        public CleanReport Clean(TargetScanResult scan, bool dryRun,
                Action<CleanEntry>? onEntry = null) =>
            _inner.Clean(scan, dryRun, onEntry);
        public IReadOnlyList<UndoableFix> ListUndoable() => _inner.ListUndoable();
        public IReadOnlyList<ActionLogEntry> ReadLog(int max = 200) => _inner.ReadLog(max);
        public IReadOnlyList<StartupEntry> ListStartup() => _inner.ListStartup();
        public bool SetStartupEnabled(string hive, string name, bool enabled) =>
            _inner.SetStartupEnabled(hive, name, enabled);
        public bool RunElevated(string cliArgs) => _inner.RunElevated(cliArgs);
        public bool CreateRestorePoint() => _inner.CreateRestorePoint();
        public long FreeDiskBytes() => _inner.FreeDiskBytes();
        public long LifetimeReclaimedBytes() => _inner.LifetimeReclaimedBytes();
        public FixOutcome KeepDisplayFix() => _inner.KeepDisplayFix();
        public SessionIdentity Session() => _inner.Session();
        public bool IsElevated() => _inner.IsElevated();
    }
}
