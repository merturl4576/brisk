using System.Collections.Generic;
using System.Linq;

namespace BriskEngine.Actions;

public sealed record RowDef(string Id, ActionGroupKind Group, string Title, string? Cost,
    string? WindowsName, bool DefaultOn, bool NeedsRestart, bool NeedsAdmin, bool Reversible,
    ActionLinkKind LinkKind, IReadOnlyList<string> LinkIds)
{
    public string TitleKey => $"action.{Id}.title";
    public string? CostKey => Cost is null ? null : $"action.{Id}.cost";
    public string? NameKey => WindowsName is null ? null : $"action.{Id}.name";
}

/// Every fixed row. Copy rules (spec): title in everyday words, cost five or
/// six words at most, Windows' own name only when the row is expanded.
public static class ActionCatalog
{
    private static RowDef R(string id, ActionGroupKind g, string title, string? cost, string? name,
        bool on = true, bool restart = false, bool admin = false, bool reversible = true,
        ActionLinkKind kind = ActionLinkKind.Rules, string[]? links = null) =>
        new(id, g, title, cost, name, on, restart, admin, reversible, kind, links ?? System.Array.Empty<string>());

    private const ActionGroupKind Space = ActionGroupKind.Space;
    private const ActionGroupKind Speed = ActionGroupKind.Speed;
    private const ActionGroupKind Privacy = ActionGroupKind.Privacy;
    private const ActionLinkKind Clean = ActionLinkKind.Cleanup;

