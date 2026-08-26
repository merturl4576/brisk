using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

/// The spec's red lines, on the side of the split that can see both the
/// shipped rules and the app's own routing list.
///
/// TWO OF THE FOUR ARE HERE, with the wave's health-score line beside them.
/// Red line 1 — no copy claims anything about what anybody else sees — runs
/// over every string the privacy topic ships, in both languages. Red line 4
/// — an unreadable probe never becomes a number — runs over every rule the
/// registry ships under an id this app routes. The health-score line — every
/// privacy finding is a Notice, so none of them grades the machine — runs
/// over the same rules.
///
/// RED LINE 2 IS NOT HERE, and a second copy of it would be worse than none.
/// Counts yes and contents never is enforced on the surface a screenshot
/// actually carries, by ReportCardModelTests: TheCard_CarriesCounts_AndNever
/// ADeviceOrAProgramName and PrivacyBan_EvidenceNamesAndPathsNeverReachThe
/// Card run over a card the real rules built from a registry with a real
/// device name planted in it, and AllTextOn_ReachesEveryStringTheModel
/// Exposes holds the reflection that makes those two cover the whole model.
/// A guard here would cover a narrower surface and drift from that one.
///
/// RED LINE 3 IS NOT HERE EITHER. A policy this edition ignores must read as
/// ignored, which is the read-back's WrittenButIgnored state, and it is held
/// in the engine's ReadBackTests by DiagnosticLevel_PolicyWritten_ButThe
/// MachineRecordsAHigherLevel_ReadsAsIgnored — where the second value a
/// machine keeps for that setting can be planted, which is what deciding
/// between the two states needs.
///
/// ONE of these is inherited and the rest are not, which is worth knowing.
/// Putting an id in PrivacyRuleIds.All is what keeps a finding off the pages
/// that grade the machine, so every rule that does so gets that line for
/// free. The Notice, the copy and the unreadable answer come from somewhere
/// the list cannot reach — each rule choosing each of them for itself — and
/// that is why they are read off the shipped rules below rather than
/// asserted about a finding written here.
public class PrivacyRedLineTests
{
    /// A characterization test: HealthScore.Compute already skips
    /// FindingKind.Notice, so this passes on the tree that introduced it. It
    /// catches exactly one regression — HealthScore charging for a Notice —
    /// and names the reason rather than letting a score quietly move.
    /// Watched red by flipping the planted finding's Kind to Problem.
    ///
    /// What it does NOT catch: the finding below is synthetic and its Notice
    /// is written here, so no real rule's Kind is ever read. A privacy rule
    /// shipped as a Problem would lower the score and this test would still
    /// pass. EveryPrivacyRule_ReportsANoticeAndCostsTheScoreNothing, further
    /// down this file, is the guard that reads real Kinds — off the shipped
    /// registry, over every id the shipped list carries. Three engine tests
    /// read them too, family by family, from id lists they keep themselves.
    [Fact]
    public void APrivacyNotice_DoesNotMoveTheHealthScore()
    {
        var withoutPrivacy = HealthScore.Compute(new[]
        {
            TestData.Finding("power-plan"),
        });
        var withPrivacy = HealthScore.Compute(new[]
        {
            TestData.Finding("power-plan"),
            TestData.Finding("advertising-id", Severity.Warning,
                RuleCategory.Auto, kind: FindingKind.Notice),
        });

        Assert.True(withoutPrivacy == withPrivacy,
            "a privacy finding moved the health score: " +
            $"{withoutPrivacy} without it, {withPrivacy} with it");
    }

    public static TheoryData<string> AllPrivacyRuleIds()
    {
        var data = new TheoryData<string>();
        foreach (var id in PrivacyRuleIds.All) data.Add(id);
        return data;
    }

    /// IsHealth was `!IsPerformance` until this commit, so without the new
    /// exclusion every one of these landed on Sağlık — the page that grades
    /// the machine's condition. Asserted through both overloads because the
    /// journal carries only a rule id and routes off the string one.
    [Theory]
    [MemberData(nameof(AllPrivacyRuleIds))]
    public void EveryPrivacyRule_RoutesToPrivacyAndNowhereElse(string ruleId)
    {
        Assert.True(FindingSections.IsPrivacy(ruleId),
            $"{ruleId} is in PrivacyRuleIds.All but IsPrivacy(string) denies it");
        Assert.False(FindingSections.IsHealth(ruleId),
            $"{ruleId} is a privacy rule and IsHealth(string) sends it to Sağlık");
        Assert.False(FindingSections.IsPerformance(ruleId),
            $"{ruleId} is a privacy rule and IsPerformance(string) sends it to Performans");

        var finding = TestData.Finding(ruleId, kind: FindingKind.Notice);
        Assert.True(FindingSections.IsPrivacy(finding),
            $"{ruleId} is in PrivacyRuleIds.All but IsPrivacy(finding) denies it");
        Assert.False(FindingSections.IsHealth(finding),
            $"{ruleId} is a privacy rule and IsHealth(finding) sends it to Sağlık");
        Assert.False(FindingSections.IsPerformance(finding),
            $"{ruleId} is a privacy rule and IsPerformance(finding) sends it to Performans");
    }

    /// Rule ids arrive from journal entries and report rows as well as from
    /// the rules themselves, so the set is matched the way the Performance
    /// set is: OrdinalIgnoreCase.
    [Fact]
    public void PrivacyRuleIds_MatchWithoutRegardToCase()
    {
        Assert.True(FindingSections.IsPrivacy("Advertising-ID"),
            "IsPrivacy is case-sensitive; the Performance set beside it is not");
    }

