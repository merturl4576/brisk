using System.Collections.Generic;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// The report-only half of the privacy topic: what Windows has already
/// written down about this machine, counted. Nothing here is a switch and
/// nothing here has a fix — Advise is the consent level FixRunner refuses to
/// apply a fix for at all, so "brisk shows the number and nothing else" is a
/// property of the build rather than a promise in the copy.
///
/// Two rules govern every subclass and neither is negotiable.
///
/// NUMBERS, NEVER CONTENTS. A count of USB devices may be reported; a device
/// name may not. A count of program records may be reported; the programs may
/// not. The names go into key paths and into nothing a reader ever sees; for
/// the two rules that read names at all, PrivacyDisclosureRuleTests plants
/// recognisable ones and reads every string their findings can carry back
/// out again.
///
/// AN UNREADABLE READ REPORTS UNREADABLE, NEVER ZERO. IRegistryProbe answers
/// an empty list for a key that is not there and for a key with nothing in it
/// alike, so a subclass counting an empty answer cannot tell those apart and
/// must not pick one. IDeliveryOptimizationProbe carries no such ambiguity
/// — it answers null for a counter it could not read — and the same rule
/// follows from either: the finding states no number, and carries no
/// Headline — a headline is what a finding leads with, and leading with a
/// reading that never arrived is the same lie in a larger font.
///
/// The Headline is also what makes these unlike the six switches beside them:
/// RevelationPicker takes findings that carry one, so a disclosure with a
/// real reading is eligible to lead a scan's presentation where a switch
/// never was. Where in that order they belong is a later task of this wave;
/// today they sort after RevelationPicker.Priority's five named rules.
///
/// WHAT A ZERO MEANS IS THE SUBCLASS'S OWN CALL. The three that count what
/// Windows wrote down report every count they can make, and an empty read is
/// not one of those — it is the ambiguity above. DeliveryOptimizationRule's
/// probe hands it an unambiguous zero, and it reports nothing at all for
/// that, because a machine that uploaded no bytes has nothing to disclose.
/// It still reports the unreadable case, which is the whole distinction.
public abstract class PrivacyDisclosureRule : AdviseRuleBase
{
    /// The shape they all share, in one place so that Kind, Severity and the
    /// impact figure cannot drift between them. Everything that differs — the
    /// keys, the prose, the number — is passed in.
    protected DiagnosticFinding Disclosure(
        string titleKey, string title, string evidenceKey, string evidence,
        IReadOnlyList<string>? evidenceArgs = null, Headline? headline = null) =>
        new(Id, titleKey, title, evidence,
            // Info, one star. The impact scale measures expected PERFORMANCE
            // impact and a record Windows keeps costs none — one rather than
            // zero only because the field is documented 1..5, and a surface
            // reusing the finding row renders a meter over whatever number it
            // is handed.
            Severity.Info, Category, ImpactStars: 1, CanFix: false,
            FixDescription: null,
            EvidenceKey: evidenceKey, EvidenceArgs: evidenceArgs,
            Headline: headline,
            // Privacy is a second axis. brisk shows it and never grades it,
            // and these three are the part of it brisk only shows.
            Kind: FindingKind.Notice);
}
