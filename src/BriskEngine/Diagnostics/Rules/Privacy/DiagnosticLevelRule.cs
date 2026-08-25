using System.Collections.Generic;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// The diagnostic data level policy. The only switch in this family that is a
/// level rather than a flag, and the only one under HKLM — writing it needs
/// administrator rights, and FixRunner reports the refusal when they are
/// missing instead of pretending the write happened.
public sealed class DiagnosticLevelRule : TelemetrySwitchRule
{
    public const string KeyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection";
    public const string ValueName = "AllowTelemetry";

    /// The second key a diagnostic data level is recorded under. brisk reads
    /// it and never writes it. Nothing consumes this read yet: the read-back
    /// that will compare the two numbers and say "written but ignored" is a
    /// later task of this wave. The reason to read one and write the other
    /// from the start is that the comparison only means anything while the
    /// numbers can disagree — a fix that wrote both would leave that later
    /// task reading brisk's own number back to itself.
    public const string EffectiveKeyPath =
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection";

    /// Enhanced and above reads as on. Basic is what the fix writes; Security
    /// (0) is below it, so a machine already there is left alone — a rule
    /// that read "anything but Basic" as on would raise it.
    private const int Enhanced = 2;
    private const int Basic = 1;

    public override string Id => "diagnostic-level";

    public override IReadOnlyList<RegistryValue> Values { get; } = new[]
    {
        new RegistryValue(KeyPath, ValueName, OnValue: Enhanced, OffValue: Basic),
    };

    /// The level recorded at the second key, or null when nothing is written
    /// there and when the key cannot be read. Never 0: "nothing to read" and
    /// "the Security level" are different answers, and this gives different
    /// ones — though it does not distinguish absent from unreadable, so a
    /// caller may not report either as the other.
    public int? EffectiveLevel(DiagnosticContext ctx) =>
        ctx.Registry.GetInt(EffectiveKeyPath, ValueName);

    /// A threshold rather than an exact match, and absent still reads as on:
    /// no policy written at all does not read as held at basic either.
    protected override bool ReadsAsOn(int? actual, RegistryValue value) =>
        actual is null || actual >= value.OnValue;

    protected override string Title =>
        "The diagnostic data level is not held at the basic level";

    protected override string Evidence =>
        "brisk read the diagnostic data policy on this machine: it is set " +
        "above the basic level, or it is not set at all, and neither of those " +
        "reads as held at basic. brisk reads the setting itself and nothing " +
        "past it. Holding it at basic writes one value, needs administrator " +
        "rights, and can be undone.";

    protected override string FixDescription =>
        "Hold the diagnostic data level at basic (undoable)";
}
