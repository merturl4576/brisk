using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;

namespace BriskEngine.Actions.Rules;

public interface IPowerOverlayProbe
{
    bool OnAcPower();
    void ApplyOverlay(Guid overlay);
}

public sealed class RealPowerOverlayProbe : IPowerOverlayProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

    /// 0 = on battery. 1 (AC) and 255 (unknown) both count as plugged in.
    public bool OnAcPower() => !GetSystemPowerStatus(out var s) || s.ACLineStatus != 0;

    public void ApplyOverlay(Guid overlay)
    {
        var rc = PowerSetActiveOverlayScheme(overlay);
        if (rc != 0) throw new Win32Exception((int)rc);
    }
}

/// "Best performance" power mode while plugged in. Battery mode is untouched.
public sealed class PowerModeRule : IReadBackable
{
    public static readonly Guid BestPerformance = Guid.Parse("ded574b5-45a0-4f42-8737-46345c09c238");
    public const string Key = @"HKLM\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";
    public const string AcValue = "ActiveOverlayAcPowerScheme";

    private sealed record Prior(string? Ac);
    private readonly IPowerOverlayProbe _overlay;

    public PowerModeRule(IPowerOverlayProbe overlay) { _overlay = overlay; }

    public string Id => "power-mode";
    public RuleCategory Category => RuleCategory.Auto;

    public bool IsOn(DiagnosticContext ctx)
    {
        if (ctx.Powercfg.GetActiveScheme().Id != PowerPlanRule.Balanced) return false;
        var current = ctx.Registry.GetString(Key, AcValue);
        return !Guid.TryParse(current, out var g) || g != BestPerformance;
    }

    public WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => WriteEffect.NotAPolicy;

    public DiagnosticFinding? Detect(DiagnosticContext ctx) => IsOn(ctx)
        ? new DiagnosticFinding(Id, $"rule.{Id}.title", "Power mode is not Best performance when plugged in",
            "While plugged in, Windows balances speed against power use.",
            Severity.Info, Category, ImpactStars: 2, CanFix: true,
            FixDescription: "Use Best performance while plugged in (undoable)", Kind: FindingKind.Notice)
        : null;

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Prior(ctx.Registry.GetString(Key, AcValue));
        ctx.Registry.SetString(Key, AcValue, BestPerformance.ToString());
        if (_overlay.OnAcPower()) _overlay.ApplyOverlay(BestPerformance);
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Prior>(priorStateJson)!;
        if (prior.Ac is null) ctx.Registry.DeleteValue(Key, AcValue);
        else ctx.Registry.SetString(Key, AcValue, prior.Ac);
        if (_overlay.OnAcPower())
            _overlay.ApplyOverlay(Guid.TryParse(prior.Ac, out var g) ? g : Guid.Empty);
    }
}
