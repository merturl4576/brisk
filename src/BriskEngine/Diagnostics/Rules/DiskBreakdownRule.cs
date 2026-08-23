using System;
using System.Collections.Generic;
using BriskEngine.Models;
using BriskEngine.Paths;

namespace BriskEngine.Diagnostics.Rules;

public sealed class DiskBreakdownRule : AdviseRuleBase
{
    public override string Id => "disk-breakdown";

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var folders = new List<(string Label, string Path, long ThresholdBytes)>();

        var localAppData = PathExpander.Expand("%LOCALAPPDATA%");
        if (localAppData != null)
            folders.Add(("AppData\\Local", localAppData, 50L << 30));

        var appData = PathExpander.Expand("%APPDATA%");
        if (appData != null)
            folders.Add(("AppData\\Roaming", appData, 20L << 30));

        folders.Add(("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), 10L << 30));

        var downloads = PathExpander.Expand(@"%USERPROFILE%\Downloads");
        if (downloads != null)
            folders.Add(("Downloads", downloads, 10L << 30));

        var evidence = new List<string>();
        var hasOverage = false;
        (string Label, long Size)? largest = null;

        foreach (var (label, path, threshold) in folders)
        {
            var size = ctx.Files.DirectorySizeBytes(path);
            var sizeStr = Fmt.Bytes(size);
            var line = $"{label}: {sizeStr}";
            if (size >= threshold)
            {
                line += " (over threshold)";
                hasOverage = true;
                if (largest is null || size > largest.Value.Size)
                    largest = (label, size);
            }
            evidence.Add(line);
        }

        if (!hasOverage) return null;

        var (topLabel, topSize) = largest!.Value;
        var topBytes = Fmt.Bytes(topSize);
        return new DiagnosticFinding(
            Id, "rule.disk-breakdown.title",
            "Disk space fragmented across system folders",
            string.Join("; ", evidence),
            Severity.Warning, Category, ImpactStars: 2, CanFix: false, FixDescription: null,
            Headline: new Headline(
                topBytes, $"{topLabel} — the largest measured folder",
                $"rule.{Id}.headline.value", new[] { topBytes },
                $"rule.{Id}.headline.caption", new[] { topLabel }));
    }
}
