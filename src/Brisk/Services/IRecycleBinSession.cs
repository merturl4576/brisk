using System.Collections.Generic;

namespace Brisk.Services;

/// Session-scoped window over the Recycle Bin: restore or purge exactly
/// the items brisk just recycled, never anything else in the bin.
public interface IRecycleBinSession
{
    bool Restore(IReadOnlyList<string> originalPaths);

    /// Purge exactly these just-recycled items (matched by their original
    /// path identity — other bin content is structurally out of reach) and
    /// return the original paths that actually left the bin. Round 12: the
    /// simple clean auto-purges through this, so partial success must be
    /// reportable — one stubborn item never hides what DID get freed.
    IReadOnlyList<string> Purge(IReadOnlyList<string> originalPaths);

    void OpenRecycleBinUi();
}
