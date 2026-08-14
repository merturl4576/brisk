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
    string? FixDescription);
