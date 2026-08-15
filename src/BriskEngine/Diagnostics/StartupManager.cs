using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Logging;

namespace BriskEngine.Diagnostics;

public sealed record StartupEntry(string Hive, string Name, bool Enabled, bool KnownHeavy);

/// Owner of the Run/StartupApproved tables. StartupBloatRule detects/fixes in
/// bulk; this class gives the GUI per-item listing and toggling. Both share
/// the same hive table and heavy-app list so they can never disagree.
public sealed class StartupManager
{
    public static readonly IReadOnlySet<string> KnownHeavy = new HashSet<string>(
        new[] { "Steam", "Discord", "Spotify", "Docker Desktop", "EpicGamesLauncher",
                "WhatsApp", "Teams", "BlueStacks", "WallpaperEngine" },
        StringComparer.OrdinalIgnoreCase);

    internal static readonly (string Hive, string Run, string Approved)[] Hives =
    {
        ("HKCU", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                 @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
        ("HKLM", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                 @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
    };

    public static bool IsHeavy(string name) => KnownHeavy.Any(h =>
        name.Contains(h, StringComparison.OrdinalIgnoreCase));

    private static readonly byte[] DisabledBytes = { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    private static readonly byte[] EnabledBytes = { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    private readonly IRegistryProbe _registry;
    private readonly ActionLog? _log;

    public StartupManager(IRegistryProbe registry, ActionLog? log)
    {
        _registry = registry;
        _log = log;
    }

    public IReadOnlyList<StartupEntry> List()
    {
        var items = new List<StartupEntry>();
        foreach (var (hive, run, approved) in Hives)
        foreach (var name in _registry.GetValueNames(run))
        {
            var bytes = _registry.GetBytes(approved, name);
            var disabled = bytes is { Length: > 0 } && (bytes[0] & 1) == 1;
            items.Add(new StartupEntry(hive, name, !disabled, IsHeavy(name)));
        }
        return items;
    }

    /// Returns false when the hive denies the write (HKLM without elevation).
    public bool SetEnabled(string hive, string name, bool enabled)
    {
        var approved = Hives.FirstOrDefault(h => h.Hive == hive).Approved;
        if (approved is null) return false;
        try
        {
            _registry.SetBytes(approved, name, enabled ? EnabledBytes : DisabledBytes);
        }
        catch (UnauthorizedAccessException) { return false; }
        _log?.Append(new { ts = DateTime.UtcNow, startup = name, hive,
            action = enabled ? "enable" : "disable" });
        return true;
    }
}
