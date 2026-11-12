using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace BriskEngine.Live;

/// The Pil block on Home. Charge and power come from GetSystemPowerStatus;
/// health and cycle count are null when this battery does not say, and the
/// GUI hides those lines rather than guess.
public sealed record BatteryInfo(int ChargePercent, bool PluggedIn, int? HealthPercent, int? CycleCount);

public interface IBatteryProbe
{
    /// null = no battery (a desktop), or its charge cannot be read: the Pil
    /// block hides.
    BatteryInfo? Read();
}

/// The three bytes of SYSTEM_POWER_STATUS brisk reads.
internal readonly record struct PowerStatus(byte AcLine, byte Flag, byte Percent);

/// One battery's raw numbers as its driver reports them; null = not read.
internal sealed record BatteryReading(long? Full, long? Design, long? Cycles);

/// What the wear read found for the whole machine.
internal sealed record BatteryWear(int? HealthPercent, int? CycleCount)
{
    public static readonly BatteryWear Unknown = new(null, null);
}

public static class Battery
{
    /// Full-charge capacity over design capacity, in percent. Not clamped: a
    /// new battery can hold more than it was designed for, and that is what
    /// it reads. null when either is unknown or not positive, and when the
    /// ratio is too large to be a reading at all.
    public static int? Health(long? full, long? design)
    {
        if (full is not > 0 || design is not > 0) return null;
        var percent = Math.Round(100.0 * full.Value / design.Value, MidpointRounding.AwayFromZero);
        return percent <= int.MaxValue ? (int)percent : null;
    }

    /// A cycle count worth showing. 0 is firmware that does not count: the
    /// maintainer's battery, at 29 % health, reads 0 through WMI and through
    /// powercfg's battery report alike, and "0 cycles" on a worn battery is a
    /// false fact. 0xFFFFFFFF is the driver's "unknown".
    internal static int? Cycles(long? raw) => raw is > 0 and <= int.MaxValue ? (int)raw.Value : null;

    /// BATTERY_FLAG 128 = no system battery; 255 = status unknown, which is
    /// still a battery if it reports a charge. A charge over 100 (255 =
    /// unknown) is no reading, and with no charge there is no block to show.
    internal static int? Charge(PowerStatus status)
    {
        const byte NoSystemBattery = 128, Unknown = 255;
        if (status.Flag != Unknown && (status.Flag & NoSystemBattery) != 0) return null;
        return status.Percent <= 100 ? status.Percent : null;
    }

    /// root\wmi answers one row per battery in each class, keyed by
    /// InstanceName. A null list is a query that failed: that number is
    /// unknown for every battery.
    internal static IReadOnlyList<BatteryReading> Join(
        IReadOnlyList<(string Instance, object? Value)>? full,
        IReadOnlyList<(string Instance, object? Value)>? design,
        IReadOnlyList<(string Instance, object? Value)>? cycles)
    {
        var names = new List<string>();
        foreach (var list in new[] { full, design, cycles })
        {
            if (list is null) continue;
            foreach (var (instance, _) in list)
                if (!names.Contains(instance, StringComparer.OrdinalIgnoreCase)) names.Add(instance);
        }
        return names.Select(n => new BatteryReading(Find(full, n), Find(design, n), Find(cycles, n))).ToList();
    }

    private static long? Find(IReadOnlyList<(string Instance, object? Value)>? list, string instance)
    {
        if (list is null) return null;
        foreach (var row in list)
            if (string.Equals(row.Instance, instance, StringComparison.OrdinalIgnoreCase))
                return Wmi.Number(row.Value);
        return null;
    }

    /// Health over every battery together (both packs of a two-battery
    /// laptop), and only when every one of them reported both numbers: one
    /// unknown battery is never filled in from the other. A cycle count only
    /// for a single battery: which of two to show is not a question one
    /// number answers.
    internal static BatteryWear Wear(IReadOnlyList<BatteryReading> batteries)
    {
        if (batteries.Count == 0) return BatteryWear.Unknown;
        int? health = null;
        if (batteries.All(b => Capacity(b.Full) is not null && Capacity(b.Design) is not null))
            health = Health(batteries.Sum(b => Capacity(b.Full)!.Value),
                            batteries.Sum(b => Capacity(b.Design)!.Value));
        var cycles = batteries.Count == 1 ? Cycles(batteries[0].Cycles) : null;
        return new BatteryWear(health, cycles);
    }

