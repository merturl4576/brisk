using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BriskEngine.Diagnostics.RealProbes;

public sealed class RealDiskInfoProbe : IDiskInfoProbe
{
    public long FreeBytes(string driveRoot)
    {
        try
        {
            return new DriveInfo(driveRoot).AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }

    public long TotalBytes(string driveRoot)
    {
        try
        {
            return new DriveInfo(driveRoot).TotalSize;
        }
        catch
        {
            return 0;
        }
    }

    /// Two questions to the kernel, no WMI and no elevation. First the
    /// volume is asked which physical disk it sits on
    /// (IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS on \\.\C:), then that disk is
    /// asked whether it pays a seek penalty (IOCTL_STORAGE_QUERY_PROPERTY,
    /// StorageDeviceSeekPenaltyProperty on \\.\PhysicalDriveN). A head that
    /// has to move is what "spinning" means to the storage stack, and it is
    /// the reading Task Manager's Performance page turns into the HDD/SSD
    /// label, so a user who doubts the finding can see the same answer there.
    ///
    /// Both handles are opened with no access rights at all, which is what
    /// the storage IOCTLs ask for and what lets a standard-user `brisk scan`
    /// answer this. Verified live, non-elevated, on Windows 11 build 26200
    /// (2026-09-09): KINGSTON SKC3000 NVMe under C: answered one extent on
    /// disk 0 with IncursSeekPenalty 0; a letter with no volume failed the
    /// open with ERROR_FILE_NOT_FOUND. A volume striped or mirrored over
    /// more than one disk has no single medium and answers Unknown.
    public DiskMedium MediumOf(string driveRoot)
    {
        try
        {
            var letter = driveRoot.TrimEnd('\\');
            if (letter.Length != 2 || letter[1] != ':') return DiskMedium.Unknown;

            uint disk;
            using (var volume = Open(@"\\.\" + letter))
            {
                if (volume.IsInvalid) return DiskMedium.Unknown;
                // VOLUME_DISK_EXTENTS: DWORD NumberOfDiskExtents, then
                // DISK_EXTENT[] at offset 8 — each DWORD DiskNumber padded
                // to 8, LARGE_INTEGER StartingOffset, LARGE_INTEGER
                // ExtentLength: 24 bytes. Room for eight; more is Unknown.
                var extents = new byte[8 + 24 * 8];
                if (!DeviceIoControl(volume, IoctlVolumeGetVolumeDiskExtents,
                        null, 0, extents, (uint)extents.Length, out _, IntPtr.Zero))
                    return DiskMedium.Unknown;
                if (BitConverter.ToUInt32(extents, 0) != 1) return DiskMedium.Unknown;
                disk = BitConverter.ToUInt32(extents, 8);
            }

            using (var drive = Open(@"\\.\PhysicalDrive" + disk))
            {
                if (drive.IsInvalid) return DiskMedium.Unknown;
                // STORAGE_PROPERTY_QUERY: DWORD PropertyId, DWORD QueryType
                // (0 = standard), BYTE AdditionalParameters[1], padded to 12.
                var query = new byte[12];
                query[0] = StorageDeviceSeekPenaltyProperty;
                // DEVICE_SEEK_PENALTY_DESCRIPTOR: DWORD Version, DWORD Size,
                // BOOLEAN IncursSeekPenalty at offset 8.
                var descriptor = new byte[12];
                if (!DeviceIoControl(drive, IoctlStorageQueryProperty,
                        query, (uint)query.Length, descriptor, (uint)descriptor.Length,
                        out var returned, IntPtr.Zero) || returned < 9)
                    return DiskMedium.Unknown;
                return descriptor[8] != 0 ? DiskMedium.Rotational : DiskMedium.Solid;
            }
        }
        catch
        {
            return DiskMedium.Unknown;
        }
    }

    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const byte StorageDeviceSeekPenaltyProperty = 7;

    private const uint FileShareReadWrite = 0x00000001 | 0x00000002;
    private const uint OpenExisting = 3;

    private static SafeFileHandle Open(string path) =>
        CreateFileW(path, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device, uint ioControlCode,
        byte[]? inBuffer, uint inBufferSize, byte[] outBuffer, uint outBufferSize,
        out uint bytesReturned, IntPtr overlapped);
}
