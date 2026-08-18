using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BriskEngine.Diagnostics.RealProbes;

public sealed class RealDisplayProbe : IDisplayProbe
{
    private const int EnumCurrentSettings = -1;
    private const uint AttachedToDesktop = 0x1;
    private const uint CdsUpdateRegistry = 0x1;
    /// dwflags 0: "the graphics mode for the current screen will be changed
    /// dynamically" — and only dynamically. The registry is untouched, which
    /// is the whole point (see IDisplayProbe).
    private const uint CdsApplyDynamically = 0x0;
    private const uint DmDisplayFrequency = 0x400000;
    private const int DispChangeSuccessful = 0;

    public IReadOnlyList<DisplayInfo> Displays()
    {
        var found = new List<DisplayInfo>();
        foreach (var (device, adapterName) in Attached())
        {
            var current = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(device, EnumCurrentSettings, ref current)) continue;
            var max = current.dmDisplayFrequency;
            var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            for (int m = 0; EnumDisplaySettings(device, m, ref mode); m++)
            {
                // Only modes the display is actually running: a higher rate
                // at a lower resolution is not an improvement.
                if (mode.dmPelsWidth == current.dmPelsWidth &&
                    mode.dmPelsHeight == current.dmPelsHeight &&
                    mode.dmBitsPerPel == current.dmBitsPerPel &&
                    mode.dmDisplayFrequency > max)
                    max = mode.dmDisplayFrequency;
                mode.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
            }
            found.Add(new DisplayInfo(device, FriendlyName(device, adapterName),
                (int)current.dmDisplayFrequency, (int)max));
        }
        return found;
    }

    /// Session-only by design, and loud when it fails: the return code carries
    /// DISP_CHANGE_BADMODE and DISP_CHANGE_FAILED, and discarding it is how a
    /// fix comes to claim a refresh rate the driver never accepted.
    public void SetRefreshRate(string deviceName, int hz)
    {
        var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode))
            throw new DisplayChangeException(
                $"{deviceName}: the current display mode could not be read");
        mode.dmDisplayFrequency = (uint)hz;
        mode.dmFields = DmDisplayFrequency;
        var result = ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero,
            CdsApplyDynamically, IntPtr.Zero);
        if (result != DispChangeSuccessful)
            throw new DisplayChangeException(
                $"{deviceName}: the display driver refused {hz} Hz — {Describe(result)}");
    }

    /// Re-applies each attached display's CURRENT mode with CDS_UPDATEREGISTRY,
    /// which is what writes it to the USER profile.
    ///
    /// Not the ChangeDisplaySettingsEx(NULL, NULL, …) form: the documentation
    /// is explicit that a NULL lpDevMode means "all the values currently in
    /// the registry will be used for the display setting", and names
    /// (NULL, NULL, NULL, 0, NULL) as the way to RETURN to the stored mode
    /// after a dynamic change. That call reads the registry; it does not save
    /// to it, so it would silently throw the confirmed mode away.
    ///
    /// Re-applying a mode that is already in effect does not restart the
    /// display — without CDS_RESET, identical settings are not re-applied —
    /// so there is no second flash on screen.
    public void PersistCurrentModes()
    {
        foreach (var (device, _) in Attached())
        {
            var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(device, EnumCurrentSettings, ref mode)) continue;
            var result = ChangeDisplaySettingsEx(device, ref mode, IntPtr.Zero,
                CdsUpdateRegistry, IntPtr.Zero);
            if (result != DispChangeSuccessful)
                throw new DisplayChangeException(
                    $"{device}: the mode now on screen could not be saved — " +
                    Describe(result));
        }
    }

    private static List<(string Device, string AdapterName)> Attached()
    {
        var devices = new List<(string, string)>();
        var adapter = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref adapter, 0); i++)
        {
            if ((adapter.StateFlags & AttachedToDesktop) != 0)
                devices.Add((adapter.DeviceName, adapter.DeviceString));
            adapter.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
        }
        return devices;
    }

    private static string Describe(int code) => code switch
    {
        1 => "the change needs a restart (DISP_CHANGE_RESTART)",
        -1 => "the display driver failed the mode (DISP_CHANGE_FAILED)",
        -2 => "the mode is not supported (DISP_CHANGE_BADMODE)",
        -3 => "the settings could not be written (DISP_CHANGE_NOTUPDATED)",
        -4 => "invalid flags (DISP_CHANGE_BADFLAGS)",
        -5 => "invalid parameter (DISP_CHANGE_BADPARAM)",
        -6 => "the system is DualView capable (DISP_CHANGE_BADDUALVIEW)",
        _ => $"code {code}",
    };

    /// The monitor attached to an adapter carries the name a user recognises;
    /// when it has none, the adapter's own description is the honest fallback.
    private static string FriendlyName(string device, string adapterName)
    {
        var monitor = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        if (EnumDisplayDevices(device, 0, ref monitor, 0) &&
            !string.IsNullOrWhiteSpace(monitor.DeviceString))
            return monitor.DeviceString;
        return adapterName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(
        string? device, uint devNum, ref DISPLAY_DEVICE info, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(
        string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(
        string deviceName, ref DEVMODE devMode, IntPtr wnd, uint flags, IntPtr param);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }
}
