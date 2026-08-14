using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Models;
using BriskEngine.Paths;

namespace BriskEngine.Diagnostics.Rules;

public sealed class StartupBloatRule : IDiagnosticRule
{
    public static readonly IReadOnlySet<string> KnownHeavy = new HashSet<string>(
        new[] { "Steam", "Discord", "Spotify", "Docker Desktop", "EpicGamesLauncher",
                "WhatsApp", "Teams", "BlueStacks", "WallpaperEngine" },
        StringComparer.OrdinalIgnoreCase);

    private const int ManyThreshold = 6;
    private static readonly (string Hive, string Run, string Approved)[] Hives =
    {
        ("HKCU", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                 @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
        ("HKLM", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                 @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
    };

    public string Id => "startup-bloat";
    public RuleCategory Category => RuleCategory.Confirm;

    private sealed record Item(string Hive, string Name, string Approved);

    private static bool IsHeavy(string name) => KnownHeavy.Any(h =>
        name.Contains(h, StringComparison.OrdinalIgnoreCase));

    private static List<Item> EnabledItems(DiagnosticContext ctx)
    {
        var items = new List<Item>();
        foreach (var (hive, run, approved) in Hives)
        foreach (var name in ctx.Registry.GetValueNames(run))
        {
            var bytes = ctx.Registry.GetBytes(approved, name);
            var disabled = bytes is { Length: > 0 } && (bytes[0] & 1) == 1;
            if (!disabled) items.Add(new Item(hive, name, approved));
        }
        return items;
    }

    private static IReadOnlyList<string> StartupFolderLinks(DiagnosticContext ctx)
    {
        var folder = PathExpander.Expand(
            @"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup");
        return folder is null
            ? Array.Empty<string>()
            : ctx.Files.ListFiles(folder).Where(f => f.EndsWith(".lnk")).ToList();
    }

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var enabled = EnabledItems(ctx);
        var links = StartupFolderLinks(ctx);
        var heavy = enabled.Where(i => IsHeavy(i.Name)).Select(i => i.Name).ToList();
        var total = enabled.Count + links.Count;
        if (heavy.Count == 0 && total < ManyThreshold) return null;

        var evidence = $"{total} programs start with Windows.";
        if (heavy.Count > 0)
            evidence += $" Heavy ones that can be started manually instead: {string.Join(", ", heavy)}.";
        return new DiagnosticFinding(Id, "rule.startup-bloat.title",
            "Too many programs start with Windows", evidence,
            Severity.Warning, Category, ImpactStars: 3, CanFix: heavy.Count > 0,
            FixDescription: heavy.Count > 0
                ? $"Disable at startup: {string.Join(", ", heavy)} (undoable; the apps still work when opened manually)"
                : null);
    }

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Dictionary<string, string?>();
        var disabledBytes = new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        foreach (var item in EnabledItems(ctx).Where(i => IsHeavy(i.Name)))
        {
            try
            {
                var existing = ctx.Registry.GetBytes(item.Approved, item.Name);
                prior[$"{item.Approved}|{item.Name}"] =
                    existing is null ? null : Convert.ToBase64String(existing);
                ctx.Registry.SetBytes(item.Approved, item.Name, disabledBytes);
            }
            catch (UnauthorizedAccessException) { /* HKLM without elevation — skip */ }
        }
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Dictionary<string, string?>>(priorStateJson)!;
        foreach (var (key, base64) in prior)
        {
            var sep = key.LastIndexOf('|');
            var (approved, name) = (key[..sep], key[(sep + 1)..]);
            if (base64 is null) ctx.Registry.DeleteValue(approved, name);
            else ctx.Registry.SetBytes(approved, name, Convert.FromBase64String(base64));
        }
    }
}
