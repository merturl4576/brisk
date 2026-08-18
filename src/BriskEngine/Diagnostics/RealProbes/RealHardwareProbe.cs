using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;

namespace BriskEngine.Diagnostics.RealProbes;

/// Windows' own hardware inventory, via WMI.
///
/// Everything here is read BY NAME rather than by position. A sibling probe in
/// this wave found that an index-based read of the same kind of payload turned
/// a boot counter into a millisecond count without failing — the shapes match,
/// so nothing complains, and the wrong number reaches the user looking exactly
/// like the right one.
///
/// The other rule is that missing means missing. A property Windows did not
/// fill in comes back as Unknown (0), never as a real zero: a module recorded
/// as running at 0 MT/s would be read downstream as memory running at nothing
/// at all, which is the largest wrong answer this data can produce. Rules skip
/// unknowns; they must never be handed a fabricated one.
public sealed class RealHardwareProbe : IHardwareProbe
{
    /// Windows did not say. Deliberately not a nullable int on the model: the
    /// rules already have to skip a module they cannot read, and one shape of
    /// "no reading" is easier to keep honest than two.
    private const int Unknown = 0;

    public IReadOnlyList<MemoryModule> MemoryModules()
    {
        var modules = new List<MemoryModule>();
        try
        {
            // SELECT * rather than a named column list on purpose:
            // ConfiguredClockSpeed does not exist on every Windows release, and
            // asking for it by name where it is absent fails the whole query —
            // losing the modules and their rated speeds along with it. Reading
            // wide and guarding each property loses only the missing one.
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PhysicalMemory");
            using var results = searcher.Get();
            foreach (ManagementBaseObject row in results)
            {
                using (row)
                {
                    modules.Add(new MemoryModule(
                        Slot(row),
                        Speed(row, "Speed"),
                        Speed(row, "ConfiguredClockSpeed"),
                        Bytes(row, "Capacity")));
                }
            }
        }
        catch (Exception)
        {
            // WMI service stopped, repository corrupt, or the class missing on
            // this edition. An empty inventory is something a rule handles; an
            // exception out of a probe is not.
            return Array.Empty<MemoryModule>();
        }
        return modules;
    }

    /// DeviceLocator is the label printed beside the slot on the board
    /// ("ChannelA-DIMM0"), which is the one a user can act on. BankLabel is the
    /// fallback because some firmware fills in only one of the two. Neither is
    /// invented: an unlabelled module stays unlabelled, and the rule words its
    /// sentence without a name rather than making one up.
    private static string Slot(ManagementBaseObject row)
    {
        var locator = Text(row, "DeviceLocator");
        return locator.Length > 0 ? locator : Text(row, "BankLabel");
    }

    /// Win32_PhysicalMemory reports both speeds as UInt32 MT/s — including
    /// ConfiguredClockSpeed, whose name says clock and whose value does not.
    /// No conversion happens here for exactly that reason: the number Windows
    /// gives is already the transfer rate, and "correcting" it would double it.
    private static int Speed(ManagementBaseObject row, string property)
    {
        var raw = Property(row, property);
        if (raw is null) return Unknown;
        try
        {
            var value = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            // A zero or a negative is firmware saying nothing, not a speed.
            return value > 0 && value <= int.MaxValue ? (int)value : Unknown;
        }
        catch (Exception)
        {
            // A property that is present but not a number (some firmware
            // writes strings). Unreadable is unknown.
            return Unknown;
        }
    }

    private static long Bytes(ManagementBaseObject row, string property)
    {
        var raw = Property(row, property);
        if (raw is null) return 0;
        try
        {
            var value = Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
            return value > 0 && value <= long.MaxValue ? (long)value : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string Text(ManagementBaseObject row, string property) =>
        Property(row, property) is string text ? text.Trim() : string.Empty;

    /// Indexing a ManagementBaseObject with a property the class does not carry
    /// throws rather than returning null, so absence is caught here and turned
    /// into the null the callers above already handle.
    private static object? Property(ManagementBaseObject row, string property)
    {
        try
        {
            return row[property];
        }
        catch (ManagementException)
        {
            return null;
        }
    }
}
