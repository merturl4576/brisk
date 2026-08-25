using System.Globalization;
using System.Windows;
using Brisk.Localization;
using Brisk.Views;
using Xunit;

namespace Brisk.Tests;

public class LocKeyConverterTests
{
    [Fact]
    public void ConvertsKeyThroughLoc()
    {
        Loc.Instance.SetLanguage("en");
        var converter = new LocKeyConverter();
        Assert.Equal("Safe", converter.Convert("clean.level.safe", typeof(string),
            null, CultureInfo.InvariantCulture));
        Assert.Equal("x.missing", converter.Convert("x.missing", typeof(string),
            null, CultureInfo.InvariantCulture));
    }
}

/// FIX WAVE, Finding 8. MainWindow binds Topmost through this while a display
/// confirmation is pending: Activate() alone loses to Windows' foreground lock
/// after a long fix batch, so a user who alt-tabbed away would never see the
/// "Is the picture back?" overlay in time to keep a change that WORKED. It has
/// to go back down the moment the confirmation resolves — brisk does not park
/// itself above every other window.
public class NullToBoolTests
{
    [Fact]
    public void PendingConfirmation_IsTopmost_AndNothingElseIs()
    {
        var converter = NullToBool.Instance;
        Assert.Equal(true, converter.Convert(new object(), typeof(bool), null,
            CultureInfo.InvariantCulture));
        Assert.Equal(false, converter.Convert(null, typeof(bool), null,
            CultureInfo.InvariantCulture));
    }
}

/// The maximize overhang. Windows maximizes a framed window by extending it
/// past every screen edge by the frame's own width, on the understanding that
/// the frame lands out there — and a WindowChrome window still HAS that frame,
/// which is exactly why brisk uses WindowChrome. brisk draws content to the
/// window edge instead, so the root content is pushed back in by the overhang
/// while maximized and by nothing at all otherwise. ShellSourceTests proves
/// the window reads its state through this converter; this proves the answer
/// the converter gives, which is the half a source parse cannot see.
public class MaximizedMarginTests
{
    [Theory]
    [InlineData(WindowState.Maximized, 7d)]
    [InlineData(WindowState.Normal, 0d)]
    [InlineData(WindowState.Minimized, 0d)]
    public void OnlyAMaximizedWindow_GivesBackTheFrame(WindowState state, double expected)
    {
        var margin = (Thickness)MaximizedMargin.Instance.Convert(state,
            typeof(Thickness), null, CultureInfo.InvariantCulture);

        Assert.Equal(new Thickness(expected), margin);
    }
}
