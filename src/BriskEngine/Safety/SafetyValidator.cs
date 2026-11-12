using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Cleaning;
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
///
/// A dynamic target has no template: its path must sit inside one of the
/// folders it walks and still fit its rule NOW, read again on the real
/// path — the scan's verdict is never trusted alone.
public sealed class SafetyValidator
{
    private readonly Func<IReadOnlyList<string>> _dynamicRoots;
    private readonly Func<DateTime> _utcNow;

    public SafetyValidator() : this(DynamicTargets.DefaultRoots, () => DateTime.UtcNow) { }

    /// Tests hand in their own roots and clock, so no test authorizes a path
    /// in the real Desktop or Documents.
    internal SafetyValidator(Func<IReadOnlyList<string>> dynamicRoots, Func<DateTime> utcNow)
    {
        _dynamicRoots = dynamicRoots;
        _utcNow = utcNow;
    }

    public AuthorizationResult Authorize(string path, CleanupTarget target)
    {
        // Queried path must be verifiable (fail-closed)
        if (!RealPath.TryResolve(path, out var pathReal))
            return AuthorizationResult.Deny($"'{path}' could not be verified (unresolvable real path)");

        if (target.Dynamic) return AuthorizeDynamic(pathReal, target);

        if (ProtectedPaths.IsProtected(pathReal))
            return AuthorizationResult.Deny($"'{pathReal}' is inside a protected folder");

        foreach (var template in target.PathTemplates)
        foreach (var concrete in TemplateResolver.Resolve(template))
        {
            if (!RealPath.TryResolve(concrete, out var templateReal)) continue;

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

    /// The only way into Desktop and Documents: every other protected folder
    /// and the profile root itself still deny.
    private AuthorizationResult AuthorizeDynamic(string pathReal, CleanupTarget target)
    {
        if (ProtectedPaths.IsProtected(pathReal, exceptUserFolders: true))
            return AuthorizationResult.Deny($"'{pathReal}' is inside a protected folder");

        var underRoot = _dynamicRoots().Any(root => RealPath.TryResolve(root, out var rootReal)
            && pathReal.StartsWith(rootReal + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (!underRoot)
            return AuthorizationResult.Deny($"'{pathReal}' is outside the folders target '{target.Id}' walks");

        if (!DynamicTargets.Matches(target.Id, pathReal, _utcNow()))
            return AuthorizationResult.Deny($"'{pathReal}' no longer fits the rule of target '{target.Id}'");

        return AuthorizationResult.Ok();
    }
}
