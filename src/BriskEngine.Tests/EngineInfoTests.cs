using BriskEngine;
using Xunit;

namespace BriskEngine.Tests;

public class EngineInfoTests
{
    [Fact]
    public void Version_IsSemver()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", EngineInfo.Version);
    }
}