    /// The one cross-check the split makes possible. PrivacyRuleIds lives in
    /// Brisk and the rules live in BriskEngine, which cannot see it, so every
    /// rule hardcodes its own Id — and a typo there routes the finding to
    /// Sağlık without a word of complaint. Reading the shipped rule objects
    /// against the shipped list, from the one project that can see both, is
    /// what turns that silence into a failure. It reaches the six telemetry
    /// switches; its sibling below reaches the four report-only disclosures,
    /// which are not TelemetrySwitchRules and so are invisible to this one.
    /// A privacy rule arriving under some third base class needs the same
    /// line extended to it again.
    [Fact]
    public void EveryTelemetrySwitchRule_ShipsAnIdThePrivacyListCarries()
    {
        var switches = DiagnosticRuleRegistry.All
            .OfType<TelemetrySwitchRule>().ToList();

        Assert.Equal(6, switches.Count);
        foreach (var rule in switches)
            Assert.True(FindingSections.IsPrivacy(rule.Id),
                $"rule '{rule.Id}' is a telemetry switch, but no id in " +
                "PrivacyRuleIds.All matches it — its findings would route to Sağlık");
    }

    /// The same cross-check for the other half of the privacy topic. These
    /// four share no base class with the switches — they can be fixed by
    /// nobody, so they are AdviseRuleBase rules — which is exactly why the
    /// theory above cannot see them and why this one exists rather than a
    /// widened OfType. The count is asserted first, so deleting a rule shows
    /// up here as a failure instead of as a loop that runs twice and passes.
    [Fact]
    public void EveryPrivacyDisclosureRule_ShipsAnIdThePrivacyListCarries()
    {
        var disclosures = DiagnosticRuleRegistry.All
            .OfType<PrivacyDisclosureRule>().ToList();

        Assert.Equal(4, disclosures.Count);
        foreach (var rule in disclosures)
            Assert.True(FindingSections.IsPrivacy(rule.Id),
                $"rule '{rule.Id}' is a privacy disclosure, but no id in " +
                "PrivacyRuleIds.All matches it — its findings would route to Sağlık");
    }

    /// WHERE A PRIVACY FINDING GOES, over all three findings pages at once.
    ///
    /// This used to assert that it reached NEITHER page, and that was true
    /// and nearly worthless: it built its own two HealthViewModels over
    /// IsHealth and IsPerformance and asserted only about those, so a third
    /// page could be added beside them and every assertion here would still
    /// pass untouched — green by construction, with only its prose going
    /// false, silently. Task 7 built that third page, so this is the test
    /// that changed rather than the guard that survived: it builds all THREE
    /// the way App.xaml.cs wires them, and asserts where the finding LANDS as
    /// well as where it does not.
    ///
    /// Both directions, because each catches a different regression. A
    /// privacy finding on Sağlık or Performans is a disclosure sitting on a
    /// page that grades the machine. A privacy finding on NO page is the
    /// state this branch shipped for six commits, where the overview counted
    /// findings the GUI could not show. The control is the complement: a
    /// performance finding still lands on Performans and stays off Gizlilik,
    /// so a filter that had simply stopped answering could not pass this.
    ///
    /// What it does NOT claim: that the "{n} findings" figure changed. That
    /// count always included privacy and still does — see OverviewViewModel,
    /// where the arithmetic was deliberately left alone. What changed is that
    /// every finding it counts now has a row somewhere.
    ///
    /// The planted finding carries no Headline, so RevelationPicker skips it;
    /// that is a fact about THIS finding and not about privacy findings. The
    /// report-only disclosures ship a Headline when they have a reading, and
    /// on a real machine one of them can lead the overview band — with a live
    /// "see the evidence" link now, because a page hosts it.
    /// OpenFinding_OverAPrivacyRevelation_OffersTheLinkAndCarriesTheId holds
    /// that end, and ShellRoutingTests drives the real window to prove the
    /// link lands on Gizlilik.
    [Fact]
    public async Task APrivacyFinding_ReachesGizlilik_AndNoPageThatGradesTheMachine()
    {
        var host = new FakeEngineHost
        {
            NextSnapshot = TestData.Snapshot(new[]
            {
                TestData.Finding("advertising-id", cat: RuleCategory.Auto,
                    canFix: true, kind: FindingKind.Notice),
                TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
            }),
        };
        var loc = new Loc();
        loc.SetLanguage("en");
        var state = new AppState(host, loc);
        var fixAll = new FixAllService(host);
        // Wired exactly as App.xaml.cs:91-98 wires the only two it builds.
        var health = new HealthViewModel(state, host, loc, () => false, fixAll,
            FindingSections.IsHealth, doneFilter: FindingSections.IsHealth,
            crossLinkKey: "health.crosslink", morphPause: () => Task.CompletedTask);
        var perf = new HealthViewModel(state, host, loc, () => false, fixAll,
            FindingSections.IsPerformance, doneFilter: FindingSections.IsPerformance,
            crossLinkKey: "performance.crosslink", morphPause: () => Task.CompletedTask);
        // The third page. Building it here is what stops this test passing
        // by never asking about it.
        //
        // Wired the way App.xaml.cs wires it in every argument that decides
        // WHERE A FINDING LANDS, which is all this test is about, and NOT in
        // the opener: App passes WindowsSettingsLink.Open and this passes a
        // stub, because a red-line test that could start the Settings app is
        // a test with a side effect nobody asked for. That divergence is what
        // App_WiresTheRealWindowsSettingsOpener covers, since no copy of a
        // wiring can vouch for the original.
        var privacy = new PrivacyViewModel(state, host, loc, () => false,
            _ => true, morphPause: () => Task.CompletedTask);

        await state.ScanAsync();

        foreach (var (name, vm) in new[] { ("Saglik", health), ("Performans", perf) })
        {
            var shown = vm.Rows.Concat(vm.AdviseRows).Concat(vm.NoticeRows)
                .Select(r => r.RuleId).ToArray();
            Assert.False(shown.Contains("advertising-id"),
                $"advertising-id reached the {name} page, which grades the " +
                "machine and must not carry a privacy finding");
        }

        var onPrivacy = privacy.DisclosureRows.Concat(privacy.UnreadableRows)
            .Concat(privacy.SafeSwitchRows).Concat(privacy.CostlySwitchRows)
            .Select(r => r.RuleId).ToArray();
        Assert.True(onPrivacy.Contains("advertising-id"),
            "advertising-id reached no page at all. It is off both pages that " +
            "grade the machine, which is right, and off the one built to show " +
            "it, which is the state this branch shipped for six commits: a " +
            "finding the overview counted and no surface could show");

        // The control, both ways: the same wiring shows a non-privacy finding
        // on the page that owns it and keeps it off Gizlilik, so a filter
        // that had stopped answering could not pass this by accident.
        Assert.Contains("power-plan",
            perf.Rows.Concat(perf.AdviseRows).Concat(perf.NoticeRows)
                .Select(r => r.RuleId));
        Assert.False(onPrivacy.Contains("power-plan"),
            "power-plan reached Gizlilik, which shows the privacy topic and " +
            "nothing else");

        // About the finding planted above, which has no Headline — not about
        // privacy findings in general. See the note on this test.
        Assert.Empty(RevelationPicker.Pick(state.Snapshot!.Findings)
            .Where(f => FindingSections.IsPrivacy(f)));
    }

