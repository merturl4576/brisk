using System;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;

namespace BriskEngine.Actions.Rules;

/// powercfg frees hiberfil.sys and turns hibernation and Fast Startup off;
/// "powercfg /hibernate on" reverses all of it.
public sealed class HibernationRule : IReadBackable
{
    private readonly IProcessRunner _runner;
    private readonly Func<bool> _fileExists;

    public HibernationRule(IProcessRunner runner, Func<bool> fileExists)
    {
        _runner = runner;
        _fileExists = fileExists;
    }

    public string Id => "hibernation";
    public RuleCategory Category => RuleCategory.Confirm;

    public bool IsOn(DiagnosticContext ctx) => _fileExists();

    public WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => WriteEffect.NotAPolicy;

    public DiagnosticFinding? Detect(DiagnosticContext ctx) => IsOn(ctx)
        ? new DiagnosticFinding(Id, $"rule.{Id}.title", "Hibernation file is present",
            "hiberfil.sys reserves space on the system drive.", Severity.Info, Category,
            ImpactStars: 1, CanFix: true, FixDescription: "Turn hibernation off (undoable)",
            Kind: FindingKind.Notice)
        : null;

    public string Fix(DiagnosticContext ctx)
    {
        Powercfg("off");
        return "\"on\"";
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson) => Powercfg("on");

    private void Powercfg(string state)
    {
        var (exit, _) = _runner.Run("powercfg.exe", $"/hibernate {state}");
        if (exit != 0) throw new InvalidOperationException($"powercfg exited {exit}");
    }
}
