using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace BriskEngine.Diagnostics.RealProbes;

public sealed class RealRegistryProbe : IRegistryProbe
{
    private static (RegistryKey Root, string SubPath) Split(string keyPath)
    {
        var sep = keyPath.IndexOf('\\');
        var root = keyPath[..sep] switch
        {
            "HKCU" => Registry.CurrentUser,
            "HKLM" => Registry.LocalMachine,
            _ => throw new ArgumentException($"Unsupported hive in '{keyPath}'"),
        };
        return (root, keyPath[(sep + 1)..]);
    }

    private static T? Read<T>(string keyPath, string valueName) where T : class
    {
        var (root, sub) = Split(keyPath);
        using var key = root.OpenSubKey(sub);
        return key?.GetValue(valueName) as T;
    }

    private static void Write(string keyPath, string valueName, object value, RegistryValueKind kind)
    {
        var (root, sub) = Split(keyPath);
        using var key = root.CreateSubKey(sub, writable: true);
        key.SetValue(valueName, value, kind);
    }

    public string? GetString(string k, string v) => Read<string>(k, v);
    public void SetString(string k, string v, string value) => Write(k, v, value, RegistryValueKind.String);
    public byte[]? GetBytes(string k, string v) => Read<byte[]>(k, v);
    public void SetBytes(string k, string v, byte[] value) => Write(k, v, value, RegistryValueKind.Binary);
    public int? GetInt(string k, string v)
    {
        var (root, sub) = Split(k);
        using var key = root.OpenSubKey(sub);
        return key?.GetValue(v) as int?;
    }
    public void SetInt(string k, string v, int value) => Write(k, v, value, RegistryValueKind.DWord);

    public void DeleteValue(string keyPath, string valueName)
    {
        var (root, sub) = Split(keyPath);
        using var key = root.OpenSubKey(sub, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    public IReadOnlyList<string> GetValueNames(string keyPath)
    {
        var (root, sub) = Split(keyPath);
        using var key = root.OpenSubKey(sub);
        return key?.GetValueNames() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> GetSubKeyNames(string keyPath)
    {
        var (root, sub) = Split(keyPath);
        using var key = root.OpenSubKey(sub);
        return key?.GetSubKeyNames() ?? Array.Empty<string>();
    }
}