    /// The list itself, pinned in the order the spec names it. The theory
    /// above proves whatever is IN the list routes correctly; only this
    /// notices something dropping OUT of it, or an id arriving unannounced.
    [Fact]
    public void PrivacyRuleIds_AreTheTenThisWaveDeclares()
    {
        Assert.Equal(
            new[]
            {
                "advertising-id", "diagnostic-level", "tailored-experiences",
                "speech-typing", "location", "activity-history",
                "recall-status", "usb-history", "run-history",
                "delivery-optimization",
            },
            PrivacyRuleIds.All);
    }

    // ---------------------------------------------------------------------
    // RED LINE 1 — no copy claims anything about what Microsoft can see
    // ---------------------------------------------------------------------

    /// One phrase brisk's privacy copy may not contain, and the sentence the
    /// failure prints beside it.
    ///
    /// A REGEX rather than a substring, because the substring form of an
    /// English word ban fires on the words that contain it: "sent" sits
    /// inside "consent", which is the noun rule.location.evidence uses for
    /// the thing Windows actually records. A guard nobody can satisfy gets
    /// deleted, and the deletion takes the real ban with it.
    ///
    /// The Turkish entries are single words matched anywhere in the string,
    /// which is why the two-word phrases the spec and the brief name need no
    /// entry of their own: "göremez" catches "artık göremez", and "gitmiyor"
    /// catches "veri gitmiyor". Exactly one of them is a STEM, and its own
    /// comment says why.
    private sealed record BannedPhrase(string Pattern, string Why);

    /// Why the first half of the list is banned — red line 1's own sentence,
    /// said in terms of the RECORD it would need.
    private const string CannotPromiseIt =
        "brisk holds no record of what anybody receives — it read what this " +
        "machine holds, a switch's value or a record Windows wrote down, and " +
        "neither is a record of what moved — so it cannot promise that " +
        "anybody stopped seeing anything";

    /// Why the second half is: the same principle pointing the other way.
    /// NOT "brisk never names a transmission" — one rule in this topic holds
    /// a record of one and says so. See the note on
    /// NoPrivacyCopy_ClaimsAnythingAboutWhatAnybodyElseSees.
    private const string CannotAssertItEither =
        "brisk holds no record of a transmission here, and a claim needs the " +
        "same record whichever way it points; the rule that DOES hold one is " +
        "delivery-optimization, off Windows' own upload counter, and its " +
        "vocabulary is deliberately absent from this list";

    private static readonly BannedPhrase[] Banned =
    {
        // The assurance red line 1 names in as many words.
        new(@"no longer sees?", CannotPromiseIt),
        new(@"cannot see", CannotPromiseIt),
        new(@"can'?t see", CannotPromiseIt),
        new(@"stop(s|ped)? sending", CannotPromiseIt),
        new(@"göremez", CannotPromiseIt),
        new(@"görmüyor", CannotPromiseIt),
        new(@"gitmiyor", CannotPromiseIt),
        new(@"toplamıyor", CannotPromiseIt),

        // The same claim asserted rather than denied. "Microsoft" is banned
        // bare rather than only beside a verb: brisk names no company
        // anywhere in this topic, and any sentence that named one would be
        // brisk speaking for a party it never read. The engine's own
        // NoDisclosureCopy_ClaimsAnythingAboutWhoReceivesWhat already bans
        // the bare word over part of the same copy, so this is the house
        // reading rather than a new one.
        //
        // WHAT IS DELIBERATELY NOT HERE: upload, uploaded, yükle. Those are
        // delivery-optimization's words, and it holds the counter that earns
        // them — putting one on this list turns the ban red on shipped copy
        // that is right. TheOneTransmissionClaimBriskHasARecordOf_IsNotBanned
        // is what stops that being an accident nobody notices.
        new(@"Microsoft", CannotAssertItEither),
        new(@"\bsends?\b", CannotAssertItEither),
        new(@"\bsending\b", CannotAssertItEither),
        new(@"\bsent\b", CannotAssertItEither),
        new(@"\bsees\b", CannotAssertItEither),
        new(@"\breceiv(e|es|ed)\b", CannotAssertItEither),
        new(@"\bcollect(s|ed|ing)?\b", CannotAssertItEither),
        // The stem, and it deliberately catches the NEGATIVE forms too —
        // "gönderilmiyor", "is not sent" — because denying a transmission
        // needs exactly the sight that asserting one does, and brisk has
        // neither. \w* rather than a bare stem so the failure prints the
        // whole word it found instead of the fragment it matched. The other
        // Turkish entries stay spelled out: "topl\w*" would ban "toplam",
        // which is the ordinary word for a total, and "gör\w*" would ban
        // half the language.
        new(@"gönder\w*", CannotAssertItEither),
        new(@"topluyor", CannotAssertItEither),
        new(@"görüyor", CannotAssertItEither),
        new(@"gidiyor", CannotAssertItEither),
    };

