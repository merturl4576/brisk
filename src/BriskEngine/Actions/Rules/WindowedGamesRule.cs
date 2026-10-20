using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;

namespace BriskEngine.Actions.Rules;

/// Windows 11's "Optimizations for windowed games": one "k=v;" pair inside a
/// string value that can hold other DirectX settings, which are kept.
public sealed class WindowedGamesRule : IReadBackable
{
    public const string Key = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    public const string Value = "DirectXUserGlobalSettings";
    public const string Setting = "SwapEffectUpgradeEnable";
    public const string BuildKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    public const int FirstWindows11Build = 22000;

    private sealed record Prior(string? Value);

    public string Id => "windowed-games";
    public RuleCategory Category => RuleCategory.Auto;

    private static bool Applies(DiagnosticContext ctx) =>
        int.TryParse(ctx.Registry.GetString(BuildKey, "CurrentBuildNumber"), out var build)
        && build >= FirstWindows11Build;

    public bool IsOn(DiagnosticContext ctx) =>
        Applies(ctx) && ReadSetting(ctx.Registry.GetString(Key, Value), Setting) != "1";

    public WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => WriteEffect.NotAPolicy;

    public DiagnosticFinding? Detect(DiagnosticContext ctx) => IsOn(ctx)
        ? new DiagnosticFinding(Id, $"rule.{Id}.title", "Optimizations for windowed games are off",
            "Games in windowed and borderless mode use the slower presentation path.",
            Severity.Info, Category, ImpactStars: 1, CanFix: true,
            FixDescription: "Turn on optimizations for windowed games (undoable)", Kind: FindingKind.Notice)
        : null;

    public string Fix(DiagnosticContext ctx)
    {
        var current = ctx.Registry.GetString(Key, Value);
        ctx.Registry.SetString(Key, Value, WithSetting(current, Setting, "1"));
        return JsonSerializer.Serialize(new Prior(current));
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Prior>(priorStateJson)!;
        if (prior.Value is null) ctx.Registry.DeleteValue(Key, Value);
        else ctx.Registry.SetString(Key, Value, prior.Value);
    }

    private static List<(string Key, string Value)> Pairs(string? all) =>
        (all ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .Select(p => (p[0].Trim(), p[1].Trim()))
            .ToList();

    internal static string? ReadSetting(string? all, string key) =>
        Pairs(all).Where(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Value).FirstOrDefault();

    internal static string WithSetting(string? all, string key, string value)
    {
        var pairs = Pairs(all);
        var i = pairs.FindIndex(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) pairs[i] = (key, value);
        else pairs.Add((key, value));
        return string.Concat(pairs.Select(p => $"{p.Key}={p.Value};"));
    }
}
