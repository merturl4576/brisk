using System.IO;
using Brisk.Session;

namespace Brisk.Tests.Session;

/// The one piece of the real backend tested here reads free space only;
/// nothing builds an engine or changes the machine (ruling R6).
public sealed class RealActionBackendTests
{
    /// Ruling R23: unread free space is null, never a thrown error, never 0.
    [Fact]
    public void FreeSpace_OfADriveThatIsNotThere_IsNull()
    {
        var present = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var missing = "ZYXWVUTSRQPONMLKJIHG".First(c => !present.Contains(c));
        Assert.Null(RealActionBackend.FreeBytesOn($"{missing}:\\"));
    }

    [Fact]
    public void FreeSpace_OfTheSystemDrive_IsRead() =>
        Assert.True(RealActionBackend.FreeBytesOn(Path.GetPathRoot(Environment.SystemDirectory)!) > 0);
}