    /// THE SPEC'S FIRST RED LINE, over every string the privacy topic ships,
    /// in both languages. Spec, "The red lines" 1: brisk never says
    /// "Microsoft can no longer see this", because brisk reads a machine and
    /// has no visibility into what Microsoft receives.
    ///
    /// THREE FAMILIES OF KEY, and the third is what this guard was widened
    /// for. `rule.<id>.*` is the rules' own copy, and two engine tests
    /// already ban a shorter list over parts of it. `readback.*` is the four
    /// sentences the read-back block renders, and ReadBackTests bans a list
    /// over exactly those four. `privacy.*` IS THE PAGE'S OWN COPY — its
    /// headings, its buttons, the sentence under each block — and nothing
    /// read what it CLAIMED until this test. LocTests names all but one of
    /// the `privacy.*` keys and proves each LOADS, which is a different
    /// question and the only one anything asked; nothing read a word of the
    /// value. That is where the one live breach was. (The key LocTests misses
    /// is `privacy.setting.failed`. This test reads it — for what it says,
    /// not for whether it resolves, which is still unguarded.)
    ///
    /// THE PRINCIPLE IS "NO TRANSMISSION CLAIM WITHOUT A RECORD OF ONE", and
    /// stating it that way is a decision this task took rather than a line
    /// the spec wrote. Red line 1 forbids the assurance, and the reason it
    /// gives is that brisk reads a machine and has no visibility into what
    /// Microsoft receives. That is a statement about EVIDENCE, so it cuts
    /// both ways: a denial needs the same record an assertion does, and a
    /// switch is not a record of what moved. The switches heading was
    /// asserting a transmission brisk had no record of — "What gets sent to
    /// Windows" / "Windows'a ne gönderiliyor", shipped from the spec's own UI
    /// section by Task 7, which flagged the collision and left the call here.
    ///
    /// IT DOES NOT SAY "brisk never names a transmission", which is what an
    /// earlier draft of this reasoning amounted to and which brisk's own copy
    /// refutes. delivery-optimization reads Windows' running count of the
    /// bytes uploaded from this machine, and a claim standing on a counter
    /// brisk read is a claim brisk earned. That is why the upload vocabulary
    /// is missing from the list above by decision, and why
    /// TheOneTransmissionClaimBriskHasARecordOf_IsNotBanned exists.
    ///
    /// Three things settled it, and the third is the one that is not a hedge.
    /// (1) It was the only string in all three families, in either language,
    /// asserting a transmission BRISK HOLDS NO RECORD OF, and the only one
    /// naming WINDOWS AS THE RECIPIENT of one. Not the only one naming a
    /// transmission: rule.delivery-optimization.* names one in six English
    /// strings and their six Turkish twins, off a counter brisk read, and
    /// names "other machines" as the recipient rather than Windows. An
    /// earlier draft of this paragraph claimed the heading was the only
    /// transmission claim in the topic at all, and that was false — the
    /// distinction the refinement carries is the whole principle, so losing
    /// it lost the argument. (2) The sentence directly beneath it said the
    /// opposite in as many words — "what leaves this
    /// machine is not something those reads can tell you" — so the mitigation
    /// worked by contradicting the heading it mitigated. (3) It was wrong
    /// about the switches it headed. An advertising ID is a number apps on
    /// this machine read; tailored experiences is Windows using data it
    /// already holds; the location consent governs what apps here may ask
    /// for. Three of the six are not about anything being sent to Windows, so
    /// the heading did not merely outrun its evidence, it mislabelled half of
    /// its own block. It now reads "Windows' own privacy switches" /
    /// "Windows'un kendi gizlilik anahtarları", and the spec's UI section
    /// records the change and quotes what it replaced.
    ///
    /// NEITHER THIS LIST NOR THE ENGINE'S IS A SUPERSET OF THE OTHER, and
    /// both run. ReadBackTests bans "leaves your machine" over its own four
    /// sentences and this does not, on purpose: the page's own note says
    /// "what leaves this machine is not something those reads can tell you",
    /// which is the honest limit and not a claim, and no phrase list can tell
    /// a claim from a denial of knowledge. A ban of that shape is affordable
    /// over four sentences one person wrote together and is not over the
    /// whole topic's copy in two languages. The engine's lists are also blind
    /// to `privacy.*` and to PrivacyRuleIds.All, which BriskEngine cannot
    /// see.
    ///
    /// THE RESX BAN REACHES THE ENGINE'S OWN PROSE, which matters because
    /// that prose is concatenated C# and a phrase split across a `+` is
    /// invisible to any matcher. It does not have to be visible here.
    /// TheEnglishResx_SaysWhatTheEngineSays — which exists in THREE engine
    /// files, between them covering all six switches, the three registry
    /// disclosures and delivery-optimization — asserts the English resx
    /// string EQUALS the concatenated whole, so a banned phrase assembled
    /// from fragments in a rule's Title or Evidence arrives in this file in
    /// one piece. The Turkish side needs no such bridge: this reads that file
    /// directly.
    ///
    /// TWO RESIDUALS, not one. An earlier version of this paragraph named
    /// only FixDescription — a false universal about this guard's own
    /// coverage, in the file whose whole job is to stop those.
    ///
    /// FixDescription. Nothing pins its wording to a resx key and nothing
    /// scans it, so a claim made there is made where neither this guard nor
    /// any other is looking. Only the six switches carry one; the four
    /// disclosures ship it null.
    ///
    /// Headline.Caption, which has the same status and a sharper edge.
    /// Headline declares Caption as ENGLISH PROSE beside CaptionKey, on the
    /// same convention as Evidence — and the three TheEnglishResx tests
    /// assert Title and Evidence ONLY. Nothing in this suite asserts a
    /// privacy rule's Headline.Caption equals rule.<id>.headline.caption, so
    /// that English never has to arrive in the resx and this guard never sees
    /// it. It is not dead text either: LocalizedText.Headline renders the
    /// English Caption whenever the key is missing.
    ///
    /// WHAT HOLDS TODAY AND WHAT DOES NOT. For the four privacy rules that
    /// carry a headline the CaptionKey is pinned PRESENT in both files, by
    /// PrivacyDisclosureRuleTests' EveryKeyTheRuleNames_IsInBothLanguages and
    /// DeliveryOptimizationRuleTests' twin, so the fallback cannot fire for
    /// them and the resx string this guard reads is the one that renders.
    /// Brisk.Cli reads no Headline at all, so nothing else prints the
    /// English. The live exposure is an ELEVENTH privacy rule carrying a
    /// headline: neither of those two tests would reach its caption key, and
    /// a claim in its English caption would render on the fallback path with
    /// nothing having read it.
    ///
    /// Every offence is collected before the assertion, so one run names
    /// every key and every phrase rather than the first one and a rerun.
    [Fact]
    public void NoPrivacyCopy_ClaimsAnythingAboutWhatAnybodyElseSees()
    {
        var offences = new List<string>();
        foreach (var file in ResxFiles)
        foreach (var (key, text) in Resx(file))
        {
            if (!IsPrivacyCopy(key)) continue;
            foreach (var (phrase, banned) in ClaimsIn(text))
                offences.Add($"{file}: {key} says \"{phrase}\" — {banned.Why}");
        }

        Assert.True(offences.Count == 0,
            $"{offences.Count} string(s) in brisk's privacy copy make a claim " +
            "about what another party sees:" + Environment.NewLine +
            string.Join(Environment.NewLine, offences));
    }

