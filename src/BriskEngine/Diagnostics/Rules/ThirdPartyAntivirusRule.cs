using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

/// Who holds the antivirus job, when it is not Defender. The first outside
/// tester's laptop (2026-09-09) wore a red McAfee shield in its tray, and
/// brisk's launch died with "cancelled by the user" for a reason nobody
/// could name. A third-party antivirus is the commonest thing on a consumer
/// laptop that both slows every file open and blocks an unsigned program
/// without a word — and brisk had no sentence for it.
///
/// Report-only and free: a product someone chose is a fact, not a fault,
/// so this is a Hygiene notice and the gauge does not move. The severity is
/// the sentence's weight, not a charge: a product that reports itself off —
/// the expired-trial shape, listed as the machine's antivirus and scanning
/// nothing — is a Warning, and so are definitions Windows calls out of
/// date. brisk removes nothing; every variant says so.
public sealed class ThirdPartyAntivirusRule : AdviseRuleBase
{
    public override string Id => "third-party-av";

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        List<AntivirusProduct> others;
        try
        {
            others = ctx.Defender.Registered().Where(p => !p.IsDefender).ToList();
        }
        catch (Exception)
        {
            return null;   // a table brisk could not read is silence, not a verdict
        }
        if (others.Count == 0) return null;

        var names = string.Join(", ", others.Select(Label));
        // The worst state decides the sentence: off beats out of date beats on.
        if (others.Any(p => !p.Enabled))
            return Finding(names, "off", Severity.Warning, stars: 3,
                $"{names} is listed as this machine's antivirus and reports itself as off — " +
                "an expired trial looks exactly like this. Windows Security may have handed " +
                "real-time scanning back to Defender, or nothing may be scanning at all; the " +
                "Windows Security app says which. Removing a product that has stopped working " +
                "gives Defender the job for certain. brisk changes nothing here; it reports.");
        if (others.Any(p => p.DefinitionsOutdated))
            return Finding(names, "outdated", Severity.Warning, stars: 2,
                $"{names} is the active antivirus and Windows reports its definitions as out " +
                "of date, so it is scanning against an old list. Either update it or, if it " +
                "came with the machine and you never chose it, remove it and let Defender, " +
                "which updates with Windows, take the job. brisk changes nothing here; it reports.");
        return Finding(names, null, Severity.Info, stars: 1,
            $"{names} is the antivirus Windows Security lists as active, and Defender stands " +
            "behind it, switched off while another product holds the job. Two things follow. " +
            "Every file you open goes through that product's real-time scanner, which on an " +
            "older machine is felt. And a program without a signature, brisk included, can be " +
            "blocked by it without a word. If it came with the machine and you never chose it, " +
            "removing it hands the job back to Defender, which is already here and updates " +
            "with Windows. brisk changes nothing here; it reports.");
    }

    private DiagnosticFinding Finding(string names, string? variant, Severity severity,
        int stars, string evidence) =>
        new(Id, $"rule.{Id}.title", "A third-party antivirus holds the job", evidence,
            severity, Category, ImpactStars: stars, CanFix: false, FixDescription: null,
            EvidenceKey: variant is null ? $"rule.{Id}.evidence" : $"rule.{Id}.evidence.{variant}",
            EvidenceArgs: new[] { names },
            Kind: FindingKind.Notice, ImpactClass: ImpactClass.Hygiene);

    /// Security Center has listed products with an empty display name; a
    /// blank in the sentence is worse than saying so.
    private static string Label(AntivirusProduct p) =>
        string.IsNullOrWhiteSpace(p.Name) ? "an unnamed antivirus product" : p.Name.Trim();
}
