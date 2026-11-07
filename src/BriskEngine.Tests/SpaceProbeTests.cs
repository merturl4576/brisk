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

    // ---- Restore points. Written by hand from vssadmin's documented
    // format (English Windows 11): `vssadmin list shadows` needs an elevated
    // prompt, and the session that wrote this test was not one.
    internal const string ThreeShadows = """
        vssadmin 1.1 - Volume Shadow Copy Service administrative command-line tool
        (C) Copyright 2001-2013 Microsoft Corp.

        Contents of shadow copy set ID: {3f1c9d4e-8a2b-4c6d-9e1f-0a2b3c4d5e6f}
           Contained 1 shadow copies at creation time: 9/14/2026 3:02:11 AM
              Shadow Copy ID: {7b2e1a90-5c4d-4f3e-8a1b-2c3d4e5f6a7b}
                 Original Volume: (C:)\\?\Volume{d1c2b3a4-0000-0000-0000-100000000000}\
                 Shadow Copy Volume: \\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1
                 Originating Machine: DESKTOP-BRISK
                 Service Machine: DESKTOP-BRISK
                 Provider: 'Microsoft Software Shadow Copy provider 1.0'
                 Type: ClientAccessibleWriters
                 Attributes: Persistent, Client-accessible, No auto release, Differential, Auto recovered

        Contents of shadow copy set ID: {a0b1c2d3-e4f5-4a6b-8c7d-9e0f1a2b3c4d}
           Contained 1 shadow copies at creation time: 10/3/2026 2:13:45 PM
              Shadow Copy ID: {c4d5e6f7-a8b9-4c0d-9e1f-2a3b4c5d6e7f}
                 Original Volume: (C:)\\?\Volume{d1c2b3a4-0000-0000-0000-100000000000}\
                 Shadow Copy Volume: \\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy4
                 Originating Machine: DESKTOP-BRISK
                 Service Machine: DESKTOP-BRISK
                 Provider: 'Microsoft Software Shadow Copy provider 1.0'
                 Type: ClientAccessibleWriters
                 Attributes: Persistent, Client-accessible, No auto release, Differential, Auto recovered

        Contents of shadow copy set ID: {5e6f7a8b-9c0d-4e1f-a2b3-c4d5e6f7a8b9}
           Contained 1 shadow copies at creation time: 11/5/2026 9:41:07 PM
              Shadow Copy ID: {0f1e2d3c-4b5a-4968-8776-655443322110}
                 Original Volume: (C:)\\?\Volume{d1c2b3a4-0000-0000-0000-100000000000}\
                 Shadow Copy Volume: \\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy7
                 Originating Machine: DESKTOP-BRISK
                 Service Machine: DESKTOP-BRISK
                 Provider: 'Microsoft Software Shadow Copy provider 1.0'
                 Type: ClientAccessibleWriters
                 Attributes: Persistent, Client-accessible, No auto release, Differential, Auto recovered

        """;

    internal const string NoShadows = """
        vssadmin 1.1 - Volume Shadow Copy Service administrative command-line tool
        (C) Copyright 2001-2013 Microsoft Corp.

        No items found that satisfy the query.

        """;

    [Fact]
    public void Parse_counts_shadow_ids()
    {
        Assert.Equal(3, ShadowCopies.Parse(ThreeShadows));
        Assert.Equal(0, ShadowCopies.Parse(NoShadows));
        Assert.Equal(0, ShadowCopies.Parse(""));
    }

    /// vssadmin's labels are translated on a Turkish Windows; the shadow
    /// copy's device name never is, so the count does not depend on them.
    [Fact]
    public void Parse_does_not_read_the_labels()
    {
        var translated = ThreeShadows
            .Replace("Shadow Copy Volume", "Gölge Kopya Birimi")
            .Replace("Shadow Copy ID", "Gölge Kopya Kimliği")
            .Replace("Contents of shadow copy set ID", "Gölge kopya kümesi içeriği");
        Assert.Equal(3, ShadowCopies.Parse(translated));
    }

    [Fact]
    public void RestorePointCount_asks_vssadmin_about_the_system_drive()
    {
        var runner = new ScriptedRunner((_, _) => (0, ThreeShadows));
        Assert.Equal(3, new RealSpaceProbe(runner).RestorePointCount());
        Assert.Equal($"vssadmin list shadows /for={ShadowCopies.SystemDrive}", Assert.Single(runner.Commands));
        Assert.Matches("^[A-Z]:$", ShadowCopies.SystemDrive);
    }

    /// Not an administrator ("You don't have the correct permissions", exit
    /// 2) and no vssadmin at all both read as "unknown", never as 0.
    [Fact]
    public void RestorePointCount_is_null_when_vssadmin_is_denied_or_missing()
    {
        var denied = new ScriptedRunner((_, _) => (2,
            "Error: You don't have the correct permissions to run this command."));
        Assert.Null(new RealSpaceProbe(denied).RestorePointCount());

        var missing = new ScriptedRunner((_, _) => throw new System.ComponentModel.Win32Exception(2));
        Assert.Null(new RealSpaceProbe(missing).RestorePointCount());
    }
}