    /// THE CONTROL, in both directions, because a sweep that searches nothing
    /// returns all-zero and looks exactly like a clean one — the two results
    /// are indistinguishable from the outside, which is what lets that
    /// failure survive a review.
    ///
    /// Outward: the scope predicate reaches all three families in both files,
    /// and every rule on the list contributes copy to it. A ban that quietly
    /// matched no keys would stay green forever.
    ///
    /// Inward: the matcher fires on planted text, including the Turkish
    /// sentence the task brief names, and it reaches real Turkish letters in
    /// the real file. CultureInvariant is not decoration — this repo is built
    /// on a Turkish-locale machine, where a culture-sensitive IgnoreCase
    /// folds I to ı and a banned phrase can match text nobody wrote.
    [Fact]
    public void ThePrivacyCopyGuard_ReachesAllThreeFamilies_AndCanFire()
    {
        foreach (var file in ResxFiles)
        {
            var keys = Resx(file).Keys.Where(IsPrivacyCopy).ToList();
            Assert.True(keys.Any(k => k.StartsWith("readback.", StringComparison.Ordinal)),
                $"{file} contributed no readback.* key to the copy ban");
            Assert.True(keys.Any(k => k.StartsWith("privacy.", StringComparison.Ordinal)),
                $"{file} contributed no privacy.* key to the copy ban — the " +
                "page's own copy is the family this guard was widened for");
            foreach (var id in PrivacyRuleIds.All)
                Assert.True(
                    keys.Any(k => k.StartsWith($"rule.{id}.", StringComparison.Ordinal)),
                    $"{file} carries no rule.{id}.* string, so the copy ban " +
                    "covers nothing that rule says");
        }

        var planted = ClaimsIn("Microsoft artık göremez.").Select(hit => hit.Phrase).ToList();
        Assert.Contains("Microsoft", planted);
        Assert.Contains("göremez", planted);
        Assert.Empty(ClaimsIn("brisk read the switch and it does not read as off."));
        // The trap every English pattern is a regex for: "consent" ends in
        // "sent", and rule.location.evidence says it. A substring ban fires
        // here, and this is what stops one being written back in.
        Assert.Empty(ClaimsIn("brisk read the location consent on this machine."));

        // The same options, on the real file, with a dotless ı in the needle:
        // proof that the comparison above reaches Turkish text rather than
        // sliding off it.
        Assert.True(Match(Resx("Strings.tr.resx")["privacy.hero.note"], "ayarları").Success,
            "the Turkish page note does not contain the Turkish word this " +
            "control looks for, so the ban above may be matching nothing at all");
    }

