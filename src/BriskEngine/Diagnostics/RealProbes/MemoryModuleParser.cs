using System;
using System.Globalization;

namespace BriskEngine.Diagnostics.RealProbes;

/// The Win32_PhysicalMemory-to-MemoryModule mapping, lifted out from behind the
/// WMI call so that it can be tested without one.
///
/// It is split out for the same reason BootEventParser is. While this mapping
/// lived inside RealHardwareProbe, not one test in the suite executed it:
/// swapping the Speed and ConfiguredClockSpeed reads left the build green,
/// every test passing, and the machine it was verified on still silent — 3200
/// configured over 2933 rated is 109%, comfortably above the line. The same
/// swap on the hardware this rule exists for turns 2133/3200 into 150%: silent
/// forever, on exactly the machine it was written to help, and printed
/// backwards if it ever did speak.
///
/// So the seam is a property lookup rather than a ManagementBaseObject. The
/// delegate is exactly the row's indexer — `name => row[name]` — including its
/// failures: indexing a WMI row with a property the class does not define
/// throws rather than returning null, and firmware that writes a speed as a
/// string is a shape a test can hand over and a live query cannot be asked for.
internal static class MemoryModuleParser
{
    /// Windows did not say. Never a real reading: a module recorded as running
    /// at 0 MT/s reads downstream as memory running at nothing at all, which is
    /// the largest wrong answer this data can produce.
    internal const int Unknown = 0;

    /// Named arguments on purpose. The two speeds are the same type, read from
    /// two properties whose names differ by one word, and nothing but the name
    /// distinguishes them at the call site.
    internal static MemoryModule Read(Func<string, object?> property) =>
        new(Slot(property),
            RatedMts: Speed(property, "Speed"),
            ConfiguredMts: Speed(property, "ConfiguredClockSpeed"),
            CapacityBytes: Bytes(property, "Capacity"));

    /// DeviceLocator is the label printed beside the slot on the board
    /// ("ChannelA-DIMM0"), which is the one a user can act on. BankLabel is the
    /// fallback because some firmware fills in only one of the two. Neither is
    /// invented: an unlabelled module stays unlabelled, and the rule words its
    /// sentence without a name rather than making one up.
    internal static string Slot(Func<string, object?> property)
    {
        var locator = Text(property, "DeviceLocator");
        return locator.Length > 0 ? locator : Text(property, "BankLabel");
    }

    /// Win32_PhysicalMemory reports both speeds as UInt32 MT/s — including
    /// ConfiguredClockSpeed, whose name says clock and whose value does not.
    /// No conversion happens here for exactly that reason: the number Windows
    /// gives is already the transfer rate, and "correcting" it to MHz would
    /// double the figure the user is shown.
    internal static int Speed(Func<string, object?> property, string name)
    {
        var raw = Value(property, name);
        if (raw is null) return Unknown;
        try
        {
            var value = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            // A zero or a negative is firmware saying nothing, not a speed.
            return value > 0 && value <= int.MaxValue ? (int)value : Unknown;
        }
        catch (Exception)
        {
            // Present but not a number. Unreadable is unknown.
            return Unknown;
        }
    }

    /// Capacity is UInt64, and some firmware writes it as a decimal string —
    /// hence decimal rather than long here, which holds both without wrapping.
    internal static long Bytes(Func<string, object?> property, string name)
    {
        var raw = Value(property, name);
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

    private static string Text(Func<string, object?> property, string name) =>
        Value(property, name) is string text ? text.Trim() : string.Empty;

    /// One property failing must never cost the whole inventory. Indexing a
    /// ManagementBaseObject with a property the class does not carry throws
    /// ManagementException rather than returning null — the documented case,
    /// and the reason this exists — but the catch is deliberately wider than
    /// that one type, because the alternative to losing one field here is
    /// losing every module to the probe's outer handler.
    private static object? Value(Func<string, object?> property, string name)
    {
        try
        {
            return property(name);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
