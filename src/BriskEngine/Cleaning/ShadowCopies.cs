using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BriskEngine.Cleaning;

/// The system drive's shadow copies as vssadmin lists them: what Windows
/// keeps its restore points in. Read by the space probe (is there a row?)
/// and again by CleanRunner right before it deletes (how many may go?).
public static class ShadowCopies
{
    /// One per shadow copy. The device name is the kernel's, so it reads the
    /// same on a Turkish Windows, where vssadmin's labels are translated.
    private static readonly Regex Device = new(@"\\Device\\HarddiskVolumeShadowCopy(\d+)",
        RegexOptions.IgnoreCase);

    /// The system drive as vssadmin's /for= takes it: "C:".
    public static string SystemDrive => Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');

    /// Removes the oldest shadow copy and nothing else. The only delete
    /// brisk ever sends: never /all, never one picked by id.
    public static string DeleteOldestArgs => $"delete shadows /for={SystemDrive} /oldest /quiet";

    /// null: vssadmin is missing, refused (it needs an administrator), or
    /// failed. Unknown is never read as 0.
    public static int? Count(IProcessRunner runner)
    {
        try
        {
            var (exit, output) = runner.Run("vssadmin", $"list shadows /for={SystemDrive}");
            return exit == 0 ? Parse(output) : null;
        }
        catch (Exception) { return null; }
    }

    /// Distinct device names, so a copy printed twice is counted once: an
    /// overcount is the one error that could make the last /oldest take the
    /// newest.
    internal static int Parse(string stdout) =>
        Device.Matches(stdout).Select(m => m.Groups[1].Value).Distinct().Count();
}
