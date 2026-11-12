using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;

namespace BriskEngine.Cleaning;

/// System Restore's own restore points, read and removed through System
/// Restore. Never vssadmin: "vssadmin delete shadows" is the line ransomware
/// runs (security tools flag it), and it would also take shadow copies that
/// backup software keeps for itself.
public interface IRestorePoints
{
    /// Every restore point's sequence number (higher = newer). null when
    /// System Restore cannot be read: not administrator, service off, WMI
    /// broken. Unknown is never read as none.
    IReadOnlyList<uint>? SequenceNumbers();

    /// Removes exactly this one point. 0 = removed; anything else is
    /// Windows' error code and nothing was removed.
    uint Remove(uint sequenceNumber);
}

public sealed class RealRestorePoints : IRestorePoints
{
    [DllImport("srclient.dll")]
    private static extern uint SRRemoveRestorePoint(uint dwRPNum);

    /// What Get-ComputerRestorePoint reads. Needs an administrator: on the
    /// maintainer's PC (2026-11-07) the unelevated query was "Access denied".
    public IReadOnlyList<uint>? SequenceNumbers()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"root\default"),
                new ObjectQuery("SELECT SequenceNumber FROM SystemRestore"));
            using var rows = searcher.Get();
            var values = new List<object?>();
            foreach (ManagementBaseObject row in rows)
                using (row) values.Add(row["SequenceNumber"]);
            return Read(values);
        }
        catch (Exception) { return null; }
    }

    public uint Remove(uint sequenceNumber) => SRRemoveRestorePoint(sequenceNumber);

    /// WMI's raw values as sequence numbers. null when any one cannot be
    /// read: a list brisk cannot read whole is one it never deletes from.
    internal static IReadOnlyList<uint>? Read(IEnumerable<object?> values)
    {
        var numbers = new List<uint>();
        foreach (var value in values)
        {
            if (value is null) return null;
            try { numbers.Add(Convert.ToUInt32(value, CultureInfo.InvariantCulture)); }
            catch (Exception) { return null; }
        }
        return numbers;
    }
}
