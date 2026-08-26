using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BriskEngine;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.RealProbes;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

/// Delivery Optimization: how many bytes this machine uploaded to other
/// machines. The one disclosure in this wave that does not read the
/// registry, and the one report-only disclosure that answers a reading it
/// took successfully with silence. The six telemetry switches beside it do
/// the same for a switch that reads as off, so the silence is not new — a
/// disclosure choosing it is.
///
/// A NUMBER, NOTHING, AND NO ANSWER ARE THREE THINGS. A machine that
/// uploaded 302 MB has something to disclose. A machine that uploaded
/// nothing has nothing to disclose, so brisk says nothing at all. A machine
/// brisk could not ask has an admission to make, and it is not "0 bytes" —
/// that is the same lie the other three disclosures refuse to tell, told
/// about a counter instead of a registry key. The pair
/// NothingUploaded_IsNoFindingAtAll and
/// ACounterItCouldNotRead_IsReportedAsUnread_NotAsNothingUploaded is that
/// rule: they plant readings that differ only in whether the probe answered,
/// and they demand different behaviour.
public class DeliveryOptimizationRuleTests
{
    private const string Id = "delivery-optimization";

    /// The whole fixture: a context whose Delivery Optimization probe
    /// answers exactly what a test plants, including by not answering.
    private static DiagnosticContext Context(long? bytes) =>
        TestContext.Empty() with
        { DeliveryOptimization = new FakeDeliveryOptimization { Bytes = bytes } };

    private static DiagnosticFinding? Detect(long? bytes) =>
        new DeliveryOptimizationRule().Detect(Context(bytes));

    /// Every string a finding can put in front of a user, arguments
    /// included: an argument is as visible as the sentence it lands in.
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

    /// About 302 MB to peers on the local network — the figure this machine
    /// actually reported — plus a megabyte over the internet. The two halves
    /// are deliberately unequal so that a parser reporting either one alone
    /// reads differently from one reporting the total.
    private const long Lan = 317_000_384;
    private const long Internet = 1_048_576;
    private const long Total = Lan + Internet;

    // ---- the three readings ---------------------------------------------

    /// A real number leads with that number, formatted the way every other
    /// byte figure in brisk is formatted. Fmt.Bytes and not a raw long: the
    /// headline is what a reader sees first, and a nine-digit count of bytes
    /// is not a quantity anybody reads.
    [Fact]
    public void ARealNumber_LeadsWithThatNumber_FormattedAsBytes()
    {
        var finding = Detect(Total);

        Assert.NotNull(finding);
        Assert.True(finding!.Headline is not null,
            "the counter answered with a number and the finding led with no headline");
        Assert.True(finding.Headline!.Value == Fmt.Bytes(Total),
            $"the counter read {Total} bytes and the headline leads with " +
            $"\"{finding.Headline.Value}\", not \"{Fmt.Bytes(Total)}\"");
        Assert.Contains(Fmt.Bytes(Total), finding.Evidence, StringComparison.Ordinal);
    }

    /// Nothing uploaded is nothing to disclose: a row leading with "0 B"
    /// leads with a number no reader needs. This is the only report-only
    /// disclosure that stays silent on a reading it actually took, and the
    /// test beside it is what stops that silence from spreading to the
    /// reading it never got.
    [Fact]
    public void NothingUploaded_IsNoFindingAtAll()
    {
        var finding = Detect(0);

        Assert.True(finding is null,
            "the counter read zero — nothing to disclose — and the rule still " +
            $"reported \"{finding?.Title}\"");
    }

    /// The heart of the task. A counter brisk could not read is not a
    /// counter reading zero, and the two must not come out of this rule
    /// looking alike: zero is silence, and no answer is an admission. The
    /// admission carries no headline — a headline is what a finding leads
    /// with, and brisk has no reading to lead with — and no digit anywhere a
    /// reader can see one, because a digit in that sentence would be a
    /// quantity brisk never measured.
    [Fact]
    public void ACounterItCouldNotRead_IsReportedAsUnread_NotAsNothingUploaded()
    {
        var unread = Detect(null);

        Assert.True(unread is not null,
            "the counter could not be read and the rule said nothing at all — " +
            "which is what it says when the counter reads zero, so a machine " +
            "brisk could not ask now looks like a machine that uploaded nothing");
        Assert.True(unread!.Headline is null,
            $"brisk could not read the counter and still led with the headline " +
            $"\"{unread.Headline?.Value}\", which is a reading it did not get");
        var digits = string.Concat(EverythingAReaderWouldSee(unread)
            .SelectMany(s => s.Where(char.IsDigit)));   // any digit at all
        Assert.True(digits.Length == 0,
            $"brisk could not read the counter and still put the digits " +
            $"\"{digits}\" in front of the user");
        Assert.True(unread.TitleKey == $"rule.{Id}.title.unread",
            $"the unread reading is titled {unread.TitleKey}");
        Assert.Equal($"rule.{Id}.evidence.unread", unread.EvidenceKey);
    }

