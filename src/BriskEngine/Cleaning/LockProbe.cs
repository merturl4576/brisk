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
    /// Enough for one root probe on every child of a big temp dir plus
    /// change for the shallow walks where locks actually live (lockfiles
    /// sit near profile roots; ACL denials sit on the dir itself).
    public const int DefaultPerTarget = 512;

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
            return ProbeDirectory(new DirectoryInfo(path), budget, ct);
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
        CancellationToken ct)
    {
        // The directory handle itself: catches ACL-denied dirs (the
        // mullvad-updates case) in one open.
        if (!budget.TryTake()) return false;
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
                locked = ProbeDirectory(sub, budget, ct);
            }
            else
            {
                if (!budget.TryTake()) return false;
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
