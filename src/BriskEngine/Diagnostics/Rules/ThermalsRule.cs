using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

public sealed class ThermalsRule : AdviseRuleBase
{
    public override string Id => "thermals";

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var cpu = ctx.Sensors.CpuTempC();
        var gpu = ctx.Sensors.GpuTempC();
        var hot = (cpu is not null && cpu >= 75) || (gpu is not null && gpu >= 70);
        if (!hot) return null;

        var parts = new System.Collections.Generic.List<string>();
        if (cpu is not null) parts.Add($"CPU {cpu:F0}°C");
        if (gpu is not null) parts.Add($"GPU {gpu:F0}°C");

        return new DiagnosticFinding(
            Id, "rule.thermals.title",
            "System is running hot",
            $"{string.Join(", ", parts)}. Sustained high temperatures throttle performance; " +
            "clean fans / renew thermal paste.",
            Severity.Warning, Category, ImpactStars: 2, CanFix: false, FixDescription: null);
    }
}
