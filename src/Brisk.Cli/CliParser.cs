using System;
using System.Collections.Generic;

namespace Brisk.Cli;

public sealed record CliCommand(string Verb, string? RuleId = null, string? Level = null,
    bool Json = false, bool Yes = false, bool All = false, bool Undo = false, string? Error = null);

public static class CliParser
{
    private static readonly HashSet<string> Verbs =
        new() { "scan", "fix", "clean", "targets", "rules", "version" };
    private static readonly HashSet<string> Levels = new() { "safe", "developer", "deep" };

    public static CliCommand Parse(string[] args)
    {
        if (args.Length == 0) return new CliCommand("help");
        var verb = args[0];
        if (!Verbs.Contains(verb))
            return new CliCommand("error", Error: $"unknown command '{verb}'");

        var cmd = new CliCommand(verb);
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json": cmd = cmd with { Json = true }; break;
                case "--yes": cmd = cmd with { Yes = true }; break;
                case "--all": cmd = cmd with { All = true }; break;
                case "--undo": cmd = cmd with { Undo = true }; break;
                case "--rule" when i + 1 < args.Length:
                    cmd = cmd with { RuleId = args[++i] }; break;
                case "--level" when i + 1 < args.Length && Levels.Contains(args[i + 1]):
                    cmd = cmd with { Level = args[++i] }; break;
                default:
                    return new CliCommand("error", Error: $"bad argument '{args[i]}'");
            }
        }
        return cmd;
    }
}
