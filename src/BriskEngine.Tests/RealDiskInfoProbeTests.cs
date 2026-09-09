using System;
using System.IO;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.RealProbes;
using Xunit;

namespace BriskEngine.Tests;

/// Live against the kernel, on whatever disk the runner has. These cannot
/// pin the answer — a CI runner's system disk is whatever it is — only
/// that the probe answers without throwing, and that a letter with
/// nothing behind it, or a path that is not a drive root, is Unknown
/// rather than a guess. The reading itself was verified by hand on the
/// maintainer's machine; see the probe's own comment.
public class RealDiskInfoProbeTests
{
    [Fact]
    public void SystemDisk_AnswersOneOfTheThree_WithoutThrowing()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory)!;
        var medium = new RealDiskInfoProbe().MediumOf(root);

        Assert.True(Enum.IsDefined(medium));
    }

    [Fact]
    public void ALetterWithNoVolume_IsUnknown()
    {
        var taken = DriveInfo.GetDrives()
            .Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var free = "ZYXWVUTSRQPONMLKJIHGFEDCBA".First(c => !taken.Contains(c));

        Assert.Equal(DiskMedium.Unknown, new RealDiskInfoProbe().MediumOf($@"{free}:\"));
    }

    [Fact]
    public void NotADriveRoot_IsUnknown()
    {
        Assert.Equal(DiskMedium.Unknown, new RealDiskInfoProbe().MediumOf(@"C:\Windows\"));
    }
}
