using System;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

public abstract class AdviseRuleBase : IDiagnosticRule
{
    public abstract string Id { get; }
    public RuleCategory Category => RuleCategory.Advise;
    public abstract DiagnosticFinding? Detect(DiagnosticContext ctx);
    public string Fix(DiagnosticContext ctx) => throw new InvalidOperationException("advise-only rule");
    public void Undo(DiagnosticContext ctx, string priorStateJson) => throw new InvalidOperationException("advise-only rule");
}
