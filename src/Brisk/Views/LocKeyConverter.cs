using System;
using System.Globalization;
using System.Windows.Data;
using Brisk.Localization;

namespace Brisk.Views;

public sealed class LocKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter,
        CultureInfo culture) => value is string key ? Loc.Instance[key] : "";

    public object ConvertBack(object? value, Type targetType, object? parameter,
        CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BoolToVis : IValueConverter
{
    public static readonly BoolToVis Instance = new();
    public object Convert(object? value, Type targetType, object? parameter,
        CultureInfo culture) =>
        value is true ? System.Windows.Visibility.Visible
                      : System.Windows.Visibility.Collapsed;
    public object ConvertBack(object? value, Type targetType, object? parameter,
        CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToVis : IValueConverter
{
    public static readonly NullToVis Instance = new();
    public object Convert(object? value, Type targetType, object? parameter,
        CultureInfo culture) =>
        value is null ? System.Windows.Visibility.Collapsed
                      : System.Windows.Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter,
        CultureInfo culture) => throw new NotSupportedException();
}

/// Windows' foreground lock is the last thing between a display change that
/// WORKED and a user who never gets to keep it: after a long fix batch the
/// user has alt-tabbed away, Activate() only flashes the taskbar button, and
/// the "Is the picture back?" overlay sits behind other windows until the 15
/// seconds run out. MainWindow binds Topmost through this for exactly as long
/// as a confirmation is pending — it goes back down the moment the question is
/// answered, one way or the other.
public sealed class NullToBool : IValueConverter
{
    public static readonly NullToBool Instance = new();
    public object Convert(object? value, Type targetType, object? parameter,
        CultureInfo culture) => value is not null;
    public object ConvertBack(object? value, Type targetType, object? parameter,
        CultureInfo culture) => throw new NotSupportedException();
}

/// The maximize overhang, and the reason MainWindow's root content has a
/// Margin at all.
///
/// A WindowChrome window keeps its real Win32 frame — that is the whole point
/// of preferring it to WindowStyle=None — and Windows maximizes a framed
/// window by extending it past every screen edge by the frame's own width, on
/// the understanding that the frame is what lands out there. brisk draws its
/// content right to the window edge instead, so without this the top of the
/// title bar, the left of the nav and the bottom of the page all sit off the
/// screen the moment someone maximizes.
///
/// Seven device-independent units is the frame WPF actually leaves us
/// (SM_CXSIZEFRAME + SM_CXPADDEDBORDER on a default Windows 11 desktop). It is
/// deliberately a plain number rather than a system-metric read: this is a
/// visual gutter, and a wrong-by-a-pixel gutter is a cosmetic flaw, while the
/// P/Invoke that would make it exact is a whole surface of its own.
public sealed class MaximizedMargin : IValueConverter
{
    public static readonly MaximizedMargin Instance = new();

    private const double Overhang = 7;

    public object Convert(object? value, Type targetType, object? parameter,
        CultureInfo culture) =>
        value is System.Windows.WindowState.Maximized
            ? new System.Windows.Thickness(Overhang)
            : new System.Windows.Thickness(0);

    public object ConvertBack(object? value, Type targetType, object? parameter,
        CultureInfo culture) => throw new NotSupportedException();
}
