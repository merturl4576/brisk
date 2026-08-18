using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

public class DisplayProbeTests
{
    [Fact]
    public void FakeDisplays_RecordsSetCalls()
    {
        var displays = new FakeDisplays();
        displays.Attached.Add(new DisplayInfo(@"\\.\DISPLAY1", "Dell U2720Q", 60, 144));

        displays.SetRefreshRate(@"\\.\DISPLAY1", 144);

        Assert.Equal(@"\\.\DISPLAY1", displays.SetCalls[0].Device);
        Assert.Equal(144, displays.SetCalls[0].Hz);
    }

    [Fact]
    public void EmptyContext_HasNoDisplays()
    {
        Assert.Empty(TestContext.Empty().Displays.Displays());
    }
}
