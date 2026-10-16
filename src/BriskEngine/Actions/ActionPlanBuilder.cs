using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Actions.Rules;
using BriskEngine.Diagnostics;
using BriskEngine.Models;

namespace BriskEngine.Actions;

/// Turns one scan into the three groups of ticked rows. Every decision that
/// depends on the machine is made here: laptop or desktop, which startup
/// entries are protected, which features exist.
public sealed class ActionPlanBuilder
{
    private readonly DiagnosticContext _ctx;
    private readonly ActionRules _rules;
    private readonly StartupManager _startup;
    private readonly ISpaceProbe _space;
    private readonly IOptionalFeatureProbe _features;

    public ActionPlanBuilder(DiagnosticContext ctx, ActionRules rules, StartupManager startup,
        ISpaceProbe space, IOptionalFeatureProbe features)
    {
        _ctx = ctx;
        _rules = rules;
        _startup = startup;
        _space = space;
        _features = features;
    }

    public static ActionItem ItemFrom(RowDef row, long? bytes = null, bool? defaultOn = null,
        string? cost = null, string? costKey = null, IReadOnlyList<ActionFile>? files = null) =>
        new(row.Id, row.Group, row.TitleKey, row.Title, costKey ?? row.CostKey, cost ?? row.Cost,
            row.NameKey, row.WindowsName, bytes, defaultOn ?? row.DefaultOn, row.NeedsRestart,
            row.NeedsAdmin, row.Reversible, row.LinkKind, row.LinkIds,
            files ?? Array.Empty<ActionFile>());

    /// The rows that need no cleanup scan; the window shows them first.
    public IReadOnlyList<ActionGroup> BuildFast() => new[]
        {
            new ActionGroup(ActionGroupKind.Speed, SpeedRows().ToList()),
            new ActionGroup(ActionGroupKind.Privacy, PrivacyRows().ToList()),
        }.Where(g => g.Items.Count > 0).ToList();

    public ActionGroup? BuildSpace(ScanResult scan)
    {
        var rows = SpaceRows(scan).ToList();
        return rows.Count == 0 ? null : new ActionGroup(ActionGroupKind.Space, rows);
    }

    public static ActionPlan Compose(DateTime nowUtc, ActionGroup? space, IReadOnlyList<ActionGroup> fast,
        ScanResult scan)
    {
        var groups = new List<ActionGroup>();
        if (space is not null) groups.Add(space);
        groups.AddRange(fast.Where(g => g.Kind == ActionGroupKind.Speed));
        groups.AddRange(fast.Where(g => g.Kind == ActionGroupKind.Privacy));
        return new ActionPlan(nowUtc, groups, scan);
    }

    public ActionPlan Build(ScanResult scan, DateTime nowUtc) => Compose(nowUtc, BuildSpace(scan), BuildFast(), scan);

    private bool Wants(string ruleId)
    {
        var rule = _rules.Find(ruleId);
        if (rule is null) return false;
        try { return rule.Detect(_ctx) is not null; }
        catch (Exception) { return false; }
    }

    private IEnumerable<ActionItem> PrivacyRows()
    {
        foreach (var row in ActionCatalog.Rows.Where(r => r.Group == ActionGroupKind.Privacy))
        {
            if (row.Id == "privacy.recall" && _features.InstallState("Recall") is not (1 or 2)) continue;
            if (row.LinkIds.Any(Wants)) yield return ItemFrom(row);
        }
    }

    private IReadOnlyCollection<string> AntivirusNames()
    {
        try { return _ctx.Defender.Registered().Where(p => !p.IsDefender).Select(p => p.Name).ToList(); }
        catch (Exception) { return Array.Empty<string>(); }
    }

    private IEnumerable<ActionItem> SpeedRows()
    {
        var antivirus = AntivirusNames();
        foreach (var e in _startup.List())
        {
            if (!e.Enabled || e.Name.Contains("brisk", StringComparison.OrdinalIgnoreCase)) continue;
            var kind = StartupProtection.Classify(e.Name, e.Command, antivirus);
            var protectedEntry = kind != StartupProtectionKind.None;
            var (costKey, cost) = ActionCatalog.StartupCost(kind);
            var id = StartupEntryRule.IdFor(e.Hive, e.Name);
            var title = e.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? e.Name[..^4] : e.Name;
            yield return new ActionItem(id, ActionGroupKind.Speed,
                null, title,
                protectedEntry ? costKey : null, protectedEntry ? cost : null,
                ActionCatalog.StartupNameKey, ActionCatalog.StartupName,
                null, !protectedEntry, false, e.Hive is "HKLM" or "FolderAll", true,
                ActionLinkKind.Rules, new[] { id }, Array.Empty<ActionFile>());
        }
        foreach (var row in ActionCatalog.Rows.Where(r => r.Group == ActionGroupKind.Speed))
            if (row.LinkIds.Any(Wants)) yield return ItemFrom(row);
    }

    private static long? Safe(Func<long?> read)
    {
        try { return read(); }
        catch (Exception) { return null; }
    }

    private IEnumerable<ActionItem> SpaceRows(ScanResult scan)
    {
        TargetScanResult? Target(string id) => scan.Targets.FirstOrDefault(t => t.Target.Id == id);

        foreach (var row in ActionCatalog.Rows.Where(r => r.Group == ActionGroupKind.Space))
        {
            switch (row.Id)
            {
                case "space.hibernation":
                {
                    var bytes = Target("hibernation-file")?.TotalBytes ?? 0;
                    if (bytes == 0) break;
                    yield return _ctx.Powercfg.HasBattery()
                        ? ItemFrom(row, bytes, defaultOn: false,
                            cost: ActionCatalog.HibernationLaptopCost, costKey: ActionCatalog.HibernationLaptopCostKey)
                        : ItemFrom(row, bytes);
                    break;
                }
                case "space.downloads":
                {
                    var t = Target("old-installers");
                    if (t is null || t.Items.Count == 0) break;
                    var files = t.Items.OrderByDescending(i => i.Bytes)
                        .Select(i => new ActionFile(i.Path, i.Bytes, i.LastWriteUtc)).ToList();
                    yield return ItemFrom(row, files.Sum(f => f.Bytes), files: files);
                    break;
                }
                case "space.docker":
                {
                    var bytes = Safe(_space.DockerReclaimableBytes);
                    if (bytes is null or 0) break;
                    yield return ItemFrom(row, bytes);
                    break;
                }
                case "space.recycle-bin":
                {
                    var bytes = Safe(() => _space.RecycleBinBytes()) ?? 0;
                    if (bytes == 0) break;
                    yield return ItemFrom(row, bytes);
                    break;
                }
                default:
                {
                    var bytes = row.LinkIds.Select(Target).OfType<TargetScanResult>()
                        .Sum(t => t.SkippedReason is null ? t.ReclaimableBytes : t.TotalBytes);
                    if (bytes > 0) yield return ItemFrom(row, bytes);
                    break;
                }
            }
        }
    }
}
