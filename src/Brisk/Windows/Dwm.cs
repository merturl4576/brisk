using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Brisk.Windows;

public static class Dwm
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr,
        ref int value, int size);

    public static void RoundCorners(Window window) =>
        Set(window, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);

    public static void DarkTitleBar(Window window, bool dark) =>
        Set(window, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);

    private static void Set(Window window, int attr, int value)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        try { _ = DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int)); }
        catch (DllNotFoundException) { }   // pre-Win11 / odd environments: no-op
        catch (EntryPointNotFoundException) { }
    }
}
