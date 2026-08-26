using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// How many entries Windows keeps under the two UserAssist keys that record
/// the programs you have started. The count is of both keys: reading one of
/// them would under-report by whatever sits in the other and would do it
/// without saying so.
///
/// THE ENTRY NAMES ARE NOT DECODED, and that is a ruling rather than an
/// oversight. They are ROT13-encoded paths, so decoding them would be a short
/// job — and it would produce exactly the contents the spec's second red line
/// forbids, inside the rule whose whole job is to report a number instead of
/// them. Code that decodes is also code a reviewer then has to prove never
/// leaks; code that never decodes needs no such proof. brisk counts the
/// entries and reads none of them.
///
/// What the count therefore is: every value under those two keys, whatever it
/// is. brisk cannot leave out the entries that are not program records
/// without reading names to recognise them, and it will not read names. The
/// evidence says so rather than letting the number imply a precision the read
/// does not have.
public sealed class RunHistoryRule : PrivacyDisclosureRule
{
    private const string UserAssist =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist";

    /// The two keys, in the order the spec names them. Deliberately not named
    /// individually: this rule does not depend on which of them is which, and
    /// a constant called "executables" would be a claim about a GUID that
    /// nothing here establishes.
    public static readonly IReadOnlyList<string> CountKeyPaths = new[]
    {
        UserAssist + @"\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count",
        UserAssist + @"\{F4E57C4B-2036-45F0-A9AB-443BCFE33D9F}\Count",
    };

    public override string Id => "run-history";

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var count = CountKeyPaths.Sum(keyPath => ValueNames(ctx, keyPath).Count);
        if (count == 0) return Unread();

        var counted = count.ToString(CultureInfo.InvariantCulture);
        return Disclosure(
            $"rule.{Id}.title",
            "Windows keeps a record of the programs you have started",
            $"rule.{Id}.evidence",
            "Windows keeps a running record of the programs you start. brisk " +
            $"counted {counted} entries across the two keys it keeps them in. " +
            "The entries are stored encoded and brisk does not decode them — it " +
            "counts them and reads none, so no program name is read or reported, " +
            "and the count also covers whatever else Windows keeps in those two keys.",
            new[] { counted },
            new Headline(
                counted, "entries in Windows' record of what you have started",
                $"rule.{Id}.headline.value", new[] { counted },
                $"rule.{Id}.headline.caption", Array.Empty<string>()));
    }

    /// Nothing counted. The read cannot tell a key that is not there from a
    /// key with nothing in it, and a key it was refused was already folded
    /// into the same empty answer — so brisk names no reason for the silence
    /// and reports no number for it. No headline either: the headline would
    /// be the count brisk does not have.
    private DiagnosticFinding Unread() => Disclosure(
        $"rule.{Id}.title.unread",
        "The number of records of programs you have started could not be established",
        $"rule.{Id}.evidence.unread",
        "brisk looked where Windows keeps its record of the programs you start " +
        "and found nothing there to count. A record with nothing in it and a " +
        "record brisk could not read look the same from here, so brisk does " +
        "not report a count of none.");

    /// A key the process may not open throws rather than answering empty, and
    /// letting that escape would reach EngineHost's catch-all and drop the
    /// whole finding. So a refusal on one key leaves the other's entries
    /// counted. KNOWN AND NOT CLAIMED AWAY: the copy does not distinguish a
    /// refused key from an empty one, so a refusal narrows the count without
    /// saying it did. Both keys are under HKCU, which is why that is a
    /// disclosed limit rather than the shape of the sentence.
    private static IReadOnlyList<string> ValueNames(DiagnosticContext ctx, string keyPath)
    {
        try { return ctx.Registry.GetValueNames(keyPath); }
        catch (Exception) { return Array.Empty<string>(); }
    }
}
