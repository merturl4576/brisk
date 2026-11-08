using System.Globalization;
using Brisk.Session;

namespace Brisk.Tests.Session;

public sealed class SizesTests
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr"), En = CultureInfo.GetCultureInfo("en");

    [Theory]
    [InlineData(0L, "0", "GB")]
    [InlineData(5_476_083_302L, "5,1", "GB")]
    [InlineData(28_346_784_153L, "26", "GB")]
    [InlineData(891_289_600L, "850", "MB")]
    [InlineData(500L, "<1", "MB")]
    public void Split_Turkish(long bytes, string number, string unit) =>
        Assert.Equal((number, unit), Sizes.Split(bytes, Tr));

    [Fact]
    public void Split_English_UsesAPoint() => Assert.Equal(("5.1", "GB"), Sizes.Split(5_476_083_302L, En));

    [Fact]
    public void Format_JoinsWithASpace() => Assert.Equal("850 MB", Sizes.Format(891_289_600L, En));
}
