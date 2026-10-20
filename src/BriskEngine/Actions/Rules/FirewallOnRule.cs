using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Models;

namespace BriskEngine.Actions.Rules;

public sealed class FirewallOnRule : IDiagnosticRule
{
    private static readonly (string Profile, string Netsh)[] Profiles =
    {
        ("DomainProfile", "domainprofile"),
        ("StandardProfile", "privateprofile"),
        ("PublicProfile", "publicprofile"),
    };

    private readonly IProcessRunner _runner;

    public FirewallOnRule(IProcessRunner runner) { _runner = runner; }

    public string Id => "firewall-on";
    public RuleCategory Category => RuleCategory.Auto;

    private static List<string> OffProfiles(DiagnosticContext ctx) => Profiles
        .Where(p =>
        {
            try { return ctx.Registry.GetInt($@"{SecurityPosture.FirewallRoot}\{p.Profile}", "EnableFirewall") == 0; }
            catch (Exception) { return false; }
        })
        .Select(p => p.Netsh)
        .ToList();

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var off = OffProfiles(ctx);
        if (off.Count == 0) return null;
        return new DiagnosticFinding(Id, $"rule.{Id}.title", "Windows Firewall is off",
            $"Windows Firewall is off for {off.Count} network profile(s).",
            Severity.Warning, Category, ImpactStars: 3, CanFix: true,
            FixDescription: "Turn Windows Firewall back on (undoable)");
    }

    public string Fix(DiagnosticContext ctx)
    {
        var off = OffProfiles(ctx);
        foreach (var profile in off) Netsh(profile, "on");
        return JsonSerializer.Serialize(off);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        foreach (var profile in JsonSerializer.Deserialize<List<string>>(priorStateJson) ?? new())
            Netsh(profile, "off");
    }

    private void Netsh(string profile, string state)
    {
        var (exit, _) = _runner.Run("netsh.exe", $"advfirewall set {profile} state {state}");
        if (exit != 0) throw new InvalidOperationException($"netsh exited {exit}");
    }
}
