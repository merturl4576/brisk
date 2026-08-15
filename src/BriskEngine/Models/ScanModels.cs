using System.Collections.Generic;
using System.Linq;

namespace BriskEngine.Models;

public sealed record TargetScanResult(
    CleanupTarget Target,
    IReadOnlyList<ResolvedItem> Items,
    string? SkippedReason)
{
    public long TotalBytes => Items.Sum(i => i.Bytes);
}

public sealed record ScanResult(IReadOnlyList<TargetScanResult> Targets)
{
    public long TotalBytes => Targets.Sum(t => t.TotalBytes);
}

public sealed record ScanProgress(int Completed, int Total, string TargetId);
