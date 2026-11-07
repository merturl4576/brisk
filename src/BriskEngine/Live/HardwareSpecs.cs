using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BriskEngine.Live;

/// The spec blocks on Home's right column. Every field is read on its own:
/// one that cannot be read is null (empty for Gpus) and shows as "—",
/// without taking the others with it.
public sealed record SpecsInfo(string? CpuName, int? CpuMhz, int? Cores, int? Threads, IReadOnlyList<string> Gpus,
    long? RamInstalledBytes, long? RamUsableBytes, string? DiskModel, long? DiskBytes);

public interface ISpecsProbe
{
    /// Never throws.
    SpecsInfo Read();
}

/// Windows' own inventory: WMI Win32_Processor, Win32_VideoController,
/// Win32_PhysicalMemory, and the Storage namespace's MSFT_Partition and
/// MSFT_Disk for the disk %SystemDrive% sits on; GlobalMemoryStatusEx for the
/// memory Windows can use. Queries only.
///
/// Every query starts at once on its own thread and all of them share one
/// deadline, so a broken WMI repository costs Timeout once and only the
/// fields it hangs — Read returns either way.
///
/// Verified live, unelevated, on the maintainer's laptop 2026-11-07: i7-10750H
/// 2592 MHz 6 cores 12 threads; Intel UHD + GTX 1650 Ti; 2 x 16 GiB; C: on
/// disk 0, KINGSTON SKC3000S1024G 1 024 209 543 168 bytes.
public sealed class RealSpecsProbe : ISpecsProbe
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private const string Cimv2 = @"root\cimv2";
    private const string Storage = @"root\Microsoft\Windows\Storage";

    public SpecsInfo Read()
    {
        try
        {
            var clock = Stopwatch.StartNew();
            var cpu = Wmi.Start(Cimv2,
                "SELECT Name, MaxClockSpeed, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor", Timeout);
            var gpus = Wmi.Start(Cimv2, "SELECT Name, PNPDeviceID FROM Win32_VideoController", Timeout);
            var memory = Wmi.Start(Cimv2, "SELECT Capacity FROM Win32_PhysicalMemory", Timeout);
            var partitions = Wmi.Start(Storage, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition", Timeout);
            var disks = Wmi.Start(Storage, "SELECT Number, Model, FriendlyName, Size FROM MSFT_Disk", Timeout);

            var usable = UsableRam();
            var (name, mhz, cores, threads) = HardwareSpecs.Cpu(Wmi.Await(cpu, clock, Timeout));
            var gpuNames = HardwareSpecs.Gpus(Wmi.Await(gpus, clock, Timeout));
            var installed = HardwareSpecs.RamInstalled(Wmi.Await(memory, clock, Timeout));
            var (diskModel, diskBytes) = HardwareSpecs.SystemDisk(
                Wmi.Await(partitions, clock, Timeout), Wmi.Await(disks, clock, Timeout),
                HardwareSpecs.SystemDriveLetter());
            return new SpecsInfo(name, mhz, cores, threads, gpuNames, installed, usable, diskModel, diskBytes);
        }
        catch (Exception)
        {
            return new SpecsInfo(null, null, null, null, Array.Empty<string>(), null, null, null, null);
        }
    }

    /// What Task Manager calls usable: installed memory minus what firmware
    /// and devices reserve.
    private static long? UsableRam() => MemoryStatus.Read()?.Total;
}

/// Physical memory as GlobalMemoryStatusEx reports it: the total Windows can
/// use, and what is available now. Shared by the spec block (the total) and
/// the live monitor (in use = total - available, Task Manager's "In use").
internal readonly record struct MemoryReading(long Total, long Available);

internal static class MemoryStatus
{
    /// null when the call fails or the total is not a positive size.
    internal static MemoryReading? Read()
    {
        try
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref status)) return null;
            if (status.ullTotalPhys is 0 or > long.MaxValue) return null;
            var available = (long)Math.Min(status.ullAvailPhys, status.ullTotalPhys);
            return new MemoryReading((long)status.ullTotalPhys, available);
        }
        catch (Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}

/// WMI's answer rows as plain property bags, so the readers below can be
/// pinned without a WMI service.
internal sealed class WmiRow : Dictionary<string, object?>
{
    public WmiRow() : base(StringComparer.OrdinalIgnoreCase) { }
}

/// The readers: WMI rows in, spec fields out. Pure.
internal static class HardwareSpecs
{
    /// Name and clock from the first socket; cores and threads summed over
    /// all of them, unknown if any socket does not say.
    internal static (string? Name, int? Mhz, int? Cores, int? Threads) Cpu(IReadOnlyList<WmiRow>? rows)
    {
        if (rows is not { Count: > 0 }) return default;
        var first = rows[0];
        var clock = Wmi.Number(first.GetValueOrDefault("MaxClockSpeed"));
        int? mhz = clock is > 0 and <= int.MaxValue ? (int)clock.Value : null;
        return (Text(first.GetValueOrDefault("Name")), mhz,
            Count(rows, "NumberOfCores"), Count(rows, "NumberOfLogicalProcessors"));
    }

    private static int? Count(IReadOnlyList<WmiRow> rows, string property)
    {
        long total = 0;
        foreach (var row in rows)
        {
            var n = Wmi.Number(row.GetValueOrDefault(property));
            if (n is not > 0) return null;
            total += n.Value;
        }
        return total <= int.MaxValue ? (int)total : null;
    }

