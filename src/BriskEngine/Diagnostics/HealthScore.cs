using System;
using System.Collections.Generic;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics;

public static class HealthScore
{
    /// 0-100. Every live finding subtracts ImpactStars x severity weight
    /// (Critical 5, Warning 3, Info 1). Floor 5 so the gauge never reads "dead".
    public static int Compute(IReadOnlyList<DiagnosticFinding> findings)
    {
        var penalty = 0;
        foreach (var f in findings)
            penalty += f.ImpactStars * f.Severity switch
            {
                Severity.Critical => 5,
                Severity.Warning => 3,
                _ => 1,
            };
        return Math.Max(5, 100 - penalty);
    }
}