    /// THE ONE TRANSMISSION CLAIM BRISK HAS EARNED, and the reason the
    /// principle is "no transmission claim without a record of one" rather
    /// than "no transmission claim".
    ///
    /// rule.delivery-optimization.* says this machine uploaded data to other
    /// machines — a transmission, stated plainly, in six English strings and
    /// their six Turkish twins. It is not banned and must not be. brisk read
    /// Windows' own running counter of those bytes, which is the only record
    /// of a TRANSMISSION anything in this topic holds: the rest of the topic
    /// reads what this machine HOLDS — a switch's value, or a record Windows
    /// wrote down, which is what usb-history and run-history count — and
    /// neither of those is a record of what moved. (An earlier draft said
    /// "every other privacy rule reads a SETTING". Two of them do not, and
    /// PrivacyDisclosureRule's own header says so: "what Windows has already
    /// written down about this machine, counted. Nothing here is a switch.")
    /// This copy also names no recipient brisk did not read: "other
    /// machines", never Windows and never a company, which is exactly what
    /// the heading this task replaced did name.
    ///
    /// THE WARRANT DOES NOT COVER ALL SIX EVENLY, and claiming it for "six
    /// English strings" without this was the same absence one level down: an
    /// exception stated at family granularity whose justification holds on
    /// one path.
    ///
    ///   THE COUNTER WAS READ — title, evidence, headline.caption. Reported()
    ///   builds all three and runs only when the probe returned a positive
    ///   count, so the counter is in brisk's hands exactly as they are
    ///   written.
    ///
    ///   THE COUNTER WAS NOT — title.unread, evidence.unread. Unread() builds
    ///   both, and is reached precisely when BytesUploadedToPeers() answered
    ///   null or a figure that is not a quantity. They are permitted because
    ///   they name the transmission only to say brisk COULD NOT MEASURE IT,
    ///   and evidence.unread refuses the count in as many words: "a machine
    ///   that uploaded nothing and a machine brisk could not ask are
    ///   different things, so brisk does not report a count of none". Naming
    ///   a quantity you failed to obtain asserts no transmission, so the
    ///   principle is SATISFIED there rather than excepted.
    ///
    ///   EITHER PATH — advice. HealthViewModel keys it on the RULE ID and not
    ///   on the finding, so it rides whichever row landed. It names the
    ///   upload as what the counter MEASURES rather than as something that
    ///   happened here, which is true on both paths.
    ///
    /// TWO NEEDLES, REQUIRED OF ONE STRING, because one is not enough in
    /// either language. Turkish "yükle" also means INSTALL, so copy rewritten
    /// to "güncelleme yükledi" would hold a verb-only watch green while the
    /// premise it guards died; and a recipient-only watch passes on any
    /// sentence that merely mentions other machines. A transmission claim
    /// needs something moving AND somewhere it moved to, so this demands both
    /// of the same string.
    ///
    /// BOTH DIRECTIONS ARE LIVE HERE. Put "upload" or "yükle" on the banned
    /// list and the ban above goes red on shipped copy that is right; rewrite
    /// this rule's copy so it no longer names a transmission and THIS goes
    /// red instead, which is when the reasoning on the ban is describing copy
    /// that is gone. Without it the exception is an absence from a list, and
    /// an absence records no decision.
    [Theory]
    [InlineData("Strings.resx", "uploaded", "to other machines")]
    [InlineData("Strings.tr.resx", "yükle", "başka makinelere")]
    public void TheOneTransmissionClaimBriskHasARecordOf_IsNotBanned(
        string file, string movingIt, string theRecipient)
    {
        var counterBacked = Resx(file)
            .Where(pair => pair.Key.StartsWith("rule.delivery-optimization.",
                StringComparison.Ordinal))
            .ToList();

        Assert.True(counterBacked.Count > 0,
            $"{file} carries no rule.delivery-optimization.* string at all, so " +
            "the one transmission claim the ban deliberately allows is not " +
            "there to allow");
        Assert.True(
            counterBacked.Any(pair => Match(pair.Value, movingIt).Success
                && Match(pair.Value, theRecipient).Success),
            $"no single rule.delivery-optimization.* string in {file} still " +
            $"says both \"{movingIt}\" and \"{theRecipient}\". This rule's " +
            "copy is the one transmission claim the ban allows, and the " +
            "reasoning on the ban is written about copy that names one — " +
            "both halves, because something moving without somewhere it moved " +
            "to is not a transmission claim (and Turkish \"yükle\" alone is " +
            "also the word for installing something)");

        foreach (var (key, text) in counterBacked)
            Assert.False(ClaimsIn(text).Any(),
                $"{key} in {file} trips the copy ban. This rule read Windows' " +
                "own upload counter, so it is the one place in this topic with " +
                "a record of a transmission and the one place that may say so " +
                "— a banned phrase reaching it means the list has grown past " +
                "its own principle");
    }

    /// Every string the privacy topic ships, in the three families named on
    /// the ban above. `privacy.readback.section` belongs to the page and
    /// matches the page's prefix; nothing outside the topic uses either.
    private static bool IsPrivacyCopy(string key) =>
        key.StartsWith("readback.", StringComparison.Ordinal)
        || key.StartsWith("privacy.", StringComparison.Ordinal)
        || PrivacyRuleIds.All.Any(id =>
            key.StartsWith($"rule.{id}.", StringComparison.Ordinal));

    private static IEnumerable<(string Phrase, BannedPhrase Banned)> ClaimsIn(string text)
    {
        foreach (var banned in Banned)
            if (Match(text, banned.Pattern) is { Success: true } hit)
                yield return (hit.Value, banned);
    }

    private static Match Match(string text, string pattern) => Regex.Match(
        text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] ResxFiles = { "Strings.resx", "Strings.tr.resx" };

