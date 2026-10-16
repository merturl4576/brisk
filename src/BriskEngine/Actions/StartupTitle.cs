using System;
using System.Linq;
using System.Text.RegularExpressions;
using BriskEngine.Diagnostics;

namespace BriskEngine.Actions;

/// The name a startup row shows: the program's own description
/// ("Microsoft Edge"), else the entry's name without a machine-made tail
/// ("MicrosoftEdgeAutoLaunch_F6C5…" loses the "_F6C5…").
public static class StartupTitle
{
    private static readonly string[] Generic =
        { "Update", "Updater", "Launcher", "Helper", "Application", "Electron" };

    // Only after a separator, so a name made of hex letters keeps itself.
    private static readonly Regex IdTail = new(
        @"[_ ](\{?[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}?|[0-9A-Fa-f]{8,})$");

    public static string For(StartupEntry e, Func<string, string?> describe)
    {
        if (StartupManager.ExecutablePath(e.Command) is { } path
            && describe(path) is { } d && !string.IsNullOrWhiteSpace(d)
            && !Generic.Contains(d.Trim(), StringComparer.OrdinalIgnoreCase))
            return d.Trim();
        return Clean(e.Name);
    }

    internal static string Clean(string name)
    {
        var n = name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        var cut = IdTail.Replace(n, "");
        return cut.Length == 0 ? n : cut;
    }
}
