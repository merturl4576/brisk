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
