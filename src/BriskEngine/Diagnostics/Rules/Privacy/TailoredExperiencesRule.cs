using System.Collections.Generic;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// Tailored experiences. One value, and its absence reads as on.
public sealed class TailoredExperiencesRule : TelemetrySwitchRule
{
    public const string KeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy";
    public const string ValueName =
        "TailoredExperiencesWithDiagnosticDataEnabled";

    public override string Id => "tailored-experiences";

    public override IReadOnlyList<RegistryValue> Values { get; } = new[]
    {
        new RegistryValue(KeyPath, ValueName, OnValue: 1, OffValue: 0),
    };

    protected override string Title => "Tailored experiences is not switched off";

    protected override string Evidence =>
        "brisk read the tailored experiences switch on this machine: it is " +
        "set to on, or it is not set at all, and neither of those reads as " +
        "off. brisk reads the setting itself and nothing past it. Switching " +
        "it off writes one value and can be undone.";

    protected override string FixDescription =>
        "Switch tailored experiences off (undoable)";
}