    public static IReadOnlyList<RowDef> Rows { get; } = new[]
    {
        // ---- Space: buckets. component-store stays last: DISM takes minutes.
        R("space.caches", Space, "Caches and temporary files", null, null, reversible: false, kind: Clean,
            links: new[] { "user-temp", "chrome-cache", "edge-cache", "firefox-cache", "brave-cache",
                "opera-cache", "thumbnail-cache", "discord-cache", "spotify-storage", "teams-cache",
                "slack-cache", "vscode-cache", "whatsapp-cache", "telegram-media-cache", "crash-dumps",
                "wer-reports", "wer-system", "windows-temp", "windows-update-cache",
                "delivery-optimization", "component-store" }),
        R("space.developer", Space, "Developer caches", "Downloads again later.", null,
            reversible: false, kind: Clean,
            links: new[] { "npm-cache", "pip-cache", "yarn-cache", "pnpm-store", "nuget-http-cache",
                "cargo-registry-cache", "gradle-caches" }),
        R("space.shaders", Space, "Game shader caches", "Games may stutter on first launch.", null,
            on: false, reversible: false, kind: Clean,
            links: new[] { "nvidia-shader-cache", "amd-shader-cache", "d3d-shader-cache" }),
        R("space.hibernation", Space, "Hibernation file", "Fast startup turns off.", "hiberfil.sys",
            admin: true, links: new[] { "hibernation" }),
        R("space.windows-old", Space, "Old Windows copy", "No going back to old Windows.", "Windows.old",
            admin: true, reversible: false, kind: Clean, links: new[] { "windows-old" }),
        R("space.downloads", Space, "Installers in Downloads", "Can't be undone.", null,
            reversible: false, kind: Clean, links: new[] { "old-installers" }),
        R("space.docker", Space, "Docker's unused data", "Stopped containers are deleted.", "docker system prune",
            on: false, reversible: false, kind: Clean, links: new[] { "docker-prune" }),
        R("space.recycle-bin", Space, "Recycle Bin", null, null, reversible: false, kind: Clean,
            links: new[] { "empty-recycle-bin" }),

        // ---- Speed: fixed switches (startup entries are added per machine).
        R("speed.power-mode", Speed, "Full power when plugged in", "The fan may spin more.",
            "Power mode: Best performance", admin: true, links: new[] { "power-mode" }),
        R("speed.game-recording", Speed, "Background video recording in games", null,
            "Game DVR background recording", links: new[] { "game-recording" }),
        R("speed.gpu-scheduling", Speed, "Lower lag in games", null,
            "Hardware-accelerated GPU scheduling", restart: true, admin: true, links: new[] { "gpu-scheduling" }),
        R("speed.windowed-games", Speed, "Speed up windowed games", null,
            "Optimizations for windowed games", links: new[] { "windowed-games" }),
        R("speed.animations", Speed, "Window opening animations", "Windows open instantly.",
            "Animate windows when minimizing and maximizing", links: new[] { "window-animations" }),
        R("speed.memory-integrity", Speed, "Extra kernel protection", "Security drops one level.",
            "Memory integrity", on: false, restart: true, admin: true, links: new[] { "memory-integrity" }),
        R("speed.vm-platform", Speed, "Virtual machine support", "WSL and Docker stop working.",
            "Virtual Machine Platform", on: false, restart: true, admin: true, links: new[] { "vm-platform" }),

        // ---- Privacy: a ticked box means "stop this".
        R("privacy.telemetry-services", Privacy, "Sending usage data to Microsoft", null,
            "Connected User Experiences and Telemetry", admin: true, links: new[] { "telemetry-services" }),
        R("privacy.telemetry-tasks", Privacy, "Collecting data in the background", null,
            "Telemetry scheduled tasks", admin: true, links: new[] { "telemetry-tasks" }),
        R("privacy.error-reports", Privacy, "Sending error and usage reports", null,
            "Diagnostic data, Windows Error Reporting", admin: true,
            links: new[] { "diagnostic-level", "error-reporting-off" }),
        R("privacy.ads", Privacy, "Showing ads and suggestions for you", null,
            "Advertising ID, tailored experiences", links: new[] { "advertising-id", "tailored-experiences" }),
        R("privacy.activity", Privacy, "Recording the files and sites you open", null,
            "Activity history", admin: true, links: new[] { "activity-history" }),
        R("privacy.start-ads", Privacy, "Ads in the Start menu", null, "Start suggestions",
            links: new[] { "windows-suggestions" }),
        R("privacy.start-web", Privacy, "Going online when you search Start", "No web search in Start.",
            "Bing in Start search", links: new[] { "search-web-results" }),
        R("privacy.speech", Privacy, "Sending your voice and typing", "Windows voice typing stops working.",
            "Online speech recognition, inking and typing", links: new[] { "speech-typing" }),
        R("privacy.feedback", Privacy, "\"How do you like Windows?\" questions", null,
            "Feedback frequency", admin: true, links: new[] { "feedback-off" }),
        R("privacy.recall", Privacy, "Saving pictures of your screen", null, "Recall", links: new[] { "recall-off" }),
        R("privacy.copilot", Privacy, "Copilot", "Copilot turns off.", "Windows Copilot", links: new[] { "copilot-off" }),
        R("privacy.delivery-peers", Privacy, "Sharing updates with strangers", null,
            "Delivery Optimization", admin: true, links: new[] { "delivery-peers-off" }),
        R("privacy.location", Privacy, "Sharing your location with apps", "Find my device stops working.",
            "Location", on: false, links: new[] { "location" }),
        R("privacy.uac", Privacy, "Programs becoming admin without asking", "Programs ask for permission again.",
            "User Account Control", restart: true, admin: true, links: new[] { "uac-prompts" }),
        R("privacy.firewall", Privacy, "The firewall being off", null, "Windows Firewall",
            admin: true, links: new[] { "firewall-on" }),
        R("privacy.defender-realtime", Privacy, "Virus protection being off", null, "Real-time protection",
            admin: true, links: new[] { "defender-realtime" }),
    };

    public static RowDef Find(string id) => Rows.First(r => r.Id == id);

    public const string HibernationLaptopCost = "Open work lost if battery dies.";
    public const string HibernationLaptopCostKey = "action.space.hibernation.cost-laptop";
    public const string StartupName = "Startup apps (Task Manager)";
    public const string StartupNameKey = "action.speed.startup.name";

