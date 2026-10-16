using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Models;

namespace BriskEngine.Actions;

public enum ActionGroupKind { Space, Speed, Privacy }

public enum ActionLinkKind { Rules, Cleanup }

public sealed record ActionFile(string Path, long Bytes, DateTime? LastWriteUtc);

/// One row a user sees. Title/Cost/WindowsName are English; the keys find the
/// translation (a startup entry has no TitleKey: its title is the app name).
/// Bytes is set on space rows. Files is set on the Downloads row.
public sealed record ActionItem(
    string Id, ActionGroupKind Group,
    string? TitleKey, string Title,
    string? CostKey, string? Cost,
    string? WindowsNameKey, string? WindowsName,
    long? Bytes, bool DefaultOn, bool NeedsRestart, bool NeedsAdmin, bool Reversible,
    ActionLinkKind LinkKind, IReadOnlyList<string> LinkIds,
    IReadOnlyList<ActionFile> Files);

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
        plan.Items.SelectMany(i => i.Files).Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase));

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
