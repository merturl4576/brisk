using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Models;

namespace BriskEngine.Actions;

public interface IReceiptRecorder
{
    void Before(string runId, ActionPlan plan, ActionSelection selection);
    void After(RunRecord run);
}

/// Applies a selection under one run id. Every rule writes through a
/// write-ahead registry: a rule that throws is rolled back, and a run that
/// died is rolled back at the start of the next one.
public sealed class ActionRunner
{
    public const string NeedsAdmin = "needs administrator";

    private static readonly ActionGroupKind[] Order =
        { ActionGroupKind.Privacy, ActionGroupKind.Speed, ActionGroupKind.Space };

    private readonly DiagnosticContext _ctx;
    private readonly ActionRules _rules;
    private readonly FixJournal _journal;
    private readonly CleanRunner _cleaner;
    private readonly DeferredCleanQueue _deferred;
    private readonly RunStore _runs;
    private readonly Func<bool> _isElevated;
    private readonly Func<string, (long Bytes, DateTime? LastWriteUtc)?> _stat;
    private readonly Func<DateTime> _now;
    private readonly IReceiptRecorder? _receipts;

    public ActionRunner(DiagnosticContext ctx, ActionRules rules, FixJournal journal,
        CleanRunner cleaner, DeferredCleanQueue deferred, RunStore runs, Func<bool> isElevated,
        Func<string, (long Bytes, DateTime? LastWriteUtc)?>? stat = null,
        Func<DateTime>? now = null, IReceiptRecorder? receipts = null)
    {
        _ctx = ctx;
        _rules = rules;
        _journal = journal;
        _cleaner = cleaner;
        _deferred = deferred;
        _runs = runs;
        _isElevated = isElevated;
        _stat = stat ?? StatFile;
        _now = now ?? (() => DateTime.UtcNow);
        _receipts = receipts;
    }

    private string WalPath => Path.Combine(_ctx.DataDirectory, "registry-wal.jsonl");

    public static (long Bytes, DateTime? LastWriteUtc)? StatFile(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? (info.Length, info.LastWriteTimeUtc) : null;
    }

    public RunRecord Run(ActionPlan plan, ActionSelection selection, Action<RowResult>? onRow = null)
    {
        WriteAheadRegistry.RecoverPending(_ctx.Registry, WalPath);
        var started = _now();
        var runId = $"{started:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..22];
        _receipts?.Before(runId, plan, selection);

        var rows = new List<RowResult>();
        var undoable = new List<string>();
        foreach (var kind in Order)
        foreach (var item in plan.Group(kind)?.Items ?? Array.Empty<ActionItem>())
        {
            if (!selection.RowIds.Contains(item.Id)) continue;
            var result = item.LinkKind == ActionLinkKind.Rules
                ? RunRules(item, undoable)
                : RunCleanup(item, plan.Scan, selection);
            rows.Add(result);
            onRow?.Invoke(result);
        }

        var record = new RunRecord(runId, started, _now(), rows, undoable);
        _runs.Save(record);
        _receipts?.After(record);
        return record;
    }

    private static int Weight(RowOutcome o) => o switch
    {
        RowOutcome.Failed => 3, RowOutcome.Skipped => 2, RowOutcome.PendingRestart => 1, _ => 0,
    };

    private static int Weight(ReadBackState s) => s switch
    {
        ReadBackState.Reverted => 3, ReadBackState.WrittenButIgnored => 2,
        ReadBackState.WrittenButUnverified => 1, _ => 0,
    };

    private ReadBackState? ReadBackOf(IDiagnosticRule rule)
    {
        var fresh = _ctx.ForScan();
        try
        {
            return rule is IReadBackable rb
                ? ReadBack.StateOf(fresh, rb)
                : rule.Detect(fresh) is null ? ReadBackState.Held : ReadBackState.Reverted;
        }
        catch (Exception) { return null; }
    }

    private RowResult RunRules(ActionItem item, List<string> undoable)
    {
        if (item.NeedsAdmin && !_isElevated())
            return new RowResult(item.Id, RowOutcome.Skipped, NeedsAdmin, null, 0, 0);

        var outcomes = new List<(RowOutcome Outcome, string? Reason, ReadBackState? ReadBack)>();
        foreach (var id in item.LinkIds)
        {
            var rule = _rules.Find(id);
            if (rule is null) { outcomes.Add((RowOutcome.Failed, $"unknown rule '{id}'", null)); continue; }

            var wal = new WriteAheadRegistry(_ctx.Registry, WalPath);
            try
            {
                var prior = rule.Fix(_ctx with { Registry = wal });
                _journal.RecordFix(rule.Id, prior);
                wal.Commit();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                wal.RollBack();
                outcomes.Add((RowOutcome.Skipped, NeedsAdmin, null));
                continue;
            }
            catch (Exception ex)
            {
                wal.RollBack();
                outcomes.Add((RowOutcome.Failed, ex.Message, null));
                continue;
            }

            if (item.Reversible) undoable.Add(rule.Id);
            outcomes.Add(item.NeedsRestart
                ? (RowOutcome.PendingRestart, null, null)
                : (RowOutcome.Done, null, ReadBackOf(rule)));
        }

        var worst = outcomes.OrderByDescending(o => Weight(o.Outcome)).First();
        var readBack = outcomes.Where(o => o.ReadBack is not null)
            .Select(o => o.ReadBack!.Value)
            .OrderByDescending(s => Weight(s))
            .Select(s => (ReadBackState?)s)
            .FirstOrDefault();
        return new RowResult(item.Id, worst.Outcome, worst.Reason, readBack, 0, 0);
    }

