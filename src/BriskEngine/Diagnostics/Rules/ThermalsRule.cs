using System.Collections.Generic;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

/// Hot at idle, from whichever of the two sensors answered — and a sentence
/// naming the one that did not.
///
/// This rule reported whatever it got and omitted the rest, which on the
/// hardware brisk was built against means it prints a GPU number and nothing
/// else, forever. Wave 1's elevation manifest was justified by CPU temperature
/// and does not deliver it: LibreHardwareMonitor reads CPU temps through the
/// WinRing0 kernel driver, WinRing0 is on Microsoft's vulnerable-driver
/// blocklist, and a machine running memory integrity refuses to load it at any
/// privilege level. GPU temperature needs no elevation at all. So on a default
/// Windows 11 the omission is not an edge case, it is the normal result, and a
/// reader seeing one line cannot tell a cool CPU from an unread one.
///
/// The note says the sensor was not read. It does NOT say why, because a null
/// from CpuTempC() is equally consistent with an unsupported chip or a probe
/// that threw — the blocklisted driver is named as the usual reason and
/// explicitly not claimed as this machine's. Same shape as the memory rule:
/// report the gap, refuse to invent its cause.
public sealed class ThermalsRule : AdviseRuleBase
{
    private const string Advice =
        "Sustained high temperatures throttle performance; clean fans / renew thermal paste.";

    private const string CpuUnread =
        "The CPU temperature is missing from that — brisk could not read it. Usually the " +
        "cause is the driver: the one that reads CPU temperature is on Microsoft's " +
        "vulnerable-driver blocklist, so Windows will not load it while memory integrity " +
        "is on. brisk will not switch that protection off for a reading, and cannot " +
        "confirm from here that this is the reason on your machine.";

    /// No equivalent known cause exists for a silent GPU sensor, so this one
    /// stops at the fact. Borrowing the sentence above would state a blocked
    /// kernel driver as the reason a GPU temperature is missing, which is not
    /// a thing that happens.
    private const string GpuUnread =
        "The GPU temperature is missing from that — brisk could not read it, and cannot " +
        "tell from here why.";

    public override string Id => "thermals";

    /// NaN is what a present-but-silent sensor reports, and it is not a
    /// reading: it fails every threshold, so nothing calls it hot, but it is
    /// not null either, so it would print "CPU NaN°C" and take the both-read
    /// template — the one case where the template outruns what was read. The
    /// real probe filters it too; this is here because the rule takes any
    /// ISensorProbe and cannot assume the one it got did.
    private static double? Reading(double? temperature) =>
        temperature is { } value && double.IsFinite(value) ? value : null;

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var cpu = Reading(ctx.Sensors.CpuTempC());
        var gpu = Reading(ctx.Sensors.GpuTempC());
        var hot = (cpu is not null && cpu >= 75) || (gpu is not null && gpu >= 70);
        if (!hot) return null;

        var parts = new List<string>();
        if (cpu is not null) parts.Add($"CPU {cpu:F0}°C");
        if (gpu is not null) parts.Add($"GPU {gpu:F0}°C");

        var readings = string.Join(", ", parts);
        // A finding requires a reading, so both can never be unread here and
        // the two notes can never both apply.
        var (variant, note) =
            cpu is null ? (".cpu-unread", $" {CpuUnread}")
            : gpu is null ? (".gpu-unread", $" {GpuUnread}")
            : (string.Empty, string.Empty);

        return new DiagnosticFinding(
            Id, "rule.thermals.title",
            "System is running hot",
            $"{readings}.{note} {Advice}",
            Severity.Warning, Category, ImpactStars: 2, CanFix: false, FixDescription: null,
            EvidenceKey: $"rule.{Id}.evidence{variant}", EvidenceArgs: new[] { readings });
    }
}
