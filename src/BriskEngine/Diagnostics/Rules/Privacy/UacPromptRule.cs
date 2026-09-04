using System.Collections.Generic;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// Whether Windows asks before a program takes administrator rights. The
/// three values live under the key UAC itself reads — not a Group Policy
/// key — so the write IS the setting and EffectOfTheWrite stays NotAPolicy.
///
/// THIS RULE INVERTS THE FAMILY'S ABSENCE RULE, ON PURPOSE. The privacy
/// switches read an unwritten value as on because they cannot read it as
/// off. Here Windows' default for all three values is prompting, so an
/// absent value cannot be read as "never prompt" and raises nothing; only a
/// WRITTEN 0 in EnableLUA or ConsentPromptBehaviorAdmin does. That is why
/// IsOn is overridden rather than left to walk Values.
///
/// PromptOnSecureDesktop is restored by the fix (to Windows' default) but
/// never decides the finding: the third position of Windows' own UAC slider
/// ("notify me, don't dim the desktop") sets it to 0 with
/// ConsentPromptBehaviorAdmin=5, and that is a choice, not a hole.
/// ConsentPromptBehaviorAdmin values 1..5 all prompt in some form; 0 alone
/// means "elevate without prompting".
///
/// Confirm, because the user will see prompts again and is told so first;
/// Confirm is also what keeps it out of `brisk fix --all`. Warning is the
/// family's one severity override; Kind stays Notice and the score does not
/// move (spec red line 5).
public sealed class UacPromptRule : TelemetrySwitchRule
{
    public const string RuleId = "uac-prompts";
    public const string KeyPath =
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    public const string EnableLua = "EnableLUA";
    public const string ConsentPromptBehaviorAdmin = "ConsentPromptBehaviorAdmin";
    public const string PromptOnSecureDesktop = "PromptOnSecureDesktop";

    public override string Id => RuleId;

    public override RuleCategory Category => RuleCategory.Confirm;

    protected override Severity Severity => Severity.Warning;

    /// OffValue is Windows' default for each; OnValue is the written 0 that
    /// means "never prompt". The family's Fix writes all three and records
    /// every prior value by path; its Undo restores them and deletes what
    /// was absent.
    public override IReadOnlyList<RegistryValue> Values { get; } = new[]
    {
        new RegistryValue(KeyPath, EnableLua, OnValue: 0, OffValue: 1),
        new RegistryValue(KeyPath, ConsentPromptBehaviorAdmin, OnValue: 0, OffValue: 5),
        new RegistryValue(KeyPath, PromptOnSecureDesktop, OnValue: 0, OffValue: 1),
    };

    /// The bad state, and nothing else: a written 0 in either of the two
    /// values that decide whether a prompt appears at all.
    public override bool IsOn(DiagnosticContext ctx) =>
        ctx.Registry.GetInt(KeyPath, EnableLua) == 0
        || ctx.Registry.GetInt(KeyPath, ConsentPromptBehaviorAdmin) == 0;

    protected override string Title =>
        "Windows does not ask before a program takes control";

    protected override string Evidence =>
        "brisk read Windows' three UAC values, and the one that decides " +
        "whether a program gets administrator rights with no prompt on screen " +
        "reads as \"never prompt\". Any program that asks for those rights " +
        "gets them, and nothing appears. Windows' default is to ask, on a " +
        "dimmed screen. Restoring the default writes up to three values and " +
        "can be undone. Programs will ask again; that is the point.";

    protected override string FixDescription =>
        "Ask before a program takes control again (undoable)";
}
