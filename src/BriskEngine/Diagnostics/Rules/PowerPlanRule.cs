using System;
using System.Linq;
using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

/// Balanced or Power saver active on a DESKTOP that also offers a performance
/// plan. Nothing else: a laptop is right to run Balanced, and a machine whose
/// only plan is Balanced has nothing to switch to. Warning, two stars,
/// Confirm, and copy that promises no speed: brisk has no measurement that
/// anyone feels a power plan, so the score treats it as hygiene (2 points).
public sealed class PowerPlanRule : IDiagnosticRule
{
    public static readonly Guid Balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid PowerSaver = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid HighPerformance = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid Ultimate = Guid.Parse("e9a42b02-d5df-448d-aa66-1f0e7d5efb5a");

    private sealed record Prior(Guid PreviousScheme);

    public string Id => "power-plan";
    public RuleCategory Category => RuleCategory.Confirm;

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        // A battery means a laptop (or "cannot tell"), and Balanced is the
        // right plan there: High performance costs battery for a gain brisk
        // cannot measure. No finding, rather than advice to ignore.
        if (ctx.Powercfg.HasBattery()) return null;

        var (id, name) = ctx.Powercfg.GetActiveScheme();
        if (id != Balanced && id != PowerSaver) return null;

        // Modern Standby machines often list Balanced alone. With nothing to
        // switch to, the finding would be a button that fails.
        var schemes = ctx.Powercfg.ListSchemes();
        if (!schemes.Any(s => s.Id == HighPerformance || s.Id == Ultimate)) return null;

        return new DiagnosticFinding(
            Id, "rule.power-plan.title",
            "A performance power plan is available and not in use",
            $"Active plan: {name}. On a desktop, High performance keeps the CPU from " +
            "idling down between bursts. brisk has no measurement that you will feel " +
            "the difference, so this is a small, undoable setting, not a speed promise.",
            Severity.Warning, Category, ImpactStars: 2, CanFix: true,
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
