using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Actions.Rules;
using BriskEngine.Actions.Switches;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;

namespace BriskEngine.Actions;

public sealed record ActionProbes(
    IProcessRunner Runner,
    IUiEffectsProbe Ui,
    IPowerOverlayProbe Overlay,
    IOptionalFeatureProbe Features,
    IScheduledTaskProbe Tasks,
    StartupManager Startup,
    Func<bool> HiberfilExists);

/// Finds the rule behind a row by id: the existing registry, the new rules,
/// and "startup:{hive}:{name}" ids built on demand.
public sealed class ActionRules
{
    private readonly Dictionary<string, IDiagnosticRule> _byId;
    private readonly StartupManager _startup;

    public ActionRules(ActionProbes probes)
    {
        _startup = probes.Startup;
        var rules = new List<IDiagnosticRule>(DiagnosticRuleRegistry.All);
        rules.AddRange(SwitchCatalog.Plain.Select(s => new SwitchRule(s)));
        rules.Add(new TelemetryServicesRule(probes.Runner));
        rules.Add(new TaskSwitchRule(probes.Tasks));
        rules.Add(new WindowAnimationsRule(probes.Ui));
        rules.Add(new WindowedGamesRule());
        rules.Add(new PowerModeRule(probes.Overlay));
        rules.Add(new VirtualMachinePlatformRule(probes.Features, probes.Runner));
        rules.Add(new FirewallOnRule(probes.Runner));
        rules.Add(new DefenderRealtimeRule(probes.Runner));
        rules.Add(new HibernationRule(probes.Runner, probes.HiberfilExists));
        _byId = rules.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IDiagnosticRule? Find(string id)
    {
        if (_byId.TryGetValue(id, out var rule)) return rule;
        return StartupEntryRule.TryParseId(id, out var hive, out var name)
            ? new StartupEntryRule(_startup, hive, name)
            : null;
    }
}
