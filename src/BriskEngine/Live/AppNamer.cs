using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BriskEngine.Live;

public interface IAppNamer
{
    string Name(string processOrExeName, string? fullPath);
}

/// The file's own description ("Google Chrome") when the file can be read,
/// else the process name with a capital first letter.
public sealed class RealAppNamer : IAppNamer
{
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string Name(string processOrExeName, string? fullPath)
    {
        var fallback = Pretty(Stem(processOrExeName));
        if (fullPath is null) return fallback;
        return _cache.GetOrAdd(fullPath, path => FileDescription.Of(path) ?? fallback);
    }

    internal static string Stem(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    internal static string Pretty(string stem) =>
        stem.Length == 0 ? stem : char.ToUpperInvariant(stem[0]) + stem[1..];
}

/// A program file's own description ("Google Chrome"); null when it has none
/// or cannot be read.
public static class FileDescription
{
    public static string? Of(string path)
    {
        try
        {
            var d = FileVersionInfo.GetVersionInfo(DevicePath.ToDos(path) ?? path).FileDescription;
            return string.IsNullOrWhiteSpace(d) ? null : d.Trim();
        }
        catch (Exception) { return null; }
    }
}

/// "\device\harddiskvolume3\x" → "C:\x" (the form Windows' usage record uses).
public static class DevicePath
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string deviceName, StringBuilder target, int max);

    public static string? ToDos(string path)
    {
        if (!path.StartsWith(@"\device\", StringComparison.OrdinalIgnoreCase)) return path;
        foreach (var drive in Environment.GetLogicalDrives())
        {
            var letter = drive.TrimEnd('\\');
            var target = new StringBuilder(260);
            if (QueryDosDevice(letter, target, target.Capacity) == 0) continue;
            var device = target.ToString();
            if (path.StartsWith(device + @"\", StringComparison.OrdinalIgnoreCase))
                return letter + path[device.Length..];
        }
        return null;
    }
}
