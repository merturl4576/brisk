using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BriskEngine.Cleaning;

namespace BriskEngine.Actions;

public interface ISpaceProbe
{
    /// null: Docker is not installed, or its engine is not running.
    long? DockerReclaimableBytes();
    long RecycleBinBytes();
    /// System Restore's restore points; null when they cannot be read (not
    /// administrator, service off).
    int? RestorePointCount();
}

public sealed class RealSpaceProbe : ISpaceProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfo { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinW(string? rootPath, ref ShQueryRbInfo info);

    private static readonly Regex Size = new(@"^\s*([\d.]+)\s*([kKMGT]?B)", RegexOptions.Multiline);
    private readonly IProcessRunner _runner;
    private readonly IRestorePoints _restorePoints;

    public RealSpaceProbe(IProcessRunner runner, IRestorePoints? restorePoints = null)
    {
        _runner = runner;
        _restorePoints = restorePoints ?? new RealRestorePoints();
    }

    public long? DockerReclaimableBytes()
    {
        try
        {
            var (exit, output) = _runner.Run("docker", "system df --format \"{{.Reclaimable}}\"");
            return exit == 0 ? ParseDockerReclaimable(output) : null;
        }
        catch (Exception) { return null; }
    }

    internal static long? ParseDockerReclaimable(string stdout)
    {
        long total = 0;
        foreach (Match m in Size.Matches(stdout))
        {
            var value = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var unit = m.Groups[2].Value.ToUpperInvariant() switch
            {
                "KB" => 1e3, "MB" => 1e6, "GB" => 1e9, "TB" => 1e12, _ => 1d,
            };
            total += (long)Math.Round(value * unit);
        }
        return total;
    }

    public long RecycleBinBytes()
    {
        var info = new ShQueryRbInfo { cbSize = Marshal.SizeOf<ShQueryRbInfo>() };
        return SHQueryRecycleBinW(null, ref info) == 0 ? info.i64Size : 0;
    }

    public int? RestorePointCount() => _restorePoints.SequenceNumbers()?.Distinct().Count();
}
