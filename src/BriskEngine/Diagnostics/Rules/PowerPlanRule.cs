using System;
using System.Linq;
using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

public sealed class PowerPlanRule : IDiagnosticRule
{
    public static readonly Guid Balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid PowerSaver = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid HighPerformance = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid Ultimate = Guid.Parse("e9a42b02-d5df-448d-aa66-1f0e7d5efb5a");

    private sealed record Prior(Guid PreviousScheme);

    public string Id => "power-plan";
    public RuleCategory Category => RuleCategory.Auto;

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var (id, name) = ctx.Powercfg.GetActiveScheme();
        if (id != Balanced && id != PowerSaver) return null;
        return new DiagnosticFinding(
            Id, "rule.power-plan.title",
            "Power plan is throttling your CPU",
            $"Active plan: {name}. This plan deliberately limits CPU boost clocks; " +
            "a performance plan lets the CPU reach its full turbo frequency.",
            Severity.Critical, Category, ImpactStars: 5, CanFix: true,
            FixDescription: "Switch to the High performance power plan (undoable)",
            EvidenceKey: $"rule.{Id}.evidence", EvidenceArgs: new[] { name });
    }

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Prior(ctx.Powercfg.GetActiveScheme().Id);
        var schemes = ctx.Powercfg.ListSchemes();
        var best = schemes.Any(s => s.Id == Ultimate) ? Ultimate : HighPerformance;
        ctx.Powercfg.SetActive(best);
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Prior>(priorStateJson)!;
        ctx.Powercfg.SetActive(prior.PreviousScheme);
    }
}
