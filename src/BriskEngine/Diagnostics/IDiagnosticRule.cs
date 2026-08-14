using BriskEngine.Models;

namespace BriskEngine.Diagnostics;

public interface IDiagnosticRule
{
    string Id { get; }
    RuleCategory Category { get; }
    DiagnosticFinding? Detect(DiagnosticContext ctx);   // null = no finding
    string Fix(DiagnosticContext ctx);                  // returns prior-state JSON
    void Undo(DiagnosticContext ctx, string priorStateJson);
}
