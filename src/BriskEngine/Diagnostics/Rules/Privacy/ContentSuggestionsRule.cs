using System.Collections.Generic;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// Windows' suggestion and promotion flags: suggested apps in Start, tips
/// and suggestions, lock-screen promotions, and silent installation of
/// promoted apps. Six per-user values; absence reads as on, which is also
/// Windows' default for them.
///
/// ContentDeliveryAllowed, the master switch above these, is deliberately
/// not here: it also turns off things people use (Spotlight pictures), and
/// this switch's promise is the safe tier's — it takes nothing away.
public sealed class ContentSuggestionsRule : TelemetrySwitchRule
{
    public const string RuleId = "windows-suggestions";
    public const string KeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    public override string Id => RuleId;

    public override IReadOnlyList<RegistryValue> Values { get; } = new[]
    {
        new RegistryValue(KeyPath, "SilentInstalledAppsEnabled", OnValue: 1, OffValue: 0),
        new RegistryValue(KeyPath, "SystemPaneSuggestionsEnabled", OnValue: 1, OffValue: 0),
        new RegistryValue(KeyPath, "SoftLandingEnabled", OnValue: 1, OffValue: 0),
        new RegistryValue(KeyPath, "RotatingLockScreenOverlayEnabled", OnValue: 1, OffValue: 0),
        new RegistryValue(KeyPath, "SubscribedContent-338388Enabled", OnValue: 1, OffValue: 0),
        new RegistryValue(KeyPath, "SubscribedContent-338389Enabled", OnValue: 1, OffValue: 0),
    };

    protected override string Title =>
        "Windows shows suggested apps and tips, and can install promoted apps on its own";

    protected override string Evidence =>
        "Six values under Windows' content delivery settings read as on or not " +
        "set: suggested apps in Start, tips and suggestions, lock-screen " +
        "promotions, and silent installation of promoted apps. brisk reads the " +
        "six values and nothing past them; what Windows has already installed " +
        "this way is not something those reads can tell you. Switching them off " +
        "writes six values and can be undone.";

    protected override string FixDescription =>
        "Switch suggestions and silent installs off (undoable)";
}
