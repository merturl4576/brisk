using System;
using System.Collections.Generic;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

/// Every millisecond in this file came off the maintainer's machine, read out
/// of Microsoft-Windows-Diagnostics-Performance/Operational. The pairings
/// matter as much as the numbers: the 51237 ms boot really had nobody blamed
/// while the faster 45333 ms boot had two, which is why nothing here expects
/// the boot time and the blamed programs to add up.
public class BootDegradationRuleTests
{
    private static DiagnosticContext Context(params BootRecord[] boots)
    {
        var log = new FakeEventLog();
        log.Boots.AddRange(boots);
        return TestContext.Empty() with { EventLog = log };
    }

    private static BootRecord Boot(int bootMs, params BootOffender[] offenders) =>
        new(new DateTime(2026, 8, 17, 20, 40, 16, DateTimeKind.Utc).AddDays(-offenders.Length),
            bootMs, MainPathMs: 24437, offenders);

    private static BootOffender Blamed(string name, string friendly, int degradationMs) =>
        new(name, friendly, $@"C:\Program Files\{name}", degradationMs);

    [Fact]
    public void SlowBootWithABlamedProgram_IsAnAdviseFinding()
    {
        var ctx = Context(
            Boot(51237),
            Boot(111814, Blamed("Spotify.exe", "Spotify", 37141)),
            Boot(57089));

        var finding = new BootDegradationRule().Detect(ctx);

        Assert.NotNull(finding);
        Assert.Equal("boot-degradation", finding!.RuleId);
        Assert.Equal(RuleCategory.Advise, finding.Category);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(4, finding.ImpactStars);
        Assert.False(finding.CanFix);
        Assert.Null(finding.FixDescription);
        Assert.Contains("Spotify", finding.Evidence);
        Assert.Contains("57 s", finding.Evidence);       // the median, not the worst
        Assert.Equal("rule.boot-degradation.evidence", finding.EvidenceKey);
    }

    /// The blamed program's degradation must never be presented as a share of
    /// the boot: 37 s of a 57 s boot is the sentence this rule exists to not
    /// write. Nothing in the output may read as a subtraction or a total.
    [Fact]
    public void Evidence_NeverJoinsBootTimeAndBlameWithASum()
    {
        var ctx = Context(
            Boot(51237),
            Boot(111814, Blamed("Spotify.exe", "Spotify", 37141)),
            Boot(57089));

        var evidence = new BootDegradationRule().Detect(ctx)!.Evidence;

        Assert.DoesNotContain(" of it", evidence);
        Assert.DoesNotContain("belongs to", evidence);
        // 57089 - 37141 = 19948 -> "20 s"; the total 94230 ms -> "94 s".
        Assert.DoesNotContain("20 s", evidence);
        Assert.DoesNotContain("94 s", evidence);
    }

    [Fact]
    public void TwoBoots_NotEnoughToJudge()
    {
        var ctx = Context(
            Boot(111814, Blamed("Spotify.exe", "Spotify", 37141)),
            Boot(57089));
        Assert.Null(new BootDegradationRule().Detect(ctx));
    }

    [Fact]
    public void OneBadBootAmongFastOnes_IsNotASlowMachine()
    {
        var ctx = Context(Boot(18000), Boot(111814), Boot(19000));
        Assert.Null(new BootDegradationRule().Detect(ctx));
    }

    [Fact]
    public void NoBoots_NoFinding()
    {
        Assert.Null(new BootDegradationRule().Detect(Context()));
    }

    [Fact]
    public void EmptyEventLogProbe_NoFinding()
    {
        Assert.Null(new BootDegradationRule().Detect(TestContext.Empty()));
    }

