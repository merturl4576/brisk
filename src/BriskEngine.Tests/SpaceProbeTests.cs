using BriskEngine.Actions;
using Xunit;

namespace BriskEngine.Tests;

sealed class FakeSpace : ISpaceProbe
{
    public long? Docker = null;
    public long Bin = 0;
    public long? DockerReclaimableBytes() => Docker;
    public long RecycleBinBytes() => Bin;
}

public sealed class SpaceProbeTests
{
    [Fact]
    public void ParseDockerReclaimable_SumsEveryLine()
    {
        const string output = "2.3GB (45%)\n0B (0%)\n1.2kB\n523.4MB (100%)\n";
        Assert.Equal(2_300_000_000L + 1_200L + 523_400_000L,
            RealSpaceProbe.ParseDockerReclaimable(output));
    }

    [Fact]
    public void ParseDockerReclaimable_EmptyOutputIsZero() =>
        Assert.Equal(0L, RealSpaceProbe.ParseDockerReclaimable(""));

    [Fact]
    public void Docker_FailingCommand_IsNull()
    {
        var probe = new RealSpaceProbe(new ScriptedRunner((_, _) => (1, "")));
        Assert.Null(probe.DockerReclaimableBytes());
    }

    [Fact]
    public void Docker_MissingExecutable_IsNull()
    {
        var probe = new RealSpaceProbe(new ScriptedRunner((_, _) =>
            throw new System.ComponentModel.Win32Exception(2)));
        Assert.Null(probe.DockerReclaimableBytes());
    }
}
