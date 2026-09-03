using System;
using System.Collections.Generic;

namespace BriskEngine.Diagnostics;

public interface IPowercfgProbe
{
    (Guid Id, string Name) GetActiveScheme();
    IReadOnlyList<(Guid Id, string Name)> ListSchemes();
    void SetActive(Guid id);
    /// True when the machine has a system battery, or when that cannot be
    /// told. The power-plan rule stays silent on such machines: Balanced is
    /// the right plan on a laptop, and "unknown" must not become a finding.
    bool HasBattery();
}

public interface IRegistryProbe
{
    string? GetString(string keyPath, string valueName);       // keyPath like @"HKCU\Software\X"
    void SetString(string keyPath, string valueName, string value);
    void DeleteValue(string keyPath, string valueName);
    byte[]? GetBytes(string keyPath, string valueName);
    void SetBytes(string keyPath, string valueName, byte[] value);
    int? GetInt(string keyPath, string valueName);
    void SetInt(string keyPath, string valueName, int value);
    IReadOnlyList<string> GetValueNames(string keyPath);
    IReadOnlyList<string> GetSubKeyNames(string keyPath);
}

public interface IProcessInfoProbe
{
    IReadOnlyList<(string Name, long WorkingSetBytes)> TopByMemory(int count);
    double MemoryLoadPercent();
}

public interface ISensorProbe
{
    double? CpuTempC();   // null = sensors unavailable (no admin / unsupported)
    double? GpuTempC();
    int GpuCount();
}

/// The one answer to "did that sensor actually say something?".
///
/// Three shipped surfaces used to decide this for themselves — the scan
/// snapshot the report card is built from, `brisk scan`'s sensor notice, and
/// the thermals rule — and two of them agreed. The notice asked `is not null`
/// alone, so on a machine whose sensor reports NaN the console said the
/// sensor answered and the card said it did not: one product contradicting
/// itself about the same reading, in the same second.
///
/// null is no answer at all. NaN is the answer a present-but-silent sensor
/// gives, and it is not a reading either: it fails every threshold, so
/// nothing calls it hot, and printed it renders "CPU NaN°C" — the one value
/// that would take the both-read template while carrying nothing to read.
/// Infinity is the same class of non-number and is refused with it.
public static class SensorReading
{
    public static bool IsReal(double? celsius) =>
        celsius is { } value && double.IsFinite(value);
}

public interface IDiskInfoProbe
{
    long FreeBytes(string driveRoot);   // driveRoot like @"C:\"
    long TotalBytes(string driveRoot);
}

/// What the machine is made of, as opposed to how it is behaving. Read from
/// Windows' own inventory rather than measured, so the numbers are only as
/// good as the firmware that filled them in.
public interface IHardwareProbe
{
    /// The physical memory modules Windows knows about, in the order it lists
    /// them. Empty when the inventory cannot be read at all — a machine with
    /// no memory is not a thing, so empty always means "brisk could not see",
    /// never "there is none". Individual modules can also come back with zeroed
    /// speeds, which means the same thing one module at a time.
    IReadOnlyList<MemoryModule> MemoryModules();
}

/// Whether Windows is currently enforcing memory integrity (HVCI).
///
/// Read because the thermals rule was offering one explanation to every
/// machine that could not read a CPU temperature: that the driver is on
/// Microsoft's vulnerable-driver blocklist and Windows will not load it while
/// memory integrity is on. On a machine with memory integrity OFF that
/// explanation cannot be true, and brisk was handing it out anyway.
///
/// Tri-state on purpose, and the third state is the point. null is "brisk
/// could not determine it" — a machine where the query fails must keep the
/// hedged sentence rather than be told either story. false is a measurement,
/// not a default.
///
/// The RUNNING state, never the configured one: memory integrity can be
/// configured and still not running (an incompatible driver stops it), and it
/// is the running enforcement that refuses to load a blocklisted driver.
/// Reading the configured value would let brisk state a cause that is switched
/// off — the exact failure this probe exists to remove.
public interface IMemoryIntegrityProbe
{
    bool? IsOn();
}

/// The mode change is split in two on purpose. Applying and persisting in one
/// call is what turns a black screen into a permanent one: the likeliest thing
/// a person does when the picture disappears is hold the power button, which
/// kills brisk before its countdown can restore anything — and if the mode is
/// already in the registry, the machine boots straight back into it. So a fix
/// applies for this session only, and the registry is told nothing until the
/// user has confirmed there is a picture to confirm.
public interface IDisplayProbe
{
    IReadOnlyList<DisplayInfo> Displays();

    /// Applies the rate for this session only: the registry keeps the mode the
    /// machine booted with, so a hard power-off is a rescue rather than a
    /// verdict. Throws DisplayChangeException when the driver refuses — the
    /// fix must never report a rate it did not get.
    void SetRefreshRate(string deviceName, int hz);

    /// Writes the modes that are on screen right now into the registry, so
    /// they survive a reboot. Called only once the change has been confirmed
    /// (Keep), or undone — an undo has to reach the registry too, or a reboot
    /// would bring back the mode the user just asked brisk to take away.
    void PersistCurrentModes();
}

/// Windows' own boot measurements.
///
/// There is deliberately no separate "recent offenders" call. A flat list of
/// blamed programs capped at N spans several boots and can be cut mid-boot with
/// nothing to signal it, so a caller could report "Windows blames these three"
/// when Windows blamed four. Offenders therefore only ever arrive attached to
/// the boot they belong to, where nothing is cut to fit a count.
///
/// That removes the truncation a paged call invites. It is not a completeness
/// guarantee — a record brisk cannot read is dropped rather than guessed at —
/// so see BootRecord.Offenders for what can still go missing and how to phrase
/// a result built on it.
public interface IEventLogProbe
{
    /// Up to `count` boots, newest first, each carrying the programs Windows
    /// blamed for it, ordered worst degradation first. A boot Windows blamed
    /// nobody for comes back with an empty Offenders list, which is common —
    /// on the machine this was verified against, three of the ten most recent
    /// boots had nobody blamed, including the newest.
    ///
    /// The channel behind this is admin-only, so an implementation returns the
    /// boots it managed to read — empty when it cannot open the log at all —
    /// rather than throwing. A missing boot history is something a rule can
    /// handle; an exception out of a probe is not.
    IReadOnlyList<BootRecord> RecentBoots(int count);
}
