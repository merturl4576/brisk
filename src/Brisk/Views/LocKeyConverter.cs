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
