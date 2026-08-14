using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using BriskEngine.Models;
using BriskEngine.Paths;

namespace BriskEngine.Diagnostics.Rules;

public sealed class HardwareAccelerationRule : IDiagnosticRule
{
    private static readonly (string Process, string LocalStateTemplate)[] Browsers =
    {
        ("chrome", @"%LOCALAPPDATA%\Google\Chrome\User Data\Local State"),
        ("msedge", @"%LOCALAPPDATA%\Microsoft\Edge\User Data\Local State"),
    };

    public string Id => "hw-acceleration";
    public RuleCategory Category => RuleCategory.Confirm;

    private static List<(string Process, string Path)> Offenders(DiagnosticContext ctx)
    {
        var offenders = new List<(string, string)>();
        foreach (var (process, template) in Browsers)
        {
            var path = PathExpander.Expand(template);
            if (path is null) continue;
            var text = ctx.Files.ReadAllText(path);
            if (text is null) continue;
            try
            {
                var enabled = JsonNode.Parse(text)?["hardware_acceleration_mode"]?["enabled"];
                if (enabled is not null && enabled.GetValue<bool>() == false)
                    offenders.Add((process, path));
            }
            catch (Exception) { /* unreadable Local State — not our problem */ }
        }
        return offenders;
    }

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var offenders = Offenders(ctx);
        if (offenders.Count == 0) return null;
        return new DiagnosticFinding(Id, "rule.hw-acceleration.title",
            "Browser hardware acceleration is turned off",
            $"Hardware acceleration is disabled in: {string.Join(", ", offenders.Select(o => o.Process))}. " +
            "Video decoding falls back to the CPU, which stutters on YouTube.",
            Severity.Warning, Category, ImpactStars: 4, CanFix: true,
            FixDescription: "Re-enable hardware acceleration (browser must be closed)");
    }

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Dictionary<string, bool>();
        foreach (var (process, path) in Offenders(ctx))
        {
            if (ctx.RunningApps.IsRunning(process))
                throw new InvalidOperationException($"Close {process} first, then retry the fix.");
            var node = JsonNode.Parse(ctx.Files.ReadAllText(path)!)!;
            prior[path] = false;
            node["hardware_acceleration_mode"]!["enabled"] = true;
            ctx.Files.WriteAllText(path, node.ToJsonString());
        }
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Dictionary<string, bool>>(priorStateJson)!;
        foreach (var (path, original) in prior)
        {
            var text = ctx.Files.ReadAllText(path);
            if (text is null) continue;
            var node = JsonNode.Parse(text)!;
            node["hardware_acceleration_mode"] ??= new JsonObject();
            node["hardware_acceleration_mode"]!["enabled"] = original;
            ctx.Files.WriteAllText(path, node.ToJsonString());
        }
    }
}
