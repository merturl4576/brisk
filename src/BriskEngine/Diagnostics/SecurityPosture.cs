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
    /// All three profiles on; null when any profile was not read; false
    /// when any read profile is off.
    ///
    /// Null is asked FIRST, and that order is the whole point: an unread
    /// profile beside two that read on must not answer "on for all three",
    /// which is what a plain three-way && would have said about a machine
    /// brisk had only two thirds of an answer for.
    public bool? Firewall =>
        FirewallDomain is null || FirewallPrivate is null || FirewallPublic is null ? null
        : FirewallDomain == true && FirewallPrivate == true && FirewallPublic == true;
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
