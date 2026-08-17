using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BriskEngine.Models;
using BriskEngine.Paths;

namespace BriskEngine.Cleaning;

public sealed class Scanner
{
    public const int OldInstallerMinAgeDays = 30;

    private readonly IReadOnlyList<CleanupTarget> _targets;
    private readonly IProcessLister _processes;
    private readonly ILockProbe? _lockProbe;

    /// lockProbe (additive, round 11): when present, every emitted item is
    /// probed for delete-locks so ReclaimableBytes can promise honestly.
    /// Without one (unit tests, fakes) nothing is marked locked.
    public Scanner(IReadOnlyList<CleanupTarget> targets, IProcessLister processes,
        ILockProbe? lockProbe = null)
    {
        _targets = targets;
        _processes = processes;
        _lockProbe = lockProbe;
    }

    public ScanResult Scan(CancellationToken ct = default,
        IProgress<ScanProgress>? progress = null)
    {
        var results = new TargetScanResult[_targets.Count];
        var completed = 0;
        Parallel.For(0, _targets.Count,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            i =>
            {
                results[i] = ScanTarget(_targets[i], ct);
                var done = Interlocked.Increment(ref completed);
                progress?.Report(new ScanProgress(done, _targets.Count, _targets[i].Id));
            });
        return new ScanResult(results);
    }

    private TargetScanResult ScanTarget(CleanupTarget target, CancellationToken ct)
    {
        // ANY candidate process name counts as running (modern WhatsApp
        // Desktop is "WhatsApp.Root" — the 2026-08-17 promise bug). A
        // skipped target is still resolved and SIZED below: the clean never
        // touches it (CleanRunner refuses skipped scans), but the GUI can
        // now say what closing the app would free.
        var appRunning = target.AppProcessCandidates.Any(_processes.IsRunning);

        // ONE probe budget for the whole target (review round 1): every
        // item draws from it, so a many-child temp dir is bounded at the
        // target, not per item.
        var budget = _lockProbe is null ? null : new LockProbeBudget();

        var items = new List<ResolvedItem>();
        foreach (var template in target.PathTemplates)
        foreach (var path in TemplateResolver.Resolve(template))
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                if (target.DeletesContentsNotDirectory && Directory.Exists(path))
                {
                    // The template directory itself is never deletable for contents-only
                    // targets (SafetyValidator correctly denies it) — emit its immediate
                    // children instead, one item per child.
                    AddChildren(items, target, path, appRunning, budget, ct);
                    continue;
                }

                DateTime? lastWrite = null;
                try { lastWrite = File.GetLastWriteTimeUtc(path); } catch { }

                if (target.Id == "old-installers" &&
                    (lastWrite is null ||
                     lastWrite > DateTime.UtcNow.AddDays(-OldInstallerMinAgeDays)))
                    continue;

                items.Add(new ResolvedItem(target.Id, path, SizeCalculator.SizeOf(path, ct),
                    lastWrite, Locked: !appRunning && IsLocked(path, budget, ct)));
            }
            catch (OperationCanceledException) { throw; }
            catch { }  // Skip this path on any other exception
        }
        return new TargetScanResult(target, items, appRunning
            ? $"{target.AppDisplayName} is running — close it to include this target"
            : null);
    }

    /// Skipped-target items are never probed (the whole target is already
    /// outside the promise); everything else asks the probe, when one
    /// exists, drawing from the target's shared budget.
    private bool IsLocked(string path, LockProbeBudget? budget, CancellationToken ct) =>
        budget is not null && (_lockProbe?.IsLockedForDelete(path, budget, ct) ?? false);

    private void AddChildren(List<ResolvedItem> items, CleanupTarget target, string path,
        bool appRunning, LockProbeBudget? budget, CancellationToken ct)
    {
        foreach (var child in Directory.EnumerateFileSystemEntries(path))
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var attrs = File.GetAttributes(child);
                if ((attrs & FileAttributes.ReparsePoint) != 0) continue;

                DateTime? lastWrite = null;
                try { lastWrite = File.GetLastWriteTimeUtc(child); } catch { }

                items.Add(new ResolvedItem(target.Id, child, SizeCalculator.SizeOf(child, ct),
                    lastWrite, Locked: !appRunning && IsLocked(child, budget, ct)));
            }
            catch (OperationCanceledException) { throw; }
            catch { }  // Skip this child on any other exception
        }
    }
}
