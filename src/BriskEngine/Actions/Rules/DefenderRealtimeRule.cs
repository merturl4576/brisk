using System;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Models;

namespace BriskEngine.Actions.Rules;

public sealed class DefenderRealtimeRule : IDiagnosticRule
{
    private readonly IProcessRunner _runner;

    public DefenderRealtimeRule(IProcessRunner runner) { _runner = runner; }

    public string Id => "defender-realtime";
    public RuleCategory Category => RuleCategory.Auto;

    private static bool Applies(DiagnosticContext ctx) =>
        ctx.Defender.Read()?.RealTimeOn == false
        && !ctx.Defender.Registered().Any(p => !p.IsDefender && p.Enabled);

    public DiagnosticFinding? Detect(DiagnosticContext ctx) => Applies(ctx)
        ? new DiagnosticFinding(Id, $"rule.{Id}.title", "Real-time protection is off",
            "Defender's real-time protection is off and no other antivirus is on.",
            Severity.Critical, Category, ImpactStars: 4, CanFix: true,
            FixDescription: "Turn real-time protection back on (undoable)")
        : null;

    public string Fix(DiagnosticContext ctx)
    {
        Ps("Set-MpPreference -DisableRealtimeMonitoring $false");
        return "\"off\"";
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson) =>
        Ps("Set-MpPreference -DisableRealtimeMonitoring $true");

    private void Ps(string command)
    {
        var (exit, _) = _runner.Run("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"");
        if (exit != 0) throw new InvalidOperationException($"Set-MpPreference exited {exit}");
    }
}
