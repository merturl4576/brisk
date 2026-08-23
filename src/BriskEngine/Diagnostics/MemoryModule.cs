namespace BriskEngine.Diagnostics;

/// One physical memory module, as Windows reports it.
///
/// Both speeds are in MT/s — megatransfers per second — and never MHz. DDR
/// moves data on both edges of the clock, so a "DDR4-3200" module runs a
/// 1600 MHz clock and performs 3200 MT/s; WMI's Speed and ConfiguredClockSpeed
/// are already the transfer figure despite the second one's name. Printing
/// either as MHz would double the number the user is being told about.
///
/// Zero means "Windows did not tell us", not "zero". Soldered laptop memory
/// and some firmware report no configured speed at all, and a fabricated zero
/// here would surface downstream as a module running at no speed whatsoever —
/// the largest overstatement the data could possibly produce.
public sealed record MemoryModule(
    string Slot,            // "ChannelA-DIMM0" — WMI's DeviceLocator; may be empty
    int RatedMts,           // what the module is rated for; 0 = unknown
    int ConfiguredMts,      // what the memory controller actually set; 0 = unknown
    long CapacityBytes);    // 0 = unknown
