using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics;

/// Chooses which measured number leads a scan's presentation. The order is
/// a product decision made visible in one list, not a heuristic — the same
/// scan always leads with the same number on every surface that asks.
///
/// NOTHING HERE READS Headline.Value. Two of the rules this ranks lead with a
/// value that is not a number at all — recall-status with the word "Off",
/// delivery-optimization with a formatted "302 MB" — so an order that parsed
/// the value would have to decide what those are worth against a count of 47
/// devices. The rule id, the severity and the impact figure are what it sorts
/// on, and TheOrder_ReadsTheDeclaredList_NotWhatTheHeadlineSays holds that.
public static class RevelationPicker
{
    /// Declared order. A rule with a headline but no entry here still shows
    /// — after the listed ones, by severity, impact, then id — so a new
    /// rule is never invisible just because nobody edited this file.
    internal static readonly string[] Priority =
    {
        "boot-degradation",
        "display-refresh",
        // The disclosure wave's one entry here. Third, because brisk leads
        // with a measurement the user can act on today and both of the two
        // above are one; the count of USB devices Windows has recorded is the
        // strongest number brisk owns that the user can do nothing about, so
        // it leads the moment nothing actionable outranks it.
        //
        // The wave's other three disclosures — run-history, recall-status and
        // delivery-optimization — are left off this list on purpose. Unlisted
        // is the tail rank, and a count of program records, a policy word and
        // a total of uploaded bytes are not what this project wants a scan to
        // open with.
        "usb-history",
        "startup-bloat",
        "disk-breakdown",
        "memory-speed",
    };

    public static IReadOnlyList<DiagnosticFinding> Pick(
        IEnumerable<DiagnosticFinding> findings) =>
        findings.Where(f => f.Headline is not null)
            .OrderBy(Rank)
            .ThenByDescending(f => SeverityRank(f.Severity))
            .ThenByDescending(f => f.ImpactStars)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ToList();

    private static int Rank(DiagnosticFinding f)
    {
        var i = Array.IndexOf(Priority, f.RuleId);
        return i < 0 ? int.MaxValue : i;
    }

    /// Explicit, so the sort never leans on the enum's numeric order.
    private static int SeverityRank(Severity s) => s switch
    {
        Severity.Critical => 2,
        Severity.Warning => 1,
        _ => 0,
    };
}
