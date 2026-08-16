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
