using System;
using System.Collections.Generic;
using BriskEngine.Models;

namespace Brisk.ViewModels;

/// The privacy rule ids, in one place. The rules, the page and the report
/// card all need the same list, and three copies of it would drift.
public static class PrivacyRuleIds
{
    public static readonly string[] All =
    {
        "advertising-id", "diagnostic-level", "tailored-experiences",
        "speech-typing", "location", "activity-history",
        "recall-status", "usb-history", "run-history",
        "delivery-optimization",
    };
}

/// Topical page routing for findings. RuleCategory is a consent level
/// (Auto/Confirm/Advise), not a topic, so pages split by rule id instead:
/// speed levers go to Performans, machine/disk condition to Sağlık.
/// Unknown future rules default to Sağlık; the privacy rule ids are the
/// one named exception, and IsPrivacy claims them before that default.
public static class FindingSections
{
    private static readonly HashSet<string> Performance = new(
        new[] { "power-plan", "browser-gpu", "hw-acceleration",
                "visual-effects", "startup-bloat", "ram-pressure",
                "display-refresh", "search-web-results",
                "boot-degradation", "memory-speed" },
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Privacy = new(
        PrivacyRuleIds.All, StringComparer.OrdinalIgnoreCase);

    public static bool IsPerformance(DiagnosticFinding finding) =>
        Performance.Contains(finding.RuleId);

    /// Same split for journal entries, which only carry a rule id — drives
    /// each page's slice of the journal-driven done report.
    public static bool IsPerformance(string ruleId) => Performance.Contains(ruleId);

    public static bool IsPrivacy(DiagnosticFinding finding) =>
        Privacy.Contains(finding.RuleId);

    public static bool IsPrivacy(string ruleId) => Privacy.Contains(ruleId);

    /// Sağlık is the default page, so it has to name what it does NOT take:
    /// without the privacy exclusion every disclosure finding lands on the
    /// page that grades the machine's condition.
    public static bool IsHealth(DiagnosticFinding finding) =>
        !IsPerformance(finding) && !IsPrivacy(finding);

    public static bool IsHealth(string ruleId) =>
        !IsPerformance(ruleId) && !IsPrivacy(ruleId);
}
