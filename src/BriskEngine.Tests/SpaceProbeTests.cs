using System;
using BriskEngine.Actions;
using BriskEngine.Cleaning;
using Xunit;

namespace BriskEngine.Tests;

sealed class FakeSpace : ISpaceProbe
{
    public long? Docker = null;
    public long Bin = 0;
    public Func<int?> RestorePoints = () => null;
    public long? DockerReclaimableBytes() => Docker;
    public long RecycleBinBytes() => Bin;
    public int? RestorePointCount() => RestorePoints();
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

    // ---- Restore points: System Restore's own list, never vssadmin.

    /// WMI hands SequenceNumber back as a boxed UInt32. This reading is all
    /// there is to parse; a value it cannot read makes the whole list unknown.
    [Fact]
    public void Parse_reads_restore_point_sequence_numbers()
    {
        Assert.Equal(new uint[] { 12, 15, 13 },
            RealRestorePoints.Read(new object?[] { 12u, 15u, 13u }));
        Assert.Empty(RealRestorePoints.Read(Array.Empty<object?>())!);
        Assert.Null(RealRestorePoints.Read(new object?[] { 12u, null, 13u }));
        Assert.Null(RealRestorePoints.Read(new object?[] { 12u, "not a number" }));
        Assert.Null(RealRestorePoints.Read(new object?[] { -1 }));
    }

    [Fact]
    public void RestorePointCount_counts_system_restore_points_and_runs_nothing()
    {
        var runner = new ScriptedRunner((_, _) => (0, ""));
        var probe = new RealSpaceProbe(runner, new FakeRestorePoints(7, 9, 12));
        Assert.Equal(3, probe.RestorePointCount());
        Assert.Empty(runner.Commands);   // no vssadmin, no process at all
    }

    /// Not an administrator, service off, WMI broken: System Restore cannot
    /// be read, and that is "unknown", never 0.
    [Fact]
    public void RestorePointCount_is_null_when_system_restore_cannot_be_read()
    {
        var probe = new RealSpaceProbe(new ScriptedRunner((_, _) => (0, "")),
            new FakeRestorePoints(7, 9) { Unreadable = true });
        Assert.Null(probe.RestorePointCount());
    }
}