    public static (string Key, string English) StartupCost(StartupProtectionKind kind) => kind switch
    {
        StartupProtectionKind.Security => ("action.speed.startup.cost.security", "Protection won't start at boot."),
        StartupProtectionKind.Hardware => ("action.speed.startup.cost.hardware", "Hardware settings may not work."),
        StartupProtectionKind.Sync => ("action.speed.startup.cost.sync", "File sync stops."),
        StartupProtectionKind.Passwords => ("action.speed.startup.cost.passwords", "Password manager won't open."),
        StartupProtectionKind.Vpn => ("action.speed.startup.cost.vpn", "VPN won't connect automatically."),
        StartupProtectionKind.Keyboard => ("action.speed.startup.cost.keyboard", "Your shortcuts won't work."),
        _ => ("", ""),
    };

    /// Every localization key the engine hands out — the resx test in
    /// Brisk.Tests checks both languages carry each one.
    public static IEnumerable<string> AllKeys()
    {
        foreach (var r in Rows)
        {
            yield return r.TitleKey;
            if (r.CostKey is not null) yield return r.CostKey;
            if (r.NameKey is not null) yield return r.NameKey;
        }
        yield return HibernationLaptopCostKey;
        yield return StartupNameKey;
        foreach (var kind in System.Enum.GetValues<StartupProtectionKind>())
            if (kind != StartupProtectionKind.None) yield return StartupCost(kind).Key;
        foreach (var key in ActionStrings.Ui.Keys) yield return key;
    }
}

/// Fixed interface words the engine's callers print: group names, results,
/// footers, receipt lines, load notes. English here; Turkish in the resx.
public static class ActionStrings
{
    public static IReadOnlyDictionary<string, string> Ui { get; } = new Dictionary<string, string>
    {
        ["action.group.space"] = "Free up space",
        ["action.group.speed"] = "Speed up",
        ["action.group.privacy"] = "Privacy",
        ["action.group.load"] = "Slowing you down now",
        ["action.result.done"] = "Done ✓",
        ["action.result.off"] = "Off ✓",
        ["action.result.ignored"] = "Windows ignored it",
        ["action.result.reverted"] = "Turned back on",
        ["action.result.written"] = "Written",
        ["action.result.pending"] = "After restart",
        ["action.result.skipped"] = "Skipped",
        ["action.result.failed"] = "Didn't work",
        ["action.result.needs-admin"] = "Needs administrator",
        ["action.result.deferred"] = "Cleans when {0} closes",
        ["action.result.cannot-undo"] = "Can't be undone",
        ["action.connections.footer"] = "A connection doesn't mean data was sent.",
        ["action.connections.local"] = "Local network",
        ["action.connections.unknown"] = "Unknown",
        ["action.connections.telemetry"] = "Telemetry",
        ["action.usage.footer"] = "Amounts only, no contents.",
        ["action.usage.windows"] = "Windows (system)",
        ["action.unread"] = "Couldn't read.",
        ["action.load.close"] = "Close",
        ["action.load.force"] = "Force close",
        ["action.load.remove-startup"] = "Remove from startup",
        ["action.load.note.defender"] = "Defender is scanning",
        ["action.load.note.update"] = "Windows is installing updates",
        ["action.load.note.search"] = "Windows is indexing for search",
        ["action.load.note.memory"] = "Windows is compressing memory",
        ["action.load.note.compat"] = "Windows is collecting compatibility data",
        ["action.load.note.dwm"] = "Windows is drawing the screen",
        ["action.load.note.system"] = "Windows itself",
        ["action.load.note.services"] = "Windows services",
        ["action.receipt.freed"] = "{0} freed",
        ["action.receipt.deferred"] = "{0} more when {1} closes",
        ["action.receipt.boot"] = "Startup {0} s → {1} s",
        ["action.receipt.startup"] = "Startup apps {0} → {1}",
        ["action.receipt.ram"] = "{0} RAM freed",
        ["action.receipt.privacy"] = "{0} settings off · Windows ignored {1}",
        ["action.receipt.telemetry"] = "Telemetry connections {0} → {1}",
        ["action.restart"] = "Restart for full effect",
        ["action.undo"] = "Undo",
    };

    public static string En(string key) => Ui.TryGetValue(key, out var s) ? s : key;
}
