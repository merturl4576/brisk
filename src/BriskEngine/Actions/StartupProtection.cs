using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BriskEngine.Actions;

public enum StartupProtectionKind { None, Security, Hardware, Sync, Passwords, Vpn, Keyboard }

/// Startup entries that break something the user would notice when they stop
/// starting. Those stay unticked, each with its cost line. Words of four
/// letters or fewer match as whole words, so "IME" never matches "Time".
public static class StartupProtection
{
    private static readonly (StartupProtectionKind Kind, string[] Words)[] Table =
    {
        (StartupProtectionKind.Security, new[] { "SecurityHealth", "Windows Security", "McAfee", "Norton",
            "Avast", "AVG", "Bitdefender", "Kaspersky", "ESET", "Malwarebytes", "Sophos", "Trend Micro",
            "Avira", "Webroot", "F-Secure" }),
        (StartupProtectionKind.Hardware, new[] { "Realtek", "RtkAud", "Synaptics", "SynTP", "ELAN",
            "ETDCtrl", "igfx", "IntelGraphics", "Intel Graphics", "IGCCTray", "AMD", "Radeon", "NVIDIA", "Nahimic", "Dolby", "Waves", "MaxxAudio",
            "Conexant", "Bluetooth", "Logitech", "Razer", "Corsair", "SteelSeries", "Armoury", "ASUS",
            "Lenovo Vantage", "Dell", "HP" }),
        (StartupProtectionKind.Sync, new[] { "OneDrive", "Google Drive", "GoogleDrive", "Dropbox", "iCloud",
            "MEGAsync", "pCloud", "Box Drive", "Nextcloud", "Syncthing" }),
        (StartupProtectionKind.Passwords, new[] { "1Password", "Bitwarden", "KeePass", "LastPass",
            "Dashlane", "Enpass", "Proton Pass" }),
        (StartupProtectionKind.Vpn, new[] { "NordVPN", "ProtonVPN", "Proton VPN", "ExpressVPN", "Mullvad",
            "WireGuard", "OpenVPN", "Surfshark", "AnyConnect", "GlobalProtect", "Windscribe", "Tailscale",
            "ZeroTier" }),
        (StartupProtectionKind.Keyboard, new[] { "PowerToys", "AutoHotkey", "AHK", "ctfmon", "IME",
            "WinCompose", "Keyboard" }),
    };

    private static bool Matches(string haystack, string word) => word.Length <= 4
        ? Regex.IsMatch(haystack, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase)
        : haystack.Contains(word, StringComparison.OrdinalIgnoreCase);

    public static StartupProtectionKind Classify(string name, string? command,
        IReadOnlyCollection<string> antivirusNames)
    {
        var haystack = $"{name} {command}";
        foreach (var av in antivirusNames)
        {
            var first = av.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (first is { Length: > 2 } && Matches(haystack, first)) return StartupProtectionKind.Security;
        }
        foreach (var (kind, words) in Table)
            if (words.Any(w => Matches(haystack, w))) return kind;
        return StartupProtectionKind.None;
    }
}
