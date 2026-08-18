using System;
using System.Collections.Generic;

namespace BriskEngine.Diagnostics;

public interface IPowercfgProbe
{
    (Guid Id, string Name) GetActiveScheme();
    IReadOnlyList<(Guid Id, string Name)> ListSchemes();
    void SetActive(Guid id);
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

public interface IDiskInfoProbe
{
    long FreeBytes(string driveRoot);   // driveRoot like @"C:\"
    long TotalBytes(string driveRoot);
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
