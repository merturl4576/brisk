using System.Collections.Generic;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// Speech and typing personalisation. The only switch in this family with two
/// values, and the only one whose numbers run the other way: here 1 is the
/// restricted state the fix writes and 0 — or nothing at all — reads as on.
/// Either value left permissive is a finding, so a machine where somebody
/// restricted text and never touched ink still reads as on.
public sealed class SpeechTypingRule : TelemetrySwitchRule
{
    public const string KeyPath = @"HKCU\Software\Microsoft\InputPersonalization";
    public const string TextValueName = "RestrictImplicitTextCollection";
    public const string InkValueName = "RestrictImplicitInkCollection";

    public override string Id => "speech-typing";

    public override IReadOnlyList<RegistryValue> Values { get; } = new[]
    {
        new RegistryValue(KeyPath, TextValueName, OnValue: 0, OffValue: 1),
        new RegistryValue(KeyPath, InkValueName, OnValue: 0, OffValue: 1),
    };

    protected override string Title =>
        "Speech and typing personalisation is not restricted";

    protected override string Evidence =>
        "brisk read the two values this setting uses on this machine: at " +
        "least one of them allows personalisation, or is not set at all, and " +
        "neither of those reads as restricted. brisk reads the settings " +
        "themselves and nothing past them. Restricting it writes two values " +
        "and can be undone.";

    protected override string FixDescription =>
        "Restrict speech and typing personalisation (undoable)";
}
