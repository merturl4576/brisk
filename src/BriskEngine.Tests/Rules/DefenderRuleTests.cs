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
        Exclusions: Array.Empty<string>(),
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

    /// Each trigger with the title the finding takes. Every state brisk
    /// MEASURED as bad is called by the rule's own name; the one state whose
    /// only trigger is a reading that did not arrive — an exclusion list with
    /// everything else reading fine — takes the unread title, and the row
    /// below it pins the other orientation of the same read.
    public static TheoryData<string, DefenderStatus, string> EachTriggerAlone()
    {
        var f = Fine();
        const string named = "title";
        const string unread = "title.unread";
        return new TheoryData<string, DefenderStatus, string>
        {
            { "exclusions", f with { ExclusionCount = 5, Exclusions = new[] { @"C:\a", @"C:\b", @"C:\c", @"C:\d", @"C:\e" } }, named },
            { "exclusions unread", f with { ExclusionCount = null }, unread },
            { "real-time unread", f with { RealTimeOn = null }, unread },
            { "tamper unread", f with { TamperOn = null }, unread },
            { "mode unread", f with { RunningMode = null }, unread },
            { "exclusions unread beside a protection off",
                f with { ExclusionCount = null, RealTimeOn = false }, named },
            { "a protection unread beside a protection off",
                f with { TamperOn = null, RealTimeOn = false }, named },
            { "mode unread beside a protection off",
                f with { RunningMode = null, RealTimeOn = false }, named },
            { "real-time off", f with { RealTimeOn = false }, named },
            { "tamper off", f with { TamperOn = false }, named },
            { "passive mode", f with { RunningMode = "Passive Mode" }, named },
            { "stale scan", f with { LastQuickScanUtc = Now.AddDays(-15), LastFullScanUtc = Now.AddDays(-40) }, named },
            { "no scan on record", f with { LastQuickScanUtc = null, LastFullScanUtc = null }, named },
            // The two scan stamps are empty because the row they ride in
            // answered nothing, not because this machine has never scanned.
            { "no stamps out of a row that read nothing",
                f with
                {
                    RealTimeOn = null, TamperOn = null, RunningMode = null,
                    LastQuickScanUtc = null, LastFullScanUtc = null,
                }, unread },
        };
    }

    [Theory]
    [MemberData(nameof(EachTriggerAlone))]
    public void EachTrigger_FiresAlone(string why, DefenderStatus status, string titleKey)
    {
        var (ctx, _) = Context(status);
        var finding = Rule().Detect(ctx);
        Assert.True(finding is not null, $"{why} did not fire");
        Assert.Equal($"rule.{DefenderRule.RuleId}.{titleKey}", finding!.TitleKey);
    }

    /// EVERY MEASURED BAD STATE LEADS WITH THE READING THAT MADE IT BAD, and
    /// in this order. A disclosure with no headline is how this family says
    /// "nothing was read" — the Privacy page bands those into what brisk
    /// could not read and the report card lists them there — so a state brisk
    /// measured and did not lead with would be filed as a failed read.
    public static TheoryData<string, DefenderStatus, string, string> EveryLead()
    {
        var f = Fine();
        return new TheoryData<string, DefenderStatus, string, string>
        {
            { "exclusions outrank everything",
                f with { ExclusionCount = 5, RealTimeOn = false, TamperOn = false },
                "5", "headline.caption" },
            { "real-time outranks tamper",
                f with { RealTimeOn = false, TamperOn = false },
                "Off", "headline.caption.realtime" },
            { "tamper", f with { TamperOn = false }, "Off", "headline.caption.tamper" },
            { "the mode is Windows' own word",
                f with { RunningMode = "Passive Mode" }, "Passive Mode", "headline.caption.mode" },
            { "scan age", f with { LastQuickScanUtc = Now.AddDays(-15), LastFullScanUtc = null },
                "15", "headline.caption.scan-age" },
            { "no scan at all", f with { LastQuickScanUtc = null, LastFullScanUtc = null },
                "None", "headline.caption.no-scan" },
            { "an unread list does not stop the clause that was read",
                f with { ExclusionCount = null, TamperOn = false },
                "Off", "headline.caption.tamper" },
            { "an unread protection does not either",
                f with { TamperOn = null, RealTimeOn = false },
                "Off", "headline.caption.realtime" },
            { "an unread mode does not either",
                f with { RunningMode = null, TamperOn = false },
                "Off", "headline.caption.tamper" },
            // Ruling 22's positive row: the exclusion list is the one thing
            // Windows hides from an unelevated process, which is the common
            // case and says nothing about the row the scan stamps ride in.
            { "an unread exclusion list does not stop no-scan leading",
                f with
                {
                    ExclusionCount = null,
                    LastQuickScanUtc = null, LastFullScanUtc = null,
                }, "None", "headline.caption.no-scan" },
        };
    }

    [Theory]
    [MemberData(nameof(EveryLead))]
    public void EveryMeasuredBadState_LeadsWithItsWorstReading(
        string why, DefenderStatus status, string value, string captionKey)
    {
        var (ctx, _) = Context(status);
        var headline = Rule().Detect(ctx)!.Headline;
        Assert.True(headline is not null, $"{why}: the finding leads with nothing");
        Assert.Equal(value, headline!.Value);
        Assert.Equal($"rule.{DefenderRule.RuleId}.{captionKey}", headline.CaptionKey);
    }

    /// WHOLE DAYS, one measure. 14.5 days is a scan 14 days old by the only
    /// number this rule prints, so it neither fires nor leads with a "14" a
    /// reader would have to square with a rule that says over 14.
    [Fact]
    public void FourteenDays_IsTheScanAgeBoundary()
    {
        var (fresh, _) = Context(Fine() with { LastQuickScanUtc = Now.AddDays(-14), LastFullScanUtc = null });
        var (half, _) = Context(Fine() with { LastQuickScanUtc = Now.AddDays(-14.5), LastFullScanUtc = null });
        var (stale, _) = Context(Fine() with { LastQuickScanUtc = Now.AddDays(-15), LastFullScanUtc = null });
        Assert.Null(Rule().Detect(fresh));
        Assert.Null(Rule().Detect(half));
        Assert.NotNull(Rule().Detect(stale));
    }

    /// EVERY PART THAT CAN GO UNREAD, ALONE, IS AN UNREADABLE DISCLOSURE.
    /// The exclusion list, either protection, the running mode: with
    /// everything else reading fine, the only reason the rule speaks is a
    /// reading that did not arrive, so it takes the unread title and leads
    /// with nothing — which is what puts it in the page's could-not-read band
    /// instead of on the report card as a fault brisk measured.
    public static TheoryData<string, DefenderStatus> EveryUnreadPart()
    {
        var f = Fine();
        return new TheoryData<string, DefenderStatus>
        {
            { "the exclusion list", f with { ExclusionCount = null } },
            { "real-time protection", f with { RealTimeOn = null } },
            { "tamper protection", f with { TamperOn = null } },
            { "the running mode", f with { RunningMode = null } },
        };
    }

    [Theory]
    [MemberData(nameof(EveryUnreadPart))]
    public void AnUnreadPartAlone_IsUnreadable_AndLeadsWithNothing(
        string what, DefenderStatus status)
    {
        var (ctx, _) = Context(status);
        var finding = Rule().Detect(ctx)!;
        Assert.True(finding.Headline is null,
            $"{what} went unread and the finding leads with " +
            $"\"{finding.Headline?.Value}\" — a reading brisk did not take");
        Assert.Equal($"rule.{DefenderRule.RuleId}.title.unread", finding.TitleKey);
    }

    /// An unread mode is a clause of its own and never brisk's English word
    /// for one. "unknown" travelled through the value slot as an argument,
    /// where no resx can reach it.
    [Fact]
    public void AnUnreadMode_IsNeverTheWordUnknown()
    {
        var (ctx, _) = Context(Fine() with { RunningMode = null });
        var finding = Rule().Detect(ctx)!;
        Assert.DoesNotContain("unknown", finding.Evidence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unknown", string.Join(" ", finding.EvidenceArgs!),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("could not read Defender's running mode", finding.Evidence,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Exclusions_LeadTheHeadline_AndTheEvidenceCarriesTheCount()
    {
        var (ctx, _) = Context(Fine() with { ExclusionCount = 5, Exclusions = new[] { @"C:\ZZZ-excluded" } });
        var finding = Rule().Detect(ctx)!;
        Assert.Equal("5", finding.Headline!.Value);
        Assert.Equal($"rule.{DefenderRule.RuleId}.headline.caption", finding.Headline.CaptionKey);
        Assert.Equal($"rule.{DefenderRule.RuleId}.evidence", finding.EvidenceKey);
        Assert.Equal(new[] { "5", "0", "0", "Normal", "2" }, finding.EvidenceArgs);
    }

    /// With everything else reading fine, an exclusion list brisk could not
    /// read is an UNREADABLE disclosure: the unread title, no headline, and
    /// an evidence sentence that says so without ever saying zero.
    [Fact]
    public void ExclusionsUnread_SaysSo_AndNeverSaysZero()
    {
        var (ctx, _) = Context(Fine() with { ExclusionCount = null });
        var finding = Rule().Detect(ctx)!;
        Assert.Equal($"rule.{DefenderRule.RuleId}.evidence.exclusions-unread", finding.EvidenceKey);
        Assert.Equal($"rule.{DefenderRule.RuleId}.title.unread", finding.TitleKey);
        Assert.Null(finding.Headline);
        Assert.DoesNotContain("0 paths", finding.Evidence, StringComparison.Ordinal);
        Assert.Contains("administrator", finding.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    /// The other orientation: something brisk DID measure as bad is the
    /// finding's own reading, so the rule's name is on it and that clause
    /// leads — and the evidence still carries the list it could not read.
    [Fact]
    public void ExclusionsUnread_BesideAMeasuredFault_IsNamedAndLedByThatFault()
    {
        var (ctx, _) = Context(Fine() with { ExclusionCount = null, RealTimeOn = false });
        var finding = Rule().Detect(ctx)!;
        Assert.Equal($"rule.{DefenderRule.RuleId}.title", finding.TitleKey);
        Assert.Equal("Off", finding.Headline!.Value);
        Assert.Equal($"rule.{DefenderRule.RuleId}.evidence.exclusions-unread", finding.EvidenceKey);
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

    /// "NO COMPLETED SCAN IS ON RECORD" IS A READING OF THE MACHINE, and
    /// MSFT_MpComputerStatus hands back a row whether or not it knows
    /// anything: a row that answered null for both protections and the mode
    /// answered null for the two scan stamps as well. Reported as measured,
    /// that machine took the rule's own title and led with "None / completed
    /// scans on record" - brisk naming a state it had not read, on the one
    /// finding family whose no-headline shape means the opposite.
    ///
    /// The exclusion list does not gate it: Windows hides that from an
    /// unelevated process by design, which is the common case and says
    /// nothing about the row (EveryLead carries that orientation).
    [Fact]
    public void NoScan_IsMeasuredOnly_WhenTheRestOfTheRowWasRead()
    {
        var (ctx, _) = Context(Fine() with
        {
            RealTimeOn = null, TamperOn = null, RunningMode = null,
            LastQuickScanUtc = null, LastFullScanUtc = null,
        });

        var finding = Rule().Detect(ctx)!;

        Assert.Null(finding.Headline);
        Assert.Equal($"rule.{DefenderRule.RuleId}.title.unread", finding.TitleKey);
        Assert.Equal($"rule.{DefenderRule.RuleId}.evidence.mode-unread.scan-unread",
            finding.EvidenceKey);
        Assert.Contains("could not read when Defender last completed a scan",
            finding.Evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("No completed scan is on record", finding.Evidence,
            StringComparison.Ordinal);
    }

    /// The same row read one protection short: the scan clause is still the
    /// unread one and the mode clause is still the measured one, so the two
    /// halves of the key move independently.
    [Fact]
    public void OneUnreadProtection_IsEnoughToMakeTheScanStampsUnread()
    {
        var (ctx, _) = Context(Fine() with
        {
            TamperOn = null, LastQuickScanUtc = null, LastFullScanUtc = null,
        });

        var finding = Rule().Detect(ctx)!;

        Assert.Null(finding.Headline);
        Assert.Equal($"rule.{DefenderRule.RuleId}.evidence.scan-unread",
            finding.EvidenceKey);
        Assert.Equal(new[] { "0", "0", "1", "Normal" }, finding.EvidenceArgs);
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
        var (ctx, _) = Context(Fine() with { ExclusionCount = 2, Exclusions = new[] { @"C:\ZZZ-excluded", @"D:\ZZZ-two" } });
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
        foreach (var state in new[]
                 {
                     "exclusions", "real-time-off", "tamper-off", "passive-mode",
                     "stale-scan", "no-scan", "mode-unread",
                     "mode-unread-scan-unread", "protection-unread-scan-unread",
                     "exclusions-unread", "exclusions-unread-no-scan",
                     "exclusions-unread-scan-unread",
                     "exclusions-unread-mode-unread",
                     "exclusions-unread-mode-unread-scan-unread",
                     "exclusions-unread-and-real-time-off", "unread",
                 })
            data.Add(state);
        return data;
    }

    /// The probe answer for each of those states. Every one of the rule's
    /// titles, evidence keys and headline keys is named by one of them.
    private static DiagnosticFinding Detect(string state)
    {
        var status = state switch
        {
            "exclusions" => Fine() with { ExclusionCount = 5 },
            "real-time-off" => Fine() with { RealTimeOn = false },
            "tamper-off" => Fine() with { TamperOn = false },
            "passive-mode" => Fine() with { RunningMode = "Passive Mode" },
            "stale-scan" => Fine() with
            {
                LastQuickScanUtc = Now.AddDays(-15), LastFullScanUtc = Now.AddDays(-40),
            },
            "no-scan" => Fine() with { LastQuickScanUtc = null, LastFullScanUtc = null },
            "mode-unread" => Fine() with { RunningMode = null },
            "mode-unread-scan-unread" => Fine() with
            {
                RunningMode = null, LastQuickScanUtc = null, LastFullScanUtc = null,
            },
            "protection-unread-scan-unread" => Fine() with
            {
                RealTimeOn = null, LastQuickScanUtc = null, LastFullScanUtc = null,
            },
            "exclusions-unread" => Fine() with { ExclusionCount = null },
            "exclusions-unread-no-scan" => Fine() with
            {
                ExclusionCount = null, LastQuickScanUtc = null, LastFullScanUtc = null,
            },
            "exclusions-unread-scan-unread" => Fine() with
            {
                ExclusionCount = null, TamperOn = null,
                LastQuickScanUtc = null, LastFullScanUtc = null,
            },
            "exclusions-unread-mode-unread" => Fine() with
            {
                ExclusionCount = null, RunningMode = null,
            },
            "exclusions-unread-mode-unread-scan-unread" => Fine() with
            {
                ExclusionCount = null, RunningMode = null,
                LastQuickScanUtc = null, LastFullScanUtc = null,
            },
            "exclusions-unread-and-real-time-off" =>
                Fine() with { ExclusionCount = null, RealTimeOn = false },
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

        // The headline as well, which the family's older tests do not reach:
        // its English Caption renders straight to the reader whenever the key
        // is missing, so a caption that never arrived in the resx would look
        // right in English and be invisible in Turkish.
        if (finding.Headline is not { } h) return;
        Assert.True(en.TryGetValue(h.ValueKey, out var value),
            $"{h.ValueKey} is missing from Strings.resx");
        Assert.Equal(h.Value, string.Format(CultureInfo.InvariantCulture,
            value!, h.ValueArgs.ToArray<object>()));
        Assert.True(en.TryGetValue(h.CaptionKey, out var caption),
            $"{h.CaptionKey} is missing from Strings.resx");
        Assert.Equal(h.Caption, caption);
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

    /// FIVE CLAUSES IN EVERY SENTENCE THE RULE ASSEMBLES, so a headline can
    /// never name a reading the evidence beside it left out — the state that
    /// could not read the exclusion list still says how old the scan is, and
    /// the state with no scan still says what is skipped.
    ///
    /// Matched on the words each clause is built from rather than the whole
    /// string: both forms of a clause share them, which is what makes this a
    /// check that the SLOT was filled either way.
    [Theory]
    [MemberData(nameof(EveryStateItCanReport))]
    public void EveryEvidence_CarriesAllFiveClauses(string state)
    {
        var finding = Detect(state);
        if (finding.TitleKey.EndsWith(".title.unread", StringComparison.Ordinal)
            && finding.EvidenceKey == $"rule.{DefenderRule.RuleId}.evidence.unread")
            return;   // nothing was read at all; that sentence has no clauses

        foreach (var (clause, words) in new[]
                 {
                     ("what is skipped", new[] { "excluded from scanning", "exclusion list" }),
                     ("the protections", new[] { "protections brisk checks" }),
                     ("the mode", new[] { "running mode" }),
                     ("the scan", new[] { "completed scan", "completed a scan" }),
                     ("where the paths are", new[] { "on this page", "nothing past it" }),
                 })
            Assert.True(words.Any(w => finding.Evidence.Contains(w, StringComparison.Ordinal)),
                $"{state}: the evidence says nothing about {clause} — " +
                $"\"{finding.Evidence}\"");
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
