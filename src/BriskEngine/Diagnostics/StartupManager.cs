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

    /// Store apps register startup tasks here rather than under Run — the same
    /// records Task Manager writes. State 2 is enabled (by the user), 1 enabled
    /// by default, 0 disabled. Missing this table made brisk blind to the second
    /// largest boot cost on the maintainer's machine.
    public const string StoreRoot =
        @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";

    public const string StoreHive = "Store";

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
        foreach (var group in StoreTasks().GroupBy(t => t.Package))
        {
            var name = FriendlyPackageName(group.Key);
            // A package can register several tasks; the row speaks for the app,
            // so it reads as enabled when any of them is.
            var enabled = group.Any(t => t.State != 0);
            items.Add(new StartupEntry(StoreHive, name, enabled, IsHeavy(name)));
        }
        return items;
    }

    /// "SpotifyAB.SpotifyMusic_zpdnekdrzrea0" -> "SpotifyMusic".
    /// "MSTeams_8wekyb3d8bbwe" -> "MSTeams".
    internal static string FriendlyPackageName(string packageFamilyName)
    {
        var withoutHash = packageFamilyName.Split('_')[0];
        var dot = withoutHash.LastIndexOf('.');
        return dot >= 0 && dot < withoutHash.Length - 1 ? withoutHash[(dot + 1)..] : withoutHash;
    }

    private IEnumerable<(string Package, string Task, int State)> StoreTasks()
    {
        foreach (var package in _registry.GetSubKeyNames(StoreRoot))
        foreach (var task in _registry.GetSubKeyNames($@"{StoreRoot}\{package}"))
        {
            var state = _registry.GetInt($@"{StoreRoot}\{package}\{task}", "State");
            // No State value means this subkey is not a startup task — the same
            // parent holds Schemas and PersistedStorageItemTable.
            if (state is not null) yield return (package, task, state.Value);
        }
    }

    /// Returns false when the hive denies the write (HKLM without elevation).
    public bool SetEnabled(string hive, string name, bool enabled)
    {
        if (string.Equals(hive, StoreHive, StringComparison.OrdinalIgnoreCase))
            return SetStoreEnabled(name, enabled);
        var approved = Hives.FirstOrDefault(h => string.Equals(h.Hive, hive, StringComparison.OrdinalIgnoreCase)).Approved;
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

    private bool SetStoreEnabled(string name, bool enabled)
    {
        var matched = false;
        foreach (var (package, task, _) in StoreTasks())
        {
            if (!string.Equals(FriendlyPackageName(package), name, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                _registry.SetInt($@"{StoreRoot}\{package}\{task}", "State", enabled ? 2 : 0);
            }
            catch (UnauthorizedAccessException) { return false; }
            matched = true;
        }
        if (!matched) return false;
        _log?.Append(new { ts = DateTime.UtcNow, startup = name, hive = StoreHive,
            action = enabled ? "enable" : "disable" });
        return true;
    }
}
