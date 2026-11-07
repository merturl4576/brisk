using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Models;

namespace BriskEngine.Actions;

public enum ActionGroupKind { Space, Speed, Privacy }

public enum ActionLinkKind { Rules, Cleanup }

/// How far Free up space goes; each level includes the ones below it.
/// MIN regenerates or is never needed again, MID has a small cost, MAX
/// includes what cannot be brought back.
public enum SpaceLevel { Min, Mid, Max }

public sealed record ActionFile(string Path, long Bytes, DateTime? LastWriteUtc);

/// One row a user sees. Title/Cost/WindowsName are English; the keys find the
/// translation (a startup entry has no TitleKey: its title is the app name).
/// Bytes is set on space rows. Files is set on the Downloads row. Level is
/// set on space rows only; Lost = a space row that cannot be brought back.
public sealed record ActionItem(
    string Id, ActionGroupKind Group,
    string? TitleKey, string Title,
    string? CostKey, string? Cost,
    string? WindowsNameKey, string? WindowsName,
    long? Bytes, bool DefaultOn, bool NeedsRestart, bool NeedsAdmin, bool Reversible,
    ActionLinkKind LinkKind, IReadOnlyList<string> LinkIds,
    IReadOnlyList<ActionFile> Files, SpaceLevel? Level = null, bool Lost = false);

public sealed record ActionGroup(ActionGroupKind Kind, IReadOnlyList<ActionItem> Items);

public sealed record ActionPlan(DateTime CreatedUtc, IReadOnlyList<ActionGroup> Groups, ScanResult Scan)
{
    public IEnumerable<ActionItem> Items => Groups.SelectMany(g => g.Items);
    public ActionGroup? Group(ActionGroupKind kind) => Groups.FirstOrDefault(g => g.Kind == kind);
}

public sealed record ActionSelection(IReadOnlySet<string> RowIds, IReadOnlySet<string> Files)
{
    public static ActionSelection Default(ActionPlan plan) => new(
        plan.Items.Where(i => i.DefaultOn).Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase),
        EveryFile(plan));

    /// A run at one Yer aç level: every Space row up to that level, plus the
    /// Speed and Privacy rows that are on by default, minus whatever the
    /// user skipped on any page. A Space row without a level is never taken.
    /// Skips match row ids without case, whatever the caller's set compares.
    public static ActionSelection ForLevel(ActionPlan plan, SpaceLevel level, IReadOnlySet<string> skipped)
    {
        var skip = new HashSet<string>(skipped, StringComparer.OrdinalIgnoreCase);
        return new(
            plan.Items
                .Where(i => i.Group == ActionGroupKind.Space ? i.Level is { } l && l <= level : i.DefaultOn)
                .Where(i => !skip.Contains(i.Id))
                .Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase),
            EveryFile(plan));
    }

    private static HashSet<string> EveryFile(ActionPlan plan) =>
        plan.Items.SelectMany(i => i.Files).Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public ActionSelection Only(ActionGroupKind group, ActionPlan plan) => this with
    {
        RowIds = RowIds.Where(id => plan.Items.Any(i => i.Group == group
                && string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase),
    };
}

public enum RowOutcome { Done, PendingRestart, Skipped, Failed }

public sealed record RowResult(string RowId, RowOutcome Outcome, string? Reason,
    ReadBackState? ReadBack, long BytesFreed, long BytesDeferred);

/// UndoneUtc is set once the run's settings were put back.
public sealed record RunRecord(string RunId, DateTime StartedUtc, DateTime FinishedUtc,
    IReadOnlyList<RowResult> Rows, IReadOnlyList<string> UndoableRuleIds, DateTime? UndoneUtc = null);
