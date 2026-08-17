using System.Linq;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

public sealed class RamPressureRule : AdviseRuleBase
{
    public override string Id => "ram-pressure";

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var load = ctx.Processes.MemoryLoadPercent();
        if (load < 80) return null;
        var top = string.Join(", ", ctx.Processes.TopByMemory(5)
            .Select(p => $"{p.Name} ({p.WorkingSetBytes >> 20} MB)"));
        return new DiagnosticFinding(Id, "rule.ram-pressure.title",
            "Memory is under pressure",
            $"RAM is {load:F0}% full. Biggest consumers: {top}. " +
            "Closing or un-starting some of these frees memory.",
            Severity.Warning, Category, ImpactStars: 2, CanFix: false, FixDescription: null,
            EvidenceKey: $"rule.{Id}.evidence", EvidenceArgs: new[] { $"{load:F0}", top });
    }
}
