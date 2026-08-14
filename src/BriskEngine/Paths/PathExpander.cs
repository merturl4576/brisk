using System;

namespace BriskEngine.Paths;

public static class PathExpander
{
    /// Expands %VAR% and a leading "~" to the user profile.
    /// Returns null when a referenced environment variable is undefined,
    /// so callers can skip templates that do not apply on this machine.
    public static string? Expand(string template)
    {
        var work = template.StartsWith('~')
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + template[1..]
            : template;
        var expanded = Environment.ExpandEnvironmentVariables(work);
        return expanded.Contains('%') ? null : expanded;
    }
}
