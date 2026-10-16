using System;
using System.Globalization;
using System.Windows.Data;

namespace Brisk.Views;

/// true → Visible, anything else → Collapsed. The report card's one converter.
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
