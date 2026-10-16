using System;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;

namespace BriskEngine.Actions.Rules;

/// One startup entry as a rule, so disabling it is journaled and undone like
/// any switch. Disabled the way Task Manager does it, so it can be seen and
/// turned back on there.
public sealed class StartupEntryRule : IReadBackable
{
    private const string Prefix = "startup:";
    private readonly StartupManager _startup;
    private readonly string _hive;
    private readonly string _name;

    public StartupEntryRule(StartupManager startup, string hive, string name)
    {
        _startup = startup;
        _hive = hive;
        _name = name;
    }

    public static string IdFor(string hive, string name) => $"{Prefix}{hive}:{name}";

    public static bool TryParseId(string id, out string hive, out string name)
    {
        hive = name = "";
        if (!id.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var rest = id[Prefix.Length..];
        var colon = rest.IndexOf(':');
        if (colon <= 0 || colon == rest.Length - 1) return false;
        hive = rest[..colon];
        name = rest[(colon + 1)..];
        return true;
    }

    public string Id => IdFor(_hive, _name);
    public RuleCategory Category => RuleCategory.Auto;

    public bool IsOn(DiagnosticContext ctx) => _startup.List().Any(e =>
        string.Equals(e.Hive, _hive, StringComparison.OrdinalIgnoreCase)
        && string.Equals(e.Name, _name, StringComparison.OrdinalIgnoreCase)
        && e.Enabled);

    public WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => WriteEffect.NotAPolicy;

    public DiagnosticFinding? Detect(DiagnosticContext ctx) => IsOn(ctx)
        ? new DiagnosticFinding(Id, "rule.startup-entry.title", $"{_name} starts with Windows",
            $"{_name} starts every time you sign in.", Severity.Info, Category,
            ImpactStars: 1, CanFix: true, FixDescription: $"Stop {_name} starting with Windows (undoable)",
            Kind: FindingKind.Notice)
        : null;

    public string Fix(DiagnosticContext ctx)
    {
        if (!_startup.SetEnabled(_hive, _name, false))
            throw new UnauthorizedAccessException($"startup entry '{_name}' could not be changed");
        return "\"enabled\"";
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        if (!_startup.SetEnabled(_hive, _name, true))
            throw new UnauthorizedAccessException($"startup entry '{_name}' could not be changed");
    }
}
