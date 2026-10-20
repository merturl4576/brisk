using System;
using System.Collections.Generic;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics;

public static class HealthScore
{
    /// 0-100. A Measured finding subtracts ImpactStars x severity weight
    /// (Critical 5, Warning 3, Info 1), whether brisk can fix it or not. A
    /// Hygiene problem subtracts a flat 2 (1 for Info), whatever its stars.
    /// A Hygiene notice subtracts nothing. Floor 5 so the gauge never reads
    /// "dead".
    ///
    /// The flat charge exists because settings flips alone once moved the
    /// score from 47 to 90 (a power plan, a start-menu switch, visual effects)
    /// while nothing brisk could measure had changed. Only what brisk can
    /// measure the other side of is allowed to move the gauge far.
    public static int Compute(IReadOnlyList<DiagnosticFinding> findings)
    {
        var penalty = 0;
        foreach (var f in findings)
        {
            // A Hygiene notice is a fact, not a fault — 47 USB devices,
            // memory below its rating on a board that will not change — and
            // costs nothing, so 100 stays reachable on hardware the user
            // cannot change.
            //
            // A MEASURED notice is charged. Windows timed a 106 s boot on a
            // laptop and this gauge read 89 over it, because the boot rule
            // will not shorten a boot and a notice was free. Two owners read
            // the 89 as a verdict on the machine and called brisk wrong
            // (2026-09-09), and they were right: the gauge is read as the
            // machine, so it has to mean the machine. What brisk can fix is
            // the sentence under the gauge, not the gauge.
            if (f.Kind == FindingKind.Notice && f.ImpactClass != ImpactClass.Measured) continue;
            penalty += f.ImpactClass == ImpactClass.Measured
                ? f.ImpactStars * SeverityWeight(f.Severity)
                : HygieneCharge(f.Severity);
        }
        return Math.Max(5, 100 - penalty);
    }

    private static int SeverityWeight(Severity s) => s switch
    {
        Severity.Critical => 5,
        Severity.Warning => 3,
        _ => 1,
    };

    private static int HygieneCharge(Severity s) => s == Severity.Info ? 1 : 2;
}
