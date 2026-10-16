using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;

namespace BriskEngine.Actions.Switches;

/// One registry value of a switch. OffValue is what the fix writes and what
/// reads as done. AbsentIsOff: a missing value already means "done", and the
/// fix never creates it. AlsoOff: other numbers that also mean "done".
public sealed record SwitchValue(string KeyPath, string ValueName, int OffValue,
    bool AbsentIsOff = false, int[]? AlsoOff = null);

public sealed record SwitchSpec(string Id, string Title, string Evidence,
    IReadOnlyList<SwitchValue> Values, WriteEffect Effect = WriteEffect.NotAPolicy);

/// A TelemetrySwitchRule built from data, so a new switch is a few lines in
/// SwitchCatalog and rides FixRunner, FixJournal and ReadBack unchanged.
public class SwitchRule : TelemetrySwitchRule
{
    private sealed record PriorValue(string KeyPath, string ValueName, int? Value);

    protected SwitchSpec Spec { get; }

    public SwitchRule(SwitchSpec spec) { Spec = spec; }

    public override string Id => Spec.Id;

    public override IReadOnlyList<RegistryValue> Values => Spec.Values
        .Select(v => new RegistryValue(v.KeyPath, v.ValueName,
            OnValue: v.OffValue == 0 ? 1 : 0, OffValue: v.OffValue))
        .ToList();

    protected override string Title => Spec.Title;
    protected override string Evidence => Spec.Evidence;
    protected override string FixDescription => Spec.Title;

    public override WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => Spec.Effect;

    internal static bool ReadsAsOn(int? actual, SwitchValue v)
    {
        if (actual is null) return !v.AbsentIsOff;
        if (actual == v.OffValue) return false;
        return v.AlsoOff is null || !v.AlsoOff.Contains(actual.Value);
    }

    protected override bool ReadsAsOn(int? actual, RegistryValue value) =>
        ReadsAsOn(actual, Spec.Values.First(s =>
            s.KeyPath == value.KeyPath && s.ValueName == value.ValueName));

    /// Writes only the values that read as on: a service that does not exist
    /// on this machine must not get a Start value created for it.
    public override string Fix(DiagnosticContext ctx)
    {
        var touched = Spec.Values
            .Where(v => ReadsAsOn(ctx.Registry.GetInt(v.KeyPath, v.ValueName), v))
            .ToList();
        var prior = touched
            .Select(v => new PriorValue(v.KeyPath, v.ValueName, ctx.Registry.GetInt(v.KeyPath, v.ValueName)))
            .ToList();
        foreach (var v in touched) ctx.Registry.SetInt(v.KeyPath, v.ValueName, v.OffValue);
        return JsonSerializer.Serialize(prior);
    }

    public override void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<List<PriorValue>>(priorStateJson) ?? new();
        foreach (var p in prior)
        {
            if (p.Value is null) ctx.Registry.DeleteValue(p.KeyPath, p.ValueName);
            else ctx.Registry.SetInt(p.KeyPath, p.ValueName, p.Value.Value);
        }
    }
}
