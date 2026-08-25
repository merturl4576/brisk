using System;
using System.Collections.Generic;
using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// Location access, and one of the two switches in this family that cost the
/// user something: Find my device relies on location, and switching location
/// off stops it working. That sentence is in the rule's own Evidence as well
/// as in both resx files, not in the advice alone. `brisk scan` prints every
/// finding's title and evidence, and `brisk fix --rule location` prints them
/// again before it asks for --yes; neither prints advice, and the word appears
/// nowhere in the CLI. A loss named only in the advice is a loss no CLI user
/// is ever shown.
///
/// Windows records this consent as a WORD — "Allow" or "Deny" — where the rest
/// of the family uses a number. RegistryValue carries an on number and an off
/// number and cannot carry a word, so this rule ships an empty Values list and
/// reads, writes and restores its own state. It keeps everything else the
/// family gives it: the same finding, the same Notice, and an undo of the same
/// shape — its own code, but code that deletes a value the fix created rather
/// than writing "Allow" over an absence nobody chose.
public sealed class LocationRule : TelemetrySwitchRule
{
    public const string KeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager" +
        @"\ConsentStore\location";
    public const string ValueName = "Value";

    /// The one word that reads as off, and the word the fix writes. There is
    /// no second constant for "on": this rule never tests for "Allow", because
    /// what cannot be read as denied is not reported as denied.
    public const string Denied = "Deny";

    /// What the fix found: the word that was there, or null for nothing there.
    /// Null is also what brisk gets when the value exists but cannot be read
    /// as text — a value of some other type, or a key it may not open — and
    /// the two cannot be told apart from here, so an undo of that fix deletes
    /// rather than guesses. That is the same trade the numbered switches make.
    private sealed record Prior(string? Value);

    public override string Id => "location";

    /// Confirm, not Auto, and that is the whole point of this rule's consent
    /// level: `brisk fix --all` selects Auto rules, so Auto here would mean
    /// somebody who typed --all and was shown no consequence loses Find my
    /// device. ProgramFixTests pins that it cannot.
    public override RuleCategory Category => RuleCategory.Confirm;

    /// Empty, and not by omission. A caller walking the family's Values to
    /// decide what a switch reads gets nothing here and must ask this rule
    /// instead; TelemetrySwitchRuleTests fails if this ever grows a value.
    public override IReadOnlyList<RegistryValue> Values { get; } =
        Array.Empty<RegistryValue>();

    /// True unless the consent reads exactly as denied, case aside. Nothing
    /// else is listed here and nothing else needs to be: absence, an
    /// unrecognised word and a value brisk cannot read as text all read as on,
    /// and so does anything nobody thought of. What cannot be read as off is
    /// not reported as off — reporting a state brisk did not read as
    /// protection is the silent direction this wave refuses.
    public override bool IsOn(DiagnosticContext ctx) =>
        !string.Equals(ctx.Registry.GetString(KeyPath, ValueName), Denied,
            StringComparison.OrdinalIgnoreCase);

    /// Overridden because the read is a word rather than a number; the finding
    /// itself is still the family's, so this rule cannot drift into carrying
    /// different keys, a different kind or a different star count.
    public override DiagnosticFinding? Detect(DiagnosticContext ctx) =>
        IsOn(ctx) ? Finding() : null;

    public override string Fix(DiagnosticContext ctx)
    {
        var prior = new Prior(ctx.Registry.GetString(KeyPath, ValueName));
        ctx.Registry.SetString(KeyPath, ValueName, Denied);
        return JsonSerializer.Serialize(prior);
    }

    public override void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Prior>(priorStateJson)!;
        if (prior.Value is null) ctx.Registry.DeleteValue(KeyPath, ValueName);
        else ctx.Registry.SetString(KeyPath, ValueName, prior.Value);
    }

    protected override string Title => "Location access is not switched off";

    /// One test named, no states listed. An evidence sentence that enumerates
    /// what a read found goes false the moment the read widens, which is what
    /// broke this wave's evidence sentences once already — and this read has a
    /// state no such list would have caught anyway: a value that is there and
    /// cannot be read as text.
    protected override string Evidence =>
        "brisk read the location consent on this machine and it does not read " +
        "as denied. brisk reads the setting itself and nothing past it; what " +
        "any app does with it is not something brisk can see from here. This " +
        "switch costs you something — switch it off and Find my device stops " +
        "working. It writes one value and can be undone.";

    protected override string FixDescription =>
        "Switch location access off — Find my device stops working (undoable)";
}
