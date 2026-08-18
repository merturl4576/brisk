using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BriskEngine.Cleaning;

/// One target's probe allowance, shared by ALL of that target's items
/// (review round 1: a per-ITEM budget let a 500-child %TEMP% spend
/// 500 × 256 handle opens per scan — the bound must live at target
/// scope). Exhausted means "stop opening handles and assume unlocked":
/// the promise may then over-count a locked deep tree, which degrades to
/// the round-10 behavior, never worse.
public sealed class LockProbeBudget
{
    /// ROUND 15, from the 2026-08-18 live run: 512 was set to bound a cost
    /// that was never there. One DELETE-access probe measures at 0.058 ms,
    /// so the whole old allowance bought ~30 ms — while a %TEMP% of ~300
    /// entries with nested children blew straight past it, and everything
    /// after the cliff was assumed unlocked. The card promised 180 MB and
    /// the clean delivered 48. This allowance costs ~475 ms if it is ever
    /// spent in full, and real %TEMP% folders never come close.
    public const int DefaultPerTarget = 8192;

    /// ROUND 15, the actual defect behind the 180-vs-48 promise: the
    /// allowance was drained by RECURSION, not by breadth. One deep
    /// directory early in %TEMP% spent everything, and every top-level file
    /// after it — each costing a single probe, and each exactly where the
    /// locks turned out to be — was waved through unprobed. Capping what
    /// one item may spend keeps the cheap, high-value checks affordable no
    /// matter what order the walk meets them in. A file still costs 1.
    public const int MaxPerItem = 64;

    private int _remaining;

    public LockProbeBudget(int probes = DefaultPerTarget) { _remaining = probes; }

    public int Remaining => _remaining;

    /// Take one probe from the allowance; false = exhausted, do not open.
    public bool TryTake()
    {
        if (_remaining <= 0) return false;
        _remaining--;
        return true;
    }
}

/// Scan-time answer to "would a recycle of this path predictably fail right
/// now?" — the honest-total probe behind ResolvedItem.Locked.
public interface ILockProbe
{
    bool IsLockedForDelete(string path, LockProbeBudget budget,
        CancellationToken ct = default);
}

/// The 2026-08-17 live incident: the Depolama card promised ~450 MB, the
/// clean delivered 22 MB. 310 MB of it was WhatsApp's WebView2 profile dir
/// (one lockfile inside → SHFileOperation 124 → the whole directory
/// survived) and 119 MB was an ACL-denied temp dir (SHFileOperation 120).
/// Both are knowable BEFORE promising: a CreateFileW probe asking for
/// DELETE access fails with a sharing violation exactly when a recycle
/// would, and it disturbs nothing (full sharing, no data access).
///
/// Directory probes short-circuit on the first locked file; unlocked trees
/// spend the target-scoped LockProbeBudget one CreateFileW per entry.
public sealed class DeleteLockProbe : ILockProbe
{
    private const uint Delete = 0x00010000;
    private const uint ShareAll = 0x1 | 0x2 | 0x4;   // read | write | delete
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;

    private const int ErrorAccessDenied = 5;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess,
        uint shareMode, IntPtr securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    public bool IsLockedForDelete(string path, LockProbeBudget budget,
        CancellationToken ct = default)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReparsePoint) != 0) return false;
            if ((attrs & FileAttributes.Directory) == 0)
                return budget.TryTake() && ProbeOne(path, isDirectory: false);
            // A tree draws from BOTH allowances: the target's, and its own
            // share of it, so it cannot starve the items behind it.
            return ProbeDirectory(new DirectoryInfo(path), budget,
                new LockProbeBudget(LockProbeBudget.MaxPerItem), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Unreadable is not provably locked — leave it in the promise and
            // let the clean's own error entry tell the story if it fails.
            return false;
        }
    }

    private bool ProbeDirectory(DirectoryInfo dir, LockProbeBudget budget,
        LockProbeBudget item, CancellationToken ct)
    {
        // The directory handle itself: catches ACL-denied dirs (the
        // mullvad-updates case) in one open.
        if (!item.TryTake() || !budget.TryTake()) return false;
        if (ProbeOne(dir.FullName, isDirectory: true)) return true;

        FileSystemInfo[] entries;
        try { entries = dir.GetFileSystemInfos(); }
        catch (UnauthorizedAccessException) { return true; }   // can't even list = can't move
        catch (IOException) { return false; }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            bool locked;
            if (entry is DirectoryInfo sub)
            {
                locked = ProbeDirectory(sub, budget, item, ct);
            }
            else
            {
                if (!item.TryTake() || !budget.TryTake()) return false;
                locked = ProbeOne(entry.FullName, isDirectory: false);
            }
            if (locked) return true;   // one held file blocks the whole move
        }
        return false;
    }

    /// Asks Windows for DELETE access with full sharing. Another process
    /// holding the path without FILE_SHARE_DELETE fails this open with a
    /// sharing violation — the same wall a recycle-move would hit.
    private static bool ProbeOne(string path, bool isDirectory)
    {
        using var handle = CreateFileW(ExtendedLength(path), Delete, ShareAll,
            IntPtr.Zero, OpenExisting, isDirectory ? BackupSemantics : 0u, IntPtr.Zero);
        if (!handle.IsInvalid) return false;
        return Marshal.GetLastWin32Error()
            is ErrorAccessDenied or ErrorSharingViolation or ErrorLockViolation;
    }

    /// Review round 1: the raw P/Invoke has no MAX_PATH handling of its own
    /// (managed System.IO prefixes internally; CreateFileW does not, and
    /// the app carries no longPathAware manifest), so a >260-char path —
    /// WebView2 profiles and node_modules routinely qualify — failed with
    /// ERROR_PATH_NOT_FOUND/ERROR_FILENAME_EXCED_RANGE and silently read
    /// "not locked". The \\?\ prefix turns off Win32 path normalization
    /// and lifts the limit; rooted local paths get \\?\, UNC gets \\?\UNC\.
    public static string ExtendedLength(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
        : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..]
        : Path.IsPathRooted(path) ? @"\\?\" + path
        : path;
}
