using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

public sealed class VisualEffectsRule : IDiagnosticRule
{
    private const string Key = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string Value = "VisualFXSetting";

    private sealed record Prior(int Previous);

    public string Id => "visual-effects";
    public RuleCategory Category => RuleCategory.Confirm;

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var current = ctx.Registry.GetInt(Key, Value);
        if (current != 1) return null;
        return new DiagnosticFinding(
            Id, "rule.visual-effects.title",
            "Visual effects are set to best appearance",
            "Windows is configured for \"Best appearance\", which enables animations and " +
            "transparency effects that cost CPU/GPU cycles on every window redraw.",
            Severity.Warning, Category, ImpactStars: 2, CanFix: true,
            FixDescription: "Switch visual effects to best performance (undoable)",
            EvidenceKey: $"rule.{Id}.evidence");
    }

    public string Fix(DiagnosticContext ctx)
    {
        var current = ctx.Registry.GetInt(Key, Value);
        var prior = new Prior(current ?? -1);
        ctx.Registry.SetInt(Key, Value, 2);
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Prior>(priorStateJson)!;
        if (prior.Previous == -1) ctx.Registry.DeleteValue(Key, Value);
        else ctx.Registry.SetInt(Key, Value, prior.Previous);
    }
}
