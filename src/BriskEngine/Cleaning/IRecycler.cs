using System.Collections.Generic;

namespace BriskEngine.Cleaning;

public interface IRecycler
{
    /// Sends a file or directory to the Recycle Bin (never permanent).
    void Recycle(string path);

    /// Sends many paths to the Recycle Bin in ONE shell operation. The
    /// per-call shell overhead is ~200 ms regardless of file size, so
    /// recycling item-by-item turns a temp folder of a few thousand small
    /// files into a silent multi-minute grind (the round-10 live incident);
    /// a batch keeps the same work at seconds. A failure may leave part of
    /// the batch recycled — callers re-check the disk before re-attempting.
    void Recycle(IReadOnlyList<string> paths);
}
