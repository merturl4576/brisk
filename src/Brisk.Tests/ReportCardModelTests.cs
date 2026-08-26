using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

public class ReportCardModelTests
{
    private static Loc Loc(string lang)
    {
        var loc = new Loc();
        loc.SetLanguage(lang);
        return loc;
    }

    private static Headline H(string value) => new(value, "cap",
        "rule.fake.headline.value", new[] { value },
        "rule.fake.headline.caption", Array.Empty<string>());

    [Fact]
    public void Findings_AreHeadlinePlusTitle_InPickerOrder_NeverEvidence()
    {
        var snapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("aa-fake", cat: RuleCategory.Advise, canFix: false,
                headline: H("13")),
            TestData.Finding("zz-fake", sev: Severity.Critical,
                cat: RuleCategory.Advise, canFix: false, headline: H("57 s")),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
        }, new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Equal(2, card.Findings.Count);                    // headline-less thermals excluded
        Assert.Equal("57 s", card.Findings[0].Lead);             // Critical outranks Warning
        Assert.Equal("Title zz-fake", card.Findings[0].Text);    // the TITLE, never the evidence
        Assert.Equal("13", card.Findings[1].Lead);
        Assert.Equal("", card.FindingsEmptyText);
        Assert.DoesNotContain(card.Findings, l => l.Text.Contains("Evidence"));
    }

    [Fact]
    public void NoHeadlines_KeepsTheSectionWithTheHonestEmptyLine()
    {
        var snapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
        }, new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Empty(card.Findings);
        Assert.Equal(
            $"All {DiagnosticRuleRegistry.All.Count} rules looked — nothing on this machine leads with a number.",
            card.FindingsEmptyText);
    }

    [Theory]
    [InlineData(true, true, null, "en", "Everything brisk tried to read, answered.")]
    [InlineData(true, true, null, "tr", "brisk'in okumaya çalıştığı her şey cevap verdi.")]
    [InlineData(true, false, null, "en", "GPU temperature — not read; brisk cannot tell from here why.")]
    // A GPU-only silence carries no reason, on purpose: a blocklisted kernel
    // driver is not why a GPU sensor is quiet, so the card does not say it is.
    [InlineData(true, false, true, "en", "GPU temperature — not read; brisk cannot tell from here why.")]
    public void UnreadSection_NeverDrops_AndSpeaksTheVariant(
        bool cpu, bool gpu, bool? integrity, string lang, string expected)
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(cpu, gpu, integrity));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc(lang));

        Assert.Equal(new[] { expected }, card.Unread);
    }

    [Theory]
    [InlineData(true, "en", "CPU temperature — not read. Memory integrity is on; the driver that reads it is on Microsoft's vulnerable-driver blocklist.")]
    [InlineData(true, "tr", "CPU sıcaklığı — okunamadı. Bellek bütünlüğü açık; onu okuyan sürücü Microsoft'un güvenlik açığı listesinde.")]
    [InlineData(false, "en", "CPU temperature — not read. Memory integrity is off here, so the usual reason is ruled out; brisk cannot tell what did it.")]
    public void CpuUnread_CarriesTheMeasuredIntegrityVariant(
        bool? integrity, string lang, string expected)
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(false, true, integrity));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc(lang));

        Assert.Equal(new[] { expected }, card.Unread);
    }

    [Fact]
    public void CpuUnread_UnknownIntegrity_KeepsTheHedge()
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(false, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Equal(
            new[] { "CPU temperature — not read. The driver that reads it will not load "
                + "while memory integrity is on; brisk could not confirm that is the reason here." },
            card.Unread);
    }

    /// The mirror of the test above, and the defect it was written against:
    /// the neither-answered line used to drop the measured reason entirely, so
    /// on an HVCI machine with no readable GPU sensor `brisk scan` explained
    /// the blocklisted driver and the card explained nothing — two surfaces of
    /// one product disagreeing about the same silent sensor. The CPU went
    /// unread in both cases, so the CPU's reason belongs on both lines.
    [Theory]
    [InlineData(true, "en", "Temperatures — neither sensor answered. Memory integrity is on; the driver that reads CPU temperature is on Microsoft's vulnerable-driver blocklist.")]
    [InlineData(true, "tr", "Sıcaklıklar — iki sensör de cevap vermedi. Bellek bütünlüğü açık; CPU sıcaklığını okuyan sürücü Microsoft'un güvenlik açığı listesinde.")]
    [InlineData(false, "en", "Temperatures — neither sensor answered. Memory integrity is off here, so the usual reason is ruled out; brisk cannot tell what did it.")]
    public void NeitherAnswered_CarriesTheMeasuredIntegrityVariantToo(
        bool? integrity, string lang, string expected)
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(false, false, integrity));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc(lang));

        Assert.Equal(new[] { expected }, card.Unread);
    }

    [Fact]
    public void NeitherAnswered_UnknownIntegrity_KeepsTheHedge()
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(false, false, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Equal(
            new[] { "Temperatures — neither sensor answered. The driver that reads CPU "
                + "temperature will not load while memory integrity is on; brisk could not "
                + "confirm that is the reason here." },
            card.Unread);
    }

    [Fact]
    public void Fixes_AreTitleAndDate_AndTheSectionDropsWhenEmpty()
    {
        var fixes = new[]
        {
            new UndoableFix("power-plan", new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc)),
        };
        var snapshot = TestData.Snapshot(null, new SensorStatus(true, true, null));

        var with = ReportCardModel.Build(snapshot, fixes, Loc("en"));
        var without = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.True(with.HasFixes);
        Assert.Single(with.Fixes);
        // The localized rule title, never the raw id — the exact resx text is
        // pinned elsewhere; here the contract is "not the id, plus the date".
        Assert.DoesNotContain("power-plan", with.Fixes[0]);
        Assert.False(string.IsNullOrWhiteSpace(with.Fixes[0]));
        Assert.Contains("2026-08-20", with.Fixes[0]);
        Assert.False(without.HasFixes);
    }

    /// The card's frame is fixed and nothing in it clips, so a fix list long
    /// enough to outgrow the body used to draw off both ends of the bitmap and
    /// vanish — no error, no test that could see it, and a shareable picture
    /// with its top and bottom sheared off. The journal is uncapped and a
    /// machine that has run fix-all carries eight or ten entries, so the bound
    /// is here and the remainder is counted rather than dropped.
    ///
    /// This card is the one where the bound IS MaxFixRows: one unread line, no
    /// findings overflow, so the sections above take the least they can and
    /// the fix list gets its ceiling. What it gets when they take more is
    /// TheFixList_GivesUpARow_ForEveryLineTheSectionsAboveItTook's subject.
    ///
    /// The counts are derived rather than typed: the third one used to be a
    /// literal 16 called "one per rule in the registry", and the registry has
    /// held more than sixteen rules since this wave's ten landed.
    public static TheoryData<int> FixCounts() => new()
    {
        ReportCardModel.MaxFixRows,        // exactly the budget
        ReportCardModel.MaxFixRows + 1,    // one over
        DiagnosticRuleRegistry.All.Count,  // one per rule the registry ships
    };

    [Theory]
    [MemberData(nameof(FixCounts))]
    public void Fixes_AreCappedAtTheFrame_WithTheRemainderCounted(int count)
    {
        var fixes = Enumerable.Range(0, count)
            .Select(i => new UndoableFix($"rule-{i:00}",
                new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc).AddMinutes(-i)))
            .ToArray();
        var snapshot = TestData.Snapshot(null, new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, fixes, Loc("en"));

        Assert.True(card.Fixes.Count <= ReportCardModel.MaxFixRows,
            $"{count} fixes produced {card.Fixes.Count} rows");
        if (count <= ReportCardModel.MaxFixRows)
        {
            Assert.Equal(count, card.Fixes.Count);
            Assert.DoesNotContain(card.Fixes, row => row.Contains("more"));
            return;
        }
        // The last row is the count, and it counts everything the rows above
        // it did not show — a card that said "and 2 more" while hiding six
        // would be the same untruth in a smaller font.
        Assert.Equal(ReportCardModel.MaxFixRows, card.Fixes.Count);
        var hidden = count - (ReportCardModel.MaxFixRows - 1);
        Assert.Equal(Loc("en").F("report.fixes.more", hidden), card.Fixes[^1]);
    }

    /// THE CARD LEADS WITH FIVE NUMBERS AND COUNTS THE REST.
    ///
    /// The section was described as bounded by "the picker takes five at
    /// most", and nothing enforced it: the picker takes every finding that
    /// carries a headline, and exactly five shipped rules carried one until
    /// this wave's disclosures brought the count to nine. A machine with six of
    /// them AND a full fix list posted a card with a row sheared off at the
    /// frame's edge — 758px measured against the 715px the column gets —
    /// silently, which is the failure this whole budget exists to stop. Six
    /// findings alone did not do it; it took the rest of the column being full
    /// as well, which is why the budget is shared rather than per-section.
    ///
    /// The counts are derived for the same reason FixCounts' are: the last one
    /// was a literal 9 called "one per rule that can carry a headline today",
    /// a number nothing in the tree declares. The registry's size is a true
    /// upper bound on how many findings a scan can carry and is asked for.
    public static TheoryData<int> FindingCounts() => new()
    {
        ReportCardModel.MaxFindingRows,        // exactly the cap
        ReportCardModel.MaxFindingRows + 1,    // one over: five rows and "and 1 more"
        DiagnosticRuleRegistry.All.Count,      // one per rule the registry ships
    };

    [Theory]
    [MemberData(nameof(FindingCounts))]
    public void Findings_AreCappedAtTheFrame_WithTheRemainderCounted(int count)
    {
        var findings = Enumerable.Range(0, count)
            .Select(i => TestData.Finding($"rule-{i:00}", cat: RuleCategory.Advise,
                canFix: false, headline: H($"{i}")))
            .ToArray();
        var snapshot = TestData.Snapshot(findings, new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.True(card.Findings.Count <= ReportCardModel.MaxFindingRows,
            $"{count} findings produced {card.Findings.Count} rows");
        if (count <= ReportCardModel.MaxFindingRows)
        {
            Assert.Equal(count, card.Findings.Count);
            Assert.Equal("", card.FindingsMoreText);
            return;
        }
        // The line counts everything the rows above it did not show. It
        // borrows the overview's key on purpose: "ve {0} bulgu daha" counts
        // FINDINGS, which under this heading is the right noun — the same
        // Turkish that made the key wrong for the fixes list.
        Assert.Equal(ReportCardModel.MaxFindingRows, card.Findings.Count);
        Assert.Equal(
            Loc("en").F("overview.revelation.more",
                count - ReportCardModel.MaxFindingRows),
            card.FindingsMoreText);
    }

    /// THE BUDGET IS SHARED, because the frame does not grow when a probe goes
    /// unread. The fix list is what gives, being the section that already
    /// counts what it drops rather than losing anything.
    ///
    /// One row per line, and both of those lines are an even trade: an unread
    /// sentence, a fix row and the findings' overflow line are all 28.61px on
    /// the real control, each charged one row. The overflow line was not
    /// always — see FixBudget for the margin that under-charged it and the
    /// commit that took it. What is asserted here is the ROW arithmetic,
    /// which is exact and was exact either way;
    /// the three heights named above are re-measured on the real control by
    /// ReportCardRenderTests' TheRowHeightsTheBudgetTrades_AreTheOnesFixBudgets
    /// DocClaims, which weighs a finding row beside them. NOT by the frame test: WorstCaseCard_FitsInsideTheFrameItIs
    /// DrawnInto asserts `wanted <= given` and holds no row height at all, so a
    /// sentence sending a reader there for these figures sends them where the
    /// figures are not.
    [Theory]
    [InlineData(0, 0, 9)]    // nothing above took an extra line
    [InlineData(4, 0, 5)]    // four probes could not read their source
    [InlineData(4, 9, 4)]    // and the findings overflowed as well
    [InlineData(0, 9, 8)]    // the overflow line alone
    public void TheFixList_GivesUpARow_ForEveryLineTheSectionsAboveItTook(
        int unreadableDisclosures, int headlineFindings, int expectedRows)
    {
        var findings = new List<DiagnosticFinding>();
        foreach (var id in new[] { "usb-history", "run-history", "recall-status",
                     "delivery-optimization" }.Take(unreadableDisclosures))
            findings.Add(TestData.Finding(id, Severity.Info, RuleCategory.Advise,
                stars: 1, canFix: false, kind: FindingKind.Notice));
        findings.AddRange(Enumerable.Range(0, headlineFindings)
            .Select(i => TestData.Finding($"rule-{i:00}", cat: RuleCategory.Advise,
                canFix: false, headline: H($"{i}"))));
        var fixes = Enumerable.Range(0, 16)
            .Select(i => new UndoableFix($"rule-{i:00}",
                new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc).AddMinutes(-i)))
            .ToArray();
        var snapshot = TestData.Snapshot(findings, new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, fixes, Loc("en"));

        Assert.Equal(1 + unreadableDisclosures, card.Unread.Count);
        Assert.Equal(expectedRows, card.Fixes.Count);
        // Nothing is dropped without being counted, whatever the budget is.
        var hidden = fixes.Length - (card.Fixes.Count - 1);
        Assert.Equal(Loc("en").F("report.fixes.more", hidden), card.Fixes[^1]);
    }

    /// THE TRADE HAS A FLOOR, and this is where it stops being a trade. The
    /// fix list cannot give up its last row — a card carrying fixes that
    /// showed none of them and said nothing about it would be the silent drop
    /// the budget exists to stop — so past a certain number of unread lines
    /// nothing pays for them and the column grows past the frame again. The
    /// shared budget is a trade, not a bound, and this is where the difference
    /// becomes visible.
    ///
    /// Today's ceiling on that section is the sensor line plus one per
    /// report-only disclosure, and it sits inside the floor. What is asserted
    /// is the HEADROOM, derived from the shipped rules rather than typed: a
    /// wave that adds report-only disclosures until the trade stops paying
    /// fails here, before it ships as a card nobody photographed.
    [Fact]
    public void TheTrade_HasHeadroomForEveryUnreadLineTheShippedRulesCanProduce()
    {
        var disclosureIds = DiagnosticRuleRegistry.All
            .OfType<PrivacyDisclosureRule>().Select(r => r.Id).ToList();
        var mostUnreadLines = 1 + disclosureIds.Count;   // the sensor line, plus one each
        // What the trade WOULD hand the fix list, before FixBudget's floor.
        var unclamped = ReportCardModel.MaxFixRows - (mostUnreadLines - 1) - 1;

        Assert.True(unclamped >= 1,
            $"{disclosureIds.Count} report-only disclosures can put {mostUnreadLines} "
            + "lines under \"what brisk could not read\", and the fix list has only "
            + $"{ReportCardModel.MaxFixRows} rows to trade away — the budget stops "
            + "paying for them and the body column grows past the frame");

        // And the model hands over exactly that many, which is what says the
        // floor did not quietly clamp the arithmetic above into looking fine.
        var findings = Enumerable.Range(0, ReportCardModel.MaxFindingRows + 1)
            .Select(i => TestData.Finding($"rule-{i:00}", cat: RuleCategory.Advise,
                canFix: false, headline: H($"{i}")))
            .ToList();
        findings.AddRange(disclosureIds.Select(id => TestData.Finding(
            id, Severity.Info, RuleCategory.Advise, stars: 1, canFix: false,
            kind: FindingKind.Notice)));
        var fixes = Enumerable.Range(0, 16)
            .Select(i => new UndoableFix($"rule-{i:00}",
                new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc).AddMinutes(-i)))
            .ToArray();

        var card = ReportCardModel.Build(
            TestData.Snapshot(findings, new SensorStatus(true, true, null)),
            fixes, Loc("en"));

        Assert.Equal(mostUnreadLines, card.Unread.Count);
        Assert.Equal(unclamped, card.Fixes.Count);
    }

    /// Newest first survives the cap: the row that falls off the end is the
    /// oldest fix, not whichever one the journal happened to list last.
    [Fact]
    public void Fixes_KeepTheNewest_WhenTheCapBites()
    {
        var fixes = Enumerable.Range(0, 12)
            .Select(i => new UndoableFix($"rule-{i:00}",
                new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc).AddDays(i)))
            .ToArray();
        var snapshot = TestData.Snapshot(null, new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, fixes, Loc("en"));

        Assert.Equal("2026-08-21", card.Fixes[0][^10..]);    // rule-11, the newest
        Assert.Equal("2026-08-14", card.Fixes[7][^10..]);    // rule-04, the last shown
        Assert.Equal(Loc("en").F("report.fixes.more", 4), card.Fixes[^1]);
    }

    /// The overflow line counts FIXES, and it has to say so in a language
    /// where that is a different word. The card borrowed the overview's
    /// "overview.revelation.more" because in English both read "and {0} more";
    /// the Turkish is "ve {0} bulgu daha" — and {0} more FINDINGS — so a
    /// Turkish install with a full journal printed the wrong noun under
    /// "Uygulanan düzeltmeler" on a picture built to be shared. English parity
    /// is exactly what hid it, so this asserts the Turkish value and forbids
    /// the findings noun outright.
    [Fact]
    public void FixesOverflow_CountsFixes_NotFindings_InTurkishToo()
    {
        var fixes = Enumerable.Range(0, 16)
            .Select(i => new UndoableFix($"rule-{i:00}",
                new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc).AddMinutes(-i)))
            .ToArray();
        var snapshot = TestData.Snapshot(null, new SensorStatus(true, true, null));

        var tr = ReportCardModel.Build(snapshot, fixes, Loc("tr"));
        var en = ReportCardModel.Build(snapshot, fixes, Loc("en"));

        Assert.Equal("ve 8 düzeltme daha", tr.Fixes[^1]);
        Assert.Equal("and 8 more", en.Fixes[^1]);
        // "bulgu" is the word the borrowed key put there, and the reason the
        // two keys can never be merged back together on an English reading.
        Assert.DoesNotContain("bulgu", tr.Fixes[^1]);
        Assert.NotEqual(Loc("tr").F("overview.revelation.more", 8), tr.Fixes[^1]);
    }

    /// THE FLATTENER'S OWN COVERAGE. The privacy bans below reach exactly as
    /// far as AllTextOn does, so what AllTextOn misses is what they do not
    /// check — and the list version this replaced said it was everything while
    /// being wrong inside the commit that wrote the sentence: FindingsMoreText
    /// was added to the model and not to the list, so every section but the
    /// new one was covered. No leak followed, because that line is a count.
    /// The coverage claim was still false.
    ///
    /// TWO cards, because two of the model's strings cannot both be lit:
    /// FindingsMoreText is what a card with more findings than it can show
    /// says, FindingsEmptyText is what a card with none says. Every property
    /// has to be non-empty on at least one of them, so a property that is
    /// empty everywhere fails rather than passing by never being exercised.
    [Fact]
    public void AllTextOn_ReachesEveryStringTheModelExposes()
    {
        var cards = new[]
        {
            ReportCardModel.Build(EverySectionPopulated(), OneFix(), Loc("en")),
            ReportCardModel.Build(
                TestData.Snapshot(null, new SensorStatus(true, true, null)),
                OneFix(), Loc("en")),
        };

        foreach (var property in typeof(ReportCardModel)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var exercised = false;
            foreach (var card in cards)
            {
                var pieces = Flatten(property, property.GetValue(card)).ToList();
                if (pieces.Count == 0 || pieces.Any(piece => piece.Length == 0))
                    continue;
                var text = AllTextOn(card);
                foreach (var piece in pieces)
                    Assert.True(text.Contains(piece, StringComparison.Ordinal),
                        $"ReportCardModel.{property.Name} does not reach AllTextOn, " +
                        "so no privacy assertion on this card covers it");
                exercised = true;
            }
            Assert.True(exercised,
                $"ReportCardModel.{property.Name} was empty on both fixtures, so " +
                "neither of them exercises the coverage this claims to check");
        }
    }

    private static UndoableFix[] OneFix() => new[]
    {
        new UndoableFix("power-plan",
            new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc)),
    };

    /// A snapshot that lights every section a single card can light at once:
    /// one finding past the cap so the overflow line is there, a disclosure
    /// that read nothing, and both sensors silent.
    private static ScanSnapshot EverySectionPopulated()
    {
        var findings = Enumerable.Range(0, ReportCardModel.MaxFindingRows + 1)
            .Select(i => TestData.Finding($"rule-{i:00}", cat: RuleCategory.Advise,
                canFix: false, headline: H($"{i}")))
            .ToList();
        findings.Add(new DiagnosticFinding(
            "usb-history", "rule.usb-history.title.unread", "unread usb-history",
            "Evidence usb-history", Severity.Info, RuleCategory.Advise, 1,
            CanFix: false, FixDescription: null, Headline: null,
            Kind: FindingKind.Notice));
        return TestData.Snapshot(findings, new SensorStatus(false, false, null));
    }

    /// EVERY STRING THE MODEL EXPOSES, read off it by reflection rather than
    /// from a list somebody has to remember to extend. A property added to the
    /// model is in reach by construction; the list version promised that and
    /// could not keep it.
    ///
    /// The model, NOT the card — and the narrower noun is deliberate, because
    /// the broad one is what the list version over-claimed with. The card also
    /// paints the "brisk" wordmark and its three section headings, which are
    /// bound to Loc.Instance in the markup and never travel through this
    /// model. They are rule-authored and product-authored static text, so
    /// nothing a machine could name reaches the card through them; what this
    /// helper covers is every string that carries a reading.
    ///
    /// A property whose type this cannot flatten THROWS. Stringifying it would
    /// contribute a type name, which reads as covered and is not — and a
    /// section nobody noticed was uncovered is the whole failure this helper
    /// exists to prevent.
    private static string AllTextOn(ReportCardModel card) => string.Join(
        Environment.NewLine,
        typeof(ReportCardModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(p => Flatten(p, p.GetValue(card))));

    private static IEnumerable<string> Flatten(PropertyInfo property, object? value) =>
        value switch
        {
            null => Array.Empty<string>(),
            string text => new[] { text },
            IEnumerable<string> lines => lines,
            IEnumerable<CardLine> rows => rows.Select(l => l.Lead + " " + l.Text),
            int or bool => new[] { value.ToString()! },
            _ => throw new InvalidOperationException(
                $"ReportCardModel.{property.Name} is a {value.GetType().Name}, which " +
                "AllTextOn cannot read. Teach it the type — stringifying it would " +
                "put a type name into the privacy assertions and read as covered."),
        };

    /// 47 USB storage instances laid out the way Windows records them — a
    /// subkey per device MODEL, a subkey per attached INSTANCE below it — one
    /// of them a Kingston; and two UserAssist entries, one spelled plainly and
    /// one in the ROT13 form Windows actually stores. The findings are then
    /// produced by the SHIPPED UsbHistoryRule and RunHistoryRule reading that
    /// registry, never written here as literals: what these tests ask is
    /// whether a name a real rule had in its hands can reach the card, and a
    /// fixture that never held the name could not ask it.
    ///
    /// SINCE THE AMENDMENT, the snapshot also CARRIES the names, in
    /// UsbDevices, off the same shipped rule reading the same registry. That
    /// is a harder question than the one this fixture asked before and the
    /// one worth asking now: the card is built over a snapshot with
    /// "Kingston" in it, in a list, in reach of anything that walked the
    /// snapshot — and prints no Kingston.
    private static ScanSnapshot SnapshotWithPlantedNames()
    {
        var reg = new FakeRegistry();
        PlantUsbInstance(reg, "Ven_Kingston&Prod_DataTraveler", "0123456789ABCD");
        for (var i = 1; i < 47; i++)
            PlantUsbInstance(reg, "Ven_Generic&Prod_Stick", $"instance-{i:00}");
        reg.SetString(RunHistoryRule.CountKeyPaths[0], "chrome.exe", "");
        reg.SetString(RunHistoryRule.CountKeyPaths[0], "puebzr.rkr", "");
        // 1282 more, so the run count is the distinctive 1284 the assertions
        // can find without matching a date or a score by accident — the usb
        // count no longer reaches the card at all (RevelationPicker.NeverLeads),
        // so run-history is the disclosure the count claims now ride on.
        for (var i = 0; i < 1282; i++)
            reg.SetString(RunHistoryRule.CountKeyPaths[0], $"entry-{i:0000}.rkr", "");

        var ctx = TestData.RegistryContext(reg);
        return TestData.Snapshot(
            new[]
            {
                new UsbHistoryRule().Detect(ctx)!,
                new RunHistoryRule().Detect(ctx)!,
            },
            new SensorStatus(true, true, null),
            Array.Empty<ReadBackResult>(),
            UsbHistoryRule.ReadDevices(ctx));
    }

    private static void PlantUsbInstance(FakeRegistry reg, string model, string instance)
    {
        Sub(reg, UsbHistoryRule.KeyPath, model);
        Sub(reg, $@"{UsbHistoryRule.KeyPath}\{model}", instance);
    }

    private static void Sub(FakeRegistry reg, string parent, string child)
    {
        if (!reg.SubKeys.TryGetValue(parent, out var children))
            reg.SubKeys[parent] = children = new List<string>();
        if (!children.Contains(child)) children.Add(child);
    }

    /// The wave's second red line, on the surface people actually post: counts
    /// yes, contents never. Both names were within reach through a real rule
    /// — see SnapshotWithPlantedNames — and the count they were counted into
    /// is what comes out.
    ///
    /// The program name is banned in both spellings because brisk never
    /// decodes the entries: "chrome.exe" would mean a decoder appeared, and
    /// "puebzr" would mean the stored name was printed raw. Neither is a
    /// count.
    [Fact]
    public void TheCard_CarriesCounts_AndNeverADeviceOrAProgramName()
    {
        var card = ReportCardModel.Build(SnapshotWithPlantedNames(),
            Array.Empty<UndoableFix>(), Loc("en"));

        var text = AllTextOn(card);

        Assert.Contains("1284", text);
        // The usb COUNT stays off the card too — not a red line but the
        // maintainer's 2026-08-26 call (RevelationPicker.NeverLeads): the
        // record lives on the Gizlilik page and leads nothing shareable.
        Assert.DoesNotContain("47", text);
        Assert.DoesNotContain("Kingston", text);
        Assert.DoesNotContain("DataTraveler", text);
        Assert.DoesNotContain("chrome.exe", text);
        Assert.DoesNotContain("puebzr", text);
    }

    /// The card's "okuyamadıklarım" is fed from ONE channel. It used to read
    /// SensorStatus alone, which meant a scan whose USB and program-record
    /// reads both came back with nothing put a card in front of a reader that
    /// said everything brisk tried to read had answered.
    ///
    /// The disclosures reach it from the FINDINGS, by the same predicate the
    /// Gizlilik page bands its unreadable rows with — the spec's fourth red
    /// line on a second surface, off one reading of it rather than two.
    [Fact]
    public void TheUnreadableDisclosures_JoinTheSensorLine_UnderWhatBriskCouldNotRead()
    {
        var ctx = TestData.RegistryContext(new FakeRegistry());   // nothing to count
        var snapshot = TestData.Snapshot(
            new[]
            {
                new UsbHistoryRule().Detect(ctx)!,
                new RunHistoryRule().Detect(ctx)!,
            },
            new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Equal(
            new[]
            {
                "Everything brisk tried to read, answered.",
                "The number of records of programs you have started could not be established",
                "The number of recorded USB storage devices could not be established",
            },
            card.Unread);
    }

    /// The control, and the half that stops the line above from being a list
    /// of every privacy finding: a disclosure that DID read its source leads
    /// the findings instead, and the unread section is the sensor line alone.
    [Fact]
    public void ADisclosureThatRead_LeadsTheFindings_AndStaysOutOfTheUnreadSection()
    {
        var card = ReportCardModel.Build(SnapshotWithPlantedNames(),
            Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Equal("1284", card.Findings[0].Lead);
        Assert.Equal(new[] { "Everything brisk tried to read, answered." }, card.Unread);
    }

    /// A headline is missing from three quite different findings and only one
    /// of them is a disclosure that read nothing. The six telemetry switches
    /// carry no headline either — they are switches, not readings — and a
    /// non-privacy finding is not on this page's business at all. A card that
    /// listed either under "what brisk could not read" would be inventing a
    /// failed read out of a finding that never attempted one.
    [Theory]
    [InlineData("advertising-id", RuleCategory.Auto)]
    [InlineData("location", RuleCategory.Confirm)]
    [InlineData("thermals", RuleCategory.Advise)]
    public void AHeadlessFindingThatIsNotAFailedRead_StaysOffTheUnreadSection(
        string ruleId, RuleCategory category)
    {
        var snapshot = TestData.Snapshot(
            new[]
            {
                TestData.Finding(ruleId, cat: category,
                    canFix: category != RuleCategory.Advise, kind: FindingKind.Notice),
            },
            new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Equal(new[] { "Everything brisk tried to read, answered." }, card.Unread);
    }

    /// The privacy ban, enforced on output rather than on good intentions:
    /// plant the user's name, the machine name, and a profile path into every
    /// engine-authored string a finding carries, and prove none of them can
    /// reach the card.
    [Fact]
    public void PrivacyBan_EvidenceNamesAndPathsNeverReachTheCard()
    {
        // The markers live ONLY in the fields that carry user data in real
        // findings (evidence, fix description) — the title is rule-authored
        // static text and legitimately appears on the card.
        var poisoned = new DiagnosticFinding(
            "zz-fake", "rule.zz-fake.title", "Too many programs run at start",
            @"C:\Users\SECRETUSER\Desktop leaks from DESKTOP-SECRETPC via SecretApp.exe",
            Severity.Warning, RuleCategory.Advise, 3, CanFix: false,
            FixDescription: @"delete C:\Users\SECRETUSER\file",
            Headline: H("47"));
        var snapshot = TestData.Snapshot(new[] { poisoned },
            new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        var everything = AllTextOn(card);
        Assert.Contains("47", everything);                       // the number survives
        Assert.DoesNotContain("SECRETUSER", everything);         // the user never does
        Assert.DoesNotContain("DESKTOP-SECRETPC", everything);
        Assert.DoesNotContain("SecretApp", everything);
        Assert.DoesNotContain(@"C:\Users", everything);
    }

    [Fact]
    public void TopStrip_CarriesLocalDateAndEngineVersion()
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Equal(EngineInfo.Version, card.VersionText);
        Assert.Contains("2026-08-15", card.DateText);            // TestData's CompletedUtc date
        Assert.Equal("github.com/merturl4576/brisk", card.RepoLine);
        Assert.Equal(72, card.Health);
    }

    /// The card paints its ring from this key, so drift between it and the
    /// app's banding would let a screenshot claim health the machine does
    /// not have. The boundaries are the assertion: 90 and 70 are where
    /// HealthBrush turns, and the card must turn on the same numbers.
    [Theory]
    [InlineData(100, "Good")]
    [InlineData(90, "Good")]
    [InlineData(89, "SeverityNotice")]
    [InlineData(72, "SeverityNotice")]
    [InlineData(70, "SeverityNotice")]
    [InlineData(69, "SeverityCritical")]
    [InlineData(35, "SeverityCritical")]
    [InlineData(0, "SeverityCritical")]
    public void ScoreBrushKey_BandsTheScoreTheWayTheRestOfTheAppDoes(
        int health, string expected)
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(true, true, null))
            with { Health = health };

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Equal(expected, card.ScoreBrushKey);
        Assert.Equal(HealthBrush.KeyFor(health), card.ScoreBrushKey);
    }
}
