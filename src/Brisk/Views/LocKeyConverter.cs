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

/// A theme key a view model chose, bound to a Shape's Fill as a LIVE resource
/// reference — what {DynamicResource} is in markup, for a key markup does not
/// know until the row arrives.
///
/// It exists so that a view model which already decides something per state
/// — ReadBackRow.StateBrushKey, which throws on a state it has no colour for
/// — does not have to have that decision written out a second time as a wall
/// of DataTriggers in markup. Two copies of one mapping is two chances to
/// drift, and the markup copy is the one that fails SILENTLY: a state with no
/// trigger simply keeps the default.
///
/// IT WAS A VALUE CONVERTER FIRST, AND THAT WAS WRONG. A converter resolves
/// the key once per binding evaluation, and a theme switch is not one:
/// ThemeManager.Apply clears and re-adds the application's merged
/// dictionaries, and nothing re-evaluates a converter binding when a
/// dictionary changes. So every read-back dot kept the previous theme's brush
/// until the next scan happened to rebuild the rows — and the palettes
/// genuinely disagree (Good is #4ADE80 dark and #16A34A light), so what stood
/// on the page after a switch was the other theme's colour on a verdict.
/// EveryReadBackDot_WearsTheThemeThatIsInstalledNow drives the real page
/// across a swap. App.xaml.cs makes the same argument about the tray icon: a
/// theme switch has to reach every mark brisk draws.
///
/// An attached property rather than a converter because neither piece is
/// optional: markup cannot bind the KEY of a DynamicResource, and a converter
/// cannot return a reference. SetResourceReference is the code form of the
/// markup extension, and it keeps the reference alive on the property.
///
/// It still fails silently in the one way the converter did: a key that is
/// not in the dictionary resolves to nothing and the dot paints nothing.
/// ResourceKeyTests cannot see that, because it reads {DynamicResource}
/// literals out of the XAML and there is no literal here.
/// EveryReadBackColour_IsAKeyBothThemesCarry is what covers it, driven off
/// the enum rather than off a list.
public static class ThemeFill
{
    public static readonly System.Windows.DependencyProperty KeyProperty =
        System.Windows.DependencyProperty.RegisterAttached(
            "Key", typeof(string), typeof(ThemeFill),
            new System.Windows.PropertyMetadata(null, OnKeyChanged));

    public static void SetKey(System.Windows.DependencyObject element, string? value) =>
        element.SetValue(KeyProperty, value);

    public static string? GetKey(System.Windows.DependencyObject element) =>
        (string?)element.GetValue(KeyProperty);

    private static void OnKeyChanged(System.Windows.DependencyObject element,
        System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (element is not System.Windows.Shapes.Shape shape) return;
        // An empty key clears rather than referencing "": a row that names no
        // colour paints none, which is the same answer the missing key gives
        // and one the theme cannot turn into a wrong one.
        if (e.NewValue is string key && key.Length > 0)
            shape.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, key);
        else
            shape.ClearValue(System.Windows.Shapes.Shape.FillProperty);
    }
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