    /// Real numbers: Defender was blamed for 7694 ms on one boot and 52661 ms
    /// on another. One row, the worst reading, never a sum and never twice.
    [Fact]
    public void SameProgramOnTwoBoots_ReportedOnceAtItsWorst()
    {
        var ctx = Context(
            Boot(94317, Blamed("MsMpEng.exe", "Antimalware Service Executable", 7694)),
            Boot(100288, Blamed("MsMpEng.exe", "Antimalware Service Executable", 52661)),
            Boot(51237));

        var finding = new BootDegradationRule().Detect(ctx)!;
        var evidence = finding.Evidence;

        // Exact, not Contains: "8 s" would also match "48 s" and "60 s" would
        // match "160 s", so a substring check could pass for the wrong reason.
        // This pins the whole row — the worst reading, never the other one and
        // never their sum.
        Assert.Equal("Antimalware Service Executable 53 s (2/3)", finding.EvidenceArgs![2]);
        Assert.Contains("53 s", evidence);              // 52661 ms
        Assert.DoesNotContain("7694", evidence);
        var first = evidence.IndexOf("Antimalware", StringComparison.Ordinal);
        Assert.Equal(first, evidence.LastIndexOf("Antimalware", StringComparison.Ordinal));
    }

    /// Three of the ten most recent boots on the maintainer's machine named
    /// nobody, including the newest and slowest. That is a normal outcome and
    /// must read as one — the rule has the answer, there simply is no name in it.
    [Fact]
    public void SlowBootNobodyWasBlamedFor_ReadsAsAnAnswerNotAGap()
    {
        var ctx = Context(Boot(51237), Boot(74005), Boot(59684));

        var finding = new BootDegradationRule().Detect(ctx);

        Assert.NotNull(finding);
        Assert.Equal("rule.boot-degradation.evidence.nobody", finding!.EvidenceKey);
        Assert.Contains("60 s", finding.Evidence);       // 59684 ms, the median
        Assert.DoesNotContain("unknown", finding.Evidence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("could not", finding.Evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, finding.EvidenceArgs!.Count);
        Assert.Equal("60 s", finding.EvidenceArgs[0]);
    }

    /// brisk-app.exe itself arrived with an empty FriendlyName. A blank in the
    /// sentence would be worse than the raw executable name.
    [Fact]
    public void EmptyFriendlyName_FallsBackToTheExecutableName()
    {
        var ctx = Context(
            Boot(51237),
            Boot(111814, Blamed("brisk-app.exe", "", 26081)),
            Boot(57089));

        var evidence = new BootDegradationRule().Detect(ctx)!.Evidence;

        Assert.Contains("brisk-app.exe 26 s", evidence);
        Assert.DoesNotContain("  ", evidence);
        Assert.DoesNotContain(": 26 s", evidence);
    }

    [Fact]
    public void FriendlyName_PreferredWhenWindowsRecordedOne()
    {
        var ctx = Context(
            Boot(51237),
            Boot(111814, Blamed("TiWorker.exe", "Windows Modules Installer Worker", 9244)),
            Boot(57089));

        var evidence = new BootDegradationRule().Detect(ctx)!.Evidence;

        Assert.Contains("Windows Modules Installer Worker 9 s", evidence);
        Assert.DoesNotContain("TiWorker.exe", evidence);
    }

    /// Windows does not always record MainPathBootTime. Nothing may be
    /// subtracted from it, and its absence may not cost the finding.
    [Fact]
    public void NullMainPath_NoCrashAndNoArithmetic()
    {
        var ctx = Context(
            new BootRecord(new DateTime(2026, 8, 17, 20, 40, 16, DateTimeKind.Utc), 51237,
                MainPathMs: null, Array.Empty<BootOffender>()),
            new BootRecord(new DateTime(2026, 8, 16, 22, 28, 43, DateTimeKind.Utc), 111814,
                MainPathMs: null, new[] { Blamed("Spotify.exe", "Spotify", 37141) }),
            new BootRecord(new DateTime(2026, 8, 15, 21, 22, 14, DateTimeKind.Utc), 57089,
                MainPathMs: 23289, Array.Empty<BootOffender>()));

        var finding = new BootDegradationRule().Detect(ctx);

        Assert.NotNull(finding);
        Assert.Contains("57 s", finding!.Evidence);
        // 57089 - 23289 = 33800, the post-boot phase Windows already publishes.
        Assert.DoesNotContain("34 s", finding.Evidence);
        Assert.DoesNotContain("33800", finding.Evidence);
    }

    [Fact]
    public void MoreThanThreeBlamedPrograms_OnlyTheWorstThreeAreNamed()
    {
        var ctx = Context(
            Boot(94317,
                Blamed("msedgewebview2.exe", "Microsoft Edge WebView2", 36541),
                Blamed("MsMpEng.exe", "Antimalware Service Executable", 7694),
                Blamed("mscorsvw.exe", ".NET Runtime Optimization Service", 4902),
                Blamed("updater.exe", "Google Updater (x64)", 4893)),
            Boot(100288),
            Boot(51237));

        var evidence = new BootDegradationRule().Detect(ctx)!.Evidence;

        Assert.Contains("Microsoft Edge WebView2 37 s", evidence);
        Assert.Contains("Antimalware Service Executable 8 s", evidence);
        Assert.Contains(".NET Runtime Optimization Service 5 s", evidence);
        Assert.DoesNotContain("Google Updater", evidence);
        // Worst first, so the biggest number a user sees is the biggest one.
        Assert.True(evidence.IndexOf("Microsoft Edge WebView2", StringComparison.Ordinal)
            < evidence.IndexOf("Antimalware Service Executable", StringComparison.Ordinal));
    }

    /// 40 s exactly is not "over 40 s". A 20 s boot is not a finding and the
    /// boundary must not drift into one.
    [Fact]
    public void MedianExactlyAtTheThreshold_NoFinding()
    {
        var ctx = Context(Boot(39000), Boot(40000), Boot(41000));
        Assert.Null(new BootDegradationRule().Detect(ctx));
    }

    [Fact]
    public void MedianJustOverTheThreshold_IsAFinding()
    {
        var ctx = Context(Boot(39000), Boot(40001), Boot(41000));
        Assert.NotNull(new BootDegradationRule().Detect(ctx));
    }

    /// The sample is the last 8 boots. A ninth, older, catastrophic boot must
    /// not move the median, and a program blamed only on it is not "recent".
    [Fact]
    public void OnlyTheLastEightBootsAreSampled()
    {
        var boots = new List<BootRecord>();
        for (var i = 0; i < 8; i++) boots.Add(Boot(45000));
        boots.Add(Boot(400000, Blamed("Ancient.exe", "Ancient", 99000)));

        var finding = new BootDegradationRule().Detect(Context(boots.ToArray()));

        Assert.NotNull(finding);
        Assert.Contains("45 s", finding!.Evidence);
        Assert.DoesNotContain("Ancient", finding.Evidence);
        Assert.Equal("8", finding.EvidenceArgs![1]);   // exact: "18" contains "8"
    }

    /// Eight DISTINCT boots, so the three candidate definitions disagree and
    /// only one passes. Lower middle is 47000; the upper middle would be 49000
    /// and the average of the two 48000. Switching to an average would make
    /// "the middle of the boots brisk could read" false — it would name a boot
    /// this machine never had — and with eight identical values nothing would
    /// have caught it.
    [Fact]
    public void EvenSample_ReportsTheLowerMiddle_NotAnAverage()
    {
        var ctx = Context(Boot(55000), Boot(41000), Boot(49000), Boot(43000),
                          Boot(53000), Boot(45000), Boot(51000), Boot(47000));

        var finding = new BootDegradationRule().Detect(ctx)!;

        Assert.Equal("47 s", finding.EvidenceArgs![0]);
        Assert.Contains("47 s", finding.Evidence);
    }

    /// The 25-offender bound is applied whole boots at a time. The boot that
    /// crosses it contributes ALL of its offenders, because cutting inside one
    /// boot could hand back a smaller "worst" reading for a program than
    /// Windows actually recorded — the truncation IEventLogProbe was shaped to
    /// avoid. The boot after it contributes none.
    [Fact]
    public void OffenderBound_AdmitsWholeBoots_NeverHalfOfOne()
    {
        var crowded = new BootOffender[24];
        for (var i = 0; i < crowded.Length; i++)
            crowded[i] = Blamed($"Small{i}.exe", $"Small {i}", 1000);

        var ctx = Context(
            Boot(51237, crowded),                       // 24 seen, still under 25
            Boot(57089,                                 // crosses it, all three land
                Blamed("Straddler.exe", "Straddler", 90000),
                Blamed("Bee.exe", "Bee", 60000),
                Blamed("Cee.exe", "Cee", 50000)),
            Boot(111814,                                // past the bound, dropped
                Blamed("Beyond.exe", "Beyond", 200000)));

        var evidence = new BootDegradationRule().Detect(ctx)!.Evidence;

        Assert.Contains("Straddler 90 s", evidence);
        Assert.Contains("Bee 60 s", evidence);
        Assert.Contains("Cee 50 s", evidence);
        // Would have been the worst of all if the bound cut per offender
        // instead of per boot, so its absence is what proves the granularity.
        Assert.DoesNotContain("Beyond", evidence);
    }

    /// ...and the newest boot is always read, however many programs Windows
    /// blamed on it. A bound that could skip it would silently drop the most
    /// recent evidence there is.
    [Fact]
    public void OffenderBound_AlwaysReadsTheNewestBoot()
    {
        var crowded = new BootOffender[30];
        for (var i = 0; i < crowded.Length; i++)
            crowded[i] = Blamed($"Small{i}.exe", $"Small {i}", 1000 + i);
        crowded[0] = Blamed("First.exe", "First", 90000);

        var ctx = Context(Boot(51237, crowded), Boot(57089), Boot(111814));

        Assert.Contains("First 90 s", new BootDegradationRule().Detect(ctx)!.Evidence);
    }

    /// The evidence args are what the Turkish template renders from, so they
    /// carry readings only — no English connective prose may hide in them.
    /// The bracketed count is a reading and stays language-neutral for the
    /// same reason "37 s" does: the words explaining it live in the resx
    /// sentence around it, in whichever language rendered it.
    [Fact]
    public void EvidenceArgs_CarryReadingsNotSentences()
    {
        var ctx = Context(
            Boot(51237),
            Boot(111814, Blamed("Spotify.exe", "Spotify", 37141)),
            Boot(57089));

        var args = new BootDegradationRule().Detect(ctx)!.EvidenceArgs!;

        Assert.Equal(3, args.Count);
        Assert.Equal("57 s", args[0]);
        Assert.Equal("3", args[1]);
        Assert.Equal("Spotify 37 s (1/3)", args[2]);
    }

    /// A rule must never throw out of Detect, whatever the probe hands back.
    [Fact]
    public void ProbeReturningRubbish_DoesNotThrow()
    {
        var ctx = Context(
            Boot(51237, Blamed("", "", 5000)),
            Boot(111814, Blamed("x", "   ", 0)),
            Boot(57089, Blamed("y", "y", -1)));

        var error = Record.Exception(() => new BootDegradationRule().Detect(ctx));
        Assert.Null(error);
    }

    /// The defect this pins came off the maintainer's machine on 2026-08-20.
    /// The top-ranked offender was brisk-app.exe at 26 s — blamed on exactly
    /// one of the eight sampled boots, three days earlier, while wave 1's
    /// autostart was being tested, and no longer starting with Windows at all.
    /// It sat in the list looking exactly like TiWorker, which Windows blamed
    /// on four of those eight, above a sentence telling the reader to go find
    /// the rest under Startup programs. There was nothing to find.
    ///
    /// One reading out of eight boots is an anecdote and four is a pattern,
    /// and the rule already keeps them apart internally — WorstPerProgram
    /// holds the worst reading per program precisely so a program blamed twice
    /// is not summed into a bigger one. The copy threw that away.
    [Fact]
    public void Evidence_SaysHowManyOfTheSampledBootsBlamedEachProgram()
    {
        var ctx = Context(
            Boot(51237, Blamed("TiWorker.exe", "Windows Modules Installer Worker", 9244)),
            Boot(111814, Blamed("brisk-app.exe", "", 26081),
                         Blamed("TiWorker.exe", "Windows Modules Installer Worker", 1998)),
            Boot(57089, Blamed("TiWorker.exe", "Windows Modules Installer Worker", 511)));

        var finding = new BootDegradationRule().Detect(ctx);

        Assert.NotNull(finding);
        Assert.Contains("brisk-app.exe 26 s (1/3)", finding!.Evidence);
        Assert.Contains("Windows Modules Installer Worker 9 s (3/3)", finding.Evidence);
    }

    /// A bare "(1/3)" beside a program name is not self-explanatory, and an
    /// unexplained number in a sentence about two other numbers is worse than
    /// no number: this rule's whole discipline is that no two figures in it
    /// may be read as arithmetic on each other.
    [Fact]
    public void Evidence_ExplainsWhatTheBracketedFigureCounts()
    {
        var ctx = Context(
            Boot(51237),
            Boot(111814, Blamed("Spotify.exe", "Spotify", 37141)),
            Boot(57089));

        var finding = new BootDegradationRule().Detect(ctx);

        Assert.NotNull(finding);
        Assert.Contains("how many of those boots", finding!.Evidence);
    }

    /// Windows records one line per program per START, not per boot: on the
    /// verified machine mscorsvw.exe was blamed twice inside a single boot.
    /// Counting records would have called that two boots out of three and
    /// turned the one-off it is into a pattern — the exact misreading the
    /// count was added to prevent.
    [Fact]
    public void ProgramBlamedTwiceInOneBoot_CountsAsOneBoot()
    {
        var ctx = Context(
            Boot(51237),
            Boot(111814, Blamed("mscorsvw.exe", "", 795),
                         Blamed("mscorsvw.exe", "", 4902)),
            Boot(57089));

        var finding = new BootDegradationRule().Detect(ctx);

        Assert.NotNull(finding);
        Assert.Contains("mscorsvw.exe 5 s (1/3)", finding!.Evidence);
    }

    /// The bound can stop the walk before the sample ends, and then the count
    /// and the median describe different sets of boots. The denominator is the
    /// boots actually counted, so "(1/2)" is never printed as "(1/3)" — a
    /// ratio of a subset, stated as that subset.
    [Fact]
    public void RecordBoundStopsTheWalk_DenominatorIsTheBootsActuallyCounted()
    {
        var crowded = new BootOffender[26];
        for (var i = 0; i < crowded.Length; i++)
            crowded[i] = Blamed($"Small{i}.exe", $"Small {i}", 1000 + i);
        crowded[0] = Blamed("First.exe", "First", 90000);

        var ctx = Context(Boot(51237, crowded), Boot(57089), Boot(111814));

        var evidence = new BootDegradationRule().Detect(ctx)!.Evidence;

        Assert.Contains("First 90 s (1/1)", evidence);
        Assert.Contains("the middle of the 3 most recent boots", evidence);
    }

    /// The headline is the median (57089 ms of these three -> "57 s"), never
    /// the worst boot — the same number the evidence sentence leads with.
    [Fact]
    public void Headline_CarriesTheMedian_WithBareDigitsForLocalization()
    {
        var ctx = Context(
            Boot(51237),
            Boot(111814, Blamed("Spotify.exe", "Spotify", 37141)),
            Boot(57089));

        var h = new BootDegradationRule().Detect(ctx)!.Headline;

        Assert.NotNull(h);
        Assert.Equal("57 s", h!.Value);
        Assert.Equal("boot time — the middle of the last 3 boots", h.Caption);
        Assert.Equal("rule.boot-degradation.headline.value", h.ValueKey);
        Assert.Equal(new[] { "57" }, h.ValueArgs);
        Assert.Equal("rule.boot-degradation.headline.caption", h.CaptionKey);
        Assert.Equal(new[] { "3" }, h.CaptionArgs);
    }
}
