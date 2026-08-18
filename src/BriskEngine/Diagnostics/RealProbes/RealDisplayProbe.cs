using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BriskEngine.Diagnostics.RealProbes;

public sealed class RealDisplayProbe : IDisplayProbe
{
    private const int EnumCurrentSettings = -1;
    private const uint AttachedToDesktop = 0x1;
    private const uint CdsUpdateRegistry = 0x1;
    private const uint DmDisplayFrequency = 0x400000;

    public IReadOnlyList<DisplayInfo> Displays()
    {
        var found = new List<DisplayInfo>();
        var adapter = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref adapter, 0); i++)
        {
            if ((adapter.StateFlags & AttachedToDesktop) == 0)
            {
                adapter.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
                continue;
            }

            var device = adapter.DeviceName;
            var current = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(device, EnumCurrentSettings, ref current))
            {
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
                found.Add(new DisplayInfo(device, FriendlyName(device, adapter.DeviceString),
                    (int)current.dmDisplayFrequency, (int)max));
            }
            adapter.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
        }
        return found;
    }

    public void SetRefreshRate(string deviceName, int hz)
    {
        var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode)) return;
        mode.dmDisplayFrequency = (uint)hz;
        mode.dmFields = DmDisplayFrequency;
        ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
    }

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
