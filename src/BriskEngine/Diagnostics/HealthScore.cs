using System;
using System.Collections.Generic;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics;

public static class HealthScore
{
    /// 0-100. Every live problem subtracts ImpactStars x severity weight
    /// (Critical 5, Warning 3, Info 1). Notices subtract nothing. Floor 5 so
    /// the gauge never reads "dead".
    public static int Compute(IReadOnlyList<DiagnosticFinding> findings)
    {
        var penalty = 0;
        foreach (var f in findings)
        {
            // Notices are facts, not faults — the spec excludes them so 100
            // stays reachable on hardware the user cannot change. Charging for
            // a 57 s boot brisk says in the same sentence it will not shorten
            // would be the score telling the reader to fix what brisk cannot.
            if (f.Kind == FindingKind.Notice) continue;
            penalty += f.ImpactStars * f.Severity switch
            {
                Severity.Critical => 5,
                Severity.Warning => 3,
                _ => 1,
            };
        }
        return Math.Max(5, 100 - penalty);
    }
}