    /// Every display adapter Windows lists, minus the software ones: a
    /// PnP id under ROOT\ or SWD\ is a driver with no card behind it (the
    /// Remote Desktop adapter, virtual displays for streaming). A card with
    /// no driver yet is still a card and stays, under the name Windows gives it.
    internal static IReadOnlyList<string> Gpus(IReadOnlyList<WmiRow>? rows)
    {
        if (rows is null) return Array.Empty<string>();
        var names = new List<string>();
        foreach (var row in rows)
        {
            var pnp = row.GetValueOrDefault("PNPDeviceID") as string ?? "";
            if (pnp.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase) ||
                pnp.StartsWith(@"SWD\", StringComparison.OrdinalIgnoreCase)) continue;
            if (Text(row.GetValueOrDefault("Name")) is { } name) names.Add(name);
        }
        return names;
    }

    /// The sum of every module, or unknown if any module's size is: a total
    /// missing a stick would read as less memory than the machine has.
    internal static long? RamInstalled(IReadOnlyList<WmiRow>? rows)
    {
        if (rows is not { Count: > 0 }) return null;
        long total = 0;
        foreach (var row in rows)
        {
            var n = Wmi.Number(row.GetValueOrDefault("Capacity"));
            if (n is not > 0) return null;
            total += n.Value;
        }
        return total;
    }

    /// The one partition that carries the letter, and the one disk with its
    /// number. Anything else — no partition, two, a disk not listed — is
    /// unknown, never the first disk. A model with no readable size keeps the
    /// model.
    internal static (string? Model, long? Bytes) SystemDisk(
        IReadOnlyList<WmiRow>? partitions, IReadOnlyList<WmiRow>? disks, char? letter)
    {
        if (partitions is null || disks is null || letter is null) return default;
        var wanted = char.ToUpperInvariant(letter.Value);
        var numbers = partitions
            .Where(p => Letter(p.GetValueOrDefault("DriveLetter")) == wanted)
            .Select(p => Wmi.Number(p.GetValueOrDefault("DiskNumber")))
            .ToList();
        if (numbers.Count != 1 || numbers[0] is not { } number) return default;
        var matches = disks.Where(d => Wmi.Number(d.GetValueOrDefault("Number")) == number).ToList();
        if (matches.Count != 1) return default;
        var disk = matches[0];
        var model = Text(disk.GetValueOrDefault("Model")) ?? Text(disk.GetValueOrDefault("FriendlyName"));
        var size = Wmi.Number(disk.GetValueOrDefault("Size"));
        return (model, size is > 0 ? size : null);
    }

    /// The letter of %SystemDrive%; the Windows folder's drive if the
    /// variable is missing.
    internal static char? SystemDriveLetter()
    {
        var root = Environment.GetEnvironmentVariable("SystemDrive");
        if (string.IsNullOrEmpty(root))
            root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        return root is { Length: >= 2 } && root[1] == ':' && char.IsAsciiLetter(root[0])
            ? char.ToUpperInvariant(root[0])
            : null;
    }

    /// MSFT_Partition.DriveLetter is a CIM char16; '\0' = no letter.
    private static char? Letter(object? raw)
    {
        var c = raw switch
        {
            char ch => ch,
            string { Length: > 0 } s => s[0],
            ushort u => (char)u,
            _ => '\0',
        };
        return c == '\0' ? null : char.ToUpperInvariant(c);
    }

    /// Trimmed, inner runs of spaces collapsed (CPU names come padded), empty
    /// = unknown.
    private static string? Text(object? raw) =>
        raw is string s && Regex.Replace(s.Trim(), @"\s+", " ") is { Length: > 0 } tidy ? tidy : null;
}

/// One WMI query, bounded. A broken repository can hang a query forever,
/// connection included. Fetch's timeout bounds only the wait between rows;
/// the whole query is bounded by running it off the caller's thread — Start
/// and Await here, the wear thread in RealBatteryProbe.
internal static class Wmi
{
    /// Runs on the calling thread. null = the query failed or timed out
    /// between rows; unknown is never an empty answer.
    internal static IReadOnlyList<WmiRow>? Fetch(string scope, string wql, TimeSpan timeout)
    {
        try
        {
            var options = new System.Management.EnumerationOptions { Timeout = timeout, ReturnImmediately = true, Rewindable = false };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql), options);
            using var results = searcher.Get();
            var rows = new List<WmiRow>();
            foreach (ManagementBaseObject result in results)
            {
                using (result)
                {
                    var row = new WmiRow();
                    foreach (var property in result.Properties) row[property.Name] = property.Value;
                    rows.Add(row);
                }
            }
            return rows;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// Fetch on a thread of its own (not the pool's: a hung COM call there
    /// would hold a pool thread), for Await to bound.
    internal static Task<IReadOnlyList<WmiRow>?> Start(string scope, string wql, TimeSpan timeout)
    {
        try
        {
            return Task.Factory.StartNew(() => Fetch(scope, wql, timeout), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch (Exception)
        {
            return Task.FromResult<IReadOnlyList<WmiRow>?>(null);
        }
    }

    /// The query's rows if it answered before `budget` ran out on `clock`;
    /// null otherwise. A late query is abandoned, not waited for.
    internal static IReadOnlyList<WmiRow>? Await(Task<IReadOnlyList<WmiRow>?> query, Stopwatch clock, TimeSpan budget)
    {
        try
        {
            var left = budget - clock.Elapsed;
            return query.Wait(left > TimeSpan.Zero ? left : TimeSpan.Zero) ? query.Result : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// A WMI integer (uint32, uint64, sometimes a string) as a long; null when
    /// absent or not a number that fits.
    internal static long? Number(object? raw)
    {
        if (raw is null or bool) return null;
        try
        {
            return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
