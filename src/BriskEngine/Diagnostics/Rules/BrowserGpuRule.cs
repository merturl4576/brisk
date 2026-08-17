using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

public sealed class BrowserGpuRule : IDiagnosticRule
{
    private const string AppPaths = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private const string Prefs = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    private static readonly string[] BrowserExes =
        { "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe" };

    public string Id => "browser-gpu";
    public RuleCategory Category => RuleCategory.Auto;

    private static List<string> Offenders(DiagnosticContext ctx)
    {
        var offenders = new List<string>();
        if (ctx.Sensors.GpuCount() < 2) return offenders;
        foreach (var exe in BrowserExes)
        {
            var path = ctx.Registry.GetString($@"{AppPaths}\{exe}", "");
            if (path is null) continue;
            var pref = ctx.Registry.GetString(Prefs, path);
            if (pref is null || !pref.Contains("GpuPreference=2")) offenders.Add(path);
        }
        return offenders;
    }

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var offenders = Offenders(ctx);
        if (offenders.Count == 0) return null;
        var names = string.Join(", ", offenders.Select(System.IO.Path.GetFileName));
        return new DiagnosticFinding(Id, "rule.browser-gpu.title",
            "Browser is not pinned to the fast GPU",
            $"This machine has two GPUs, but {names} has no high-performance GPU " +
            "preference, so Windows may run it on the slow integrated GPU.",
            Severity.Warning, Category, ImpactStars: 4, CanFix: true,
            FixDescription: "Set the high-performance GPU preference for each browser (undoable)",
            EvidenceKey: $"rule.{Id}.evidence", EvidenceArgs: new[] { names });
    }

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Dictionary<string, string?>();
        foreach (var path in Offenders(ctx))
        {
            prior[path] = ctx.Registry.GetString(Prefs, path);
            ctx.Registry.SetString(Prefs, path, "GpuPreference=2;");
        }
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Dictionary<string, string?>>(priorStateJson)!;
        foreach (var (path, value) in prior)
        {
            if (value is null) ctx.Registry.DeleteValue(Prefs, path);
            else ctx.Registry.SetString(Prefs, path, value);
        }
    }
}
