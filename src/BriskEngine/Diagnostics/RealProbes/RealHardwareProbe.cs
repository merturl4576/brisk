using System;
using System.Collections.Generic;
using System.Management;

namespace BriskEngine.Diagnostics.RealProbes;

/// Windows' own hardware inventory, via WMI.
///
/// This class is only the query and the failure handling. Every property is
/// read by name, and the reading itself lives in MemoryModuleParser so that it
/// can be pinned without a WMI service, on any machine — see the comment there
/// for what a mapping nothing executes costs.
///
/// Verified live against Win32_PhysicalMemory on Windows 11 build 26200: two
/// modules, DeviceLocator ChannelA-DIMM0 and ChannelB-DIMM0, Speed 3200,
/// ConfiguredClockSpeed 2933, Capacity 17179869184.
public sealed class RealHardwareProbe : IHardwareProbe
{
    public IReadOnlyList<MemoryModule> MemoryModules()
    {
        var modules = new List<MemoryModule>();
        try
        {
            // SELECT * rather than a named column list on purpose:
            // ConfiguredClockSpeed does not exist on every Windows release, and
            // a WQL query naming a property the class does not define is
            // rejected wholesale rather than degrading to null — losing the
            // modules and their rated speeds along with it. Reading wide and
            // guarding each property by name loses only the missing one.
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PhysicalMemory");
            using var results = searcher.Get();
            foreach (ManagementBaseObject row in results)
            {
                using (row)
                {
                    modules.Add(MemoryModuleParser.Read(name => row[name]));
                }
            }
        }
        catch (Exception)
        {
            // WMI service stopped, repository corrupt, or the class missing on
            // this edition. An empty inventory is something a rule handles; an
            // exception out of a probe is not. Empty always means "brisk could
            // not see", never "there is none" — a machine with no memory is
            // not a thing.
            return Array.Empty<MemoryModule>();
        }
        return modules;
    }
}
