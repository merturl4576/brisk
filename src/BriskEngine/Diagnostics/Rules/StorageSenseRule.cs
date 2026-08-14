using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

public sealed class StorageSenseRule : IDiagnosticRule
{
    private const string Key = @"HKCU\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy";
    private const string Value = "01";
    private const string DriveRoot = @"C:\";

    private sealed record Prior(int Previous);

    public string Id => "storage-sense";
    public RuleCategory Category => RuleCategory.Confirm;

    private static bool LowDisk(DiagnosticContext ctx)
    {
        var free = ctx.Disk.FreeBytes(DriveRoot);
        var total = ctx.Disk.TotalBytes(DriveRoot);
        if (total <= 0) return false;
        return free / (double)total < 0.15;
    }

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        if (!LowDisk(ctx)) return null;
        if (ctx.Registry.GetInt(Key, Value) == 1) return null;

        var free = ctx.Disk.FreeBytes(DriveRoot);
        var total = ctx.Disk.TotalBytes(DriveRoot);
        var pct = total > 0 ? free / (double)total * 100 : 0;
        return new DiagnosticFinding(
            Id, "rule.storage-sense.title",
            "Disk is nearly full and Storage Sense is off",
            $"Only {pct:F0}% free on {DriveRoot}, and Storage Sense automatic cleanup is disabled.",
            Severity.Warning, Category, ImpactStars: 2, CanFix: true,
            FixDescription: "Turn on Storage Sense automatic cleanup (undoable)");
    }

    public string Fix(DiagnosticContext ctx)
    {
        var current = ctx.Registry.GetInt(Key, Value);
        var prior = new Prior(current ?? -1);
        ctx.Registry.SetInt(Key, Value, 1);
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Prior>(priorStateJson)!;
        if (prior.Previous == -1) ctx.Registry.DeleteValue(Key, Value);
        else ctx.Registry.SetInt(Key, Value, prior.Previous);
    }
}
