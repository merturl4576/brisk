using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Logging;

namespace BriskEngine.Diagnostics;

public sealed record StartupEntry(string Hive, string Name, bool Enabled, bool KnownHeavy,
    string? Command = null);

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

    /// The two Startup folders Task Manager lists, and the StartupApproved
    /// key it disables their shortcuts under (value name = shortcut file name).
    internal static readonly (string Hive, string Folder, string Approved)[] Folders =
    {
        ("Folder", @"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup",
                   @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"),
        ("FolderAll", @"%ProgramData%\Microsoft\Windows\Start Menu\Programs\StartUp",
                      @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"),
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

    /// The measured half of "heavy". The known list above is nine names
    /// brisk has heard of; a laptop whose fourteen startup programs are none
    /// of them read as nothing to do, while Windows' own boot log (event ID
    /// 101) named, per boot, exactly which programs were late. brisk already
    /// reads that log for the boot rule. From here it acts on it: a Run
    /// entry whose executable Windows put at least BlamedMinMs late on at
    /// least BlamedBoots of the last SampledBoots boots is heavy, on the same
    /// terms as a known name. One late start is a one-off; two is a habit.
    public const int BlamedBoots = 2;
    public const int BlamedMinMs = 5_000;
    public const int SampledBoots = 8;

    /// Never disabled on Windows' word alone. OneDrive not starting means
    /// files silently not syncing, and Security Health is Windows' own
    /// tray; neither loss would be traced back to a brisk fix by the person
    /// living with it. A known-list name is unaffected — none is here.
    internal static readonly IReadOnlySet<string> NeverBlamed = new HashSet<string>(
        new[] { "OneDrive", "SecurityHealth" }, StringComparer.OrdinalIgnoreCase);

    /// Executable name → how many of the sampled boots blamed it. Keyed
    /// case-insensitively, because the registry's command and the event's
    /// FileName do not agree on case.
    public static IReadOnlyDictionary<string, int> BlamedExes(IEventLogProbe log) =>
        BlamedExes(log.RecentBoots(SampledBoots));

    /// Over boots already read: the startup rule reads the channel once
    /// and uses the same records for the blame and for the "of N boots"
    /// in its sentence. The CLI has no per-scan cache in front of the log.
    public static IReadOnlyDictionary<string, int> BlamedExes(IReadOnlyList<BootRecord> boots)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var boot in boots)
        foreach (var name in boot.Offenders
                     .Where(o => o.DegradationMs >= BlamedMinMs)
                     .Select(o => o.Name)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            counts[name] = counts.GetValueOrDefault(name) + 1;
        foreach (var name in counts.Where(kv => kv.Value < BlamedBoots).Select(kv => kv.Key).ToList())
            counts.Remove(name);
        return counts;
    }

    /// Heavy by name, or heavy by Windows' blame of the program the Run
    /// command starts. Store apps have no command and keep the name match.
    public static bool IsHeavy(string name, string? command,
        IReadOnlyDictionary<string, int> blamed)
    {
        if (IsHeavy(name)) return true;
        var exe = ExecutableName(command);
        if (exe is null || !blamed.ContainsKey(exe)) return false;
        return !NeverBlamed.Any(n =>
            name.Contains(n, StringComparison.OrdinalIgnoreCase)
            || exe.Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    /// The file name of the program a Run command starts: everything up to
    /// the first ".exe", quoted or not, then its last path segment. Run
    /// commands come in every shape — quoted paths, unquoted paths with
    /// spaces, trailing switches — and the one thing they share is the
    /// extension. No ".exe" means no name rather than a guess.
    internal static string? ExecutableName(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var end = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (end < 0) return null;
        var path = command[..(end + 4)].TrimStart('"', ' ');
        var cut = path.LastIndexOfAny(new[] { '\\', '/' });
        return cut < 0 ? path : path[(cut + 1)..];
    }

    /// The full path of the program a Run command starts, variables expanded;
    /// null when the command names no ".exe".
    internal static string? ExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var end = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (end < 0) return null;
        return Environment.ExpandEnvironmentVariables(command[..(end + 4)].TrimStart('"', ' '));
    }

    private static readonly byte[] DisabledBytes ={ 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    private static readonly byte[] EnabledBytes = { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    private readonly IRegistryProbe _registry;
    private readonly ActionLog? _log;
    private readonly IEventLogProbe? _eventLog;
    // Read once per process: a boot record only ever appears after a boot,
    // and brisk does not survive one. Without this every HasWork check on
    // the fix-all button would reopen the Diagnostics-Performance channel.
    private IReadOnlyDictionary<string, int>? _blamed;
    private readonly IFileProbe? _files;

    public StartupManager(IRegistryProbe registry, ActionLog? log,
        IEventLogProbe? eventLog = null, IFileProbe? files = null)
    {
        _registry = registry;
        _log = log;
        _eventLog = eventLog;
        _files = files;
    }

    private IReadOnlyDictionary<string, int> Blamed() =>
        _blamed ??= _eventLog is null
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : BlamedExes(_eventLog);

    public IReadOnlyList<StartupEntry> List()
    {
        var items = new List<StartupEntry>();
        foreach (var (hive, run, approved) in Hives)
        foreach (var name in _registry.GetValueNames(run))
        {
            var bytes = _registry.GetBytes(approved, name);
            var disabled = bytes is { Length: > 0 } && (bytes[0] & 1) == 1;
            var command = _registry.GetString(run, name);
            items.Add(new StartupEntry(hive, name, !disabled,
                IsHeavy(name, command, Blamed()), command));
        }
        if (_files is not null)
        foreach (var (hive, folder, approved) in Folders)
        {
            var dir = Paths.PathExpander.Expand(folder);
            if (dir is null) continue;
            foreach (var file in _files.ListFiles(dir))
            {
                var name = System.IO.Path.GetFileName(file);
                if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                var bytes = _registry.GetBytes(approved, name);
                var disabled = bytes is { Length: > 0 } && (bytes[0] & 1) == 1;
                items.Add(new StartupEntry(hive, name, !disabled, IsHeavy(name), file));
            }
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
        var approved = Hives.FirstOrDefault(h => string.Equals(h.Hive, hive, StringComparison.OrdinalIgnoreCase)).Approved
            ?? Folders.FirstOrDefault(f => string.Equals(f.Hive, hive, StringComparison.OrdinalIgnoreCase)).Approved;
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
