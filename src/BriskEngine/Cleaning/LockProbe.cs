using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BriskEngine.Cleaning;

/// Scan-time answer to "would a recycle of this path predictably fail right
/// now?" — the honest-total probe behind ResolvedItem.Locked.
public interface ILockProbe
{
    bool IsLockedForDelete(string path, CancellationToken ct = default);
}

/// The 2026-08-17 live incident: the Depolama card promised ~450 MB, the
/// clean delivered 22 MB. 310 MB of it was WhatsApp's WebView2 profile dir
/// (one lockfile inside → SHFileOperation 124 → the whole directory
/// survived) and 119 MB was an ACL-denied temp dir (SHFileOperation 120).
/// Both are knowable BEFORE promising: a CreateFileW probe asking for
/// DELETE access fails with a sharing violation exactly when a recycle
/// would, and it disturbs nothing (full sharing, no data access).
public sealed class DeleteLockProbe : ILockProbe
{
    /// Directory probes short-circuit on the first locked file; an unlocked
    /// tree pays one CreateFileW per file, so deep trees get a budget. A
    /// lock past the budget merely over-promises that item — the clean
    /// itself is unaffected either way.
    public const int MaxProbesPerItem = 256;

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

    public bool IsLockedForDelete(string path, CancellationToken ct = default)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReparsePoint) != 0) return false;
            if ((attrs & FileAttributes.Directory) == 0) return ProbeOne(path, false);
            var budget = MaxProbesPerItem;
            return ProbeDirectory(new DirectoryInfo(path), ref budget, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Unreadable is not provably locked — leave it in the promise and
            // let the clean's own error entry tell the story if it fails.
            return false;
        }
    }

    private bool ProbeDirectory(DirectoryInfo dir, ref int budget, CancellationToken ct)
    {
        if (budget-- <= 0) return false;
        // The directory handle itself: catches ACL-denied dirs (the
        // mullvad-updates case) in one open.
        if (ProbeOne(dir.FullName, isDirectory: true)) return true;

        FileSystemInfo[] entries;
        try { entries = dir.GetFileSystemInfos(); }
        catch (UnauthorizedAccessException) { return true; }   // can't even list = can't move
        catch (IOException) { return false; }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (budget-- <= 0) return false;
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            var locked = entry switch
            {
                DirectoryInfo sub => ProbeDirectory(sub, ref budget, ct),
                _ => ProbeOne(entry.FullName, isDirectory: false),
            };
            if (locked) return true;   // one held file blocks the whole move
        }
        return false;
    }

    /// Asks Windows for DELETE access with full sharing. Another process
    /// holding the path without FILE_SHARE_DELETE fails this open with a
    /// sharing violation — the same wall a recycle-move would hit.
    private static bool ProbeOne(string path, bool isDirectory)
    {
        using var handle = CreateFileW(path, Delete, ShareAll, IntPtr.Zero,
            OpenExisting, isDirectory ? BackupSemantics : 0u, IntPtr.Zero);
        if (!handle.IsInvalid) return false;
        return Marshal.GetLastWin32Error()
            is ErrorAccessDenied or ErrorSharingViolation or ErrorLockViolation;
    }
}
