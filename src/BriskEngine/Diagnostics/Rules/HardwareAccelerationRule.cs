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

    /// The readable files whose switch is off, and the files that exist but
    /// could not be parsed. A missing file is neither: that browser is not
    /// installed, and there is nothing to say about it.
    private static (List<(string Process, string Path)> Off, List<string> Unread) Look(
        DiagnosticContext ctx)
    {
        var off = new List<(string, string)>();
        var unread = new List<string>();
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
                    off.Add((process, path));
            }
            catch (Exception)
            {
                // A Local State that exists and does not parse was swallowed
                // here and reported as nothing — the same silence as a browser
                // with acceleration on (2026-09-07 live-run finding). brisk
                // did not read it, and Detect says so.
                unread.Add(process);
            }
        }
        return (off, unread);
    }

    private static List<(string Process, string Path)> Offenders(DiagnosticContext ctx) =>
        Look(ctx).Off;

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var (off, unread) = Look(ctx);
        if (off.Count == 0 && unread.Count == 0) return null;
        var unreadNames = string.Join(", ", unread);

        // Nothing readable is off, but something could not be read: a free
        // notice, no button. A fix over a file brisk cannot read would be a
        // write into the dark.
        if (off.Count == 0)
            return new DiagnosticFinding(Id, $"rule.{Id}.unread.title",
                "A browser's settings file could not be read",
                $"brisk could not read the settings file (Local State) of: {unreadNames}, so it " +
                "cannot tell whether hardware acceleration is on there. Nothing is changed over " +
                "a file brisk cannot read.",
                Severity.Info, Category, ImpactStars: 1, CanFix: false, FixDescription: null,
                EvidenceKey: $"rule.{Id}.evidence.unread", EvidenceArgs: new[] { unreadNames },
                Kind: FindingKind.Notice, ImpactClass: ImpactClass.Hygiene);

        var names = string.Join(", ", off.Select(o => o.Process));
        var evidence = $"Hardware acceleration is disabled in: {names}. " +
            "Video decoding falls back to the CPU, which stutters on YouTube.";
        if (unread.Count > 0)
            evidence += $" brisk could not read the settings file of: {unreadNames}, so it " +
                "cannot say for that one, and the fix leaves it alone.";
        return new DiagnosticFinding(Id, $"rule.{Id}.title",
            "Browser hardware acceleration is turned off", evidence,
            Severity.Warning, Category, ImpactStars: 4, CanFix: true,
            FixDescription: "Re-enable hardware acceleration (browser must be closed)",
            EvidenceKey: unread.Count == 0 ? $"rule.{Id}.evidence" : $"rule.{Id}.evidence.partly",
            EvidenceArgs: unread.Count == 0 ? new[] { names } : new[] { names, unreadNames });
    }

    public string Fix(DiagnosticContext ctx)
    {
        var offenders = Offenders(ctx);

        // Validate-then-write: check ALL browsers closed before any write
        var running = offenders
            .Where(o => ctx.RunningApps.IsRunning(o.Process))
            .Select(o => o.Process)
            .ToList();
        if (running.Count > 0)
            throw new InvalidOperationException($"Close {string.Join(", ", running)} first, then retry the fix.");

        // All validated; now write. Only the files Look() could parse are
        // offenders, so an unreadable one is never written.
        var prior = new Dictionary<string, bool>();
        foreach (var (process, path) in offenders)
        {
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
