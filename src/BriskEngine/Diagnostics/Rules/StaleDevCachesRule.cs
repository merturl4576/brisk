using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Models;
using BriskEngine.Paths;

namespace BriskEngine.Diagnostics.Rules;

public sealed class StaleDevCachesRule : AdviseRuleBase
{
    public override string Id => "stale-dev-caches";

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var now = DateTime.UtcNow;
        var sixtyDaysAgo = now.AddDays(-60);
        var staleCaches = new List<string>();

        foreach (var target in CleanupTargetRegistry.All)
        {
            if (target.Level != CleanupLevel.Developer || !target.Regenerates || target.PathTemplates.Count == 0)
            {
                continue;
            }

            foreach (var template in target.PathTemplates)
            {
                var expandedPath = PathExpander.Expand(template);
                if (string.IsNullOrEmpty(expandedPath)) continue;

                var size = ctx.Files.DirectorySizeBytes(expandedPath);
                var newest = ctx.Files.NewestWriteUtc(expandedPath);

                if (size >= 500L << 20 && newest.HasValue && newest.Value <= sixtyDaysAgo) // 500 MB
                {
                    var idleDays = (int)(now - newest.Value).TotalDays;
                    staleCaches.Add($"{target.DisplayName}: {Fmt.Bytes(size)}, idle {idleDays} days");
                }
            }
        }

        if (staleCaches.Count == 0) return null;

        return new DiagnosticFinding(
            Id, "rule.stale-dev-caches.title",
            "Development caches unchanged for 60+ days",
            string.Join("; ", staleCaches),
            Severity.Info, Category, ImpactStars: 2, CanFix: false, FixDescription: null);
    }
}
