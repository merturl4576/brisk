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
        // The three report-only disclosures that can lead at all —
        // run-history, recall-status and delivery-optimization — are left
        // off this list on purpose. Unlisted is the tail rank, and a count
        // of program records, a policy word and a total of uploaded bytes
        // are not what this project wants a scan to open with. The fourth,
        // usb-history, is not merely unlisted: see NeverLeads below.
        "startup-bloat",
        // A NAMED FILE OUTRANKS THE FOLDER THAT HOLDS IT. Both of these come
        // out of the same walk of the same four folders, and they describe
        // the same disk two ways: "Desktop: 58.8 GB (over threshold)" and
        // "23.5 GB — the largest single file in your profile". Only the
        // second one is a thing a reader can go and decide about, which is
        // what the field test on a neglected machine and the maintainer's
        // own live look both said, so it leads and the total follows it.
        "large-files",
        "disk-breakdown",
        "memory-speed",
    };

    /// Rules whose number never leads any surface Pick feeds — today the
    /// Overview hero band and the report card, which both take Pick's
    /// answer. The Gizlilik page does not ask Pick; it reads Headline
    /// itself, and that page is where these still render.
    ///
    /// usb-history sat THIRD in Priority above, on the ruling that the
    /// strongest number brisk owns should lead the moment nothing
    /// actionable outranks it. The maintainer's first live look at 0.6.0
    /// showed the other side of that ruling — his machine holds ONE
    /// recorded device, and "1" led surfaces carrying a 58.1 GB disk
    /// finding — and he overturned it on that data (2026-08-26).
    internal static readonly string[] NeverLeads = { "usb-history" };

    public static IReadOnlyList<DiagnosticFinding> Pick(
        IEnumerable<DiagnosticFinding> findings) =>
        findings.Where(f => f.Headline is not null)
            .Where(f => Array.IndexOf(NeverLeads, f.RuleId) < 0)
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
