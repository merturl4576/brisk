using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Logging;

namespace BriskEngine.Diagnostics;

public sealed record StartupEntry(string Hive, string Name, bool Enabled, bool KnownHeavy);

/// One Store package's startup tasks, grouped so that one row speaks for one
/// app. Name is both the label the GUI shows and the handle SetEnabled
/// resolves, so it is unique across the returned list.
public sealed record StoreStartupApp(
    string Name, string PackageFamilyName, IReadOnlyList<string> TaskKeys, bool Enabled);

/// Owner of the Run/StartupApproved tables and of the Store startup-task table.
/// StartupBloatRule detects/fixes in bulk; this class gives the GUI per-item
/// listing and toggling. Both read every startup source through this class —
/// the same hive table, the same StoreApps rows, the same heavy-app list — so
/// they can never disagree about what starts with Windows.
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
    /// records Task Manager writes. Missing this table made brisk blind to the
    /// second largest boot cost on the maintainer's machine.
    public const string StoreRoot =
        @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";

    public const string StoreHive = "Store";

    /// The State value mirrors WinRT's StartupTaskState: 0 Disabled,
    /// 1 DisabledByUser, 2 Enabled, 3 DisabledByPolicy, 4 EnabledByPolicy —
    /// so three of the five values mean the task does NOT start, and reading
    /// "anything but 0" as enabled would have brisk claim two disabled states
    /// start with Windows.
    ///
    /// Measured on real hardware: only 0 and 2 have ever been observed in this
    /// table here. 1, 3 and 4 are taken from the enum this table mirrors, not
    /// from an observation, and are classified accordingly rather than guessed.
    internal static bool IsEnabledState(int state) => state is 2 or 4;

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
        foreach (var app in StoreApps(_registry))
            items.Add(new StartupEntry(StoreHive, app.Name, app.Enabled, IsHeavy(app.Name)));
        return items;
    }

    /// Every Store package that registers at least one startup task, one row
    /// per package. This is the single reader behind both the GUI's Store rows
    /// and StartupBloatRule's Store items.
    public static IReadOnlyList<StoreStartupApp> StoreApps(IRegistryProbe registry)
    {
        var tasks = StoreTasks(registry).ToList();
        var labels = LabelPackages(
            tasks.Select(t => t.Package).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        var apps = new List<StoreStartupApp>();
        foreach (var group in tasks.GroupBy(t => t.Package, StringComparer.OrdinalIgnoreCase))
            apps.Add(new StoreStartupApp(
                labels[group.Key], group.Key,
                group.Select(t => $@"{StoreRoot}\{t.Package}\{t.Task}").ToList(),
                // The row speaks for the app, so it starts with Windows when
                // any one of its tasks does. Spotify registers two.
                group.Any(t => IsEnabledState(t.State))));
        return apps;
    }

    /// "SpotifyAB.SpotifyMusic_zpdnekdrzrea0" -> "SpotifyMusic".
    /// "MSTeams_8wekyb3d8bbwe" -> "MSTeams".
    internal static string FriendlyPackageName(string packageFamilyName)
    {
        var withoutHash = PublisherQualifiedName(packageFamilyName);
        var dot = withoutHash.LastIndexOf('.');
        return dot >= 0 && dot < withoutHash.Length - 1 ? withoutHash[(dot + 1)..] : withoutHash;
    }

    /// "SpotifyAB.SpotifyMusic_zpdnekdrzrea0" -> "SpotifyAB.SpotifyMusic".
    private static string PublisherQualifiedName(string packageFamilyName) =>
        packageFamilyName.Split('_')[0];

    /// Two packages can shorten to the same name — "Microsoft.Copilot_…" and a
    /// third-party "Contoso.Copilot_…" both give "Copilot". That name is also
    /// the handle SetEnabled resolves, so leaving the duplicate would put two
    /// identical rows on the page and make either one silently toggle both
    /// packages. Colliding rows keep their publisher instead, and where even
    /// that collides — the same package name signed twice, Store-signed beside
    /// sideloaded, or a publisher identity change — they keep the short name
    /// and add the only thing that still differs, the publisher hash.
    ///
    /// Not the raw family name: StartupItemRow binds Name straight to the page,
    /// and a heavy package would carry "SpotifyAB.SpotifyMusic_zpdnekdrzrea0"
    /// into the finding's prose and into the Turkish sentence with it.
    private static Dictionary<string, string> LabelPackages(IReadOnlyList<string> packages)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var byShort in packages.GroupBy(FriendlyPackageName, StringComparer.OrdinalIgnoreCase))
        {
            if (byShort.Count() == 1)
            {
                labels[byShort.First()] = byShort.Key;
                continue;
            }
            foreach (var byPublisher in byShort.GroupBy(PublisherQualifiedName, StringComparer.OrdinalIgnoreCase))
            {
                var unique = byPublisher.Count() == 1;
                foreach (var package in byPublisher)
                    labels[package] = unique ? byPublisher.Key : Hashed(byShort.Key, package);
            }
        }
        return labels;
    }

    /// "SpotifyMusic (zpdnekdrzrea0)". Uniqueness rides on the hash, which is
    /// what distinguishes two packages that agree this far. A family name with
    /// no hash at all is not a shape Windows produces, and falls back to itself
    /// rather than to a label that would collide.
    private static string Hashed(string shortName, string packageFamilyName)
    {
        var underscore = packageFamilyName.LastIndexOf('_');
        return underscore >= 0 && underscore < packageFamilyName.Length - 1
            ? $"{shortName} ({packageFamilyName[(underscore + 1)..]})"
            : packageFamilyName;
    }

    private static IEnumerable<(string Package, string Task, int State)> StoreTasks(
        IRegistryProbe registry)
    {
        foreach (var package in registry.GetSubKeyNames(StoreRoot))
        foreach (var task in registry.GetSubKeyNames($@"{StoreRoot}\{package}"))
        {
            var state = registry.GetInt($@"{StoreRoot}\{package}\{task}", "State");
            // No State value means this subkey is not a startup task — the same
            // parent holds Schemas, SplashScreen, PersistedStorageItemTable and
            // friends, none of which belong on a user's startup list.
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
        // Resolved through the same labelled rows the list was built from, so
        // the name the user clicked addresses exactly one package.
        var app = StoreApps(_registry).FirstOrDefault(
            a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (app is null) return false;
        foreach (var taskKey in app.TaskKeys)
        {
            // Every task moves, including one already at the target value: a
            // package whose tasks disagree still starts if any of them is left
            // enabled.
            try { _registry.SetInt(taskKey, "State", enabled ? 2 : 0); }
            catch (UnauthorizedAccessException) { return false; }
        }
        _log?.Append(new { ts = DateTime.UtcNow, startup = name, hive = StoreHive,
            action = enabled ? "enable" : "disable" });
        return true;
    }
}
