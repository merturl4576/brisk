using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using BriskEngine.Models;
using BriskEngine.Paths;

namespace BriskEngine.Diagnostics.Rules;

public sealed class StartupBloatRule : IDiagnosticRule
{
    private const int ManyThreshold = 6;

    public string Id => "startup-bloat";
    public RuleCategory Category => RuleCategory.Confirm;

    /// One enabled startup row. A Run entry is disabled by writing its single
    /// StartupApproved value, named by Approved; a Store entry is disabled by
    /// moving the State value of every key in Tasks, because a package can
    /// register several and moving only one still leaves the app starting.
    private sealed record Item(string Hive, string Name,
        string? Approved, IReadOnlyList<string> Tasks);

    /// Prefix marking a prior-state entry as a Store task State value rather
    /// than a StartupApproved blob. Registry paths start with a hive name, so
    /// it cannot collide with the "{approvedKey}|{valueName}" form, and older
    /// undo records — which carry no such prefix — still restore correctly.
    private const string StorePrior = "store:";

    private static readonly byte[] DisabledBytes = { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    /// Every source StartupManager lists, read the way StartupManager reads it.
    /// Leaving Store apps out here while the Startup page showed them made the
    /// finding and the page disagree about the single heaviest entry on the
    /// maintainer's machine, and put it beyond the reach of Fix All.
    private static List<Item> EnabledItems(DiagnosticContext ctx)
    {
        var items = new List<Item>();
        foreach (var (hive, run, approved) in StartupManager.Hives)
        foreach (var name in ctx.Registry.GetValueNames(run))
        {
            var bytes = ctx.Registry.GetBytes(approved, name);
            var disabled = bytes is { Length: > 0 } && (bytes[0] & 1) == 1;
            if (!disabled) items.Add(new Item(hive, name, approved, Array.Empty<string>()));
        }
        foreach (var app in StartupManager.StoreApps(ctx.Registry))
            if (app.Enabled)
                items.Add(new Item(StartupManager.StoreHive, app.Name, null, app.TaskKeys));
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
        var heavy = enabled.Where(i => StartupManager.IsHeavy(i.Name)).Select(i => i.Name).ToList();
        var total = enabled.Count + links.Count;
        if (heavy.Count == 0 && total < ManyThreshold) return null;

        var heavyNames = string.Join(", ", heavy);
        var evidence = $"{total} programs start with Windows.";
        if (heavy.Count > 0)
            evidence += $" Heavy ones that can be started manually instead: {heavyNames}.";
        // This tail is what the reader sees just after a successful fix has
        // taken every heavy program out — the count alone still trips the
        // threshold. Read as a demand it would say the fix achieved nothing,
        // so with nothing left that brisk would touch it names the remaining
        // programs as the reader's judgement rather than a problem.
        //
        // It points by name, not by "below": the CLI prints this same
        // sentence into a terminal that has no list below it and no startup
        // verb at all. Naming the surface — as the boot rule already does —
        // is true wherever the sentence is read.
        else
            evidence += " None of them is on brisk's heavy list, so which ones "
                + "you actually need is your call — review them under Startup "
                + "programs on the Performance page.";
        var totalText = total.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new DiagnosticFinding(Id, "rule.startup-bloat.title",
            "Too many programs start with Windows", evidence,
            Severity.Warning, Category, ImpactStars: 3, CanFix: heavy.Count > 0,
            FixDescription: heavy.Count > 0
                ? $"Disable at startup: {heavyNames} (undoable; the apps still work when opened manually)"
                : null,
            // Two templates: with and without the heavy-programs tail.
            EvidenceKey: heavy.Count > 0
                ? $"rule.{Id}.evidence.heavy" : $"rule.{Id}.evidence",
            EvidenceArgs: heavy.Count > 0
                ? new[] { totalText, heavyNames } : new[] { totalText },
            Headline: new Headline(
                totalText, "programs start with Windows",
                $"rule.{Id}.headline.value", new[] { totalText },
                $"rule.{Id}.headline.caption", Array.Empty<string>()));
    }

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Dictionary<string, string?>();
        var heavyItems = EnabledItems(ctx).Where(i => StartupManager.IsHeavy(i.Name)).ToList();
        foreach (var item in heavyItems)
        {
            if (item.Approved is not null)
            {
                try
                {
                    var existing = ctx.Registry.GetBytes(item.Approved, item.Name);
                    ctx.Registry.SetBytes(item.Approved, item.Name, DisabledBytes);
                    prior[$"{item.Approved}|{item.Name}"] =
                        existing is null ? null : Convert.ToBase64String(existing);
                }
                catch (UnauthorizedAccessException) { /* HKLM without elevation — skip */ }
            }
            foreach (var taskKey in item.Tasks)
            {
                try
                {
                    var existing = ctx.Registry.GetInt(taskKey, "State");
                    ctx.Registry.SetInt(taskKey, "State", 0);
                    // The exact prior value, not "enabled": a task restored to
                    // EnabledByPolicy is not the same as one set to Enabled.
                    prior[StorePrior + taskKey] =
                        existing?.ToString(CultureInfo.InvariantCulture);
                }
                catch (UnauthorizedAccessException) { /* locked-down hive — skip */ }
            }
        }
        if (prior.Count == 0 && heavyItems.Count > 0)
            throw new InvalidOperationException("startup items could not be disabled (administrator required)");
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Dictionary<string, string?>>(priorStateJson)!;
        foreach (var (key, value) in prior)
        {
            try
            {
                if (key.StartsWith(StorePrior, StringComparison.Ordinal))
                {
                    var taskKey = key[StorePrior.Length..];
                    if (value is null)
                    {
                        ctx.Registry.DeleteValue(taskKey, "State");
                        continue;
                    }
                    // Fix wrote State=0 here, so State missing now means the row
                    // itself is gone: Windows drops the whole SystemAppData entry
                    // when the package is uninstalled. Restoring the value would
                    // route through CreateSubKey and rebuild the task key, and
                    // brisk reads that table back — the recreated State=2 becomes
                    // a live Startup row, is counted by EnabledItems, and if the
                    // family name carries a heavy token it gets named in the
                    // finding as a program to disable. brisk would be reporting a
                    // startup program that is not installed, out of a key brisk
                    // itself wrote, which is the shape OrphanedDataRule exists to
                    // complain about.
                    if (ctx.Registry.GetInt(taskKey, "State") is null) continue;
                    ctx.Registry.SetInt(taskKey, "State",
                        int.Parse(value, CultureInfo.InvariantCulture));
                    continue;
                }
                var sep = key.LastIndexOf('|');
                var (approved, name) = (key[..sep], key[(sep + 1)..]);
                if (value is null) ctx.Registry.DeleteValue(approved, name);
                else ctx.Registry.SetBytes(approved, name, Convert.FromBase64String(value));
            }
            catch (UnauthorizedAccessException) { /* HKLM without elevation — skip */ }
        }
    }
}