    private static Dictionary<string, string> Resx(string fileName) =>
        XDocument.Load(Path.Combine(BriskDir(), "Localization", fileName))
            .Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!,
                e => e.Element("value")?.Value ?? "");

    // ---------------------------------------------------------------------
    // The health-score line and RED LINE 4 — over the SHIPPED rules
    // ---------------------------------------------------------------------

    /// EVERY PRIVACY RULE THE BUILD SHIPS EMITS A NOTICE, taken off the two
    /// shipped lists rather than off a list a test keeps.
    ///
    /// The characterization test at the top of this file plants a Notice and
    /// proves HealthScore skips it, and says in as many words that no real
    /// rule's Kind is read there. Three engine tests do read real Kinds —
    /// TelemetrySwitchRuleTests, PrivacyDisclosureRuleTests and
    /// DeliveryOptimizationRuleTests — and each drives its own family from a
    /// HAND-WRITTEN id list and constructs the rule objects itself. Between
    /// them they cover today's ten and grow for nobody: an eleventh privacy
    /// rule, registered and routed and shipped, is graded by none of them.
    ///
    /// This intersects DiagnosticRuleRegistry.All with PrivacyRuleIds.All, so
    /// a rule added to both is covered the day it is added, whatever base
    /// class it arrives under. Brisk.Tests is the only project that can see
    /// both lists.
    ///
    /// The finding is asserted to EXIST before its Kind is read. On a machine
    /// where every setting and record this topic reads is present and on, a
    /// rule that reports nothing is not a rule that passed — it is a rule
    /// whose Kind nothing here ever looked at.
    [Theory]
    [MemberData(nameof(AllPrivacyRuleIds))]
    public void EveryPrivacyRule_ReportsANoticeAndCostsTheScoreNothing(string ruleId)
    {
        var finding = ShippedRule(ruleId).Detect(EverythingReadsAsOn());

        Assert.True(finding is not null,
            $"{ruleId} reports nothing on a machine where every setting and " +
            "record this topic reads is present and on, so nothing here ever " +
            "read its Kind");
        Assert.True(finding!.Kind == FindingKind.Notice,
            $"{ruleId} ships as {finding.Kind}; every finding in this wave is a " +
            "Notice, including the ones brisk can fix (spec, \"Health score\")");
        Assert.True(HealthScore.Compute(new[] { finding }) == 100,
            $"{ruleId} moved the health score to " +
            $"{HealthScore.Compute(new[] { finding })} on an otherwise clean " +
            "machine; privacy is a second axis and brisk does not grade it");
    }

    /// RED LINE 4, over every rule the topic ships: what could not be read
    /// never becomes a number.
    ///
    /// The fixture answers nothing — an empty registry, and a Delivery
    /// Optimization probe that returns null, which is that probe's own way of
    /// saying it could not read its counter and is a different answer from
    /// zero. A rule that COUNTS something lands on its unreadable sentence
    /// there, which is the red line's own case. A SWITCH lands somewhere
    /// adjacent and deliberately so: absence reads as on for it, because what
    /// brisk cannot read as off it does not report as off. So this covers the
    /// red line in both of the forms it takes in this topic, and claims
    /// neither of them is the other. Which rule falls where is the rule's
    /// business and is not listed here — the assertions below hold for both
    /// answers.
    ///
    /// WHAT IS ASSERTED. No headline, because a headline is the number a
    /// finding leads with and there is no reading here to make one out of.
    /// No digit in any sentence a reader is shown — the engine's own English,
    /// which is what `brisk scan` prints, and the string behind each key in
    /// BOTH resx files, which is what the GUI renders instead. No digit in an
    /// evidence argument either, since an argument is where a number is
    /// substituted in. Format placeholders are stripped before the scan: {0}
    /// is not a number, the argument it would take is, and on this machine
    /// there are none.
    [Theory]
    [MemberData(nameof(AllPrivacyRuleIds))]
    public void NoPrivacyRule_TurnsAnUnreadableProbeIntoANumber(string ruleId)
    {
        var finding = ShippedRule(ruleId).Detect(NothingReadsAtAll());

        Assert.True(finding is not null,
            $"{ruleId} reports nothing at all on a machine it could read " +
            "nothing from. A probe that failed belongs in okuyamadıklarım, and " +
            "silence puts it nowhere");
        Assert.True(finding!.Headline is null,
            $"{ruleId} leads with \"{finding.Headline?.Value}\" on a machine it " +
            "could read nothing from — a headline is the number a finding leads " +
            "with, and nothing here was read to make one out of");

        NoNumberIn(ruleId, "the English the CLI prints", finding.Title);
        NoNumberIn(ruleId, "the English the CLI prints", finding.Evidence);
        foreach (var argument in finding.EvidenceArgs ?? Array.Empty<string>())
            NoNumberIn(ruleId, "an evidence argument", argument);

        Assert.True(finding.EvidenceKey is not null,
            $"{ruleId} names no evidence key, so the GUI has nothing to render " +
            "its unreadable sentence from");
        foreach (var file in ResxFiles)
        foreach (var key in new[] { finding.TitleKey, finding.EvidenceKey! })
        {
            Assert.True(Resx(file).TryGetValue(key, out var text),
                $"{ruleId} names {key} and {file} does not carry it, so the " +
                "GUI renders the raw key where the sentence goes");
            NoNumberIn(ruleId, $"{key} in {file}", text!);
        }
    }

    private static void NoNumberIn(string ruleId, string where, string text)
    {
        var number = Regex.Match(Regex.Replace(text, @"\{\d+\}", ""), @"\d+");
        Assert.False(number.Success,
            $"{ruleId} states the number {number.Value} in {where} on a machine " +
            $"it could read nothing from: \"{Collapse(text)}\"");
    }

    /// The rule THE BUILD SHIPS under this id, never one this test builds. A
    /// list entry with no rule behind it is a page that will never show that
    /// row, and nothing else in the suite reads the list in this direction.
    private static IDiagnosticRule ShippedRule(string ruleId)
    {
        var matches = DiagnosticRuleRegistry.All
            .Where(r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(matches.Count == 1,
            $"PrivacyRuleIds.All carries '{ruleId}' and DiagnosticRuleRegistry " +
            $"ships {matches.Count} rules under that id; the page shows the " +
            "findings of exactly the rules registered under it");
        return matches[0];
    }

    /// A machine where every switch this topic reads is on and every record
    /// it counts has something in it — the one state in which all ten rules
    /// have something to report.
    ///
    /// The numbered switches are planted from their OWN Values collection, so
    /// a switch that moves its keys stays planted. Location's state is a WORD
    /// and its Values is empty by design, so it is planted by name from that
    /// rule's own constants: anything that does not read as Deny reads as on,
    /// and Allow is the word Windows writes there.
    private static DiagnosticContext EverythingReadsAsOn()
    {
        var reg = new FakeRegistry();
        foreach (var rule in DiagnosticRuleRegistry.All.OfType<TelemetrySwitchRule>())
        foreach (var value in rule.Values)
            reg.SetInt(value.KeyPath, value.ValueName, value.OnValue);
        reg.SetString(LocationRule.KeyPath, LocationRule.ValueName, "Allow");

        // One USB model with one instance under it — the two levels deep
        // Windows records one attached device at.
        const string model = "Ven_Test&Prod_Stick";
        Sub(reg, UsbHistoryRule.KeyPath, model);
        Sub(reg, $@"{UsbHistoryRule.KeyPath}\{model}", "instance-01");
        // One entry under one of the two keys UserAssist counts are kept in.
        reg.SetString(RunHistoryRule.CountKeyPaths[0], "an-encoded-entry", "");
        // Zero is the reading that LEAVES Recall's data analysis on: the
        // value names what it disables, so its sense is inverted.
        reg.SetInt(RecallStatusRule.KeyPath, RecallStatusRule.ValueName, 0);

        return Context(reg, uploadedBytes: 4L << 30);
    }

    /// A machine that answers nothing at all.
    private static DiagnosticContext NothingReadsAtAll() =>
        Context(new FakeRegistry(), uploadedBytes: null);

    /// The eleven probes no privacy rule reads THROW, so a rule that grows a
    /// reading nobody arranged fails loudly rather than measuring a machine
    /// this fixture never described. The twelfth answers, because
    /// delivery-optimization is the one rule in the topic that reads
    /// something other than the registry — and NoOtherProbes' throw would be
    /// swallowed by that rule's own catch and turn silently into the
    /// unreadable answer, which is one of the two states these tests exist to
    /// tell apart.
    private static DiagnosticContext Context(IRegistryProbe registry, long? uploadedBytes)
    {
        var none = new NoOtherProbes();
        return new DiagnosticContext(none, registry, none, none, none, none, none,
            none, none, none, none,
            new DeliveryOptimizationReading(uploadedBytes),
            Path.Combine(Path.GetTempPath(), "brisk-privacy-red-line-context"));
    }

    /// Null is the probe's own "I could not read the counter", and a number
    /// is a reading. Nothing here rounds one into the other.
    private sealed record DeliveryOptimizationReading(long? Bytes)
        : IDeliveryOptimizationProbe
    {
        public long? BytesUploadedToPeers() => Bytes;
    }

    private static void Sub(FakeRegistry reg, string parent, string child)
    {
        if (!reg.SubKeys.TryGetValue(parent, out var children))
            reg.SubKeys[parent] = children = new List<string>();
        if (!children.Contains(child)) children.Add(child);
    }

    /// The half a required constructor parameter cannot reach.
    ///
    /// PrivacyViewModel's opener has no default, so the compiler guarantees
    /// every construction site passes SOMETHING — and every test in this
    /// suite passes a stub, because a test that could start the Settings app
    /// has a side effect nobody asked for. What no test could otherwise
    /// notice is the production site passing one too: `_ => false` compiles,
    /// ships, and withholds nothing visibly while the Recall row's link does
    /// nothing at all on a real machine.
    ///
    /// So the one wiring that matters is read from SOURCE. Crude on purpose
    /// — it matches the call and looks for the name inside it — because the
    /// alternative is a composition root refactored to be observable, which
    /// is a larger claim than this needs.
    [Fact]
    public void App_WiresTheRealWindowsSettingsOpener()
    {
        var source = File.ReadAllText(Path.Combine(BriskDir(), "App.xaml.cs"));
        var call = Regex.Match(source,
            @"new\s+PrivacyViewModel\s*\((?<args>[^;]*?)\)\s*;",
            RegexOptions.Singleline);

        Assert.True(call.Success,
            "App.xaml.cs builds no PrivacyViewModel at all — the Gizlilik " +
            "page is not wired into the running app");
        var args = call.Groups["args"].Value;
        Assert.True(args.Contains("WindowsSettingsLink.Open", StringComparison.Ordinal),
            "App.xaml.cs builds the Gizlilik page with " +
            $"`{Collapse(args)}` and nothing in it names " +
            "WindowsSettingsLink.Open. The opener is a required parameter, so " +
            "this compiles with a stub in it — and a stub there is a Recall " +
            "row whose only control reaches nothing on a real machine");
    }

    private static string Collapse(string text) =>
        Regex.Replace(text, @"\s+", " ").Trim();

    private static string BriskDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null;
             dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return Path.Combine(dir.FullName, "src", "Brisk");
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }
}
