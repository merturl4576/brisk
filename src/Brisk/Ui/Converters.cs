using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Brisk.Session;
using BriskEngine.Actions;

namespace Brisk.Ui;

/// Tone (and the engine's ResultTone) to the palette colour a line wears.
public sealed class ToneBrush : IValueConverter
{
    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        Tone.Good or ResultTone.Good => B("Ui.Green"),
        Tone.Warn or ResultTone.Warn => B("Ui.Amber"),
        Tone.Bad or ResultTone.Bad => B("Ui.Red"),
        _ => B("Ui.Muted"),
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// Visible when the value is not null (and, for strings, not empty);
/// ConverterParameter="invert" flips it.
public sealed class PresentToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var present = value is not null && value is not string { Length: 0 } && value is not false;
        if (parameter as string == "invert") present = !present;
        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// Visible when the value's text equals ConverterParameter; "!x" means not x.
public sealed class EqualsToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var p = parameter as string ?? "";
        var negate = p.StartsWith('!');
        var equal = string.Equals(value?.ToString(), negate ? p[1..] : p, StringComparison.Ordinal);
        return equal != negate ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
