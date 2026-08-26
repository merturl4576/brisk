using System.Collections.Generic;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// The per-user advertising ID. One value, and its absence reads as on.
public sealed class AdvertisingIdRule : TelemetrySwitchRule
{
    public const string KeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo";
    public const string ValueName = "Enabled";

    public override string Id => "advertising-id";

    public override IReadOnlyList<RegistryValue> Values { get; } = new[]
    {
        new RegistryValue(KeyPath, ValueName, OnValue: 1, OffValue: 0),
    };

    protected override string Title => "The advertising ID is not switched off";

    protected override string Evidence =>
        "brisk read the advertising ID switch on this machine and it does not " +
        "read as off: the value is either something other than off, or not " +
        "set at all. brisk reads the setting itself and nothing past it; what " +
        "any app does with it is not something brisk can see from here. " +
        "Switching it off writes one value and can be undone.";

    protected override string FixDescription =>
        "Switch the advertising ID off (undoable)";
}
