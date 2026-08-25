using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// What brisk can find out about whether this machine is acting on the value
/// a fix wrote. The read-back asks a rule this AFTER the rule has already
/// said the switch READS AS OFF, so every member below describes a machine in
/// that state — which is not the same as a machine where the value brisk
/// wrote is the value there. Where the two come apart, and which rules do it,
/// is on EffectOfTheWrite below.
///
/// It exists because a POLICY — a value under HKLM\SOFTWARE\Policies — is
/// the one kind of setting an edition of Windows can have written down and
/// still not act on. That is the Home-edition case the whole wave was built
/// around, and it is not something a switch's own read can see: the rule
/// re-reads the value it wrote, so a successful write always reads back as
/// off whether or not the edition honoured it.
///
/// The four answers are not a description of the six switches, they are what
/// a rule is allowed to say. Which one each switch actually says is asserted
/// in ReadBackTests, from the registry paths themselves rather than from a
/// list somebody maintains.
public enum WriteEffect
{
    /// This switch is not a policy: brisk writes the very value the setting
    /// is kept in, so there is nothing between the write and the setting for
    /// an edition to ignore, and no second value to go looking for.
    NotAPolicy,

    /// A policy, and a second value brisk reads and never writes says this
    /// machine is where the policy asks it to be.
    ActedOn,

    /// A policy, and that second value says this machine is not.
    Ignored,

    /// A policy, and brisk has no second reading for it. Either none was ever
    /// established for this setting — Task 3 declined to name a path it could
    /// not vouch for, rather than build this answer on a read that means
    /// nothing — or the one that exists could not be read on this machine.
    /// The two are one answer here on purpose: both mean brisk does not know,
    /// and a caller may report neither of them as the other.
    Unread,
}

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

    /// What brisk can find out about whether this machine is acting on the
    /// value this rule's fix wrote — see WriteEffect for what each answer
    /// means and why the question exists at all.
    ///
    /// NotAPolicy is the default because four of the six switches are not
    /// policies: their value IS the setting, and re-reading it is the whole
    /// answer. The two that live under a Policies key override this. Only
    /// DiagnosticLevelRule can answer it from a reading; ActivityHistoryRule
    /// overrides it to say it cannot, which is a different sentence from the
    /// default's "there is nothing here to check".
    ///
    /// The read-back asks this only after IsOn has read false, so an
    /// implementation may assume THE SWITCH READS AS OFF and does not have to
    /// re-establish that.
    ///
    /// It may NOT assume the value the fix wrote is the value sitting there.
    /// Those two coincide only for a rule that keeps BOTH default reads: the
    /// default IsOn, which walks Values, AND the default ReadsAsOn, which
    /// treats exactly OffValue as off. Replacing either is enough to part
    /// them, and one rule has replaced each:
    ///
    ///   DiagnosticLevelRule overrides READSASON with a threshold, so a
    ///   machine somebody else set to 0 reads as off with brisk's 1 gone.
    ///
    ///   LocationRule keeps the default ReadsAsOn and parts them anyway. It
    ///   overrides ISON to match a word without regard to case and ships an
    ///   empty Values, so "deny" reads as off where the fix wrote "Deny".
    ///
    /// The second is why the question is not "did I keep ReadsAsOn?". IsOn is
    /// the read this family EXPECTS a subclass to replace — the comment on it
    /// says so in as many words — so the override that parts them is the
    /// COMMON one, and a criterion naming only ReadsAsOn answers "safe" for
    /// the rule that already does it.
    ///
    /// WhichSwitchesReadAsOff_AtAStateTheirOwnFixDidNotWrite runs over every
    /// TelemetrySwitchRule DiagnosticRuleRegistry ships, so a seventh switch
    /// fails there until somebody records which side of this it falls on, and
    /// a switch that none of its planted states could make READ AS OFF fails
    /// rather than reporting a witness for a state it never reached. That
    /// test is the rule's own read: it was once the proxy "does this rule
    /// carry any RegistryValue?", which a word-valued rule shipping a
    /// non-empty Values walks straight past. What it does NOT do is prove a
    /// rule cannot part them: it plants a BOUNDED set of states and reports
    /// what it found among those.
    public virtual WriteEffect EffectOfTheWrite(DiagnosticContext ctx) =>
        WriteEffect.NotAPolicy;

    /// Public, and read from outside this class by the tests that pin the
    /// family's registry surface and by the tests that plant a switch back on
    /// — not by the read-back, which was the reason it was made public and
    /// which ended up not needing it. ReadBack decides everything through
    /// IsOn and EffectOfTheWrite, both virtual, so the rule answers for its
    /// own state; walking this collection instead would have been the defect
    /// the paragraph below warns about.
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
