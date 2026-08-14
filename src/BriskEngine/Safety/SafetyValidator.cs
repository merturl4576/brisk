using System;
using System.IO;
using BriskEngine.Models;
using BriskEngine.Paths;

namespace BriskEngine.Safety;

public sealed record AuthorizationResult(bool Allowed, string Reason)
{
    public static AuthorizationResult Ok() => new(true, "ok");
    public static AuthorizationResult Deny(string reason) => new(false, reason);
}

/// The only component allowed to authorize a mutation. Allowlist-only:
/// a path is deletable only when its REAL path (junctions resolved) stays
/// inside a registered template's real path, and protected folders win
/// over any template as defense in depth.
public sealed class SafetyValidator
{
    public AuthorizationResult Authorize(string path, CleanupTarget target)
    {
        // Queried path must be verifiable (fail-closed)
        if (!RealPath.TryResolve(path, out var pathReal))
            return AuthorizationResult.Deny($"'{path}' could not be verified (unresolvable real path)");

        if (ProtectedPaths.IsProtected(pathReal))
            return AuthorizationResult.Deny($"'{pathReal}' is inside a protected folder");

        foreach (var template in target.PathTemplates)
        {
            var expanded = PathExpander.Expand(template);
            if (expanded is null) continue;

            // Templates must also be verifiable; skip if unresolvable
            if (!RealPath.TryResolve(expanded, out var templateReal)) continue;

            var isTemplateItself = string.Equals(pathReal, templateReal,
                StringComparison.OrdinalIgnoreCase);
            var isUnder = pathReal.StartsWith(templateReal + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

            if (isTemplateItself && !target.DeletesContentsNotDirectory)
                return AuthorizationResult.Ok();
            if (isUnder)
                return AuthorizationResult.Ok();
        }
        return AuthorizationResult.Deny(
            $"'{pathReal}' is outside the allowlist of target '{target.Id}'");
    }
}
