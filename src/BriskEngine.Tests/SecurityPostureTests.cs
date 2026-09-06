using System;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using Xunit;

namespace BriskEngine.Tests;

/// The positive lines. Every assertion here is about one of the two red
/// lines the posture block exists under: a null never reads as fine (red
/// line 2), and nothing in the record sums to a verdict (red line 4) — there
/// is no "secure" member to assert about, on purpose.
public class SecurityPostureTests
{
    private static (DiagnosticContext ctx, FakeRegistry reg, FakeDefender defender) Context()
    {
        var reg = new FakeRegistry();
        var defender = new FakeDefender();
        return (TestContext.Empty() with { Registry = reg, Defender = defender }, reg, defender);
    }

    private static void Firewall(FakeRegistry reg, int? domain, int? standard, int? pub)
    {
        if (domain is { } d) reg.SetInt($@"{SecurityPosture.FirewallRoot}\DomainProfile", "EnableFirewall", d);
        if (standard is { } s) reg.SetInt($@"{SecurityPosture.FirewallRoot}\StandardProfile", "EnableFirewall", s);
        if (pub is { } p) reg.SetInt($@"{SecurityPosture.FirewallRoot}\PublicProfile", "EnableFirewall", p);
    }

    /// The field-test fixture: the maintainer's own machine as it read on
    /// 2026-09-05. Four rows fine and the one the uac-prompts rule fires on
    /// not fine — the shape the whole block was designed against.
    [Fact]
    public void TheMaintainersMachine_ReadsFineEverywhereButUac()
    {
        var (ctx, reg, defender) = Context();
        Firewall(reg, 1, 1, 1);
        reg.SetInt(SecurityPosture.TerminalServerKey, "fDenyTSConnections", 1);
        reg.SetInt(UacPromptRule.KeyPath, UacPromptRule.EnableLua, 1);
        reg.SetInt(UacPromptRule.KeyPath, UacPromptRule.ConsentPromptBehaviorAdmin, 0);
        reg.SetInt(UacPromptRule.KeyPath, UacPromptRule.PromptOnSecureDesktop, 0);
        defender.Status = new DefenderStatus(true, true, "Normal", 5, new[] { "x" }, null, null, 0);

        var p = SecurityPosture.Read(ctx);

        Assert.True(p.Firewall);
        Assert.True(p.RemoteDesktopOff);
        Assert.False(p.UacPrompts);
        Assert.True(p.DefenderRealTime);
        Assert.True(p.DefenderTamper);
    }

    /// Red line 2, at the source: an empty registry and a probe that answered
    /// nothing produce nulls, and not one of them is a true.
    [Fact]
    public void NothingReadable_IsNullEverywhere_NeverFine()
    {
        var (ctx, _, _) = Context();
        var p = SecurityPosture.Read(ctx);
        Assert.Null(p.Firewall);
        Assert.Null(p.RemoteDesktopOff);
        Assert.Null(p.UacPrompts);
        Assert.Null(p.DefenderRealTime);
        Assert.Null(p.DefenderTamper);
    }

    /// The firewall row is three reads folded into one line, and the two ways
    /// that fold can go wrong are opposite: a profile that reads OFF makes the
    /// line false, a profile that was not read makes it null. Neither is the
    /// other.
    ///
    /// The third row is the tie-break, and it is the reason the fold asks
    /// false before null: with one profile off and another unread, brisk HAS
    /// read a profile that is off, and no missing reading takes that back.
    [Fact]
    public void OneFirewallProfileOff_IsFalse_AndOneUnread_IsNull()
    {
        var (off, reg1, _) = Context();
        Firewall(reg1, 1, 0, 1);
        Assert.False(SecurityPosture.Read(off).Firewall);

        var (unread, reg2, _) = Context();
        Firewall(reg2, 1, null, 1);
        Assert.Null(SecurityPosture.Read(unread).Firewall);

        var (mixed, reg3, _) = Context();
        Firewall(reg3, 1, 0, null);
        Assert.False(SecurityPosture.Read(mixed).Firewall);
    }

    /// fDenyTSConnections reads inverted — 1 denies connections — so the row
    /// is named for what it reports rather than for the value it reads.
    [Fact]
    public void RemoteDesktopAllowed_IsFalse()
    {
        var (ctx, reg, _) = Context();
        reg.SetInt(SecurityPosture.TerminalServerKey, "fDenyTSConnections", 0);
        Assert.False(SecurityPosture.Read(ctx).RemoteDesktopOff);
    }

    /// The UAC row asks the rule, so it inherits the rule's own inversion
    /// (spec red line 3): a written prompting value is fine, and NOTHING
    /// written is not read rather than fine — the row cannot go green on a
    /// key the process never saw.
    [Fact]
    public void UacWithDefaultsWritten_IsFine_AndNothingWritten_IsUnread()
    {
        var (fine, reg, _) = Context();
        reg.SetInt(UacPromptRule.KeyPath, UacPromptRule.ConsentPromptBehaviorAdmin, 5);
        Assert.True(SecurityPosture.Read(fine).UacPrompts);

        var (unread, _, _) = Context();
        Assert.Null(SecurityPosture.Read(unread).UacPrompts);
    }

    /// One WMI read per scan. The rule and the posture block share the
    /// context's memo, so the second reader gets the first reader's answer
    /// and the probe is asked once.
    [Fact]
    public void ReadTwice_HitsTheMemo()
    {
        var (ctx, _, defender) = Context();
        defender.Status = new DefenderStatus(true, true, "Normal", 0, Array.Empty<string>(), null, null, 0);
        var a = SecurityPosture.Read(ctx);
        var b = SecurityPosture.Read(ctx);
        Assert.Same(a, b);
        Assert.Equal(1, defender.Reads);
    }
}
