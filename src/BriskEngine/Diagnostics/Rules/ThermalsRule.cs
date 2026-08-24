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

    /// Memory integrity could not be read, so neither story is available and
    /// the sentence stays exactly as hedged as it was.
    private const string CpuUnread =
        "The CPU temperature is missing from that — brisk could not read it. Usually the " +
        "cause is the driver: the one that reads CPU temperature is on Microsoft's " +
        "vulnerable-driver blocklist, so Windows will not load it while memory integrity " +
        "is on. brisk will not switch that protection off for a reading, and cannot " +
        "confirm from here that this is the reason on your machine.";

    /// Measured on. The hedge moves: brisk no longer wonders whether memory
    /// integrity is on, and still refuses to call it the proven cause, because
    /// an unsupported chip and a probe that threw read identically from here.
    private const string CpuUnreadIntegrityOn =
        "The CPU temperature is missing from that — brisk could not read it. Memory " +
        "integrity is on here, and the driver that reads CPU temperature is on Microsoft's " +
        "vulnerable-driver blocklist, so Windows will not load it at any privilege level. " +
        "brisk will not switch that protection off for a reading. That is the usual cause " +
        "and it fits this machine, but brisk cannot prove it is the only one.";

    /// Measured off, which rules the usual cause out rather than confirming
    /// it. Saying less here is the whole point of reading the setting: this
    /// machine used to be handed an explanation that could not be true of it.
    private const string CpuUnreadIntegrityOff =
        "The CPU temperature is missing from that — brisk could not read it. Memory " +
        "integrity is off here, so the usual cause — a driver Windows refuses to load — " +
        "is not what happened, and brisk cannot tell from here what did.";

    /// No equivalent known cause exists for a silent GPU sensor, so this one
    /// stops at the fact. Borrowing the sentence above would state a blocked
    /// kernel driver as the reason a GPU temperature is missing, which is not
    /// a thing that happens.
    private const string GpuUnread =
        "The GPU temperature is missing from that — brisk could not read it, and cannot " +
        "tell from here why.";

    public override string Id => "thermals";

    /// Three states, three sentences, and null is not folded into either
    /// answer: a machine whose Device Guard query failed is not a machine with
    /// memory integrity off.
    private static (string Variant, string Note) CpuUnreadNote(bool? memoryIntegrityOn) =>
        memoryIntegrityOn switch
        {
            true => (".cpu-unread.integrity-on", $" {CpuUnreadIntegrityOn}"),
            false => (".cpu-unread.integrity-off", $" {CpuUnreadIntegrityOff}"),
            null => (".cpu-unread", $" {CpuUnread}"),
        };

    /// The rule takes any ISensorProbe and cannot assume the one it got
    /// filters NaN (the real probe does). SensorReading is the shared
    /// predicate rather than a third private copy of it — the copies had
    /// already drifted apart once, leaving `brisk scan` calling a NaN an
    /// answer while the report card called the same reading silence.
    private static double? Reading(double? temperature) =>
        SensorReading.IsReal(temperature) ? temperature : null;

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
            cpu is null ? CpuUnreadNote(ctx.MemoryIntegrity.IsOn())
            : gpu is null ? (".gpu-unread", $" {GpuUnread}")
            : (string.Empty, string.Empty);

        return new DiagnosticFinding(
            Id, "rule.thermals.title",
            "System is running hot",
            $"{readings}.{note} {Advice}",
            Severity.Warning, Category, ImpactStars: 2, CanFix: false, FixDescription: null,
            EvidenceKey: $"rule.{Id}.evidence{variant}", EvidenceArgs: new[] { readings },
            // A notice: this is a temperature brisk read, and the advice it
            // carries is fans and thermal paste. Docking the score for it
            // would be brisk grading a machine on work only hands can do.
            Kind: FindingKind.Notice);
    }
}
