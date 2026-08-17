using System.Collections.Generic;
using System.Linq;

namespace BriskEngine.Models;

public sealed record TargetScanResult(
    CleanupTarget Target,
    IReadOnlyList<ResolvedItem> Items,
    string? SkippedReason)
{
    public long TotalBytes => Items.Sum(i => i.Bytes);

    /// What a clean of this target can actually take RIGHT NOW: nothing
    /// while the target is skipped (its app is running — round 11 sizes
    /// skipped targets so the GUI can say what closing the app would free),
    /// and never the bytes of items the lock probe saw held. The honest
    /// promise sums THIS, not TotalBytes.
    public long ReclaimableBytes => SkippedReason is not null
        ? 0 : Items.Where(i => !i.Locked).Sum(i => i.Bytes);

    /// The complement: bytes on the shelf that a clean right now would
    /// leave behind (running-app targets whole, plus locked items).
    public long BlockedBytes => TotalBytes - ReclaimableBytes;
}

public sealed record ScanResult(IReadOnlyList<TargetScanResult> Targets)
{
    public long TotalBytes => Targets.Sum(t => t.TotalBytes);
    public long ReclaimableBytes => Targets.Sum(t => t.ReclaimableBytes);
}

public sealed record ScanProgress(int Completed, int Total, string TargetId);
