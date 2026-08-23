using System;
using System.Management;

namespace BriskEngine.Diagnostics.RealProbes;

/// Windows' own answer to "is memory integrity running right now", from
/// Win32_DeviceGuard in the root\Microsoft\Windows\DeviceGuard namespace.
///
/// SecurityServicesRunning is a list of service codes rather than a flag, and
/// 2 is hypervisor-enforced code integrity — the thing Windows Security shows
/// as "Memory integrity". Verified on Windows 11 build 26200 with memory
/// integrity on: SecurityServicesConfigured {2}, SecurityServicesRunning {2}.
///
/// Deliberately NOT the registry. HypervisorEnforcedCodeIntegrity\Enabled says
/// what was asked for, and a machine can ask for memory integrity and not get
/// it — an incompatible driver leaves it configured and not running. The
/// sentence this probe feeds is about a driver Windows refuses to load, which
/// only a running enforcement does.
///
/// No elevation needed: this query answers for a standard user.
public sealed class RealMemoryIntegrityProbe : IMemoryIntegrityProbe
{
    /// Hoisted so a test can pin the namespace and the property without a WMI
    /// service. Both are easy to typo into something that always returns null,
    /// which would look exactly like a machine that could not be read.
    internal const string Scope = @"root\Microsoft\Windows\DeviceGuard";
    internal const string Query = "SELECT SecurityServicesRunning FROM Win32_DeviceGuard";

    /// Hypervisor-enforced code integrity, in Microsoft's numbering.
    internal const int HvciCode = 2;

    /// The reading, apart from the query — so every shape WMI can hand back is
    /// pinned by a test, on a machine that can only ever produce one of them.
    ///
    /// Non-generic IEnumerable on purpose: WMI hands back a uint32[], and a
    /// value-type array implements IEnumerable&lt;uint&gt;, never
    /// IEnumerable&lt;object&gt;. The generic pattern compiles, reads correctly,
    /// and misses every real machine — returning "unknown", which is also what
    /// a working probe returns on a machine it cannot read, so nothing in the
    /// output looks wrong.
    ///
    /// A string is excluded by name because it is IEnumerable too, of char:
    /// "2" would enumerate to 50 and report a confident "off" for something
    /// brisk did not understand.
    internal static bool? ReadRunning(object? securityServicesRunning)
    {
        if (securityServicesRunning is string) return null;
        // Absent on Windows editions carrying no Device Guard at all: a
        // machine brisk could not read, never one it read as off.
        if (securityServicesRunning is not System.Collections.IEnumerable running) return null;
        foreach (var code in running)
        {
            try { if (Convert.ToInt32(code) == HvciCode) return true; }
            catch (Exception) { return null; }   // an element that is not a number
        }
        // Device Guard answered and this service was not in the list.
        return false;
    }

    public bool? IsOn()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(Scope), new ObjectQuery(Query));
            using var results = searcher.Get();
            foreach (ManagementBaseObject row in results)
            {
                using (row) return ReadRunning(row["SecurityServicesRunning"]);
            }
        }
        catch (Exception)
        {
            // Namespace missing, WMI stopped, access denied. Unknown, which
            // the caller must keep separate from off.
            return null;
        }
        // The query succeeded and returned no rows at all: nothing was read.
        return null;
    }
}
