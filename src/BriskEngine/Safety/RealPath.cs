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

    /// Final filesystem path with every link in the chain resolved.
    /// A path that cannot be opened (does not exist) falls back to GetFullPath —
    /// it cannot be deleted anyway, and the validator still gets a canonical string.
    public static string Resolve(string path)
    {
        var full = Path.GetFullPath(path);
        var handle = CreateFileW(full, 0 /* query attributes only */, 7 /* rwd share */,
            IntPtr.Zero, 3 /* OPEN_EXISTING */, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle == new IntPtr(-1)) return full;
        try
        {
            var sb = new StringBuilder(1024);
            var len = GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, 0);
            if (len == 0 || len > sb.Capacity) return full;
            var final = sb.ToString();
            return final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
        }
        finally { CloseHandle(handle); }
    }
}
