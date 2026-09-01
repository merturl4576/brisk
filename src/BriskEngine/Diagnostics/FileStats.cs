using BriskEngine.Models;

namespace BriskEngine.Diagnostics;

/// ONE walk per folder per scan, and the two numbers every caller of it
/// agrees to ask for.
///
/// Walking %LOCALAPPDATA% is the most expensive thing a brisk scan does —
/// seconds, on a real profile — and two rules now want it: disk-breakdown
/// for the total, large-files for the names. Both go through here, so the
/// second one costs nothing. A rule calling ctx.Files.DirectoryStats
/// directly would be a second walk of the same tree, which is exactly what
/// this file exists to make unnecessary.
///
/// THE FLOOR AND THE CUT ARE CONSTANTS RATHER THAN PARAMETERS, and that is
/// load-bearing: the memo is keyed by path alone, so a caller asking for a
/// different floor would silently receive the first caller's answer. One
/// floor, declared once, is what makes that key honest.
public static class FileStats
{
    /// Below this, a named file is not a revelation. 500 MB is the smallest
    /// size a person recognises as "that one" in a folder listing — an
    /// installer, a video, a disk image — and it is small enough that the
    /// list has something on it on a machine whose biggest file is 1 GB.
    public const long MinFileBytes = 500L << 20;

    /// How many of them are kept per folder. Ten is more than any surface
    /// shows and few enough that the kept list never becomes the walk's
    /// cost — the merge across roots cuts it again.
    public const int Take = 10;

    /// This scan's answer for this folder, measured once. The cast is safe
    /// by construction: this is the only writer of a "stats:" key.
    public static DirectoryStats Of(DiagnosticContext ctx, string path) =>
        (DirectoryStats)ctx.Memo.GetOrAdd("stats:" + path,
            _ => ctx.Files.DirectoryStats(path, MinFileBytes, Take));
}
