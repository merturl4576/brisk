using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
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

    // ---- the USB record's own contents ---------------------------------

    /// THE RECORD, SHOWN TO ITS OWNER. The spec's red line 2 was amended on
    /// the maintainer's call at his first live look (2026-08-26): a device
    /// model and its dates are the user's own data, and the page only the
    /// user looks at may show them in full, behind a fold. Every surface
    /// built to be shared still carries the count alone — that half is
    /// TheDeviceName_RendersOnThePrivacyPage_AndOnNothingTheCardCarries, in
    /// ReportCardModelTests, over one snapshot.
    ///
    /// The whole LINE is asserted, not the name inside it: what this page
    /// owes the reader is which model, first recorded when, last seen when.
    /// A test that looked for the name alone would pass over a row that had
    /// swapped the two dates.
    [Fact]
    public async Task TheUsbRecord_RendersEachDevice_WithItsModelAndBothDates()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(
            new[] { Disclosure("usb-history", "2") },
            new SensorStatus(false, false, null),
            Array.Empty<ReadBackResult>(),
            new[]
            {
                new UsbDeviceRecord("Ven_Kingston&Prod_DataTraveler",
                    new DateTime(2021, 3, 4, 5, 6, 7, DateTimeKind.Utc),
                    new DateTime(2026, 8, 20, 9, 30, 0, DateTimeKind.Utc)),
                new UsbDeviceRecord("Ven_SanDisk&Prod_Cruzer",
                    new DateTime(2019, 7, 16, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2019, 7, 16, 0, 0, 0, DateTimeKind.Utc)),
            });
        await state.ScanAsync();

        Assert.Equal(
            new[]
            {
                "Ven_Kingston&Prod_DataTraveler — first recorded 2021-03-04 · last seen 2026-08-20",
                "Ven_SanDisk&Prod_Cruzer — first recorded 2019-07-16 · last seen 2019-07-16",
            },
            vm.UsbDeviceRows);
    }

    /// A date brisk did not read renders as the dash this app prints where it
    /// has no reading — never a guess, and never the blank an eye reads as a
    /// zero. Both halves get their own row, because they are two reads and
    /// either can be refused on its own.
    ///
    /// The stamps are NOT converted to local time, deliberately. The usb row
    /// directly above this fold prints "the oldest date it could read among
    /// them is 2017-05-09" straight off the rule, unconverted, and one record
    /// wearing two spellings of its own date six lines apart on one page is
    /// the defect this matches the rule to avoid. Every date here is a
    /// calendar day out of a FILETIME, and 08:30 UTC is 11:30 in the timezone
    /// this is built in — far enough from midnight to have shown the shift.
    [Theory]
    [InlineData(null, null, "Ven_A&Prod_Stick — first recorded — · last seen —")]
    [InlineData("2017-05-09", null, "Ven_A&Prod_Stick — first recorded 2017-05-09 · last seen —")]
    [InlineData(null, "2026-08-20", "Ven_A&Prod_Stick — first recorded — · last seen 2026-08-20")]
    public async Task ADateBriskCouldNotRead_RendersTheDash_NotAGuess(
        string? first, string? last, string expected)
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(
            new[] { Disclosure("usb-history", "1") },
            new SensorStatus(false, false, null),
            Array.Empty<ReadBackResult>(),
            new[] { new UsbDeviceRecord("Ven_A&Prod_Stick", Utc(first), Utc(last)) });
        await state.ScanAsync();

        Assert.Equal(expected, Assert.Single(vm.UsbDeviceRows));
    }

    /// 08:30 UTC on the planted day, or nothing — the shape a FILETIME read
    /// off the property store arrives in, at an hour a timezone conversion
    /// would visibly move.
    private static DateTime? Utc(string? day) => day is null ? null
        : DateTime.SpecifyKind(DateTime.Parse(day, CultureInfo.InvariantCulture),
            DateTimeKind.Utc).AddHours(8.5);

    /// A machine whose USB record brisk could not read leaves the fold with
    /// nothing in it, which is what collapses it in the markup. No row saying
    /// so: the page's "what brisk could not read" band is where that claim
    /// belongs, and the finding is what carries it there.
    [Fact]
    public async Task NoDeviceRecordRead_LeavesTheFoldEmpty()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[] { Unreadable("usb-history") });
        await state.ScanAsync();

        Assert.Empty(vm.UsbDeviceRows);
    }

    /// The rows are REBUILT per snapshot, like every other band on this page.
    /// An ObservableCollection that was appended to instead would show the
    /// same stick twice after a rescan and call it two devices.
    [Fact]
    public async Task ASecondScan_RebuildsTheDeviceRows_RatherThanAppending()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(
            new[] { Disclosure("usb-history", "1") },
            new SensorStatus(false, false, null),
            Array.Empty<ReadBackResult>(),
            new[] { new UsbDeviceRecord("Ven_A&Prod_Stick", null, null) });
        await state.ScanAsync();
        await state.ScanAsync();

        Assert.Single(vm.UsbDeviceRows);
    }

    /// WHICH APPS USED THE MICROPHONE OR THE CAMERA, on the one surface that
    /// may name them. The order is the snapshot's — newest use first, which
    /// is what CapabilityUseRule.ReadAllRecent hands over — and the line
    /// carries the app's name, which capability it used and the day it last
    /// did. A desktop app carries its PATH on a SECOND line beneath that one:
    /// this page is the owner's own screen and a name alone cannot tell two
    /// same-named executables apart, but a full path folded into the name
    /// line wraps it into unreadability. A packaged app has no path and gets
    /// no second line — null rather than an empty string, so the markup
    /// collapses it instead of leaving a gap a reader takes for missing
    /// information.
    ///
    /// None of this is on a finding. The names arrive on
    /// ScanSnapshot.CapabilityUses, exactly as the USB models above do, so
    /// every surface built to be shared carries the count and cannot carry
    /// more — the spec's red line 2 held by the shape of the pipeline.
    [Fact]
    public async Task CapabilityUses_RenderBehindTheFold_NewestFirst_WithNameCapabilityAndDate()
    {
        var uses = new[]
        {
            new CapabilityUseRecord(Capability.Camera, "5319275A.WhatsAppDesktop", null,
                new DateTime(2026, 9, 1, 14, 59, 0, DateTimeKind.Utc)),
            new CapabilityUseRecord(Capability.Microphone, "chrome.exe",
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                new DateTime(2026, 8, 11, 16, 48, 0, DateTimeKind.Utc)),
        };
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(findings: null, capabilityUses: uses);
        await state.ScanAsync();

        Assert.Equal(2, vm.CapabilityUseRows.Count);
        Assert.Contains("5319275A.WhatsAppDesktop", vm.CapabilityUseRows[0].Text);
        Assert.Contains("camera", vm.CapabilityUseRows[0].Text);
        Assert.Contains("2026-09-01", vm.CapabilityUseRows[0].Text);
        Assert.Null(vm.CapabilityUseRows[0].AppPath);

        Assert.Contains("chrome.exe", vm.CapabilityUseRows[1].Text);
        Assert.Contains("microphone", vm.CapabilityUseRows[1].Text);
        Assert.Equal(@"C:\Program Files\Google\Chrome\Application\chrome.exe",
            vm.CapabilityUseRows[1].AppPath);
        // The path is its own line and not folded into the name line — the
        // whole point of the second line.
        Assert.DoesNotContain(@"C:\", vm.CapabilityUseRows[1].Text);
    }

    /// The third fold, on the same one-way terms as the two above it: the
    /// excluded paths arrive on the snapshot, this page renders them, and no
    /// finding is ever handed one.
    [Fact]
    public async Task DefenderExclusions_RenderBehindTheFold_AndNowhereElse()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(findings: null,
            capabilityUses: Array.Empty<CapabilityUseRecord>(),
            defenderExclusions: new[] { @"C:\Users\U\.claude", @"C:\Temp\claude" });
        await state.ScanAsync();

        Assert.Equal(2, vm.DefenderExclusionRows.Count);
        Assert.Equal(@"C:\Users\U\.claude", vm.DefenderExclusionRows[0]);
        Assert.Equal(@"C:\Temp\claude", vm.DefenderExclusionRows[1]);
    }

    /// A machine whose consent store brisk could not read leaves this fold
    /// empty, which is what collapses it in the markup — the same answer the
    /// USB fold gives above, and for the same reason: the claim that brisk
    /// could not read something belongs on the finding, not on a row here.
    ///
    /// The fold being empty is half the fact and the weaker half: a snapshot
    /// carrying no uses would empty it too. What is pinned with it is where
    /// the FINDING went — an unreadable camera-mic-use into the page's
    /// could-not-read band, a headlined one into the disclosures — because
    /// that routing is what an empty fold is supposed to accompany.
    [Fact]
    public async Task NoCapabilityUseRead_LeavesTheFoldEmpty()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[] { Unreadable("camera-mic-use") });
        await state.ScanAsync();

        Assert.Empty(vm.CapabilityUseRows);
        Assert.Contains("camera-mic-use", vm.UnreadableRows.Select(r => r.RuleId));
        Assert.DoesNotContain("camera-mic-use", vm.DisclosureRows.Select(r => r.RuleId));

        var (vm2, host2, state2) = Build();
        host2.NextSnapshot = TestData.Snapshot(new[] { Disclosure("camera-mic-use", "3") });
        await state2.ScanAsync();

        Assert.Contains("camera-mic-use", vm2.DisclosureRows.Select(r => r.RuleId));
        Assert.DoesNotContain("camera-mic-use", vm2.UnreadableRows.Select(r => r.RuleId));
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

    /// The other half of red line 4, and the half only a reader sees: the row
    /// under "what brisk could not read" says it could not read.
    ///
    /// The band routes on the absence of a Headline; the SENTENCE comes from
    /// the finding's own TitleKey, and the disclosure rules write a separate
    /// "rule.<id>.title.unread" for it. Nothing joins those two facts — a row
    /// can land in this band wearing the readable title, which is a finished
    /// claim ("Windows uploaded data from this machine to other machines this
    /// month") sitting under a heading that says brisk established nothing.
    /// That is not hypothetical: it is what the tall render photographed, and
    /// the picture is the only thing that caught it.
    [Fact]
    public async Task AnUnreadableRow_WearsTheRulesUnreadTitle_NotItsReadableOne()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            Unreadable("delivery-optimization"),
        });
        await state.ScanAsync();

        var row = Assert.Single(vm.UnreadableRows);
        Assert.Equal(loc["rule.delivery-optimization.title.unread"], row.Title);
        Assert.NotEqual(loc["rule.delivery-optimization.title"], row.Title);
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
            ReadBackState.Held => HeldText(loc, days),
            ReadBackState.Reverted =>
                loc.F("readback.reverted", ReadBackRow.LocalDate(fixedAt)),
            ReadBackState.WrittenButIgnored => loc["readback.ignored"],
            ReadBackState.WrittenButUnverified => ByAge(loc, "readback.unverified", days),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state,
                "a read-back state this theory has no expected sentence for — " +
                "the renderer has one, and an unchecked sentence is a sentence " +
                "nobody has read"),
        };

        var row = await OneReadBackRow(
            new ReadBackResult("advertising-id", state, fixedAt), now);

        Assert.Equal(expected, row.Text);
    }

    /// WHICH DIRECTION THE LINE IS WRITTEN IN. Six of the seven switches turn
    /// something OFF and the read-back's sentences say so — "You switched
    /// this off 3 days ago; it still reads as off". uac-prompts is the
    /// inversion: its fix turns Windows' consent prompts back ON, so the
    /// family's line over a uac row would tell the reader the opposite of
    /// what brisk did, and a Held row — the good news — would be the loudest
    /// place it happened.
    ///
    /// The direction is the RULE's (TelemetrySwitchRule.FixTurnsItOn) and the
    /// row resolves it from the registry by the id it already carries, so no
    /// rule id is written down in the view model or in this theory's
    /// expectation. Both directions are here on purpose: an on-direction key
    /// set that also captured the other six would pass a theory that only
    /// looked at uac-prompts.
    [Theory]
    [InlineData("advertising-id", ReadBackState.Held, "readback.held")]
    [InlineData("advertising-id", ReadBackState.Reverted, "readback.reverted")]
    [InlineData("uac-prompts", ReadBackState.Held, "readback.on.held")]
    [InlineData("uac-prompts", ReadBackState.Reverted, "readback.on.reverted")]
    public async Task EachRow_TakesTheSentenceWrittenForTheDirectionItsRuleMoved(
        string ruleId, ReadBackState state, string key)
    {
        var loc = EnglishLoc();
        var fixedAt = new DateTime(2026, 8, 12, 9, 30, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 8, 15, 9, 30, 0, DateTimeKind.Utc);
        var expected = state == ReadBackState.Held
            ? ByAge(loc, key, ReadBackRow.DaysAgo(fixedAt, now))
            : loc.F(key, ReadBackRow.LocalDate(fixedAt));

        var row = await OneReadBackRow(new ReadBackResult(ruleId, state, fixedAt), now);

        Assert.Equal(expected, row.Text);
        // Loc answers a miss with the key itself and this theory builds its
        // expectation from the same key the renderer picks, so a key missing
        // from the resx would make both sides read "readback.on.held" and
        // pass. Nothing on screen may be a key.
        Assert.DoesNotContain("readback.", row.Text, StringComparison.Ordinal);
    }

    /// The two days a person has a word for. A count is right from the day
    /// after tomorrow onward and wrong before it: the live workbench read
    /// "0 gün önce kapattın" the afternoon of the fix, and the morning after
    /// it would have said "1 days ago".
    private static string HeldText(Loc loc, int days) => ByAge(loc, "readback.held", days);

    private static string ByAge(Loc loc, string key, int days) => days switch
    {
        0 => loc[$"{key}.today"],
        1 => loc[$"{key}.yesterday"],
        _ => loc.F(key, days),
    };

    /// Both directions, because ByAge builds the two spellings by SUFFIX: an
    /// on-direction switch whose `.today` twin was never written renders
    /// "readback.on.held.today" on a page, and the theory above cannot see it
    /// (it builds its expectation with the same helper, so both sides read
    /// the key). Here the raw-key guard is what catches it.
    [Theory]
    [InlineData("advertising-id", 0, "readback.held")]
    [InlineData("advertising-id", 1, "readback.held")]
    [InlineData("uac-prompts", 0, "readback.on.held")]
    [InlineData("uac-prompts", 1, "readback.on.held")]
    public async Task TheFreshestReadBacks_SayTodayAndYesterday(
        string ruleId, int daysAgo, string key)
    {
        var loc = EnglishLoc();
        var now = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);

        var row = await OneReadBackRow(
            new ReadBackResult(ruleId, ReadBackState.Held,
                now.AddDays(-daysAgo)), now);

        Assert.Equal(ByAge(loc, key, daysAgo), row.Text);
        Assert.DoesNotContain("0 ", row.Text);
        Assert.DoesNotContain("1 days", row.Text);
        Assert.DoesNotContain("readback.", row.Text, StringComparison.Ordinal);
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
    /// Written against the machine's own offset and anchored to ITS midnight,
    /// which is what makes the three answers below the same in every time
    /// zone. Hard-coding a UTC stamp and a UTC "now" would not: 23:00 UTC and
    /// 09:00 UTC the next morning are one local day apart in Istanbul and the
    /// same local day in Honolulu, so a pair of fixed instants proves
    /// whatever the developer's clock happens to be set to. Reading
    /// TimeZoneInfo.Local to BUILD the instants is the opposite of reading it
    /// to decide what to expect.
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

    /// What that floor looks like ON SCREEN, which is where it matters and
    /// where nothing was reading it.
    ///
    /// Two different machines reach zero — a fix applied earlier today, and a
    /// stamp from a clock that has moved — and the line they produce is the
    /// same one. That is the deliberate outcome and not an accident: brisk
    /// does not know which of the two readings is wrong on the second
    /// machine, so it says the smallest true thing rather than picking. What
    /// this pins is that the sentence is the ordinary one with a zero in it,
    /// not a stray "-355155 days ago" or an empty argument, in both cases.
    ///
    /// Both stamps are expressed as an offset FROM the same instant rather
    /// than as calendar dates, for the reason the day-count test above gives:
    /// a fixed date and a fixed "now" six hours apart are the same local day
    /// in Istanbul and two different ones in Honolulu, and a test that reads
    /// differently in two time zones proves whichever one it was run in.
    [Theory]
    [InlineData(0)]               // applied at this very instant — today
    [InlineData(24 * 3650)]       // a clock that has moved, ten years ahead
    public async Task AZeroDayCount_RendersTheOrdinarySentence(int hoursAhead)
    {
        var loc = EnglishLoc();
        var now = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);

        var row = await OneReadBackRow(
            new ReadBackResult("advertising-id", ReadBackState.Held,
                now.AddHours(hoursAhead)), now);

        Assert.Equal(HeldText(loc, 0), row.Text);
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

    /// The read-back dot's colour is a theme KEY the row chooses and the
    /// installed theme resolves — bound through ThemeFill, not written into
    /// the XAML as a {DynamicResource} literal — so ResourceKeyTests, which
    /// reads those literals out of the markup, cannot see it. A key that is
    /// not in the dictionary resolves to nothing and the dot paints nothing:
    /// no exception, no binding error, and a read-back line that has quietly
    /// lost its verdict colour.
    ///
    /// This asks whether the key EXISTS in each dictionary and nothing more.
    /// Whether the dot re-resolves it when the theme changes is a different
    /// question, and both dictionaries carried every key the whole time the
    /// answer to that one was no — EveryReadBackDot_WearsTheThemeThatIs
    /// InstalledNow is what asks it, by driving the real page across a swap.
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
                $"{theme} has no such key — the reference resolves to nothing " +
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

    // ---- The one row brisk points somewhere else for --------------------

    /// The spec's own requirement for Recall, quoted here and in
    /// HealthViewModel: the row shows STATE ONLY, WITH A LINK TO WINDOWS' OWN
    /// SETTING. The state shipped with the page; the link did not, and
    /// nothing on the page, in this file or in the report said it was
    /// missing — the row rendered as an ordinary advice card and the whole
    /// second half of the requirement was absent without a word.
    ///
    /// The spec said it as "Recall appears HERE as state only, with a link to
    /// Windows' own setting", under the switches block. The "here" was struck
    /// from that block, because the page never put the row there — Band sends
    /// a finding carrying a Headline to the disclosure band, which is exactly
    /// where the assertion below goes looking for it, and has since this test
    /// was written. The LINK half is unchanged and is what this asserts.
    ///
    /// Both halves are asserted together, because the link only means what
    /// it says while the other one holds. RecallStatusRule is Advise — the
    /// consent level FixRunner declines to apply a fix for — precisely
    /// because the surface is new, differs between builds, and a fix brisk
    /// cannot check afterwards is the one thing this project refuses to
    /// ship. A link sitting beside a Fix button would be brisk offering the
    /// change it just declined to make.
    ///
    /// The URI is asserted by SCHEME rather than by page, and the two
    /// assertions say different things: the scheme is the claim — this
    /// opens Windows' own Settings app and nothing brisk runs — and the
    /// equality is that the click carries what the row advertises, so the
    /// button cannot open one thing while the row names another.
    [Fact]
    public async Task RecallStatus_LinksToWindowsOwnSetting_AndOffersNoFix()
    {
        var opened = new List<string>();
        var (vm, host, state) = Build(openWindowsSetting: uri =>
        {
            opened.Add(uri);
            return true;
        });
        host.NextSnapshot = TestData.Snapshot(WholeTopic());
        await state.ScanAsync();

        var row = Assert.Single(
            vm.DisclosureRows.Where(r => r.RuleId == "recall-status"));
        Assert.False(row.CanFix,
            "the Recall row offers a fix. brisk reports this one and does not " +
            "change it — the setting is new and differs between builds — and a " +
            "link to Windows' own setting beside a Fix button is brisk offering " +
            "the change it just declined to make");
        Assert.True(row.HasWindowsSettingAction,
            "the Recall row carries no link to Windows' own setting, and the " +
            "spec requires this row to show state only WITH one");

        row.OpenWindowsSettingCommand.Execute(null);

        var uri = Assert.Single(opened);
        Assert.StartsWith("ms-settings:", uri, StringComparison.Ordinal);
        Assert.Equal(row.WindowsSettingUri, uri);
    }

    /// The control. Every other disclosure on this page is report-only too,
    /// and none of them grows a link — the link belongs to the one rule
    /// whose own advice string points at a setting Windows owns, not to
    /// "advice rows" as a class.
    [Fact]
    public async Task ADisclosureBriskPointsNowhereFor_CarriesNoLink()
    {
        var (vm, host, state) = Build(openWindowsSetting: _ => true);
        host.NextSnapshot = TestData.Snapshot(WholeTopic());
        await state.ScanAsync();

        var row = Assert.Single(
            vm.DisclosureRows.Where(r => r.RuleId == "usb-history"));

        Assert.False(row.HasWindowsSettingAction,
            "the USB-history row offers to open a Windows setting. brisk has " +
            "no setting to point at for it, so the link would open the " +
            "Settings app on something the row never mentioned");
        Assert.Equal("", row.WindowsSettingUri);
    }

    /// The other half of the same flag: a row nobody wired an opener for
    /// withholds the control rather than rendering a button that swallows
    /// the click. Same shape and same reason as HasStorageAction, which is
    /// false on every page that never handed FindingRow a way to navigate.
    [Fact]
    public void ARecallRow_WithNobodyToOpenTheSetting_WithholdsTheLink()
    {
        var row = Row(Disclosure("recall-status", "Off"));

        Assert.False(row.HasWindowsSettingAction,
            "a Recall row built with no opener behind it still advertises the " +
            "link, and clicking it would reach nothing at all");
    }

    /// Windows refusing to open its own settings is reported, not swallowed.
    /// The click is the row's only action, so a silent failure is a control
    /// that did nothing and said nothing — the dead affordance this branch
    /// has shipped once already, wearing a different coat.
    [Fact]
    public async Task WhenWindowsSettingsDoNotOpen_ThePageSaysSo()
    {
        var loc = EnglishLoc();
        var (vm, host, state) = Build(openWindowsSetting: _ => false);
        host.NextSnapshot = TestData.Snapshot(WholeTopic());
        await state.ScanAsync();

        var row = Assert.Single(
            vm.DisclosureRows.Where(r => r.RuleId == "recall-status"));
        row.OpenWindowsSettingCommand.Execute(null);

        Assert.Equal(loc["privacy.setting.failed"], vm.Message);
        Assert.NotEqual("privacy.setting.failed", vm.Message);
    }

    /// The caption and the destination are ONE declaration, and this is what
    /// says so.
    ///
    /// The button's text used to be a fixed key in the markup while the
    /// destinations lived in a Dictionary — one entry today, and a map is a
    /// map. A second rule pointing at, say, the diagnostics page would have
    /// rendered "Open Windows privacy settings" over a button that opens
    /// something else, and nothing anywhere would have objected: the caption
    /// was not a function of the URI. Now the rule declares both, so a second
    /// entry cannot be added without saying what its button says.
    ///
    /// Driven off the map rather than off a list here, so an entry added
    /// without a caption key — or with one NO resx carries — fails: Loc's
    /// indexer is `GetString(key, culture) ?? key` (Loc.cs:17) and hands the
    /// key back, which would render a button captioned
    /// "finding.action.windowssetting".
    ///
    /// The loop runs both languages and only the `en` pass can fail, which is
    /// why this is not named for both. ResourceManager falls back to the
    /// NEUTRAL resource, so a key present in English and missing from Turkish
    /// returns the ENGLISH string to the `tr` pass and stays green — the exact
    /// trap LocTests:88-104 measured on this branch, where the missing Turkish
    /// caption keys were caught by the key-set test and waved through by a
    /// theory shaped like this one. ResxFiles_ExposeTheSameKeySet is what
    /// holds the both-languages end, and it holds it over every key in the two
    /// files rather than over these.
    [Theory]
    [MemberData(nameof(EveryWindowsSettingRule))]
    public void EveryWindowsSettingRule_NamesAPageAndACaptionTheResxCarries(
        string ruleId, string uri, string captionKey)
    {
        // TWO PROTOCOLS, not one. WindowsSettingsLink.Open hands whatever is
        // declared here to the shell, and Windows Security's pages are not
        // under ms-settings: at all — its threat settings are reached through
        // windowsdefender://, the app's own protocol. Pinning the Settings
        // prefix alone would have banned the only URI that opens the page
        // defender-blind-spots is about.
        Assert.True(
            uri.StartsWith("ms-settings:", StringComparison.Ordinal)
            || uri.StartsWith("windowsdefender:", StringComparison.Ordinal),
            $"{ruleId} points at \"{uri}\", which is neither a Settings page " +
            "nor a Windows Security one");

        foreach (var language in new[] { "en", "tr" })
        {
            var loc = new Loc();
            loc.SetLanguage(language);
            Assert.NotEqual(captionKey, loc[captionKey]);
        }

        var row = new FindingRow(Disclosure(ruleId, "Off"), EnglishLoc(),
            canUndo: false, _ => { }, _ => { }, onOpenWindowsSetting: _ => { });
        Assert.Equal(EnglishLoc()[captionKey], row.WindowsSettingCaption);
        Assert.Equal(uri, row.WindowsSettingUri);
    }

    public static TheoryData<string, string, string> EveryWindowsSettingRule()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (ruleId, destination) in FindingRow.WindowsSettingRules)
            data.Add(ruleId, destination.Uri, destination.CaptionKey);
        return data;
    }

    // ---- The Security tier ----------------------------------------------

    /// The two security ids leave the bands they would otherwise fall into,
    /// and both are asserted because they arrive at the tier from opposite
    /// sides of Band: uac-prompts is Confirm and fixable, so without the
    /// routing it is a costly switch; defender-blind-spots carries a reading,
    /// so without it it is one of the numbers.
    [Fact]
    public async Task ASecurityFinding_LandsInSecurityRows_NotInTheSwitchBands()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            Switch("uac-prompts", RuleCategory.Confirm),
            Disclosure("defender-blind-spots", "5"),
        });
        await state.ScanAsync();

        Assert.Equal(
            new[] { "defender-blind-spots", "uac-prompts" },
            vm.SecurityRows.Select(r => r.RuleId)
                .OrderBy(id => id, StringComparer.Ordinal));
        Assert.Empty(vm.CostlySwitchRows);
        Assert.Empty(vm.SafeSwitchRows);
        Assert.Empty(vm.DisclosureRows);
        Assert.Empty(vm.UnreadableRows);
    }

    /// The page moves the row; the shareable card's "could not read" list
    /// does not move with it. ReportCardModel asks IsUnreadableDisclosure of
    /// the SNAPSHOT's findings rather than of this page's bands, so a
    /// Defender read that arrived empty is still named as unread on the card
    /// while standing under Security here — one claim, two surfaces, and the
    /// routing change must not have quietly taken it off one of them.
    [Fact]
    public async Task ASecurityFindingThatReadNothing_IsStillUnreadForTheCard()
    {
        var finding = Unreadable("defender-blind-spots");
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[] { finding });
        await state.ScanAsync();

        Assert.Single(vm.SecurityRows);
        Assert.Empty(vm.UnreadableRows);
        Assert.True(PrivacyViewModel.IsUnreadableDisclosure(finding),
            "the card's own unread section is fed from this predicate, and a " +
            "Defender read that arrived empty must still be named on it");
    }

    /// Three states, one row each, and the null row is Unread rather than
    /// Fine — the spec's second red line on the surface it was written for.
    /// The last assertion is about the strings: Loc answers a miss with the
    /// key itself, so a gap in the resx renders "privacy.posture.uac.unread"
    /// where the sentence goes, in the tier that leads the page.
    [Fact]
    public async Task PostureRows_RenderThreeStates_AndNullIsNeverFine()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(findings: null,
            posture: new SecurityPostureReport(true, true, true, false, null, true, true));
        await state.ScanAsync();

        Assert.Equal(5, vm.PostureRows.Count);
        Assert.Equal(PostureState.Fine, vm.PostureRows[0].State);       // firewall
        Assert.Equal(PostureState.Attention, vm.PostureRows[1].State);  // remote desktop
        Assert.Equal(PostureState.Unread, vm.PostureRows[2].State);     // uac
        Assert.Equal(PostureState.Fine, vm.PostureRows[3].State);       // defender real-time
        Assert.Equal(PostureState.Fine, vm.PostureRows[4].State);       // defender tamper
        Assert.DoesNotContain(vm.PostureRows,
            r => r.Text.Length == 0
                 || r.Text.StartsWith("privacy.posture.", StringComparison.Ordinal));
    }

    // ---- Helpers --------------------------------------------------------

    private static IReadOnlyList<FindingRow> AllRows(PrivacyViewModel vm) =>
        vm.DisclosureRows.Concat(vm.UnreadableRows).Concat(vm.SecurityRows)
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

    /// The same rule on a machine where its read found nothing. TWO things
    /// make it that, and this fixture carried only one of them until now: no
    /// Headline, which is the disclosure family's own way of saying it has no
    /// reading to lead with and what routes the row into the page's "could
    /// not read" band — and the rule's OWN unread title key, which is a
    /// different sentence from its readable one.
    ///
    /// TestData.Finding builds "rule.<id>.title", the READABLE one, so a row
    /// built with it renders "Windows uploaded data from this machine to
    /// other machines this month" under the heading saying brisk could not
    /// read it: a claim the real page never makes. The snapshot fixture was
    /// corrected for exactly this when the photograph showed it; this one was
    /// left as it was, and two fixtures for one shape is how the corrected
    /// half stops protecting the other.
    private static DiagnosticFinding Unreadable(string ruleId) => new(
        ruleId, $"rule.{ruleId}.title.unread", $"unread {ruleId}",
        $"evidence {ruleId}", Severity.Info, RuleCategory.Advise,
        ImpactStars: 1, CanFix: false, FixDescription: null,
        EvidenceKey: $"rule.{ruleId}.evidence.unread", EvidenceArgs: null,
        Headline: null, Kind: FindingKind.Notice);

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

    /// `openWindowsSetting` defaults to a stub that claims success and does
    /// nothing, so a test that never clicks the link cannot start the real
    /// Settings app; a test that DOES click passes its own and reads what it
    /// was handed.
    private static (PrivacyViewModel Vm, FakeEngineHost Host, AppState State) Build(
        Func<DateTime>? utcNow = null, Func<string, bool>? openWindowsSetting = null)
    {
        var host = new FakeEngineHost();
        var loc = EnglishLoc();
        var state = new AppState(host, loc);
        var vm = new PrivacyViewModel(state, host, loc, () => false,
            openWindowsSetting ?? (_ => true), utcNow,
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