    /// A total below zero is not a count of bytes, so it is not reported as
    /// one — and it is not quietly rounded into "nothing uploaded" either,
    /// which would turn a reading brisk cannot make sense of into the most
    /// reassuring sentence available.
    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void AnUploadFigureBelowZero_IsNotACount(long bytes)
    {
        var finding = Detect(bytes);

        Assert.True(finding is not null && finding.TitleKey == $"rule.{Id}.title.unread",
            $"{bytes} is not a count of bytes and the rule reported " +
            $"\"{finding?.TitleKey ?? "no finding at all"}\"");
    }

    // ---- the shape the privacy disclosures share -------------------------

    /// Report-only: Advise is the consent level FixRunner refuses to apply a
    /// fix for at all, so "brisk shows the number and nothing else" is a
    /// property of the build rather than a promise in the copy.
    [Fact]
    public void TheDisclosure_IsAdviseAndCannotBeFixed()
    {
        var rule = new DeliveryOptimizationRule();
        var finding = rule.Detect(Context(Total));

        Assert.True(rule.Category == RuleCategory.Advise,
            $"{Id} ships as {rule.Category}; a report-only disclosure is Advise");
        Assert.NotNull(finding);
        Assert.False(finding!.CanFix, $"{Id} reports a number brisk offers no fix for");
        Assert.Null(finding.FixDescription);
        Assert.Equal(rule.Category, finding.Category);
    }

    /// Privacy is a second axis: brisk shows it and never grades it. Both
    /// readings that produce a finding are checked, because the unread one is
    /// the finding a machine with no readable counter actually gets.
    [Theory]
    [InlineData(Total)]
    [InlineData(null)]
    public void TheFinding_IsANotice_AndCostsTheHealthScoreNothing(long? bytes)
    {
        var finding = Detect(bytes)!;

        Assert.True(finding.Kind == FindingKind.Notice,
            $"{Id} ships as {finding.Kind}; every finding in this wave is a Notice");
        Assert.True(HealthScore.Compute(new[] { finding }) == 100,
            $"{Id} moved the health score to {HealthScore.Compute(new[] { finding })}");
    }

    /// The impact scale measures expected PERFORMANCE impact. Bytes that
    /// already left this machine cost it none — one rather than zero because
    /// the field is documented 1..5 and a surface reusing the finding row
    /// renders a meter over whatever number it is handed.
    [Theory]
    [InlineData(Total)]
    [InlineData(null)]
    public void TheFinding_ClaimsNoPerformanceImpact(long? bytes)
    {
        var finding = Detect(bytes)!;

        Assert.True(finding.ImpactStars == 1,
            $"{Id} claims {finding.ImpactStars} stars of performance impact; " +
            "an upload that already happened costs none, and 1 is the floor of " +
            "the documented 1..5 scale");
        Assert.True(finding.Severity == Severity.Info,
            $"{Id} ships as {finding.Severity}");
    }

    /// A rule brisk never runs is a rule that never fires.
    [Fact]
    public void TheDisclosure_IsRegisteredExactlyOnce()
    {
        Assert.True(DiagnosticRuleRegistry.All.Count(r => r.Id == Id) == 1,
            $"'{Id}' appears {DiagnosticRuleRegistry.All.Count(r => r.Id == Id)} " +
            "times in DiagnosticRuleRegistry.All");
    }

    /// The keys a localized surface renders instead of the engine's English.
    [Fact]
    public void AReadableCount_LeadsWithItsOwnHeadlineKeys()
    {
        var finding = Detect(Total)!;

        Assert.Equal(Id, finding.RuleId);
        Assert.Equal($"rule.{Id}.headline.value", finding.Headline!.ValueKey);
        Assert.Equal($"rule.{Id}.headline.caption", finding.Headline.CaptionKey);
        Assert.Equal($"rule.{Id}.title", finding.TitleKey);
        Assert.Equal($"rule.{Id}.evidence", finding.EvidenceKey);
    }

    // ---- one claim, two languages ---------------------------------------

