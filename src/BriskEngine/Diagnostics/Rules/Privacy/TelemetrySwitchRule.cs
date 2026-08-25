using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// The six switches this wave gives brisk, on a privacy page that does not
/// exist yet. Five of them share one shape: a handful of registry values, a
/// number that reads as on, a number the fix writes, and an undo that restores
/// every value a COMPLETED fix recorded. Not every value a fix touched:
/// FixRunner journals the prior state only after Fix returns, so a multi-value
/// write that threw partway leaves the writes it already made unrecorded and
/// un-undoable. Nothing here is atomic and nothing here says it is.
///
/// The sixth, LocationRule, keeps the family's finding and its undo discipline
/// but not its numbers: that switch's state is a WORD, so it carries no
/// RegistryValue at all and overrides every member that would have read or
/// written one. What it shares is what matters — the same finding, the same
/// Notice, and an undo that puts back what the fix found.
///
/// The shape exists because of one property none of brisk's other fixable
/// rules has: ABSENCE reads as on. Not because brisk knows what Windows does
/// with an unwritten value, but because it cannot read one as off, and what
/// cannot be read as off is not reported as off. So Detect fires on a registry
/// with nothing in it, and Undo has to DELETE the value the fix created rather
/// than write the off number over it. A number written where there was none is
/// a second change wearing an undo's name — it would leave a machine reading
/// "somebody decided this" on a setting nobody had touched.
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

    /// Auto is a consent level, not a topic, and brisk's two fix-all surfaces
    /// read it differently — so this default is load-bearing, and the two
    /// switches that cost the user something override it to Confirm.
    ///
    /// The GUI's "Fix all (safe)" excludes the whole privacy topic by rule id,
    /// category or no category. That predicate lives in Brisk's FixAllService,
    /// which this assembly cannot see, cannot reference and does not enforce;
    /// FixAllServiceTests is what holds it.
    ///
    /// `brisk fix --all` on the CLI is a different path with a different
    /// answer: it selects on this property alone, so it DOES turn the four
    /// consequence-free switches off today. That is a decision, not a gap —
    /// nothing a user relies on stops working when an advertising ID goes off.
    /// It is also exactly why location and activity-history are Confirm:
    /// `--all` may never take Find my device or Timeline away from somebody
    /// who was shown no consequence. ProgramFixTests pins that end.
    public virtual RuleCategory Category => RuleCategory.Auto;

    /// Public because the read-back a later task of this wave adds will ask a
    /// second question of the same values — "is the number brisk wrote still
    /// the number that is there?" — and IsOn, which answers only "does this
    /// read as on", cannot. Nothing outside this class reads it today.
    ///
    /// EMPTY for LocationRule, whose state is a word and not a number. A
    /// caller that walks this collection to decide what a switch is set to
    /// gets no values for that rule and must not read that as "nothing to
    /// check" — it means "ask this rule instead".
    public abstract IReadOnlyList<RegistryValue> Values { get; }

    /// English, and pinned identical to this rule's English resx entry by
    /// TelemetrySwitchRuleTests. The engine's prose is what the CLI prints
    /// and what any consumer without a resource table falls back to, so the
    /// two have to say the same thing.
    protected abstract string Title { get; }

    protected abstract string Evidence { get; }

    protected abstract string FixDescription { get; }

    /// True when any one of the switch's values does not read as off.
    ///
    /// Virtual, and it is the ONLY read a subclass has to replace. A rule
    /// whose state is not a number has no values to walk here and would
    /// answer "off" for every machine ever built; Detect dispatches through
    /// this, so overriding it is what makes Detect right for that rule too.
    /// A public read that says off while Detect fires would be the rule
    /// contradicting itself.
    public virtual bool IsOn(DiagnosticContext ctx) =>
        Values.Any(v => ReadsAsOn(ctx.Registry.GetInt(v.KeyPath, v.ValueName), v));

    /// Anything that does not explicitly read as off reads as on. That
    /// covers absence — brisk cannot read an unwritten value as off, and it
    /// does not know what Windows does with one, so it does not report one as
    /// off — and it covers a number that is neither the on nor the off value,
    /// which an exact match on OnValue used to report as protection.
    /// Reporting an unrecognised number as off is the silent direction, and
    /// this wave's standing rule is that what cannot be read as off is not
    /// reported as off.
    protected virtual bool ReadsAsOn(int? actual, RegistryValue value) =>
        actual != value.OffValue;

    /// Not virtual, and neither is the finding below it factored out for a
    /// subclass to reuse. Both were, for one commit, to serve a LocationRule
    /// override that turned out to be byte-identical to this: IsOn is the
    /// only read that differs, IsOn is virtual, and this dispatches through
    /// it. An override that does nothing is a second place to drift.
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

    public virtual string Fix(DiagnosticContext ctx)
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

    public virtual void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Prior>(priorStateJson)!;
        foreach (var v in prior.Values)
        {
            if (v.Value is null) ctx.Registry.DeleteValue(v.KeyPath, v.ValueName);
            else ctx.Registry.SetInt(v.KeyPath, v.ValueName, v.Value.Value);
        }
    }
}
