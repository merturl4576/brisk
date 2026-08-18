using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

public sealed class SearchWebResultsRule : IDiagnosticRule
{
    public const string PolicyKey = @"HKCU\Software\Policies\Microsoft\Windows\Explorer";
    public const string PolicyValue = "DisableSearchBoxSuggestions";
    public const string LegacyKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search";
    public const string LegacyValue = "BingSearchEnabled";

    private sealed record Prior(int? Policy, int? Legacy);

    public string Id => "search-web-results";
    public RuleCategory Category => RuleCategory.Auto;

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        // Any existing policy value is somebody's decision: 1 means the fix is
        // already in place, anything else means an administrator wants web
        // results. Either way there is nothing for brisk to do.
        if (ctx.Registry.GetInt(PolicyKey, PolicyValue) is not null) return null;
        // Windows 10's own switch, already thrown.
        if (ctx.Registry.GetInt(LegacyKey, LegacyValue) == 0) return null;

        return new DiagnosticFinding(
            Id, "rule.search-web-results.title",
            "Start menu search waits on the internet",
            "Every keystroke in Start is sent to Bing, and local results for " +
            "your apps and files wait for that round-trip. Turning web results " +
            "off makes Start answer immediately. Takes effect after you sign in again.",
            Severity.Warning, Category, ImpactStars: 4, CanFix: true,
            FixDescription: "Stop Start menu search from querying the web (undoable)",
            EvidenceKey: $"rule.{Id}.evidence", EvidenceArgs: null);
    }

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Prior(ctx.Registry.GetInt(PolicyKey, PolicyValue),
                              ctx.Registry.GetInt(LegacyKey, LegacyValue));
        ctx.Registry.SetInt(PolicyKey, PolicyValue, 1);
        ctx.Registry.SetInt(LegacyKey, LegacyValue, 0);
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Prior>(priorStateJson)!;
        if (prior.Policy is null) ctx.Registry.DeleteValue(PolicyKey, PolicyValue);
        else ctx.Registry.SetInt(PolicyKey, PolicyValue, prior.Policy.Value);
        if (prior.Legacy is null) ctx.Registry.DeleteValue(LegacyKey, LegacyValue);
        else ctx.Registry.SetInt(LegacyKey, LegacyValue, prior.Legacy.Value);
    }
}
