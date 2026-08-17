using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace BriskEngine.Cleaning;

public sealed class WindowsRecycler : IRecycler
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW op);

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;      // -> Recycle Bin
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    public void Recycle(string path)
    {
        var op = new SHFILEOPSTRUCTW
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0\0", // double-null-terminated list
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };
        var code = SHFileOperationW(ref op);
        if (code != 0) throw new IOException($"SHFileOperation failed ({code}) for '{path}'");
    }

    /// One SHFileOperation for the whole batch — pFrom takes a
    /// null-separated list, and embedded nulls survive Unicode string
    /// marshaling. This is exactly how Explorer deletes a multi-selection.
    public void Recycle(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        var op = new SHFILEOPSTRUCTW
        {
            wFunc = FO_DELETE,
            pFrom = string.Join("\0", paths) + "\0\0", // double-null-terminated list
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };
        var code = SHFileOperationW(ref op);
        if (code != 0)
            throw new IOException($"SHFileOperation failed ({code}) for a batch of {paths.Count} items");
        if (op.fAnyOperationsAborted)
            throw new IOException($"SHFileOperation aborted part of a batch of {paths.Count} items");
    }
}
