using System.Collections.Generic;
using BriskEngine.Models;

namespace BriskEngine.Cleaning;

public static class CleanupTargetRegistry
{
    private static CleanupTarget T(string id, string name, CleanupLevel level,
        string category, string[] paths, bool contents = false, bool regen = false,
        string? app = null, bool pick = false, bool optIn = false,
        bool noBin = false, bool admin = false) =>
        new(id, name, level, paths, category, contents, regen, app, pick, optIn, noBin, admin);

    public static readonly IReadOnlyList<CleanupTarget> All = new List<CleanupTarget>
    {
        // ---- Safe: regenerates on its own, no elevation, zero functional impact
        T("user-temp", "User temp files", CleanupLevel.Safe, "System",
            new[] { @"%TEMP%" }, contents: true, regen: true),
        T("chrome-cache", "Chrome cache", CleanupLevel.Safe, "Browser",
            new[] { @"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Cache",
                    @"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Code Cache",
                    @"%LOCALAPPDATA%\Google\Chrome\User Data\Default\GPUCache" },
            contents: true, regen: true, app: "chrome"),
        T("edge-cache", "Edge cache", CleanupLevel.Safe, "Browser",
            new[] { @"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Cache",
                    @"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Code Cache",
                    @"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\GPUCache" },
            contents: true, regen: true, app: "msedge"),
        T("firefox-cache", "Firefox cache", CleanupLevel.Safe, "Browser",
            new[] { @"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\cache2" },
            contents: true, regen: true, app: "firefox"),
        T("brave-cache", "Brave cache", CleanupLevel.Safe, "Browser",
            new[] { @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\Default\Cache",
                    @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\Default\Code Cache",
                    @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\Default\GPUCache" },
            contents: true, regen: true, app: "brave"),
        T("opera-cache", "Opera cache", CleanupLevel.Safe, "Browser",
            new[] { @"%LOCALAPPDATA%\Opera Software\Opera Stable\Cache" },
            contents: true, regen: true, app: "opera"),
        T("thumbnail-cache", "Explorer thumbnail cache", CleanupLevel.Safe, "System",
            new[] { @"%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db" },
            regen: true),
        T("discord-cache", "Discord cache", CleanupLevel.Safe, "App",
            new[] { @"%APPDATA%\discord\Cache", @"%APPDATA%\discord\Code Cache" },
            contents: true, regen: true, app: "Discord"),
        T("spotify-storage", "Spotify cache", CleanupLevel.Safe, "App",
            new[] { @"%LOCALAPPDATA%\Spotify\Storage" },
            contents: true, regen: true, app: "Spotify"),
        T("teams-cache", "Microsoft Teams cache", CleanupLevel.Safe, "App",
            new[] { @"%APPDATA%\Microsoft\Teams\Cache",
                    @"%LOCALAPPDATA%\Packages\MSTeams_8wekyb3d8bbwe\LocalCache" },
            contents: true, regen: true, app: "ms-teams"),
        T("slack-cache", "Slack cache", CleanupLevel.Safe, "App",
            new[] { @"%APPDATA%\Slack\Cache", @"%APPDATA%\Slack\Code Cache",
                    @"%APPDATA%\Slack\GPUCache" },
            contents: true, regen: true, app: "slack"),
        T("vscode-cache", "VS Code cache", CleanupLevel.Safe, "App",
            new[] { @"%APPDATA%\Code\Cache", @"%APPDATA%\Code\CachedData",
                    @"%APPDATA%\Code\Code Cache" },
            contents: true, regen: true, app: "Code"),
        // Modern WhatsApp Desktop's root process is "WhatsApp.Root", not
        // "WhatsApp" — with only the latter, the running-app exclusion never
        // fired and a locked 310 MB WebView2 profile dir landed in the
        // promise (the 2026-08-17 live incident). Both names count.
        T("whatsapp-cache", "WhatsApp cache", CleanupLevel.Safe, "App",
            new[] { @"%LOCALAPPDATA%\Packages\5319275A.WhatsAppDesktop_cv1g1gvanyjgm\LocalCache" },
            contents: true, regen: true, app: "WhatsApp|WhatsApp.Root"),
        T("telegram-media-cache", "Telegram media cache", CleanupLevel.Safe, "App",
            new[] { @"%APPDATA%\Telegram Desktop\tdata\user_data\media_cache" },
            contents: true, regen: true, app: "Telegram"),
        T("crash-dumps", "Crash dumps", CleanupLevel.Safe, "System",
            new[] { @"%LOCALAPPDATA%\CrashDumps" }, contents: true, regen: true),
        T("wer-reports", "Windows Error Reporting queues", CleanupLevel.Safe, "System",
            new[] { @"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportQueue",
                    @"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive" },
            contents: true, regen: true),

        // ---- Developer: re-downloads or rebuilds on demand
        T("npm-cache", "npm cache", CleanupLevel.Developer, "Package Manager",
            new[] { @"%LOCALAPPDATA%\npm-cache" }, contents: true, regen: true),
        T("pip-cache", "pip cache", CleanupLevel.Developer, "Package Manager",
            new[] { @"%LOCALAPPDATA%\pip\cache" }, contents: true, regen: true),
        T("yarn-cache", "Yarn cache", CleanupLevel.Developer, "Package Manager",
            new[] { @"%LOCALAPPDATA%\Yarn\Cache" }, contents: true, regen: true),
        T("pnpm-store", "pnpm store", CleanupLevel.Developer, "Package Manager",
            new[] { @"%LOCALAPPDATA%\pnpm\store" }, contents: true, regen: true),
        T("nuget-http-cache", "NuGet HTTP cache", CleanupLevel.Developer, "Package Manager",
            new[] { @"%LOCALAPPDATA%\NuGet\v3-cache" }, contents: true, regen: true),
        T("cargo-registry-cache", "Cargo registry cache", CleanupLevel.Developer, "Package Manager",
            new[] { @"~\.cargo\registry\cache" }, contents: true, regen: true),
        T("gradle-caches", "Gradle caches", CleanupLevel.Developer, "Package Manager",
            new[] { @"~\.gradle\caches" }, contents: true, regen: true),
        T("docker-prune", "Docker unused data (docker system prune)", CleanupLevel.Developer,
            "Container", System.Array.Empty<string>(), optIn: true),

        // ---- Deep: look before you leap
        T("windows-temp", "Windows temp", CleanupLevel.Deep, "System",
            new[] { @"%SystemRoot%\Temp" }, contents: true, regen: true, admin: true),
        T("windows-update-cache", "Windows Update download cache", CleanupLevel.Deep, "System",
            new[] { @"%SystemRoot%\SoftwareDistribution\Download" },
            contents: true, regen: true, admin: true),
        // Shares its id string with DeliveryOptimizationRule, which reports
        // how much this machine UPLOADED. Separate registries, so nothing
        // collides. REASONED, NOT OBSERVED: that rule's number is a monthly
        // total of bytes already sent to peers, so deleting cached files
        // should not change it — nobody here has emptied this cache and
        // re-read the counter to confirm.
        T("delivery-optimization", "Delivery Optimization cache", CleanupLevel.Deep, "System",
            new[] { @"%SystemRoot%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache" },
            contents: true, regen: true, admin: true),
        // ---- The heavy system trio (2026-08-30): gigabytes each, and each a
        // decision — explicit opt-in, administrator, and (where a real path
        // exists) past the recycle bin, because a 30 GB Windows.old does not
        // fit in one and pretending otherwise poisons undo.
        T("windows-old", "Windows.old (previous Windows installation)", CleanupLevel.Deep, "System",
            new[] { @"%SystemDrive%\Windows.old" }, optIn: true, noBin: true, admin: true),
        T("hibernation-file", "Hibernation file (hiberfil.sys)", CleanupLevel.Deep, "System",
            new[] { @"%SystemDrive%\hiberfil.sys" }, optIn: true, noBin: true, admin: true),
        T("component-store", "Windows component store (superseded updates)", CleanupLevel.Deep, "System",
            System.Array.Empty<string>(), optIn: true, admin: true),
        T("old-installers", "Old installers in Downloads", CleanupLevel.Deep, "Downloads",
            new[] { @"%USERPROFILE%\Downloads\*.exe", @"%USERPROFILE%\Downloads\*.msi",
                    @"%USERPROFILE%\Downloads\*.iso" },
            pick: true),
        T("empty-recycle-bin", "Empty Recycle Bin", CleanupLevel.Deep, "System",
            System.Array.Empty<string>(), noBin: true, pick: true),
    };
}
