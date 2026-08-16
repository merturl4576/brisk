using System;
using System.Collections.Generic;
using System.Linq;
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
public sealed class FixAllService
{
    /// StartupBloatRule.Fix is exactly "disable every enabled known-heavy
    /// item", journaled and undoable, so fix-all routes through the rule
    /// instead of duplicating the disable logic via SetStartupEnabled.
    private const string StartupBloatRuleId = "startup-bloat";

    private readonly IEngineHost _host;

    public FixAllService(IEngineHost host) { _host = host; }

    /// True when Run would actually change something on this snapshot: a
    /// fixable non-advise finding exists — counting startup-bloat only while
    /// heavy items are still enabled, because its fix is a no-op once every
    /// heavy item is already off. The fix-all buttons (overview and health)
    /// query this instead of duplicating the predicate.
    public bool HasWork(ScanSnapshot snapshot) => snapshot.Findings.Any(f =>
        f.Category != RuleCategory.Advise && f.CanFix
        && (!string.Equals(f.RuleId, StartupBloatRuleId, StringComparison.OrdinalIgnoreCase)
            || EnabledHeavyNames().Count > 0));

    /// Callers guard dry-run and busy-state before calling; this always acts.
    public FixAllResult Run(ScanSnapshot snapshot)
    {
        var fixedRules = new List<DiagnosticFinding>();
        var disabled = new List<string>();
        var attempted = 0;
        var applied = 0;
        foreach (var finding in snapshot.Findings
                     .Where(f => f.Category != RuleCategory.Advise && f.CanFix))
        {
            attempted++;
            var heavyBefore = string.Equals(finding.RuleId, StartupBloatRuleId,
                StringComparison.OrdinalIgnoreCase) ? EnabledHeavyNames() : null;
            if (!_host.Fix(finding.RuleId).Ok) continue;
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
