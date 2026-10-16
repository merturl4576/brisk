using System.Globalization;
using BriskEngine.Cleaning;
using BriskEngine.Models;
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

public sealed class PartialSpaceTests
{
    private static TargetScanResult T(string id, long bytes, string? skipped = null) => new(
        CleanupTargetRegistry.All.Single(t => t.Id == id),
        new[] { new ResolvedItem(id, $@"C:\f\{id}", bytes, null) }, skipped);

    [Fact]
    public void Estimate_FoundCountsEverything_TickedOnlyDefaultOnRows()
    {
        var (found, ticked) = PartialSpace.Estimate(new[]
        {
            T("user-temp", 100),
            T("nvidia-shader-cache", 50),
            T("hibernation-file", 1000),
            T("chrome-cache", 30, skipped: "Chrome is running"),
        });
        Assert.Equal(1180, found);
        Assert.Equal(130, ticked);
    }
}
