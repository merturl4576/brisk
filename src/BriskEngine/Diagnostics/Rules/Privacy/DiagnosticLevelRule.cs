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
    /// it and never writes it, and that separation is what makes the
    /// comparison mean anything: a fix that wrote both would leave the
    /// read-back reading brisk's own number back to itself. EffectOfTheWrite
    /// below is what consumes it — it is the one reading in this family that
    /// can tell an edition that acted on a policy from one that did not.
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

    /// The one switch in this family brisk can say more about than "the value
    /// I wrote is still there". The policy asks for basic; this reads the
    /// second value this machine keeps for the same setting, at a key brisk
    /// never writes, and reports whether the two disagree.
    ///
    /// The threshold is the same Enhanced this rule's own read uses, so the
    /// two readings are speaking one language: what the rule calls on at the
    /// policy key is what this calls not-acted-on at the second key.
    ///
    /// Null is Unread and never ActedOn. EffectiveLevel cannot tell an absent
    /// value from one it could not read, so neither can this — and neither of
    /// those is "the machine is at basic".
    ///
    /// WHAT THIS IS NOT: brisk did not watch Windows do anything, and it did
    /// not establish that the second key is where an applied policy lands. It
    /// read two values this machine keeps for one setting and found them
    /// disagreeing, and Ignored is the reading it takes from that. Everything
    /// downstream is about a local policy not being acted on; nothing
    /// downstream is about what leaves the machine, which brisk never
    /// measured.
    public override WriteEffect EffectOfTheWrite(DiagnosticContext ctx) =>
        EffectiveLevel(ctx) switch
        {
            null => WriteEffect.Unread,
            >= Enhanced => WriteEffect.Ignored,
            _ => WriteEffect.ActedOn,
        };

    protected override string Title =>
        "The diagnostic data level is not held at the basic level";

    /// The closing clause was in rule.diagnostic-level.advice and nowhere
    /// else, which meant no CLI user was ever shown it: `brisk scan` and
    /// `brisk fix --rule <id>` print a finding's title and evidence, and
    /// nothing in Brisk.Cli reads a rule.*.advice string at all. So it is in
    /// the evidence too, word for word as the advice has it, and
    /// ThePolicySwitch_DoesNotPromiseTheEditionObeyedIt now demands it of
    /// both policy switches.
    protected override string Evidence =>
        "brisk read the diagnostic data policy on this machine: it is set " +
        "above the basic level, or it is not set at all, and neither of those " +
        "reads as held at basic. brisk reads the setting itself and nothing " +
        "past it. Holding it at basic writes one value, needs administrator " +
        "rights, and can be undone. What brisk re-reads on the next scan is " +
        "the policy value it wrote; whether this edition of Windows acts on " +
        "that policy is not something that read can tell you.";

    protected override string FixDescription =>
        "Hold the diagnostic data level at basic (undoable)";
}
