using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace BriskEngine.Tests;

/// Gigabyte fixtures that take (almost) no disk. The file is marked sparse
/// BEFORE it is stretched, and the mark is checked: where the volume will
/// not take it, Create deletes the empty file and throws rather than write
/// real gigabytes. Tests that use it carry [SparseFact], which skips them
/// at discovery on such a machine, so the throw is only a backstop.
internal static class SparseFile
{
    private const uint FsctlSetSparse = 0x000900C4;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr inBuffer,
        uint inSize, IntPtr outBuffer, uint outSize, out uint returned, IntPtr overlapped);

    /// Why sparse fixtures cannot be made in the temp folder, or null when
    /// they can. Probed once, on a scratch file of zero bytes.
    internal static string? Unsupported => Probe.Value;

    private static readonly Lazy<string?> Probe = new(() =>
    {
        string? scratch = null;
        try
        {
            scratch = Directory.CreateTempSubdirectory("brisk-sparse-probe-").FullName;
            var path = Path.Combine(scratch, "probe.bin");
            using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
            return MarkSparse(stream, path) is { } refused
                ? $"the temp folder cannot hold sparse files ({refused}); skipped rather than write real gigabytes"
                : null;
        }
        catch (Exception e)
        {
            return $"the sparse-file probe could not run: {e.Message}";
        }
        finally
        {
            if (scratch is not null)
                try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }
    });

    /// A file of `bytes` logical size at `path`, sparse, so it costs nothing.
    internal static string Create(string path, long bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string? refused;
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
        {
            refused = MarkSparse(stream, path);
            if (refused is null) stream.SetLength(bytes);
        }
        if (refused is null) return path;

        File.Delete(path);
        throw new InvalidOperationException($"{path} could not be made sparse ({refused}); " +
            $"refusing to write {bytes} real bytes");
    }

    /// Null when the file is now sparse; otherwise why not.
    private static string? MarkSparse(FileStream stream, string path)
    {
        if (!DeviceIoControl(stream.SafeFileHandle, FsctlSetSparse, IntPtr.Zero, 0, IntPtr.Zero, 0,
                out _, IntPtr.Zero))
            return $"FSCTL_SET_SPARSE failed, Win32 error {Marshal.GetLastWin32Error()}";
        return (File.GetAttributes(path) & FileAttributes.SparseFile) != 0
            ? null
            : "FSCTL_SET_SPARSE succeeded but the file is not marked sparse";
    }
}

/// A test whose fixtures are gigabyte sparse files. xunit 2 decides skips at
/// discovery, so the attribute asks the probe and skips, with the reason,
/// where the temp folder cannot hold sparse files.
public sealed class SparseFactAttribute : FactAttribute
{
    public SparseFactAttribute() => Skip = SparseFile.Unsupported;
}

/// Sparse fixtures and a junction: skips, with the first reason that
/// applies, where either cannot be made.
public sealed class SparseJunctionFactAttribute : FactAttribute
{
    public SparseJunctionFactAttribute() => Skip = SparseFile.Unsupported ?? JunctionProbe.Value;

    /// Probed once. Not JunctionFactAttribute's probe: that one deletes its
    /// scratch folder recursively with the junction still in it, which
    /// throws on Windows and leaves the folder behind on every discovery.
    /// Removing the link on its own first lets the folder go.
    private static readonly Lazy<string?> JunctionProbe = new(() =>
    {
        var scratch = Directory.CreateTempSubdirectory("brisk-junction-probe-").FullName;
        var link = Path.Combine(scratch, "link");
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(scratch, "target")).FullName;
            return DynamicTargetsTests.Junction(link, target)
                ? null
                : "mklink /J could not create a junction on this machine";
        }
        catch (Exception e)
        {
            return $"mklink /J could not run: {e.Message}";
        }
        finally
        {
            try { if (Directory.Exists(link)) Directory.Delete(link); } catch { /* best effort */ }
            try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }
    });
}
