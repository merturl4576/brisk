using Xunit;

namespace BriskEngine.Tests;

public class RegistryProbeLongTests
{
    [Fact]
    public void FakeRegistry_ReadsBackAQwordItPlanted_AndNullForAnythingElse()
    {
        var reg = new FakeRegistry();
        reg.SetLong(@"HKCU\X", "Stamp", 134309229721436686L);
        reg.SetInt(@"HKCU\X", "Small", 5);

        Assert.Equal(134309229721436686L, reg.GetLong(@"HKCU\X", "Stamp"));
        Assert.Null(reg.GetLong(@"HKCU\X", "Small"));
        Assert.Null(reg.GetLong(@"HKCU\X", "Missing"));
    }
}
