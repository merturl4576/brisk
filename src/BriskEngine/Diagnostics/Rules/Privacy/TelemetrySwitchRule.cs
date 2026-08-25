using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// The four switches a later task of this wave will turn off behind one
/// button on a privacy page that does not exist yet. Four rules share
/// one shape: a handful of registry values, a number that reads as on, a
/// number the fix writes, and an undo that restores every value a COMPLETED
/// fix recorded. Not every value a fix touched: FixRunner journals the prior
/// state only after Fix returns, so a multi-value write that threw partway
/// leaves the writes it already made unrecorded and un-undoable. Nothing
/// here is atomic and nothing here says it is.
///
/// The shape exists because of one property none of brisk's other fixable
/// rules has: ABSENCE reads as on. A value nobody has written is the
/// permissive state on every one of these paths, so Detect fires on a
/// registry with nothing in it, and Undo has to DELETE the value the fix
/// created rather than write the off number over it. A number written where
/// there was none is a second change wearing an undo's name — it would leave
/// a machine reading "somebody decided this" on a setting nobody had touched.
///
/// What every subclass's copy says, and what none of it may say: it reports
/// how a switch on this machine currently reads. brisk reads a registry
/// value. It cannot observe what any app or service does with the setting and
/// never speaks for one.
public abstract class TelemetrySwitchRule : IDiagnosticRule
{
    /// One value a switch owns. OffValue is what the fix writes and the only
    /// number the default read treats as off. OnValue is the number that
    /// reads as on — the default read does not need it, since anything that
    /// is not OffValue reads as on, but the one switch that is a level
    /// compares against it as a threshold, and the table test pins it.
    public sealed record RegistryValue(
        string KeyPath, string ValueName, int OnValue, int OffValue);

    /// What the fix found, value by value and naming its own path rather than
    /// relying on position: the journal outlives the build that wrote it, so
    /// an undo reading an old entry restores the paths that entry names
    /// instead of whatever now sits at the same index.
    private sealed record PriorValue(string KeyPath, string ValueName, int? Value);

    private sealed record Prior(IReadOnlyList<PriorValue> Values);

    public abstract string Id { get; }

    /// Auto is a consent level, not a topic. These four cost the user nothing
    /// visible, which is why a later task of this wave can put them behind a
    /// single button; the privacy rules that cost something will be Confirm
    /// and are not written yet. Nothing acts on this category for a privacy
    /// finding today — FixAllService excludes the topic outright.
    public RuleCategory Category => RuleCategory.Auto;

    /// Public because the read-back a later task of this wave adds will ask a
    /// second question of the same values — "is the number brisk wrote still
    /// the number that is there?" — and IsOn, which answers only "does this
    /// read as on", cannot. Nothing outside this class reads it today.
    public abstract IReadOnlyList<RegistryValue> Values { get; }

    /// English, and pinned identical to this rule's English resx entry by
    /// TelemetrySwitchRuleTests. The engine's prose is what the CLI prints
    /// and what any consumer without a resource table falls back to, so the
    /// two have to say the same thing.
    protected abstract string Title { get; }

    protected abstract string Evidence { get; }

    protected abstract string FixDescription { get; }

    /// True when any one of the switch's values does not read as off.
    public bool IsOn(DiagnosticContext ctx) =>
        Values.Any(v => ReadsAsOn(ctx.Registry.GetInt(v.KeyPath, v.ValueName), v));

    /// Anything that does not explicitly read as off reads as on. That
    /// covers absence, which is the permissive state on all of these paths —
    /// a machine nobody has touched reads as on — and it covers a number
    /// that is neither the on nor the off value, which an exact match on
    /// OnValue used to report as protection. Reporting an unrecognised
    /// number as off is the silent direction, and this wave's standing rule
    /// is that what cannot be read as off is not reported as off.
    protected virtual bool ReadsAsOn(int? actual, RegistryValue value) =>
        actual != value.OffValue;

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        if (!IsOn(ctx)) return null;
        return new DiagnosticFinding(
            Id, $"rule.{Id}.title", Title, Evidence,
            // Info, one star. The impact scale measures expected PERFORMANCE
            // impact and a switch left on costs none — one rather than zero
            // only because the field is documented 1..5. A surface that shows
            // privacy findings has no business rendering a speed meter over
            // them at all.
            Severity.Info, Category, ImpactStars: 1, CanFix: true,
            FixDescription: FixDescription,
            EvidenceKey: $"rule.{Id}.evidence", EvidenceArgs: null,
            // No headline: these measure no number, and the spec forbids
            // inventing one to lead with.
            Headline: null,
            // Privacy is a second axis. brisk shows it and acts on it and
            // never grades it — including the parts it can fix.
            Kind: FindingKind.Notice);
    }

    public string Fix(DiagnosticContext ctx)
    {
        // Read every value before writing any of them, so the record of what
        // was there is taken from the machine as found.
        var prior = new Prior(Values
            .Select(v => new PriorValue(v.KeyPath, v.ValueName,
                ctx.Registry.GetInt(v.KeyPath, v.ValueName)))
            .ToList());
        foreach (var v in Values) ctx.Registry.SetInt(v.KeyPath, v.ValueName, v.OffValue);
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Prior>(priorStateJson)!;
        foreach (var v in prior.Values)
        {
            if (v.Value is null) ctx.Registry.DeleteValue(v.KeyPath, v.ValueName);
            else ctx.Registry.SetInt(v.KeyPath, v.ValueName, v.Value.Value);
        }
    }
}
