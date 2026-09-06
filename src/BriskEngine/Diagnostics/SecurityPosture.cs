using System;
using BriskEngine.Diagnostics.Rules.Privacy;

namespace BriskEngine.Diagnostics;

/// The positive lines. Each member is true when the value reads as
/// Windows' safer setting, false when it reads as the other, null when
/// brisk did not read it. Null never renders green (spec red line 2), and
/// nothing here sums to a verdict (red line 4).
///
/// THERE IS NO MEMBER THAT ADDS THESE UP, and there is not going to be one:
/// five rows brisk read are five rows brisk read, and a sixth member folding
/// them into a score, a badge or a count would be brisk saying the machine
/// is secure off five registry values and one WMI answer. Firewall below is
/// the one fold, and it folds three readings of ONE setting rather than
/// readings of different ones.
public sealed record SecurityPostureReport(
    bool? FirewallDomain,
    bool? FirewallPrivate,
    bool? FirewallPublic,
    bool? RemoteDesktopOff,
    bool? UacPrompts,
    bool? DefenderRealTime,
    bool? DefenderTamper)
{
    /// A report where brisk read nothing: five rows that each say "brisk
    /// could not read this". It is spelled once here because the seven nulls
    /// written out by hand are the kind of literal that acquires a stray
    /// true — and an all-true default would put "firewall on for all three
    /// profiles" on a surface built from a scan that read no firewall, which
    /// is the spec's second red line broken by a convenience.
    public static readonly SecurityPostureReport NothingRead =
        new(null, null, null, null, null, null, null);

    /// True when all three profiles read on; false as soon as any ONE of
    /// them reads off; null only when nothing read off and at least one
    /// profile was not read at all.
    ///
    /// A MEASURED FALSE WINS OVER AN UNREAD SIBLING, and that order is the
    /// whole point. (on, off, unread) is false: brisk read a profile that is
    /// off, and no missing third reading can take that back — the row has
    /// something to say and says it. Only when nothing measured bad does the
    /// unread profile decide, and then it answers null rather than "on for
    /// all three", which is what a plain three-way && would have said about a
    /// machine brisk had two thirds of an answer for. Null never renders
    /// green (spec red line 2), so neither branch can flatter a machine.
    public bool? Firewall =>
        FirewallDomain == false || FirewallPrivate == false || FirewallPublic == false ? false
        : FirewallDomain is null || FirewallPrivate is null || FirewallPublic is null ? null
        : true;
}

/// Reads the posture once per scan (memoized on the context) so the rules,
/// the snapshot and the CLI line share one reading of one machine.
public static class SecurityPosture
{
    public const string FirewallRoot =
        @"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
    public const string TerminalServerKey =
        @"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server";
    private const string MemoKey = "posture";

    /// GetOrAdd's value factory is not atomic and DiagnosticContext reserves
    /// the right to run rules in parallel, so two readers can race into
    /// ReadUncached. The worst case is a second registry round trip whose
    /// report is dropped — never a wrong answer, because both readers read
    /// the same machine.
    public static SecurityPostureReport Read(DiagnosticContext ctx) =>
        (SecurityPostureReport)ctx.Memo.GetOrAdd(MemoKey, _ => ReadUncached(ctx));

    private static SecurityPostureReport ReadUncached(DiagnosticContext ctx)
    {
        // Guarded per value rather than per block: OpenSubKey throws on a key
        // this process is refused, and a throw on one profile must cost that
        // profile's reading and nothing else. An absent value and a refused
        // key both answer null, which is the same sentence to a reader — brisk
        // did not read this — and neither is ever a false.
        bool? Flag(string key, string value, int fineWhen)
        {
            int? read;
            try { read = ctx.Registry.GetInt(key, value); }
            catch (Exception) { return null; }
            return read is null ? null : read == fineWhen;
        }

        // THE UAC ROW ASKS THE RULE, so the row and the finding cannot come to
        // disagree about one machine. The two raw reads in front of it are
        // what tell "not read" from "fine": the rule inverts the family's
        // absence rule (spec red line 3) and answers false for a machine that
        // wrote nothing, which is the right answer for a FINDING and the wrong
        // one for a row that must never go green on a value nobody read.
        bool? uac;
        try
        {
            var lua = ctx.Registry.GetInt(UacPromptRule.KeyPath, UacPromptRule.EnableLua);
            var admin = ctx.Registry.GetInt(UacPromptRule.KeyPath, UacPromptRule.ConsentPromptBehaviorAdmin);
            uac = lua is null && admin is null ? null : !new UacPromptRule().IsOn(ctx);
        }
        catch (Exception)
        {
            uac = null;
        }

        // Memoized on the same context the rule read it through, so this is
        // the rule's answer and not a second WMI round trip.
        var defender = DefenderRule.Status(ctx);

        return new SecurityPostureReport(
            Flag($@"{FirewallRoot}\DomainProfile", "EnableFirewall", 1),
            Flag($@"{FirewallRoot}\StandardProfile", "EnableFirewall", 1),
            Flag($@"{FirewallRoot}\PublicProfile", "EnableFirewall", 1),
            // fDenyTSConnections reads inverted: 1 DENIES remote connections,
            // which is the row's fine state and why the member is named for
            // what it reports rather than for the value it reads.
            Flag(TerminalServerKey, "fDenyTSConnections", 1),
            uac,
            defender?.RealTimeOn,
            defender?.TamperOn);
    }
}
