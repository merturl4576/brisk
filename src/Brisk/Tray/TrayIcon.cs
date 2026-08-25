using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Brisk.Localization;

namespace Brisk.Tray;

public sealed class TrayIcon : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private readonly NotifyIcon _notify;
    // Not readonly: the mark is redrawn when the theme changes, because
    // brisk's signature is the palette's and the palette moves. See
    // SetAccent.
    private Icon _icon;
    private IntPtr _iconHandle;

    public event Action? LeftClick;
    public event Action? OpenRequested;
    public event Action? ScanRequested;
    public event Action? ExitRequested;

    public TrayIcon(Color accent, Loc loc)
    {
        (_icon, _iconHandle) = DrawIcon(accent);
        var menu = new ContextMenuStrip();
        menu.Items.Add(loc["tray.open"], null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(loc["tray.scan"], null, (_, _) => ScanRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(loc["tray.exit"], null, (_, _) => ExitRequested?.Invoke());
        _notify = new NotifyIcon
        {
            Icon = _icon,
            Text = "brisk",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notify.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) LeftClick?.Invoke();
        };
    }

    /// Redraw the mark in a new accent. Called when the theme changes: the
    /// title bar and the tray icon are the same mark in two places, and a
    /// switch that moved one and not the other would leave them describing
    /// different themes.
    ///
    /// The new icon is handed to the NotifyIcon BEFORE the old one is
    /// destroyed — Icon.FromHandle does not own its handle, so the HICON has
    /// to be released by hand, and releasing one the shell is still drawing
    /// from is how a tray icon turns into a black square.
    public void SetAccent(Color accent)
    {
        var (icon, handle) = DrawIcon(accent);
        var (oldIcon, oldHandle) = (_icon, _iconHandle);
        (_icon, _iconHandle) = (icon, handle);
        _notify.Icon = icon;
        oldIcon.Dispose();
        DestroyIcon(oldHandle);
    }

    public void UpdateTooltip(string text) =>
        _notify.Text = text.Length <= 63 ? text : text[..63];

    /// Internal rather than private so the mark's colour can be asserted:
    /// what a 16x16 tray icon is filled with is otherwise only checkable by
    /// looking at the notification area.
    internal static (Icon Icon, IntPtr Handle) DrawIcon(Color accent)
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath();

        // Build rounded rectangle using AddArc (AddRoundedRectangle not available in this System.Drawing version)
        int radius = 4;
        var rect = new Rectangle(0, 0, 15, 15);
        int w = rect.Width;
        int h = rect.Height;
        int x = rect.X;
        int y = rect.Y;

        path.AddArc(x, y, radius * 2, radius * 2, 180, 90);                              // Top-left
        path.AddArc(x + w - radius * 2, y, radius * 2, radius * 2, 270, 90);            // Top-right
        path.AddArc(x + w - radius * 2, y + h - radius * 2, radius * 2, radius * 2, 0, 90); // Bottom-right
        path.AddArc(x, y + h - radius * 2, radius * 2, radius * 2, 90, 90);             // Bottom-left
        path.CloseFigure();

        using var fill = new SolidBrush(accent);
        g.FillPath(fill, path);
        using var font = new Font("Segoe UI", 9, System.Drawing.FontStyle.Bold,
            GraphicsUnit.Pixel);
        var size = g.MeasureString("b", font);
        g.DrawString("b", font, Brushes.White,
            (16 - size.Width) / 2f, (16 - size.Height) / 2f);
        var handle = bmp.GetHicon();
        return (Icon.FromHandle(handle), handle);
    }

    public void Dispose()
    {
        _notify.Visible = false;
        _notify.Dispose();
        _icon.Dispose();
        DestroyIcon(_iconHandle);
    }
}
