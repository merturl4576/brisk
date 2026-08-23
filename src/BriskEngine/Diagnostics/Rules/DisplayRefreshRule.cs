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

    /// Shared with the surfaces that have to recognise this one rule by name:
    /// the CLI's --keep, and the app state that raises the confirmation.
    public const string RuleId = "display-refresh";

    public string Id => RuleId;
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

    /// The mode is applied for this session only — the probe never touches the
    /// registry here. It becomes permanent when the user confirms the picture
    /// is back (IDisplayProbe.PersistCurrentModes), and not one moment sooner.
    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Dictionary<string, int>();
        try
        {
            foreach (var display in Behind(ctx))
            {
                ctx.Displays.SetRefreshRate(display.DeviceName, display.MaxHz);
                // Recorded only once the driver has accepted the rate: a
                // prior state for a display that never moved would send the
                // undo chasing a change that did not happen.
                prior[display.DeviceName] = display.CurrentHz;
            }
        }
        catch (DisplayChangeException)
        {
            // Half a fix is not a fix. FixRunner journals nothing when Fix
            // throws, so anything already raised would be a change with no
            // undo behind it — put those displays back before the failure is
            // reported, and report it rather than swallow it.
            foreach (var (device, hz) in prior)
            {
                try { ctx.Displays.SetRefreshRate(device, hz); }
                catch (DisplayChangeException) { /* nothing better is available */ }
            }
            throw;
        }
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Dictionary<string, int>>(priorStateJson)!;
        if (prior.Count == 0) return;
        foreach (var (device, hz) in prior) ctx.Displays.SetRefreshRate(device, hz);
        // An undo has to reach the registry: if the raise was confirmed, the
        // registry is carrying it, and a session-only restore would hand the
        // rejected mode straight back at the next reboot.
        ctx.Displays.PersistCurrentModes();
    }
}