    /// 0xFFFFFFFF is BATTERY_UNKNOWN_CAPACITY, not four terawatt-hours.
    private static long? Capacity(long? raw) => raw is > 0 and < 0xFFFFFFFF ? raw : null;

    /// BATTERY_INFORMATION (batclass.h), 36 bytes: ULONG Capabilities; UCHAR
    /// Technology; UCHAR Reserved[3]; UCHAR Chemistry[4]; ULONG
    /// DesignedCapacity @12; ULONG FullChargedCapacity @16; ULONG
    /// DefaultAlert1, DefaultAlert2, CriticalBias; ULONG CycleCount @32.
    internal static BatteryReading? FromInformation(ReadOnlySpan<byte> info)
    {
        if (info.Length < 36) return null;
        return new BatteryReading(
            Full: BitConverter.ToUInt32(info.Slice(16, 4)),
            Design: BitConverter.ToUInt32(info.Slice(12, 4)),
            Cycles: BitConverter.ToUInt32(info.Slice(32, 4)));
    }
}

/// Charge and AC from GetSystemPowerStatus on every read; health and cycle
/// count from the battery driver, read once and kept for Refresh — they
/// move over weeks, and the GUI asks every second.
///
/// The wear read is WMI root\wmi first (BatteryFullChargedCapacity,
/// BatteryStaticData.DesignedCapacity, BatteryCycleCount). Where WMI leaves a
/// number unknown, the battery device itself is asked
/// (IOCTL_BATTERY_QUERY_INFORMATION, the source powercfg /batteryreport
/// reads), which is the same driver data without WMI's wrapping. Verified on
/// the maintainer's laptop 2026-11-07, unelevated: BatteryStaticData failed
/// with "Generic failure" while BatteryFullChargedCapacity answered 13 680
/// and BatteryCycleCount 0; the battery report read design 46 740 mWh. Both
/// paths are read-only: queries, and a device handle opened for reading.
///
/// The wear read runs on its own thread. The first read of a refresh waits
/// for it up to `wait`; a read that is still running after that is never
/// waited for again, so a broken WMI repository costs one wait, not one per
/// second, and its answer is picked up whenever it lands.
public sealed class RealBatteryProbe : IBatteryProbe
{
    internal static readonly TimeSpan Refresh = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    private readonly Func<PowerStatus?> _power;
    private readonly Func<BatteryWear> _readWear;
    private readonly TimeSpan _wait;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();
    private Task<BatteryWear>? _pending;
    private BatteryWear _wear = BatteryWear.Unknown;
    private DateTime? _wearAt;

    public RealBatteryProbe() : this(ReadPowerStatus, ReadWear) { }

