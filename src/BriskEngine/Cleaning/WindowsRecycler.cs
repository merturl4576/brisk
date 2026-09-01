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

    /// SHFileOperation return codes (shellapi.h DE_*), in words. The live
    /// workbench printed "SHFileOperation failed (120)" fourteen times for a
    /// cache the shell is not allowed to touch; nobody should have to look
    /// 0x78 up to learn that.
    public static string Describe(int code) => code switch
    {
        0x71 => "source and destination are the same file",
        0x72 => "multiple sources for one destination",
        0x73 => "source and destination are in different folders",
        0x74 => "the source is a root directory",
        0x75 => "the operation was cancelled",
        0x76 => "the destination is inside the source",
        0x78 => "access denied at the source",
        0x79 => "the path is too deep",
        0x7A => "more than one destination",
        0x7C => "the path is invalid or the item is in use",
        0x7D => "the destination is in the same tree as the source",
        0x7E => "the destination is a file, not a folder",
        0x80 => "the destination is a folder, not a file",
        0x81 => "the name is too long",
        0x82 or 0x83 or 0x84 => "the destination is optical media",
        0x85 => "the file is too large for the destination",
        0x86 => "a sharing violation",
        0x87 => "the source is optical media",
        0x88 => "the source is a recordable disc",
        _ => "an unknown shell error",
    };

    public void Recycle(string path)
    {
        var op = new SHFILEOPSTRUCTW
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0\0", // double-null-terminated list
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };
        var code = SHFileOperationW(ref op);
        if (code != 0) throw new IOException($"the shell refused: {Describe(code)} (code {code}) for '{path}'");
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
            throw new IOException($"the shell refused: {Describe(code)} (code {code}) for a batch of {paths.Count} items");
        if (op.fAnyOperationsAborted)
            throw new IOException($"SHFileOperation aborted part of a batch of {paths.Count} items");
    }
}
