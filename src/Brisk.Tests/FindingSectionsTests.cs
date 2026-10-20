using Brisk.ViewModels;
using Xunit;

namespace Brisk.Tests;

public class FindingSectionsTests
{
    /// The spinning-disk finding sits beside the boot finding it explains,
    /// on the page whose question is "why is this slow" — not on Sağlık,
    /// the default for an unlisted id, which would file the cause on one
    /// page and the symptom on another.
    [Fact]
    public void SystemDiskHdd_IsAPerformanceFinding()
    {
        Assert.True(FindingSections.IsPerformance("system-disk-hdd"));
    }
}
