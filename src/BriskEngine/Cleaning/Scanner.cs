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
                    lastWrite, Locked: false));   // probed below, largest first
            }
            catch (OperationCanceledException) { throw; }
            catch { }  // Skip this path on any other exception
        }
        ProbeLargestFirst(target, items, appRunning, budget, ct);
        return new TargetScanResult(target, items, appRunning
            ? $"{target.AppDisplayName} is running — close it to include this target"
            : null);
    }

    /// ROUND 15 review (I1): the allowance is finite, so WHAT it is spent on
    /// decides the promise's error bound. Spent in walk order it leaves an
    /// arbitrary tail unverified — and an unverified item is counted as
    /// free, so the arbitrary tail is exactly the promise's risk. Spent
    /// largest-first the unverified tail is the SMALLEST it can be in the
    /// only unit the promise is made in: bytes. Sizes are already computed
    /// above, so this costs an ordering, not a walk.
    private void ProbeLargestFirst(CleanupTarget target, List<ResolvedItem> items,
        bool appRunning, LockProbeBudget? budget, CancellationToken ct)
    {
        // A skipped target is wholly outside the promise; nothing to verify.
        if (appRunning || budget is null || _lockProbe is null) return;
        // The probe asks one question — "can the SHELL delete this?" — and a
        // past-the-bin target is never given to the shell: hiberfil.sys is
        // kernel-held its whole life yet powercfg frees it, and an ACL-denied
        // entry inside Windows.old stops nothing takeown will not fix. Probing
        // these marks them Locked, zeroes ReclaimableBytes, and silently
        // deletes the exact gigabytes the deep reveal exists to announce
        // (2026-08-30 review, CONFIRMED on the live hiberfil).
        if (target.BypassesRecycleBin) return;
        foreach (var i in Enumerable.Range(0, items.Count)
                     .OrderByDescending(n => items[n].Bytes)
                     .ToList())
        {
            ct.ThrowIfCancellationRequested();
            if (IsLocked(items[i].Path, budget, ct))
                items[i] = items[i] with { Locked = true };
        }
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
                    lastWrite, Locked: false));   // probed by the caller, largest first
            }
            catch (OperationCanceledException) { throw; }
            catch { }  // Skip this child on any other exception
        }
    }
}
