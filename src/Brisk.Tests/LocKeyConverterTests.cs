using System.Globalization;
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
