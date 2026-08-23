using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics;

/// Chooses which measured number leads a scan's presentation. The order is
/// a product decision made visible in one list, not a heuristic — the same
/// scan always leads with the same number on every surface that asks.
public static class RevelationPicker
{
    /// Declared order. A rule with a headline but no entry here still shows
    /// — after the listed ones, by severity, impact, then id — so a new
    /// rule is never invisible just because nobody edited this file.
    internal static readonly string[] Priority =
    {
        "boot-degradation",
        "display-refresh",
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
