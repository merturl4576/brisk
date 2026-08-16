using System.Globalization;

namespace BriskEngine;

public static class Fmt
{
    public static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("F1", CultureInfo.InvariantCulture) + " GB",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("F0", CultureInfo.InvariantCulture) + " MB",
        >= 1L << 10 => (bytes / (double)(1L << 10)).ToString("F0", CultureInfo.InvariantCulture) + " KB",
        _ => $"{bytes} B",
    };
}
