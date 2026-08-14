using System;
using System.Collections.Generic;
using System.IO;

namespace BriskEngine.Paths;

public static class TemplateResolver
{
    /// Expands env vars, then any '*' wildcards against the real filesystem.
    /// Returns only paths that exist right now. Used by BOTH the scanner and
    /// the validator so the two can never disagree about what a template means.
    public static IReadOnlyList<string> Resolve(string template)
    {
        var expanded = PathExpander.Expand(template);
        if (expanded is null) return Array.Empty<string>();
        return Glob(Path.GetFullPath(expanded));
    }

    private static IReadOnlyList<string> Glob(string path)
    {
        var star = path.IndexOf('*');
        if (star < 0)
            return File.Exists(path) || Directory.Exists(path)
                ? new[] { path }
                : Array.Empty<string>();

        var sepBefore = path.LastIndexOf('\\', star);
        var sepAfter = path.IndexOf('\\', star);
        var parent = path[..sepBefore];
        var pattern = sepAfter < 0 ? path[(sepBefore + 1)..] : path[(sepBefore + 1)..sepAfter];
        var rest = sepAfter < 0 ? null : path[(sepAfter + 1)..];
        if (!Directory.Exists(parent)) return Array.Empty<string>();

        var results = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(parent, pattern))
        {
            if (rest is null) results.Add(entry);
            else results.AddRange(Glob(entry + '\\' + rest));
        }
        return results;
    }
}
