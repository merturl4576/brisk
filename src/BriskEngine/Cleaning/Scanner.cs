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

    public Scanner(IReadOnlyList<CleanupTarget> targets, IProcessLister processes)
    {
        _targets = targets;
        _processes = processes;
    }

    public ScanResult Scan(CancellationToken ct = default)
    {
        var results = new TargetScanResult[_targets.Count];
        Parallel.For(0, _targets.Count,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            i => results[i] = ScanTarget(_targets[i], ct));
        return new ScanResult(results);
    }

    private TargetScanResult ScanTarget(CleanupTarget target, CancellationToken ct)
    {
        if (target.RequiresAppClosedProcess is { } app && _processes.IsRunning(app))
            return new TargetScanResult(target, Array.Empty<ResolvedItem>(),
                $"{app} is running — close it to include this target");

        var items = new List<ResolvedItem>();
        foreach (var template in target.PathTemplates)
        foreach (var path in TemplateResolver.Resolve(template))
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                DateTime? lastWrite = null;
                try { lastWrite = File.GetLastWriteTimeUtc(path); } catch { }

                if (target.Id == "old-installers" &&
                    (lastWrite is null ||
                     lastWrite > DateTime.UtcNow.AddDays(-OldInstallerMinAgeDays)))
                    continue;

                items.Add(new ResolvedItem(target.Id, path, SizeCalculator.SizeOf(path, ct), lastWrite));
            }
            catch (OperationCanceledException) { throw; }
            catch { }  // Skip this path on any other exception
        }
        return new TargetScanResult(target, items, null);
    }
}
