using System.Collections.Generic;

namespace Brisk.Services;

/// Session-scoped window over the Recycle Bin: restore or purge exactly
/// the items brisk just recycled, never anything else in the bin.
public interface IRecycleBinSession
{
    bool Restore(IReadOnlyList<string> originalPaths);

    /// Payload identities of bin entries currently matching these original
    /// paths — the simple clean's pre-recycle snapshot, so items the USER
    /// deleted earlier at the same paths can be excluded from the purge.
    IReadOnlyList<string> MatchingItemIds(IReadOnlyList<string> originalPaths);

    /// Purge exactly these just-recycled items (matched by original path,
    /// minus the excluded payload identities — other bin content is
    /// structurally out of reach) and return the original paths that
    /// actually left the bin. Round 12: the simple clean auto-purges
    /// through this, so partial success must be reportable — one stubborn
    /// item never hides what DID get freed.
    IReadOnlyList<string> Purge(IReadOnlyList<string> originalPaths,
        IReadOnlyList<string>? excludeItemIds = null);

    void OpenRecycleBinUi();
}
