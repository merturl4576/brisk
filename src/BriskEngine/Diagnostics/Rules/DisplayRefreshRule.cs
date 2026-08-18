using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

public sealed class DisplayRefreshRule : IDiagnosticRule
{
    /// Below this a gap is unit rounding — 59.94 Hz surfaces as 59 beside a
    /// nominal 60 — rather than a display parked on the wrong mode.
    public const int MinimumGapHz = 10;

    public string Id => "display-refresh";
    public RuleCategory Category => RuleCategory.Auto;

    private static List<DisplayInfo> Behind(DiagnosticContext ctx) =>
        ctx.Displays.Displays()
            .Where(d => d.MaxHz - d.CurrentHz >= MinimumGapHz)
            .ToList();

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var behind = Behind(ctx);
        if (behind.Count == 0) return null;

        var readings = string.Join(", ",
            behind.Select(d => $"{d.FriendlyName} {d.CurrentHz} Hz / {d.MaxHz} Hz"));
        return new DiagnosticFinding(
            Id, "rule.display-refresh.title",
            "A display is running below its refresh rate",
            $"{readings}. Windows left the display slower than it supports, " +
            "so everything on screen moves at the lower rate.",
            Severity.Critical, Category, ImpactStars: 5, CanFix: true,
            FixDescription: "Raise each display to its highest refresh rate (undoable)",
            EvidenceKey: $"rule.{Id}.evidence", EvidenceArgs: new[] { readings });
    }

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Dictionary<string, int>();
        foreach (var display in Behind(ctx))
        {
            prior[display.DeviceName] = display.CurrentHz;
            ctx.Displays.SetRefreshRate(display.DeviceName, display.MaxHz);
        }
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Dictionary<string, int>>(priorStateJson)!;
        foreach (var (device, hz) in prior) ctx.Displays.SetRefreshRate(device, hz);
    }
}
