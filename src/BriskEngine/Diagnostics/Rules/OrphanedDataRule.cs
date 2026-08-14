using System;
using System.Collections.Generic;
using BriskEngine.Models;
using BriskEngine.Paths;

namespace BriskEngine.Diagnostics.Rules;

public sealed class OrphanedDataRule : AdviseRuleBase
{
    public override string Id => "orphaned-data";

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var toolDefinitions = new[]
        {
            ("Docker Desktop", @"%LOCALAPPDATA%\Docker"),
            ("BlueStacks", @"%ProgramData%\BlueStacks_nxt"),
            ("Unity", @"%LOCALAPPDATA%\Unity"),
            ("JetBrains", @"%LOCALAPPDATA%\JetBrains"),
        };

        var orphans = new List<string>();

        foreach (var (name, templatePath) in toolDefinitions)
        {
            var dataDir = PathExpander.Expand(templatePath);
            if (dataDir == null) continue;

            if (IsInstalled(ctx, name)) continue;

            var size = ctx.Files.DirectorySizeBytes(dataDir);
            if (size >= 500L << 20) // 500 MB
            {
                orphans.Add($"{name}: {Fmt.Bytes(size)}");
            }
        }

        if (orphans.Count == 0) return null;

        return new DiagnosticFinding(
            Id, "rule.orphaned-data.title",
            "Uninstalled tools left behind data",
            string.Join("; ", orphans),
            Severity.Warning, Category, ImpactStars: 3, CanFix: false, FixDescription: null);
    }

    private bool IsInstalled(DiagnosticContext ctx, string toolName)
    {
        var uninstallPaths = new[]
        {
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };

        foreach (var uninstallPath in uninstallPaths)
        {
            try
            {
                var subKeys = ctx.Registry.GetSubKeyNames(uninstallPath);
                foreach (var subKey in subKeys)
                {
                    var displayName = ctx.Registry.GetString($@"{uninstallPath}\{subKey}", "DisplayName");
                    if (!string.IsNullOrEmpty(displayName) &&
                        displayName.Contains(toolName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Key might not exist or be accessible; continue
            }
        }

        return false;
    }
}
