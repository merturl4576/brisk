using System.Collections.Generic;
using Brisk.Localization;
using Brisk.Services;

namespace Brisk.ViewModels;

/// One line of a post-run completion report. Done lines wear the green dot;
/// info lines (dry-run notice, partial-failure note) stay dotless so the
/// report never paints a caveat green.
public sealed record ReportLine(string Text, bool IsDone);

/// Composes the shared completion report from a fix-all result: past-tense
/// outcome lines plus the "Result: …" summary sentence. The summary is empty
/// when nothing actually ran — the pages hide the ✓ lead line and the
/// closing note then.
internal static class FixReport
{
    public static string Populate(Loc loc, FixAllResult result,
        ICollection<ReportLine> lines)
    {
        foreach (var finding in result.FixedRules)
            lines.Add(new ReportLine(DoneLabel.For(loc, finding.RuleId,
                finding.TitleKey, finding.Title), IsDone: true));
        foreach (var name in result.DisabledStartup)
            lines.Add(new ReportLine(loc.F("overview.report.disabled", name),
                IsDone: true));
        if (result.Attempted == 0)
        {
            lines.Add(new ReportLine(loc["health.nofixables"], IsDone: false));
            return "";
        }
        if (result.Applied < result.Attempted)
            lines.Add(new ReportLine(loc.F("health.fixpartial",
                result.Applied, result.Attempted), IsDone: false));
        // The field-test lesson (2026-08-30): a machine went 47 -> 90 and its
        // owner felt nothing, because nothing SAID the difference arrives at
        // the next restart — so they never restarted. Every applied run now
        // names when it will show, and where the measurement will appear.
        if (result.Applied > 0)
            lines.Add(new ReportLine(loc["report.expectation"], IsDone: false));
        var parts = new List<string>();
        if (result.DisabledStartup.Count > 0)
            parts.Add(loc.F("overview.report.part.startup",
                result.DisabledStartup.Count));
        if (result.FixedRules.Count > 0)
            parts.Add(loc.F("overview.report.part.fixes", result.FixedRules.Count));
        return parts.Count == 0
            ? ""
            : loc.F("overview.report.summary", string.Join(" · ", parts));
    }
}
