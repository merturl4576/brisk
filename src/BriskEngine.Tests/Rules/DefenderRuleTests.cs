using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

/// What Defender skips and whether it is on, read through a probe. Every
/// trigger fires alone; the all-fine shape stays silent; unreadable is never
/// zero; no excluded path reaches a finding string or the JSON.
public class DefenderRuleTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private static DefenderStatus Fine() => new(
        RealTimeOn: true, TamperOn: true, RunningMode: "Normal", ExclusionCount: 0,
        ExclusionPaths: Array.Empty<string>(),
        LastQuickScanUtc: Now.AddDays(-2), LastFullScanUtc: Now.AddDays(-21), SignatureAgeDays: 0);

    private static (DiagnosticContext ctx, FakeDefender probe) Context(DefenderStatus? status)
    {
        var probe = new FakeDefender { Status = status };
        return (TestContext.Empty() with { Defender = probe }, probe);
    }

    private static DefenderRule Rule() => new(() => Now);

    private static IEnumerable<string> EverythingAReaderWouldSee(DiagnosticFinding f)
    {
        yield return f.Title;
        yield return f.Evidence;
        foreach (var arg in f.EvidenceArgs ?? Array.Empty<string>()) yield return arg;
        if (f.Headline is not { } h) yield break;
        yield return h.Value;
        yield return h.Caption;
        foreach (var arg in h.ValueArgs) yield return arg;
        foreach (var arg in h.CaptionArgs) yield return arg;
    }

    [Fact]
    public void AllFine_IsNoFinding()
    {
        var (ctx, _) = Context(Fine());
        Assert.Null(Rule().Detect(ctx));
    }

    public static TheoryData<string, DefenderStatus> EachTriggerAlone()
    {
        var f = Fine();
        return new TheoryData<string, DefenderStatus>
        {
            { "exclusions", f with { ExclusionCount = 5, ExclusionPaths = new[] { @"C:\a", @"C:\b", @"C:\c", @"C:\d", @"C:\e" } } },
            { "exclusions unread", f with { ExclusionCount = null } },
            { "real-time off", f with { RealTimeOn = false } },
            { "tamper off", f with { TamperOn = false } },
            { "passive mode", f with { RunningMode = "Passive Mode" } },
            { "stale scan", f with { LastQuickScanUtc = Now.AddDays(-15), LastFullScanUtc = Now.AddDays(-40) } },
            { "no scan on record", f with { LastQuickScanUtc = null, LastFullScanUtc = null } },
        };
    }

    [Theory]
    [MemberData(nameof(EachTriggerAlone))]
    public void EachTrigger_FiresAlone(string why, DefenderStatus status)
    {
        var (ctx, _) = Context(status);
        var finding = Rule().Detect(ctx);
        Assert.True(finding is not null, $"{why} did not fire");
        Assert.Equal($"rule.{DefenderRule.RuleId}.title", finding!.TitleKey);
    }

    [Fact]
    public void FourteenDays_IsTheScanAgeBoundary()
    {
        var (fresh, _) = Context(Fine() with { LastQuickScanUtc = Now.AddDays(-14), LastFullScanUtc = null });
        var (stale, _) = Context(Fine() with { LastQuickScanUtc = Now.AddDays(-15), LastFullScanUtc = null });
        Assert.Null(Rule().Detect(fresh));
        Assert.NotNull(Rule().Detect(stale));
    }

    [Fact]
    public void Exclusions_LeadTheHeadline_AndTheEvidenceCarriesTheCount()
    {
        var (ctx, _) = Context(Fine() with { ExclusionCount = 5, ExclusionPaths = new[] { @"C:\ZZZ-excluded" } });
        var finding = Rule().Detect(ctx)!;
        Assert.Equal("5", finding.Headline!.Value);
        Assert.Equal($"rule.{DefenderRule.RuleId}.headline.caption", finding.Headline.CaptionKey);
        Assert.Equal($"rule.{DefenderRule.RuleId}.evidence", finding.EvidenceKey);
        Assert.Equal(new[] { "5", "0", "0", "Normal", "2" }, finding.EvidenceArgs);
    }

    [Fact]
    public void ExclusionsUnread_SaysSo_AndNeverSaysZero()
    {
        var (ctx, _) = Context(Fine() with { ExclusionCount = null });
        var finding = Rule().Detect(ctx)!;
        Assert.Equal($"rule.{DefenderRule.RuleId}.evidence.exclusions-unread", finding.EvidenceKey);
        Assert.Null(finding.Headline);
        Assert.DoesNotContain("0 paths", finding.Evidence, StringComparison.Ordinal);
        Assert.Contains("administrator", finding.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoScanOnRecord_UsesTheNoScanKey()
    {
        var (ctx, _) = Context(Fine() with { LastQuickScanUtc = null, LastFullScanUtc = null });
        var finding = Rule().Detect(ctx)!;
        Assert.Equal($"rule.{DefenderRule.RuleId}.evidence.no-scan", finding.EvidenceKey);
        Assert.Equal(new[] { "0", "0", "0", "Normal" }, finding.EvidenceArgs);
    }

    [Fact]
    public void ProtectionsOff_AreCountedInTheEvidence()
    {
        var (ctx, _) = Context(Fine() with { RealTimeOn = false, TamperOn = null });
        var finding = Rule().Detect(ctx)!;
        // {1} = protections reading off, {2} = protections brisk could not read
        Assert.Equal(new[] { "0", "1", "1", "Normal", "2" }, finding.EvidenceArgs);
    }

    [Fact]
    public void WholeReadFailed_ReportsUnreadable_WithNoDigits()
    {
        var (ctx, _) = Context(null);
        var finding = Rule().Detect(ctx)!;
        Assert.Equal($"rule.{DefenderRule.RuleId}.title.unread", finding.TitleKey);
        Assert.Null(finding.Headline);
        var digits = string.Concat(EverythingAReaderWouldSee(finding).SelectMany(s => s.Where(char.IsDigit)));
        Assert.True(digits.Length == 0, $"unreadable finding carries digits: {digits}");
    }

    [Fact]
    public void NoExcludedPath_ReachesAnyFindingString_OrTheJson()
    {
        var (ctx, _) = Context(Fine() with { ExclusionCount = 2, ExclusionPaths = new[] { @"C:\ZZZ-excluded", @"D:\ZZZ-two" } });
        var finding = Rule().Detect(ctx)!;
        var everything = string.Join(" | ", EverythingAReaderWouldSee(finding))
            + " | " + JsonSerializer.Serialize(finding);
        Assert.DoesNotContain("ZZZ", everything, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Status_IsReadOnce_PerScan()
    {
        var (ctx, probe) = Context(Fine() with { ExclusionCount = 1 });
        Rule().Detect(ctx);
        Rule().Detect(ctx);
        var again = DefenderRule.Status(ctx);
        Assert.Equal(1, probe.Reads);
        Assert.NotNull(again);
    }

    // ---- one claim, two languages ---------------------------------------

    /// EVERY state this rule can report, one plant each. The rule names a
    /// different evidence key per state, so a single reading would check one
    /// of its four sentences against the resx and the rest against nothing —
    /// the same reason CapabilityUseRuleTests and PrivacyDisclosureRuleTests
    /// each keep a list like this beside their rules.
    public static TheoryData<string> EveryStateItCanReport()
    {
        var data = new TheoryData<string>();
        foreach (var state in new[] { "exclusions", "no-scan", "exclusions-unread", "unread" })
            data.Add(state);
        return data;
    }

    /// The probe answer for each of those states.
    private static DiagnosticFinding Detect(string state)
    {
        var status = state switch
        {
            "exclusions" => Fine() with { ExclusionCount = 5 },
            "no-scan" => Fine() with { LastQuickScanUtc = null, LastFullScanUtc = null },
            "exclusions-unread" => Fine() with { ExclusionCount = null },
            "unread" => null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(state), state, "no probe answer for this state"),
        };
        var (ctx, _) = Context(status);
        return Rule().Detect(ctx)!;
    }

    /// The engine ships English prose the CLI prints verbatim and a resx key
    /// the GUI renders instead, and the two have to say the same thing. This
    /// is also what carries the rule's engine prose — concatenated C#, where
    /// a phrase split across a `+` is invisible to any matcher — into the
    /// file PrivacyRedLineTests scans for claims.
    [Theory]
    [MemberData(nameof(EveryStateItCanReport))]
    public void TheEnglishResx_SaysWhatTheEngineSays(string state)
    {
        var en = PrivacyDisclosureRuleTests.Resx("Strings.resx");
        var finding = Detect(state);

        Assert.True(en.TryGetValue(finding.TitleKey, out var title),
            $"{finding.TitleKey} is missing from Strings.resx");
        Assert.Equal(finding.Title, title);

        Assert.True(en.TryGetValue(finding.EvidenceKey!, out var evidence),
            $"{finding.EvidenceKey} is missing from Strings.resx");
        Assert.Equal(finding.Evidence, string.Format(CultureInfo.InvariantCulture,
            evidence!, (finding.EvidenceArgs ?? Array.Empty<string>()).ToArray<object>()));
    }

    /// LocTests holds the two key sets equal to each other; nothing there
    /// knows which keys a rule actually asks for, so a rule naming a key that
    /// is in neither file leaves both sets equal and both readers looking at
    /// a raw key string.
    [Theory]
    [MemberData(nameof(EveryStateItCanReport))]
    public void EveryKeyTheRuleNames_IsInBothLanguages(string state)
    {
        var finding = Detect(state);
        var keys = new List<string>
        {
            finding.TitleKey, finding.EvidenceKey!,
            $"rule.{DefenderRule.RuleId}.advice",
        };
        if (finding.Headline is { } h) { keys.Add(h.ValueKey); keys.Add(h.CaptionKey); }

        foreach (var file in new[] { "Strings.resx", "Strings.tr.resx" })
        {
            var strings = PrivacyDisclosureRuleTests.Resx(file);
            foreach (var key in keys)
                Assert.True(strings.ContainsKey(key), $"{key} is missing from {file}");
        }
    }

    [Fact]
    public void TheFinding_IsAdvise_Notice_Info_AndRegisteredOnce()
    {
        var (ctx, _) = Context(Fine() with { ExclusionCount = 1 });
        var rule = Rule();
        var finding = rule.Detect(ctx)!;
        Assert.Equal(RuleCategory.Advise, rule.Category);
        Assert.Equal(FindingKind.Notice, finding.Kind);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal(100, HealthScore.Compute(new[] { finding }));
        Assert.Equal(1, DiagnosticRuleRegistry.All.Count(r => r.Id == DefenderRule.RuleId));
    }
}
