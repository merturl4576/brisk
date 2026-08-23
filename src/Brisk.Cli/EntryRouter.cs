using System;
using System.Collections.Generic;

namespace Brisk.Cli;

/// brisk ships as one file. That file has to be both things it has always
/// been: the window people double-click and the command people type. This
/// decides which one an invocation meant.
///
/// The rule is deliberately asymmetric. A switch brisk's console does not
/// claim belongs to the window — Windows passes switches of its own to GUI
/// processes it restarts, and brisk's own autostart passes "--tray". A word
/// that is not a switch belongs to the console even when it is nonsense,
/// because the console can say "unknown command 'scna'" and a window cannot.
public static class EntryRouter
{
    /// Switch spellings of the two verbs people try before reading anything.
    /// The parser knows verbs only, so these are translated rather than
    /// passed through — see Normalize.
    private static readonly Dictionary<string, string[]> SwitchVerbs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["--help"] = new string[0],
            ["-h"] = new string[0],
            ["-?"] = new string[0],
            ["/?"] = new string[0],
            ["--version"] = new[] { "version" },
            ["-v"] = new[] { "version" },
        };

    public static bool RoutesToConsole(string[] args)
    {
        if (args.Length == 0) return false;
        var first = args[0];
        if (first.Length == 0) return false;
        if (SwitchVerbs.ContainsKey(first)) return true;
        return first[0] is not ('-' or '/');
    }

    /// Rewrites a help/version switch into the verb the parser understands and
    /// leaves every real command line exactly as it was.
    public static string[] Normalize(string[] args) =>
        args.Length > 0 && SwitchVerbs.TryGetValue(args[0], out var verb) ? verb : args;
}
