using System;
using BriskEngine.Actions.Rules;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

public sealed class SecurityRowRuleTests
{
    private static string Profile(string p) => $@"{SecurityPosture.FirewallRoot}\{p}";

    [Fact]
    public void Firewall_NoFinding_WhenEveryProfileIsOnOrUnread()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetInt(Profile("DomainProfile"), "EnableFirewall", 1);
        Assert.Null(new FirewallOnRule(new FakeRunner()).Detect(ctx));
    }

    [Fact]
    public void Firewall_TurnsOnOnlyTheProfilesThatAreOff_AndUndoTurnsThoseOff()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetInt(Profile("DomainProfile"), "EnableFirewall", 1);
        reg.SetInt(Profile("PublicProfile"), "EnableFirewall", 0);
        var runner = new ScriptedRunner((_, args) =>
        {
            if (args == "advfirewall set publicprofile state on") reg.SetInt(Profile("PublicProfile"), "EnableFirewall", 1);
            if (args == "advfirewall set publicprofile state off") reg.SetInt(Profile("PublicProfile"), "EnableFirewall", 0);
            return (0, "");
        });
        var rule = new FirewallOnRule(runner);
        Assert.NotNull(rule.Detect(ctx));

        var prior = rule.Fix(ctx);
        Assert.Equal(new[] { "netsh.exe advfirewall set publicprofile state on" }, runner.Commands);
        Assert.Null(rule.Detect(ctx));

        rule.Undo(ctx, prior);
        Assert.Equal(0, reg.GetInt(Profile("PublicProfile"), "EnableFirewall"));
    }

    [Fact]
    public void Firewall_NetshFailure_Throws()
    {
        var ctx = TestContext.Empty();
        ((FakeRegistry)ctx.Registry).SetInt(Profile("StandardProfile"), "EnableFirewall", 0);
        var rule = new FirewallOnRule(new ScriptedRunner((_, _) => (1, "")));
        Assert.Throws<InvalidOperationException>(() => rule.Fix(ctx));
    }

    [Fact]
    public void RealTime_OfferedOnlyWhenOffAndNoOtherAntivirusIsOn()
    {
        var ctx = TestContext.Empty();
        var defender = (FakeDefender)ctx.Defender;
        var rule = new DefenderRealtimeRule(new FakeRunner());

        defender.Status = new DefenderStatus(true, true, "Normal", 0, Array.Empty<string>(), null, null, 0);
        Assert.Null(rule.Detect(ctx));

        defender.Status = new DefenderStatus(false, true, "Normal", 0, Array.Empty<string>(), null, null, 0);
        Assert.NotNull(rule.Detect(ctx));

        defender.Products.Add(new AntivirusProduct("McAfee LiveSafe", IsDefender: false, Enabled: true, DefinitionsOutdated: false));
        Assert.Null(rule.Detect(ctx));
    }

    [Fact]
    public void RealTime_FixAndUndoCallSetMpPreference()
    {
        var runner = new FakeRunner();
        var rule = new DefenderRealtimeRule(runner);
        var prior = rule.Fix(TestContext.Empty());
        rule.Undo(TestContext.Empty(), prior);
        Assert.Contains(runner.Commands, c => c.Contains("-DisableRealtimeMonitoring $false"));
        Assert.Contains(runner.Commands, c => c.Contains("-DisableRealtimeMonitoring $true"));
    }
}