    /// The engine ships English prose the CLI prints verbatim and a resx key
    /// the GUI renders instead, and the two have to say the same thing. Over
    /// both readings that produce a finding, because a machine that lands on
    /// the unread one has that sentence and no other.
    [Theory]
    [InlineData(Total)]
    [InlineData(null)]
    public void TheEnglishResx_SaysWhatTheEngineSays(long? bytes)
    {
        var en = PrivacyDisclosureRuleTests.Resx("Strings.resx");
        var finding = Detect(bytes)!;

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
    [InlineData(Total)]
    [InlineData(null)]
    public void EveryKeyTheRuleNames_IsInBothLanguages(long? bytes)
    {
        var finding = Detect(bytes)!;
        var keys = new List<string>
            { finding.TitleKey, finding.EvidenceKey!, $"rule.{Id}.advice" };
        if (finding.Headline is { } h) { keys.Add(h.ValueKey); keys.Add(h.CaptionKey); }

        foreach (var file in new[] { "Strings.resx", "Strings.tr.resx" })
        {
            var strings = PrivacyDisclosureRuleTests.Resx(file);
            foreach (var key in keys)
                Assert.True(strings.ContainsKey(key), $"{key} is missing from {file}");
        }
    }

    /// The wave's first red line, read off disk in both languages. brisk
    /// measures bytes that left this machine; it says nothing about who
    /// received them, and this is the rule where that temptation is largest
    /// because the bytes demonstrably went somewhere.
    [Fact]
    public void NoCopy_ClaimsAnythingAboutWhoReceivesWhat()
    {
        string[] forbidden =
        {
            "Microsoft", "sends", "sent", "sees", "receives", "collect",
            "gönderi", "gidiyor", "görüyor", "topluyor",
        };

        foreach (var file in new[] { "Strings.resx", "Strings.tr.resx" })
        foreach (var (key, text) in PrivacyDisclosureRuleTests.Resx(file))
        {
            if (!key.StartsWith($"rule.{Id}.", StringComparison.Ordinal)) continue;
            foreach (var word in forbidden)
                Assert.False(text.Contains(word, StringComparison.OrdinalIgnoreCase),
                    $"{key} in {file} says \"{word}\" — brisk reads this machine and " +
                    "makes no claim about what leaves it or who receives it");
        }
    }

    // ---- the probe behind it --------------------------------------------

    /// The parser requires a month marker, so every fixture in this section
    /// carries one. Without that, a case meant to prove a missing upload
    /// half is refused would pass because the MARKER was missing instead —
    /// green for a reason it does not name, which is the failure a fixture
    /// helper like this one exists to prevent.
    ///
    /// The value is the one this machine's snapshot carried. Whether every
    /// Windows build writes such a marker is not something one machine
    /// establishes — and if one does not, brisk reports that counter as
    /// unread, which is the cost this check accepts.
    private const string MonthMarker =
        ",\"MonthStartDate\":\"\\/Date(1785531600008)\\/\"";

    private static string WithMonth(string fields) => "{" + fields + MonthMarker + "}";

    /// The half of the real probe that can be tested without a machine that
    /// uploads anything: what it makes of the text the cmdlet printed. The
    /// sample below is the real output of
    /// Get-DeliveryOptimizationPerfSnapThisMonth on the machine this was
    /// written on, trimmed of nothing.
    private const string RealSnapshotJson =
        "{\"UploadLanBytes\":317000384,\"UploadInternetBytes\":0," +
        "\"DownloadHttpBytes\":9786010671,\"DownloadCacheHostBytes\":6148715417," +
        "\"DownloadLanBytes\":0,\"DownloadInternetBytes\":0," +
        "\"DownloadFgRateKbps\":13312,\"DownloadBgRateKbps\":11530," +
        "\"UploadLimitReached\":false,\"MonthStartDate\":\"\\/Date(1785531600008)\\/\"}";

    /// Both halves, added. A parser that read only the local-network figure
    /// would have answered 302 MB on this machine and been right by accident,
    /// which is why the assertion below plants an internet figure the real
    /// sample does not have.
    [Fact]
    public void TheParser_AddsBothUploadFigures()
    {
        Assert.Equal(317_000_384L,
            RealDeliveryOptimizationProbe.ParseUploadedBytes(RealSnapshotJson));

        var both = RealDeliveryOptimizationProbe.ParseUploadedBytes(
            WithMonth("\"UploadLanBytes\":10,\"UploadInternetBytes\":7"));
        Assert.True(both == 17,
            "10 bytes to the local network and 7 over the internet is 17 bytes " +
            $"uploaded, and the parser read {both?.ToString() ?? "nothing at all"}");
    }

    /// A snapshot carrying one half is a shape brisk only half recognises,
    /// and reporting the half it got would under-report in the direction that
    /// reassures. Unread, not a total. Each case carries the month marker, so
    /// the only thing missing is the half it names.
    [Theory]
    [InlineData("\"UploadLanBytes\":10")]
    [InlineData("\"UploadInternetBytes\":10")]
    [InlineData("\"UploadLanBytes\":10,\"UploadInternetBytes\":\"seven\"")]
    public void ASnapshotMissingEitherHalf_IsNotATotal(string fields)
    {
        var read = RealDeliveryOptimizationProbe.ParseUploadedBytes(WithMonth(fields));

        Assert.True(read is null,
            $"{WithMonth(fields)} was read as {read} bytes uploaded, and brisk " +
            "only read one of the two figures that make up that total");
    }

    /// brisk's copy says "for the current calendar month". Until this check
    /// existed that clause rested on the cmdlet's name and on nothing the
    /// read had seen — a snapshot carrying both upload halves and no month
    /// marker would have been reported as a month's total. It is not.
    [Fact]
    public void ASnapshotWithNoMonthMarker_IsNotAMonthsTotal()
    {
        var read = RealDeliveryOptimizationProbe.ParseUploadedBytes(
            "{\"UploadLanBytes\":10,\"UploadInternetBytes\":7}");

        Assert.True(read is null,
            $"a snapshot with no {RealDeliveryOptimizationProbe.MonthField} was read " +
            $"as {read} bytes uploaded this month, which is a window brisk never saw");
    }

    /// Nothing the cmdlet could print, or fail to print, becomes a number.
    /// The last case is a whole, well-formed month snapshot whose total comes
    /// out below zero, so it carries the marker: what it tests is the total,
    /// not the shape.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"UploadLanBytes\":-2,\"UploadInternetBytes\":1," +
                "\"MonthStartDate\":\"\\/Date(1785531600008)\\/\"}")]
    public void OutputItCannotRead_IsNotZero(string? json)
    {
        var read = RealDeliveryOptimizationProbe.ParseUploadedBytes(json);

        Assert.True(read is null,
            $"the parser turned {json ?? "no output at all"} into {read}");
    }

    /// The cmdlet the probe actually asks for, pinned as a literal. The plan
    /// named Get-DeliveryOptimizationPerfSnap and its BytesToPeers field; on
    /// the machine this was written on that cmdlet answers with no field of
    /// that name, so a probe built to the plan's letter would have reported
    /// "unreadable" beside a counter that was sitting there readable. This
    /// test is where that correction is visible rather than buried.
    [Fact]
    public void TheProbe_AsksForTheMonthCounter_AndSwallowsItsOwnErrors()
    {
        var args = RealDeliveryOptimizationProbe.Arguments;

        Assert.Contains("Get-DeliveryOptimizationPerfSnapThisMonth", args,
            StringComparison.Ordinal);
        Assert.True(args.Contains("catch { exit 1 }", StringComparison.Ordinal),
            "the command does not swallow its own failure, so PowerShell's error " +
            $"text would print into the middle of `brisk scan`: {args}");
        Assert.True(args.Contains("-NonInteractive", StringComparison.Ordinal)
                 && args.Contains("-NoProfile", StringComparison.Ordinal),
            $"the command can stop for a prompt or load a user profile: {args}");
        Assert.Equal("UploadLanBytes", RealDeliveryOptimizationProbe.LanField);
        Assert.Equal("UploadInternetBytes", RealDeliveryOptimizationProbe.InternetField);
        Assert.Equal("MonthStartDate", RealDeliveryOptimizationProbe.MonthField);
    }

    /// The one thing about the real probe that no fake can establish, and the
    /// one that matters most: it runs inside a scan, and EngineHost's
    /// catch-all would swallow an exception along with the whole finding. So
    /// this launches the real process on whatever machine the suite runs on
    /// and asserts nothing about the answer — a number and an honest null are
    /// both passes here, and only a throw is a failure.
    [Fact]
    public void TheRealProbe_AnswersWithoutThrowing()
    {
        var probe = new RealDeliveryOptimizationProbe();

        var thrown = Record.Exception(() => probe.BytesUploadedToPeers());

        Assert.True(thrown is null,
            $"the real probe threw {thrown?.GetType().Name} into a scan: " +
            thrown?.Message);
    }
}
