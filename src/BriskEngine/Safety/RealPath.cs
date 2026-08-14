using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BriskEngine.Safety;

public static class RealPath
{
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000; // required to open directories

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess,
        uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(IntPtr hFile,
        StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// Attempts to resolve the final filesystem path with every link in the chain resolved.
    /// Returns true if the path could be opened and its real path determined via the filesystem.
    /// Returns false if the path cannot be opened (permission denied, doesn't exist, sharing violation, etc.)
    /// or if resolution fails. Retries once if the initial buffer is too small.
    public static bool TryResolve(string path, out string real)
    {
        real = "";
        var full = Path.GetFullPath(path);

        // For paths longer than 260 chars, use the \\?\ prefix to enable long path support
        var toOpen = full.Length > 260 && !full.StartsWith(@"\\?\", StringComparison.Ordinal)
            ? @"\\?\" + full
            : full;

        var handle = CreateFileW(toOpen, 0 /* query attributes only */, 7 /* rwd share */,
            IntPtr.Zero, 3 /* OPEN_EXISTING */, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle == new IntPtr(-1)) return false;
        try
        {
            var sb = new StringBuilder(1024);
            var len = GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, 0);
            if (len == 0) return false;

            // If buffer was too small, retry with the required size (per Win32 contract)
            if (len > sb.Capacity)
            {
                sb = new StringBuilder((int)len);
                len = GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, 0);
                if (len == 0) return false;
            }

            var final = sb.ToString();
            real = final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
            return true;
        }
        finally { CloseHandle(handle); }
    }

    /// Final filesystem path with every link in the chain resolved.
    /// Falls back to GetFullPath for paths that cannot be opened (does not exist, etc.).
    /// Used for comparison-only contexts where fallback is acceptable (e.g., ProtectedPaths roots).
    public static string Resolve(string path)
    {
        return TryResolve(path, out var real) ? real : Path.GetFullPath(path);
    }
}