    internal RealBatteryProbe(Func<PowerStatus?> power, Func<BatteryWear> readWear,
        TimeSpan? wait = null, Func<DateTime>? utcNow = null)
    {
        _power = power;
        _readWear = readWear;
        _wait = wait ?? DefaultWait;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public BatteryInfo? Read()
    {
        try
        {
            if (_power() is not { } status) return null;
            if (Battery.Charge(status) is not { } charge) return null;
            var wear = Wear();
            return new BatteryInfo(charge, status.AcLine == 1, wear.HealthPercent, wear.CycleCount);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private BatteryWear Wear()
    {
        lock (_gate)
        {
            var started = false;
            if (_pending is null && (_wearAt is null || _utcNow() - _wearAt.Value >= Refresh))
            {
                _pending = Task.Factory.StartNew(SafeReadWear, CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
                started = true;
            }
            if (_pending is not null && (started ? _pending.Wait(_wait) : _pending.IsCompleted))
            {
                _wear = _pending.Result;
                _wearAt = _utcNow();
                _pending = null;
            }
            return _wear;
        }
    }

    private BatteryWear SafeReadWear()
    {
        try { return _readWear(); }
        catch (Exception) { return BatteryWear.Unknown; }
    }

    private static PowerStatus? ReadPowerStatus() =>
        GetSystemPowerStatus(out var s) ? new PowerStatus(s.ACLineStatus, s.BatteryFlag, s.BatteryLifePercent) : null;

    /// WMI first; each number WMI leaves unknown is asked of the device.
    private static BatteryWear ReadWear()
    {
        var wmi = Battery.Wear(Battery.Join(
            WmiValues("BatteryFullChargedCapacity", "FullChargedCapacity"),
            WmiValues("BatteryStaticData", "DesignedCapacity"),
            WmiValues("BatteryCycleCount", "CycleCount")));
        if (wmi.HealthPercent is not null && wmi.CycleCount is not null) return wmi;
        var device = ReadDevices() is { } readings ? Battery.Wear(readings) : BatteryWear.Unknown;
        return new BatteryWear(wmi.HealthPercent ?? device.HealthPercent, wmi.CycleCount ?? device.CycleCount);
    }

    private static IReadOnlyList<(string Instance, object? Value)>? WmiValues(string wmiClass, string property)
    {
        var rows = Wmi.Fetch(@"root\wmi", $"SELECT InstanceName, {property} FROM {wmiClass}", QueryTimeout);
        return rows?.Select(r => (r.GetValueOrDefault("InstanceName") as string ?? "", r.GetValueOrDefault(property)))
            .ToList();
    }

    /// Every battery device interface, asked for BATTERY_INFORMATION. null =
    /// the device list could not be read. A battery that does not answer is a
    /// reading of unknowns (so health stays unknown); an empty slot (tag 0)
    /// is skipped.
    private static IReadOnlyList<BatteryReading>? ReadDevices()
    {
        var batteryClass = GuidDeviceBattery;
        var set = SetupDiGetClassDevsW(ref batteryClass, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return null;
        try
        {
            var readings = new List<BatteryReading>();
            for (uint index = 0; index < 16; index++)
            {
                var data = new SpDeviceInterfaceData { cbSize = (uint)Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref batteryClass, index, ref data)) break;
                var reading = InterfacePath(set, ref data) is { } path
                    ? ReadDevice(path)
                    : new BatteryReading(null, null, null);
                if (reading is not null) readings.Add(reading);
            }
            return readings;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static string? InterfacePath(IntPtr set, ref SpDeviceInterfaceData data)
    {
        SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out var required, IntPtr.Zero);
        if (required < 8) return null;
        var buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W: DWORD cbSize, then WCHAR
            // DevicePath[]. cbSize is the fixed part as the compiler pads it:
            // 8 on x64, 6 on x86.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetailW(set, ref data, buffer, required, out _, IntPtr.Zero)) return null;
            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// null = no battery in this slot.
    private static BatteryReading? ReadDevice(string path)
    {
        var unread = new BatteryReading(null, null, null);
        using var device = CreateFileW(path, GenericRead, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (device.IsInvalid) return unread;

        // IOCTL_BATTERY_QUERY_TAG with a wait of 0: answer now.
        var tag = new byte[4];
        if (!DeviceIoControl(device, IoctlBatteryQueryTag, new byte[4], 4, tag, 4, out var got, IntPtr.Zero) || got < 4)
            return unread;
        var batteryTag = BitConverter.ToUInt32(tag);
        if (batteryTag == 0) return null;

        // BATTERY_QUERY_INFORMATION: ULONG BatteryTag; InformationLevel
        // (0 = BatteryInformation); LONG AtRate.
        var query = new byte[12];
        BitConverter.TryWriteBytes(query.AsSpan(0, 4), batteryTag);
        var info = new byte[36];
        if (!DeviceIoControl(device, IoctlBatteryQueryInformation, query, (uint)query.Length,
                info, (uint)info.Length, out got, IntPtr.Zero) || got < info.Length)
            return unread;
        return Battery.FromInformation(info) ?? unread;
    }

    // GUID_DEVICE_BATTERY, batclass.h.
    private static readonly Guid GuidDeviceBattery = new("72631e54-78a4-11d0-bcf7-00aa00b7b32a");
    private const uint DigcfPresent = 0x2, DigcfDeviceInterface = 0x10;
    // CTL_CODE(FILE_DEVICE_BATTERY 0x29, 0x10 / 0x11, METHOD_BUFFERED, FILE_READ_ACCESS)
    private const uint IoctlBatteryQueryTag = 0x00294040;
    private const uint IoctlBatteryQueryInformation = 0x00294044;
    private const uint GenericRead = 0x80000000;
    private const uint FileShareReadWrite = 0x1 | 0x2;
    private const uint OpenExisting = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr deviceInfo, ref Guid interfaceClass,
        uint index, ref SpDeviceInterfaceData data);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SpDeviceInterfaceData data,
        IntPtr detail, uint detailSize, out uint requiredSize, IntPtr deviceInfo);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode,
        byte[] inBuffer, uint inBufferSize, byte[] outBuffer, uint outBufferSize,
        out uint bytesReturned, IntPtr overlapped);
}
