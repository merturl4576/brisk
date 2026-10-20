using System.Collections.Generic;
using System.Linq;
using BriskEngine.Actions;
using BriskEngine.Models;

namespace Brisk.Session;

/// The space numbers while the scan is still sizing targets, which the hero
/// climbs through before the builder's exact rows arrive. A target counts the
/// way the builder counts it (an open app's target whole); hibernation and the
/// rows the builder decides per machine count only as found.
public static class PartialSpace
{
    public static (long Found, long Ticked) Estimate(IEnumerable<TargetScanResult> sized)
    {
        long found = 0, ticked = 0;
        foreach (var t in sized)
        {
            found += t.TotalBytes;
            var row = ActionCatalog.Rows.FirstOrDefault(r => r.Group == ActionGroupKind.Space
                && r.LinkKind == ActionLinkKind.Cleanup && r.LinkIds.Contains(t.Target.Id));
            if (row is { DefaultOn: true })
                ticked += t.SkippedReason is null ? t.ReclaimableBytes : t.TotalBytes;
        }
        return (found, ticked);
    }
}
