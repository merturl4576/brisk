using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Models;
using BriskEngine.Paths;

namespace BriskEngine.Diagnostics.Rules;

public sealed class DiskBreakdownRule : AdviseRuleBase
{
    public override string Id => "disk-breakdown";

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var folders = new List<(string Label, string Path, long ThresholdBytes)>
        {
            ("AppData", PathExpander.Expand("%LOCALAPPDATA%")!, 50L << 30),
            ("AppData", PathExpander.Expand("%APPDATA%")!, 20L << 30),
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), 10L << 30),
            ("Downloads", PathExpander.Expand(@"%USERPROFILE%\Downloads")!, 10L << 30),
        };

        var evidence = new List<string>();
        var hasOverage = false;

        foreach (var (label, path, threshold) in folders)
        {
            var size = ctx.Files.DirectorySizeBytes(path);
            var sizeStr = Fmt.Bytes(size);
            var line = $"{label}: {sizeStr}";
            if (size >= threshold)
            {
                line += " (over threshold)";
                hasOverage = true;
            }
            evidence.Add(line);
        }

        if (!hasOverage) return null;

        return new DiagnosticFinding(
            Id, "rule.disk-breakdown.title",
            "Disk space fragmented across system folders",
            string.Join("; ", evidence),
            Severity.Warning, Category, ImpactStars: 2, CanFix: false, FixDescription: null);
    }
}
