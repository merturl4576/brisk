using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// The switches the Privacy page turns off with one button. Four rules share
/// one shape: a handful of registry values, a number that reads as on, a
/// number the fix writes, and an undo that puts the machine back where it was
/// found.
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
    /// One value a switch owns. OnValue is the number that reads as on — a
    /// threshold rather than an exact match for one subclass, which is what
    /// ReadsAsOn being virtual is for. OffValue is what the fix writes.
    public sealed record RegistryValue(
        string KeyPath, string ValueName, int OnValue, int OffValue);

    /// What the fix found, value by value and naming its own path rather than
    /// relying on position: the journal outlives the build that wrote it, so
    /// an undo reading an old entry restores the paths that entry names
    /// instead of whatever now sits at the same index.
    private sealed record PriorValue(string KeyPath, string ValueName, int? Value);

    private sealed record Prior(IReadOnlyList<PriorValue> Values);

    public abstract string Id { get; }

    /// Auto is a consent level, not a topic. These four are the ones that
    /// cost the user nothing visible, which is why they sit behind one
    /// button; the privacy rules that cost something are Confirm.
    public RuleCategory Category => RuleCategory.Auto;

    /// Public because the read-back asks a second question of the same
    /// values — "is the number brisk wrote still the number that is there?"
    /// — and IsOn, which answers only "does this read as on", cannot.
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

    /// Absent counts as on for every switch in this family: a value nobody
    /// has written is the permissive state on all of these paths, so a
    /// machine nobody has touched reads as on.
    protected virtual bool ReadsAsOn(int? actual, RegistryValue value) =>
        actual is null || actual == value.OnValue;

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
