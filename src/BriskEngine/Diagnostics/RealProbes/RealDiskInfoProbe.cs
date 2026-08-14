using System.IO;

namespace BriskEngine.Diagnostics.RealProbes;

public sealed class RealDiskInfoProbe : IDiskInfoProbe
{
    public long FreeBytes(string driveRoot)
    {
        try
        {
            return new DriveInfo(driveRoot).AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }

    public long TotalBytes(string driveRoot)
    {
        try
        {
            return new DriveInfo(driveRoot).TotalSize;
        }
        catch
        {
            return 0;
        }
    }
}
