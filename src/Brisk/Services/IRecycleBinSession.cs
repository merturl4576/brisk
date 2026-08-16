using System.Collections.Generic;

namespace Brisk.Services;

/// Session-scoped undo window over the Recycle Bin: restore or purge exactly
/// the items brisk just recycled, never anything else in the bin.
public interface IRecycleBinSession
{
    bool Restore(IReadOnlyList<string> originalPaths);
    bool Purge(IReadOnlyList<string> originalPaths);
    void OpenRecycleBinUi();
}
