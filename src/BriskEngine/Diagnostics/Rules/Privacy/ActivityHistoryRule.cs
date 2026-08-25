using System.Collections.Generic;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// Activity history, and one of the two switches in this family that cost the
/// user something: Timeline is built on it, and switching it off ends
/// Timeline. That sentence is in this rule's own Evidence as well as in both
/// resx files, because the CLI prints a finding's title and its evidence and
/// never prints advice.
///
/// Two values under one policy key, and either of them not reading as off is
/// the finding: a machine where somebody set one and left the other alone
/// still does not read as off. Both live under HKLM, so the fix needs
/// administrator rights — FixRunner reports the refusal when they are missing
/// instead of pretending the write happened.
public sealed class ActivityHistoryRule : TelemetrySwitchRule
{
    public const string KeyPath = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System";
    public const string PublishValueName = "PublishUserActivities";
    public const string UploadValueName = "UploadUserActivities";

    public override string Id => "activity-history";

    /// Confirm, not Auto, and that is the whole point of this rule's consent
    /// level: `brisk fix --all` selects Auto rules, so Auto here would mean
    /// somebody who typed --all and was shown no consequence loses Timeline.
    /// ProgramFixTests pins that it cannot.
    public override RuleCategory Category => RuleCategory.Confirm;

    public override IReadOnlyList<RegistryValue> Values { get; } = new[]
    {
        new RegistryValue(KeyPath, PublishValueName, OnValue: 1, OffValue: 0),
        new RegistryValue(KeyPath, UploadValueName, OnValue: 1, OffValue: 0),
    };

    protected override string Title => "Activity history is not switched off";

    /// One test named, no states listed — see LocationRule.Evidence for why
    /// this wave stopped enumerating what a read found.
    protected override string Evidence =>
        "brisk read the two values this policy uses on this machine and at " +
        "least one of them does not read as off. brisk reads the settings " +
        "themselves and nothing past them. This switch costs you something — " +
        "switch it off and Timeline ends. It writes two values, needs " +
        "administrator rights, and can be undone.";

    protected override string FixDescription =>
        "Switch activity history off — Timeline ends (needs administrator " +
        "rights, undoable)";
}
