using System;
using System.Collections.Generic;
using System.Linq;
using Brisk.ViewModels;
using BriskEngine.Models;

namespace Brisk.Services;

public sealed record FixAllResult(
    IReadOnlyList<DiagnosticFinding> FixedRules,
    IReadOnlyList<string> DisabledStartup,
    int Attempted,
    int Applied);

/// The one implementation of "Fix all (safe)". Pressing that button is the
/// user's consent for every safe, reversible, per-action fix — including
/// disabling known-heavy startup items (spec ruling). Advise-only findings
/// are never touched. Safe-level cleaning is a separate button by design and
/// must never be bundled in here.
///
/// Neither is a privacy setting. This button is about speed and hygiene, and
/// the disclosure spec's action model is two-tier: the Privacy page carries
/// its own button over the four consequence-free switches
/// (PrivacyViewModel.IsConsequenceFree), and its own per-switch control for
/// each of the two that cost the user something (Find my device, Timeline)
/// with the loss named beside it. This exclusion never waited for those
/// surfaces and does not lean on them now: a generic button cannot carry
/// either consent, so it does not reach a privacy finding whether or not
/// anything else can.
public sealed class FixAllService
{
    /// StartupBloatRule.Fix is exactly "disable every enabled known-heavy
    /// item", journaled and undoable, so fix-all routes through the rule
    /// instead of duplicating the disable logic via SetStartupEnabled.
    private const string StartupBloatRuleId = "startup-bloat";

    private readonly IEngineHost _host;

    public FixAllService(IEngineHost host) { _host = host; }

    /// Per-rule progress for row-level UI states, raised on the worker
    /// thread right before and right after each fix attempt. Every findings
    /// page subscribes, so rows animate no matter which surface (page,
    /// overview, flyout) launched the run.
    public event Action<DiagnosticFinding>? FixingRule;
    public event Action<DiagnosticFinding, bool>? FixedRule;

    /// True when Run would actually change something on this snapshot: a
    /// fixable non-advise finding exists — counting startup-bloat only while
    /// heavy items are still enabled, because its fix is a no-op once every
    /// heavy item is already off. The fix-all buttons (overview and health)
    /// query this instead of duplicating the predicate.
    public bool HasWork(ScanSnapshot snapshot) => snapshot.Findings.Any(f =>
        IsOneClickFixable(f)
        && (!string.Equals(f.RuleId, StartupBloatRuleId, StringComparison.OrdinalIgnoreCase)
            || EnabledHeavyNames().Count > 0));

    /// The one predicate every surface reads: HasWork and Run here, and the
    /// two places that COUNT the button's work for the "{n} one-click
    /// fixable" line. Public for that reason — the count and the action have
    /// to be the same question, or the sentence beside the button promises
    /// clicks the button declines. Category is a consent level, not a topic,
    /// and excluding by category would not do the job: all six of the wave's
    /// privacy switches ship today, the four consequence-free ones as Auto
    /// and the two that cost the user something as Confirm. Both levels are
    /// inside the topic, so the topic has to be excluded by rule id.
    public static bool IsOneClickFixable(DiagnosticFinding f) =>
        f.Category != RuleCategory.Advise && f.CanFix
        && !FindingSections.IsPrivacy(f);

    /// Callers guard dry-run and busy-state before calling; this always acts.
    public FixAllResult Run(ScanSnapshot snapshot)
    {
        var fixedRules = new List<DiagnosticFinding>();
        var disabled = new List<string>();
        var attempted = 0;
        var applied = 0;
        foreach (var finding in snapshot.Findings
                     .Where(IsOneClickFixable))
        {
            attempted++;
            FixingRule?.Invoke(finding);
            var heavyBefore = string.Equals(finding.RuleId, StartupBloatRuleId,
                StringComparison.OrdinalIgnoreCase) ? EnabledHeavyNames() : null;
            var ok = _host.Fix(finding.RuleId).Ok;
            FixedRule?.Invoke(finding, ok);
            if (!ok) continue;
            applied++;
            if (heavyBefore is null)
            {
                fixedRules.Add(finding);
                continue;
            }
            // Report by name exactly the heavy items the rule fix disabled;
            // when no per-item diff is observable, report the rule fix itself.
            var still = EnabledHeavyNames();
            var names = heavyBefore
                .Where(n => !still.Contains(n, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (names.Count == 0) fixedRules.Add(finding);
            else disabled.AddRange(names);
        }
        return new FixAllResult(fixedRules, disabled, attempted, applied);
    }

    private List<string> EnabledHeavyNames() => _host.ListStartup()
        .Where(s => s.Enabled && s.KnownHeavy)
        .Select(s => s.Name)
        .ToList();
}
