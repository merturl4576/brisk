using System;
using System.Windows;

namespace Brisk.Ui.Shell;

/// Where the window opens: a client area showing the design canvas at its
/// own size, shrunk with its shape kept when the screen's work area is
/// smaller (a 1080p laptop at 150 % has 672 DIP of height), and centred in
/// that work area.
public static class FirstWindowSize
{
    public static Rect For(Size canvas, Thickness frame, Rect workArea)
    {
        var frameW = frame.Left + frame.Right;
        var frameH = frame.Top + frame.Bottom;
        var scale = Math.Min(1, Math.Min((workArea.Width - frameW) / canvas.Width,
            (workArea.Height - frameH) / canvas.Height));
        var width = canvas.Width * scale + frameW;
        var height = canvas.Height * scale + frameH;
        return new Rect(workArea.Left + (workArea.Width - width) / 2,
            workArea.Top + (workArea.Height - height) / 2, width, height);
    }
}
