using System.Collections.Generic;

namespace BriskEngine.Models;

public sealed record DiagnosticFinding(
    string RuleId,
    string TitleKey,        // stable localization key, e.g. "rule.power-plan.title"
    string Title,           // English
    string Evidence,        // English, concrete: "Active plan: Balanced"
    Severity Severity,
    RuleCategory Category,
    int ImpactStars,        // 1..5 expected impact
    bool CanFix,
    string? FixDescription,
    // Stable localization key + data for the evidence sentence, so GUIs can
    // render it in the user's language ("rule.power-plan.evidence" + the
    // plan name). Evidence stays the engine's English prose — the CLI and
    // any consumer without a resource table keep working unchanged; a rule
    // whose evidence is a raw data dump may leave these null.
    string? EvidenceKey = null,
    IReadOnlyList<string>? EvidenceArgs = null,
    // The measured number this finding leads with on presentation surfaces.
    // Optional and per-rule: a finding whose honest content is a sentence
    // (thermals) carries none, and no surface invents one for it.
    Headline? Headline = null,
    // Problem or Notice. Trailing and defaulted, so a rule that says nothing
    // here keeps the judgement it always made; the four rules that report a
    // fact brisk cannot act on opt in explicitly.
    FindingKind Kind = FindingKind.Problem);
