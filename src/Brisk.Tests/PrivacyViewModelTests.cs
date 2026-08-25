using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

/// The Privacy page's view model. Three blocks and a button, and every one of
/// them exists because of something this wave refuses to do:
///
///   * the switches are split by CONSENT LEVEL, so the one button cannot
///     reach a switch whose cost the user was never shown;
///   * a disclosure that read nothing gets a place of its own rather than a
///     zero;
///   * the read-back's four sentences do not take the same argument, and
///     nothing before this pinned that the renderer passes the right one.
public class PrivacyViewModelTests
{
    // ---- The grouping -------------------------------------------------

    /// The whole privacy topic on one machine, sorted into the four bands the
    /// page renders. Asserted as SETS of rule ids rather than by count: a
    /// count passes over two findings that swapped bands.
    [Fact]
    public async Task EveryPrivacyFinding_LandsInTheBandThePageRendersItIn()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(WholeTopic());
        await state.ScanAsync();

        Assert.Equal(
            new[] { "advertising-id", "diagnostic-level", "speech-typing",
                    "tailored-experiences" },
            vm.SafeSwitchRows.Select(r => r.RuleId).OrderBy(id => id,
                StringComparer.Ordinal));
        Assert.Equal(
            new[] { "activity-history", "location" },
            vm.CostlySwitchRows.Select(r => r.RuleId).OrderBy(id => id,
                StringComparer.Ordinal));
        Assert.Equal(
            new[] { "delivery-optimization", "recall-status", "run-history",
                    "usb-history" },
            vm.DisclosureRows.Select(r => r.RuleId).OrderBy(id => id,
                StringComparer.Ordinal));
    }

    /// The page shows the privacy topic and nothing else — the complement of
    /// the two pages that grade the machine, which exclude these same ids.
    [Fact]
    public async Task ANonPrivacyFinding_ReachesNoBandOnThisPage()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
            Switch("advertising-id", RuleCategory.Auto),
        });
        await state.ScanAsync();

        Assert.DoesNotContain("power-plan", AllRows(vm).Select(r => r.RuleId));
        Assert.Contains("advertising-id", AllRows(vm).Select(r => r.RuleId));
    }

    /// "Largest first", and only over the readings that ARE numbers.
    ///
    /// Driven BOTH WAYS from one pair, because that is what makes it say
    /// anything: whichever of the two carries the bigger count leads, so the
    /// order is the number and not the rule id. Alphabetically run-history
    /// comes first every time, and it leads in only one of these two rows.
    [Theory]
    [InlineData("312", "47", "usb-history", "run-history")]
    [InlineData("47", "1284", "run-history", "usb-history")]
    public async Task TheDisclosureRows_LeadWithTheLargestNumber(
        string usb, string run, string first, string second)
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            Disclosure("usb-history", usb),
            Disclosure("run-history", run),
            Disclosure("delivery-optimization", "1.2 GB"),
            Disclosure("recall-status", "Off"),
        });
        await state.ScanAsync();

        var order = vm.DisclosureRows.Select(r => r.RuleId).ToArray();
        Assert.Equal(new[] { first, second }, order.Take(2));
        // A byte amount and a policy word are not quantities this page can
        // rank against a device count, so they sort after every number
        // instead of being given an invented place among them.
        Assert.Equal(
            new[] { "delivery-optimization", "recall-status" },
            order.Skip(2).OrderBy(id => id, StringComparer.Ordinal));
    }

    /// The spec's fourth red line, on this page: what could not be read is
    /// never a silent zero. The unreadable disclosures carry no Headline —
    /// that is the disclosure family's own contract — and this page reads
    /// that absence rather than keeping a second list of "the unreadable
    /// ones" beside the rules.
    [Fact]
    public async Task ADisclosureThatReadNothing_GetsItsOwnBand_NotAZero()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            Disclosure("usb-history", "47"),
            Unreadable("run-history"),
            Unreadable("recall-status"),
        });
        await state.ScanAsync();

        Assert.Equal(new[] { "usb-history" },
            vm.DisclosureRows.Select(r => r.RuleId));
        Assert.Equal(new[] { "recall-status", "run-history" },
            vm.UnreadableRows.Select(r => r.RuleId).OrderBy(id => id,
                StringComparer.Ordinal));
        Assert.DoesNotContain("0",
            vm.UnreadableRows.Select(r => r.HeadlineValue).ToArray());
    }

    // ---- The one button -----------------------------------------------

    /// THE GUARD. One button carries one consent, and the consent it carries
    /// is "this takes nothing away that you use". location and
    /// activity-history are Confirm precisely so that consent cannot cover
    /// them, and this is what fails if the button ever widens: it asserts the
    /// exact set of rule ids the click reached, so a Confirm switch joining
    /// the walk is a failure and not a longer list.
    [Fact]
    public async Task TurnOffSafe_FixesTheConsequenceFreeSwitches_AndNeverACostlyOne()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(WholeTopic());
        await state.ScanAsync();

        await vm.TurnOffSafeAsync();

        Assert.Equal(
            new[] { "advertising-id", "diagnostic-level", "speech-typing",
                    "tailored-experiences" },
            host.Fixed.OrderBy(id => id, StringComparer.Ordinal));
        foreach (var costly in new[] { "location", "activity-history" })
            Assert.False(host.Fixed.Contains(costly, StringComparer.OrdinalIgnoreCase),
                $"the one button fixed '{costly}' — that switch is Confirm " +
                "because it costs the user something, and this button's caption " +
                "promises it takes nothing away");
    }

    /// The same button over a machine with nothing but the costly two on it:
    /// there is nothing for it to do, it says so by being disabled, and it
    /// still touches neither of them if it is driven anyway.
    [Fact]
    public async Task TurnOffSafe_OnAMachineWithOnlyCostlySwitches_DoesNothing()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            Switch("location", RuleCategory.Confirm),
            Switch("activity-history", RuleCategory.Confirm),
        });
        await state.ScanAsync();

        Assert.False(vm.TurnOffSafeCommand.CanExecute(null),
            "the one button offers itself over a page with no consequence-free " +
            "switch on it, and a button that accepts a click it will not act on " +
            "is the dead affordance this branch has shipped once already");

        await vm.TurnOffSafeAsync();
        Assert.Empty(host.Fixed);
    }

    /// The button's caption counts what the click will do, off the same rows
    /// the walk acts on — the reason the predicate behind them is one thing
    /// and not two.
    [Fact]
    public async Task TheButtonsCaption_CountsTheSwitchesTheClickWillReach()
    {
        var (vm, host, state) = Build();
        var loc = EnglishLoc();
        host.NextSnapshot = TestData.Snapshot(WholeTopic());
        await state.ScanAsync();

        Assert.Equal(loc.F("privacy.turnoff.safe", vm.SafeSwitchRows.Count),
            vm.TurnOffSafeText);
        Assert.Equal(4, vm.SafeSwitchRows.Count);
    }

    /// diagnostic-level writes under HKLM, so on an unelevated machine it
    /// fails cleanly through FixRunner with Ok:false while the other three
    /// succeed. That is the ORDINARY outcome of this button on a standard
    /// account, and a page that swallowed it would leave the user believing
    /// four settings went off when three did. The batch finishes, and the
    /// sentence afterwards says how many refused and repeats what the attempt
    /// reported.
    [Fact]
    public async Task WhenASwitchRefuses_TheButtonSaysSo_AndTheRestStillGoOff()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(WholeTopic());
        host.OnFix = id => id == "diagnostic-level"
            ? new FixOutcome(false, "diagnostic-level: fix failed — access denied")
            : new FixOutcome(true, id);
        await state.ScanAsync();

        await vm.TurnOffSafeAsync();

        Assert.Contains("diagnostic-level", host.Fixed);
        Assert.Contains("advertising-id", host.Fixed);
        Assert.Contains("1", vm.Message);
        Assert.Contains("access denied", vm.Message);
    }

    /// A run where nothing refused says nothing. An empty message is the
    /// page's "it worked", and a leftover sentence from a previous attempt
    /// would be a claim about a run that already ended.
    [Fact]
    public async Task WhenNothingRefuses_ThePageSaysNothing()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(WholeTopic());
        await state.ScanAsync();

        await vm.TurnOffSafeAsync();

        Assert.Equal("", vm.Message);
    }

    // ---- The read-back -------------------------------------------------

    /// The four sentences do NOT take the same argument: reverted takes a
    /// DATE, held and unverified take a DAY COUNT, and ignored takes nothing
    /// at all. ReadBackTests pins which key takes which; nothing until now
    /// pinned that the renderer passes the right one, and passing a date
    /// where a count belongs renders "You switched this off 2026-08-12 days
    /// ago" without failing anything anywhere.
    ///
    /// Asserted against the localized sentence built with the argument this
    /// row should have used, so a swapped pair is a failure rather than a
    /// different-looking string nobody reads.
    [Theory]
    [MemberData(nameof(EveryReadBackState))]
    public async Task EachRow_RendersItsOwnSentenceWithItsOwnArgument(
        ReadBackState state)
    {
        var loc = EnglishLoc();
        var fixedAt = new DateTime(2026, 8, 12, 9, 30, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 8, 15, 9, 30, 0, DateTimeKind.Utc);
        var days = ReadBackRow.DaysAgo(fixedAt, now);
        var expected = state switch
        {
            ReadBackState.Held => loc.F("readback.held", days),
            ReadBackState.Reverted =>
                loc.F("readback.reverted", ReadBackRow.LocalDate(fixedAt)),
            ReadBackState.WrittenButIgnored => loc["readback.ignored"],
            ReadBackState.WrittenButUnverified => loc.F("readback.unverified", days),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state,
                "a read-back state this theory has no expected sentence for — " +
                "the renderer has one, and an unchecked sentence is a sentence " +
                "nobody has read"),
        };

        var row = await OneReadBackRow(
            new ReadBackResult("advertising-id", state, fixedAt), now);

        Assert.Equal(expected, row.Text);
    }

    /// Every member of the enum reaches the theory above, read off the enum
    /// itself rather than from a list here — the same discipline
    /// ReadBackTests uses, so a fifth state arrives as a row with no
    /// expected sentence and fails.
    public static TheoryData<ReadBackState> EveryReadBackState()
    {
        var data = new TheoryData<ReadBackState>();
        foreach (var state in Enum.GetValues<ReadBackState>()) data.Add(state);
        return data;
    }

    /// FixedAtUtc is UTC and the sentence a reader sees is local, and the two
    /// part company at the ends of a day. A fix stamped at 23:00 UTC is
    /// 02:00 the NEXT day in Istanbul (UTC+3), so from 09:00 UTC the
    /// following morning a 24-hour tick count answers 0 and the local
    /// calendar answers 0 as well — while a fix stamped at 23:00 the day
    /// BEFORE that is one local day back, not two.
    ///
    /// Written against a fixed offset rather than the machine's own zone: a
    /// test that reads TimeZoneInfo.Local proves whatever the developer's
    /// clock happens to be set to.
    [Fact]
    public void TheDayCount_IsCountedInLocalCalendarDays()
    {
        var offset = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 8, 15));
        // Local midnight, expressed back in UTC: the instant the day turns
        // over for the reader.
        var localMidnightUtc = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc) - offset;

        Assert.Equal(0, ReadBackRow.DaysAgo(localMidnightUtc, localMidnightUtc));
        Assert.Equal(1, ReadBackRow.DaysAgo(
            localMidnightUtc.AddMinutes(-1), localMidnightUtc));
        Assert.Equal(0, ReadBackRow.DaysAgo(
            localMidnightUtc, localMidnightUtc.AddHours(23)));
    }

    /// ReadBack carries a stamp in the future through untouched, on purpose —
    /// nothing there reads a clock. On screen "-3 days ago" would be
    /// nonsense, so the renderer floors at today. It does not correct the
    /// stamp and does not say which of the two readings is wrong, because it
    /// does not know.
    [Fact]
    public void AStampInTheFuture_RendersAsToday_RatherThanAsANegative()
    {
        var now = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(0, ReadBackRow.DaysAgo(
            new DateTime(2999, 1, 1, 0, 0, 0, DateTimeKind.Utc), now));
    }

    /// The lines are the journal's, newest fix first — the same order every
    /// other journal-driven list in brisk uses.
    [Fact]
    public async Task TheReadBackLines_AreNewestFixFirst()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(Array.Empty<DiagnosticFinding>(), new[]
        {
            new ReadBackResult("advertising-id", ReadBackState.Held,
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)),
            new ReadBackResult("location", ReadBackState.Reverted,
                new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc)),
            new ReadBackResult("speech-typing", ReadBackState.Held,
                new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc)),
        });
        await state.ScanAsync();

        Assert.Equal(new[] { "location", "speech-typing", "advertising-id" },
            vm.ReadBackRows.Select(r => r.RuleId));
    }

    /// The read-back rows come from the SNAPSHOT, which is where the state
    /// and the findings were read in one pass. A page that asked the host for
    /// a fresh read-back would be a second channel for one claim, and the
    /// claim ReadBack.StateOf makes — that a reverted switch is exactly a
    /// switch brisk is reporting again — is only true while both come from
    /// the same read.
    [Fact]
    public async Task TheReadBackLines_ComeFromTheScan_NotFromASecondRead()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(Array.Empty<DiagnosticFinding>(), new[]
        {
            new ReadBackResult("advertising-id", ReadBackState.Held,
                new DateTime(2026, 8, 12, 9, 30, 0, DateTimeKind.Utc)),
        });
        // A journal that disagrees with the snapshot: if the page read the
        // journal for its lines it would show two rows, or the wrong one.
        host.Undoable.Add(new UndoableFix("location",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await state.ScanAsync();

        Assert.Equal(new[] { "advertising-id" },
            vm.ReadBackRows.Select(r => r.RuleId));
    }

    /// Undo from a read-back line reaches the rule the line is about — the
    /// page's only claim of reversibility that a user can act on.
    [Fact]
    public async Task UndoFromAReadBackLine_UndoesThatRule()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(Array.Empty<DiagnosticFinding>(), new[]
        {
            new ReadBackResult("location", ReadBackState.Held,
                new DateTime(2026, 8, 12, 9, 30, 0, DateTimeKind.Utc)),
        });
        await state.ScanAsync();

        await vm.UndoAsync(vm.ReadBackRows[0]);

        Assert.Equal(new[] { "location" }, host.Undone);
    }

    // ---- The impact meter ----------------------------------------------

    /// ImpactStars is documented 1..5 and measures expected PERFORMANCE
    /// impact. Every privacy rule reports ONE — and reports it because zero
    /// is outside the range, not because anything was measured. Rendered,
    /// that is ●○○○○ on every row of this page: a meter claiming a
    /// measurement nobody made, on the page whose whole subject is brisk not
    /// claiming what it did not read.
    ///
    /// The control is what makes this say anything: the same row type over a
    /// performance finding still shows its meter, so the suppression is
    /// about privacy rather than about the meter having quietly gone away
    /// everywhere.
    [Theory]
    [InlineData("advertising-id", RuleCategory.Auto)]
    [InlineData("location", RuleCategory.Confirm)]
    [InlineData("usb-history", RuleCategory.Advise)]
    public void APrivacyRow_ShowsNoImpactMeter(string ruleId, RuleCategory category)
    {
        var row = Row(TestData.Finding(ruleId, cat: category, stars: 1,
            canFix: category != RuleCategory.Advise, kind: FindingKind.Notice));

        Assert.False(row.ShowsImpact,
            $"{ruleId} renders an impact meter. ImpactStars is 1 on every " +
            "privacy finding because the field is documented 1..5 and zero is " +
            "outside it — nothing about a privacy setting's speed cost was " +
            "ever measured, and ●○○○○ says otherwise");
    }

    [Fact]
    public void APerformanceRow_StillShowsItsImpactMeter()
    {
        var row = Row(TestData.Finding("power-plan", cat: RuleCategory.Auto, stars: 4));

        Assert.True(row.ShowsImpact,
            "the impact meter has gone from a performance finding too, so " +
            "suppressing it on privacy rows says nothing");
    }

    /// The read-back dot's colour is a theme KEY resolved at render time
    /// through ThemeBrush, not a {DynamicResource} literal in the XAML — so
    /// ResourceKeyTests, which reads those literals out of the markup, cannot
    /// see it. A key that is not in the dictionary resolves to null and the
    /// dot paints nothing: no exception, no binding error, and a read-back
    /// line that has quietly lost its verdict colour.
    ///
    /// Driven off the enum rather than off a list here, so a fifth state
    /// arrives with a colour nobody checked and fails, and read from BOTH
    /// dictionaries because either one can be the live theme.
    [Theory]
    [MemberData(nameof(EveryReadBackState))]
    public void EveryReadBackColour_IsAKeyBothThemesCarry(ReadBackState state)
    {
        var row = new ReadBackRow(
            new ReadBackResult("advertising-id", state,
                new DateTime(2026, 8, 12, 9, 30, 0, DateTimeKind.Utc)),
            EnglishLoc(), new DateTime(2026, 8, 15, 9, 30, 0, DateTimeKind.Utc),
            _ => Task.CompletedTask);

        foreach (var theme in new[] { "Dark.xaml", "Light.xaml" })
            Assert.True(ThemeSource.Keys(theme).Contains(row.StateBrushKey),
                $"{state} paints its dot from \"{row.StateBrushKey}\" and " +
                $"{theme} has no such key — the converter resolves it to null " +
                "and the dot renders as nothing at all");
    }

    // ---- What the two costly switches say ------------------------------

    /// The loss beside the switch, in the rule's own words. The page does not
    /// keep a list of which switches cost something — the rule declares it by
    /// shipping a `rule.<id>.cost` string, and a rule that declares none has
    /// no key and no label.
    [Theory]
    [InlineData("location", "Find my device stops working")]
    [InlineData("activity-history", "Timeline ends")]
    public void ACostlySwitch_NamesWhatItCosts(string ruleId, string expected)
    {
        var row = Row(Switch(ruleId, RuleCategory.Confirm));

        Assert.True(row.HasCost,
            $"{ruleId} is one of the two switches the user is warned about and " +
            "it carries no named loss");
        Assert.Equal(expected, row.CostText);
    }

    [Fact]
    public void ASwitchThatCostsNothing_NamesNoLoss()
    {
        var row = Row(Switch("advertising-id", RuleCategory.Auto));

        Assert.False(row.HasCost,
            "a consequence-free switch grew a named loss — the one button's " +
            "caption promises it takes nothing away");
        Assert.Equal("", row.CostText);
    }

    // ---- Helpers --------------------------------------------------------

    private static IReadOnlyList<FindingRow> AllRows(PrivacyViewModel vm) =>
        vm.DisclosureRows.Concat(vm.UnreadableRows)
            .Concat(vm.SafeSwitchRows).Concat(vm.CostlySwitchRows).ToList();

    /// Every privacy rule this wave ships, each reporting on a machine where
    /// it has something to say — the shape the page is designed against.
    private static DiagnosticFinding[] WholeTopic() => new[]
    {
        Switch("advertising-id", RuleCategory.Auto),
        Switch("diagnostic-level", RuleCategory.Auto),
        Switch("tailored-experiences", RuleCategory.Auto),
        Switch("speech-typing", RuleCategory.Auto),
        Switch("location", RuleCategory.Confirm),
        Switch("activity-history", RuleCategory.Confirm),
        Disclosure("usb-history", "47"),
        Disclosure("run-history", "1284"),
        Disclosure("delivery-optimization", "1.2 GB"),
        Disclosure("recall-status", "Off"),
    };

    /// A telemetry switch's finding, in the shape TelemetrySwitchRule.Detect
    /// actually builds one: Notice, fixable, one star, and NO headline.
    private static DiagnosticFinding Switch(string ruleId, RuleCategory category) =>
        TestData.Finding(ruleId, Severity.Info, category, stars: 1, canFix: true,
            kind: FindingKind.Notice);

    /// A report-only disclosure with a reading, in the shape
    /// PrivacyDisclosureRule.Disclosure builds one: Advise, not fixable, and
    /// a Headline carrying the value it leads with.
    private static DiagnosticFinding Disclosure(string ruleId, string value) =>
        TestData.Finding(ruleId, Severity.Info, RuleCategory.Advise, stars: 1,
            canFix: false, kind: FindingKind.Notice,
            headline: new Headline(value, $"caption {ruleId}",
                $"rule.{ruleId}.headline.value", new[] { value },
                $"rule.{ruleId}.headline.caption", Array.Empty<string>()));

    /// The same rule on a machine where its read found nothing: no Headline,
    /// which is the disclosure family's own way of saying it has no reading
    /// to lead with.
    private static DiagnosticFinding Unreadable(string ruleId) =>
        TestData.Finding(ruleId, Severity.Info, RuleCategory.Advise, stars: 1,
            canFix: false, kind: FindingKind.Notice);

    private static FindingRow Row(DiagnosticFinding finding) =>
        new(finding, EnglishLoc(), canUndo: false, _ => { }, _ => { });

    private static async Task<ReadBackRow> OneReadBackRow(
        ReadBackResult result, DateTime nowUtc)
    {
        var (vm, host, state) = Build(() => nowUtc);
        host.NextSnapshot = TestData.Snapshot(
            Array.Empty<DiagnosticFinding>(), new[] { result });
        await state.ScanAsync();
        return Assert.Single(vm.ReadBackRows);
    }

    private static (PrivacyViewModel Vm, FakeEngineHost Host, AppState State) Build(
        Func<DateTime>? utcNow = null)
    {
        var host = new FakeEngineHost();
        var loc = EnglishLoc();
        var state = new AppState(host, loc);
        var vm = new PrivacyViewModel(state, host, loc, () => false, utcNow,
            morphPause: () => Task.CompletedTask);
        return (vm, host, state);
    }

    private static Loc EnglishLoc()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        return loc;
    }
}
