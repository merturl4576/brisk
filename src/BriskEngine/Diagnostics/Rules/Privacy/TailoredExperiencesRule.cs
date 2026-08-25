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
        "brisk read the tailored experiences switch on this machine and it " +
        "does not read as off: the value is either something other than off, " +
        "or not set at all. brisk reads the setting itself and nothing past " +
        "it. Switching it off writes one value and can be undone.";

    protected override string FixDescription =>
        "Switch tailored experiences off (undoable)";
}
