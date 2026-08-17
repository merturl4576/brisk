using System;
using System.Collections.Generic;
using BriskEngine.Models;

namespace Brisk.ViewModels;

/// Topical page routing for findings. RuleCategory is a consent level
/// (Auto/Confirm/Advise), not a topic, so pages split by rule id instead:
/// speed levers go to Performans, machine/disk condition to Sağlık.
/// Unknown future rules default to Sağlık.
public static class FindingSections
{
    private static readonly HashSet<string> Performance = new(
        new[] { "power-plan", "browser-gpu", "hw-acceleration",
                "visual-effects", "startup-bloat", "ram-pressure" },
        StringComparer.OrdinalIgnoreCase);

    public static bool IsPerformance(DiagnosticFinding finding) =>
        Performance.Contains(finding.RuleId);

    /// Same split for journal entries, which only carry a rule id — drives
    /// each page's slice of the journal-driven done report.
    public static bool IsPerformance(string ruleId) => Performance.Contains(ruleId);

    public static bool IsHealth(DiagnosticFinding finding) =>
        !IsPerformance(finding);

    public static bool IsHealth(string ruleId) => !IsPerformance(ruleId);
}