    private static bool IsPathless(string id) =>
        id is "docker-prune" or "empty-recycle-bin" or "component-store";

    private TargetScanResult SelectedDownloads(TargetScanResult t, ActionSelection selection, List<string> reasons)
    {
        var keep = new List<ResolvedItem>();
        foreach (var i in t.Items)
        {
            if (!selection.Files.Contains(i.Path)) continue;
            var now = _stat(i.Path);
            if (now is null || now.Value.Bytes != i.Bytes || now.Value.LastWriteUtc != i.LastWriteUtc)
            {
                reasons.Add($"{Path.GetFileName(i.Path)} changed since the plan");
                continue;
            }
            keep.Add(i);
        }
        return t with { Items = keep };
    }

    private RowResult RunCleanup(ActionItem item, ScanResult scan, ActionSelection selection)
    {
        if (item.NeedsAdmin && !_isElevated())
            return new RowResult(item.Id, RowOutcome.Skipped, NeedsAdmin, null, 0, 0);

        long freed = 0, deferred = 0;
        var did = false;
        var adminSkips = 0;
        var reasons = new List<string>();
        foreach (var id in item.LinkIds)
        {
            var t = scan.Targets.FirstOrDefault(x => x.Target.Id == id);
            if (t is null) continue;

            var appOpen = t.SkippedReason is not null
                || t.Target.AppProcessCandidates.Any(_ctx.RunningApps.IsRunning);
            if (appOpen)
            {
                if (t.TotalBytes > 0)
                {
                    _deferred.Add(id, _now());
                    deferred += t.TotalBytes;
                }
                continue;
            }
            if (t.Target.RequiresElevation && !_isElevated()) { adminSkips++; continue; }

            var toClean = id == "old-installers" ? SelectedDownloads(t, selection, reasons) : t;
            if (toClean.Items.Count == 0 && !IsPathless(id)) continue;

            var report = _cleaner.Clean(toClean, dryRun: false, permanent: true);
            freed += report.DeletedBytes + report.RemovedBytes;
            if ((id is "docker-prune" or "empty-recycle-bin") && report.Entries.Any(e => e.Action == "external"))
                freed += item.Bytes ?? 0;
            did |= report.Entries.Any(e => e.Action is "deleted" or "removed" or "external");
            var error = report.Entries.FirstOrDefault(e => e.Action is "error" or "refused")?.Reason;
            if (error is not null) reasons.Add(error);
        }

        if (did || deferred > 0)
            return new RowResult(item.Id, RowOutcome.Done, reasons.FirstOrDefault(), null, freed, deferred);
        if (adminSkips > 0)
            return new RowResult(item.Id, RowOutcome.Skipped, NeedsAdmin, null, 0, 0);
        return new RowResult(item.Id, RowOutcome.Skipped, reasons.FirstOrDefault() ?? "nothing to clean", null, 0, 0);
    }

    /// Reverses a run's reversible rules, newest first. Deleted files are not
    /// in the list: they cannot come back, and undo never says they did.
    public IReadOnlyList<RowResult> Undo(string runId)
    {
        var record = _runs.Load(runId);
        if (record is null) return Array.Empty<RowResult>();
        var results = new List<RowResult>();
        foreach (var id in Enumerable.Reverse(record.UndoableRuleIds))
        {
            var rule = _rules.Find(id);
            var prior = _journal.LastUndoablePriorState(id);
            if (rule is null || prior is null)
            {
                results.Add(new RowResult(id, RowOutcome.Skipped, "nothing to undo", null, 0, 0));
                continue;
            }
            var wal = new WriteAheadRegistry(_ctx.Registry, WalPath);
            try
            {
                rule.Undo(_ctx with { Registry = wal }, prior);
                _journal.RecordUndo(id);
                wal.Commit();
                results.Add(new RowResult(id, RowOutcome.Done, null, null, 0, 0));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                wal.RollBack();
                results.Add(new RowResult(id, RowOutcome.Skipped, NeedsAdmin, null, 0, 0));
            }
            catch (Exception ex)
            {
                wal.RollBack();
                results.Add(new RowResult(id, RowOutcome.Failed, ex.Message, null, 0, 0));
            }
        }
        return results;
    }
}
