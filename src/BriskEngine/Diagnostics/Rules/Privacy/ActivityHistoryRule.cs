using System.Collections.Generic;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// Activity history, and one of the two switches in this family that cost the
/// user something: Timeline is built on it, and the loss brisk names is that
/// switching this off ends Timeline. That sentence is in this rule's own
/// Evidence as well as in both resx files, because the CLI prints a finding's
/// title and its evidence and never prints advice — and it never travels
/// alone, because of the policy paragraph below.
///
/// Two values under one policy key, and either of them not reading as off is
/// the finding: a machine where somebody set one and left the other alone
/// still does not read as off. Both live under HKLM, so the fix needs
/// administrator rights — FixRunner reports the refusal when they are missing
/// instead of pretending the write happened.
///
/// And both are a POLICY, which is the thing an edition of Windows can
/// ignore. brisk re-reads the two values it wrote and nothing else, so after
/// a successful fix this finding always clears — whether or not the machine
/// acted on the policy. The copy has to say that, or brisk is reporting a
/// consequence it did not achieve, which is the one failure this whole wave
/// exists to refuse. DiagnosticLevelRule makes the same admission, in its
/// evidence and its advice as this rule does, and can do better than this
/// rule can: it reads a second, consumer-side key, so it has two numbers to
/// compare. No second key is read here: none was established for this setting
/// the way diagnostic-level's was, and a registry path brisk cannot vouch for
/// would be a worse defect than the promise it replaces — brisk would be
/// reading a path that may hold nothing and building the "written but
/// ignored" sentence on top of it. So the copy says what brisk can see
/// instead of promising what it cannot, and EffectOfTheWrite below says the
/// same thing to the read-back.
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

    /// Unread, on every machine, and it is the honest answer rather than a
    /// gap. This is a policy — the one kind of value an edition of Windows
    /// can have written down and still not act on — and brisk has no second
    /// value for it, so it cannot tell an edition that honoured it from one
    /// that ignored it.
    ///
    /// The alternative was for the read-back to fall through to its "still
    /// off" line, which on a Home machine that ignored the policy would tell
    /// somebody their switch is off on exactly the machine where it is not.
    /// That is the failure this wave exists to refuse, so the rule says it
    /// does not know.
    ///
    /// It takes the context it is handed and reads nothing from it; the
    /// parameter is the base's, and an answer that varied by machine would
    /// need a value brisk does not have.
    public override WriteEffect EffectOfTheWrite(DiagnosticContext ctx) =>
        WriteEffect.Unread;

    protected override string Title => "Activity history is not switched off";

    /// One test named, no states listed — see LocationRule.Evidence for why
    /// this wave stopped enumerating what a read found. The closing clause is
    /// word for word the one rule.diagnostic-level.advice already uses, so a
    /// grep finds every place brisk makes this admission; it is in the
    /// EVIDENCE as well as the advice because the CLI prints evidence and
    /// never prints advice.
    protected override string Evidence =>
        "brisk read the two values this policy uses on this machine and at " +
        "least one of them does not read as off. brisk reads the settings " +
        "themselves and nothing past them. This switch costs you something — " +
        "switch it off and Timeline ends. It writes two values, needs " +
        "administrator rights, and can be undone. What brisk re-reads on the " +
        "next scan is the policy it wrote, so it will report this switch as " +
        "off once the write succeeds; whether this edition of Windows acts " +
        "on that policy is not something that read can tell you.";

    protected override string FixDescription =>
        "Switch activity history off — Timeline ends (needs administrator " +
        "rights, undoable)";
}
