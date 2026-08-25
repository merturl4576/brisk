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

/// The two lines the disclosure wave rests on: a privacy finding never moves
/// the health score, and a privacy rule never lands on a page that grades the
/// machine. Only the second is inherited: putting an id in PrivacyRuleIds.All
/// is what routes it, and every rule that does so gets that line for free.
/// The score exemption comes from somewhere the list cannot reach — each rule
/// choosing FindingKind.Notice for itself.
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
    /// pass. That guard runs over the real rules and lives in the engine's
    /// own suite, where a DiagnosticContext can be built — it covers the six
    /// telemetry switches and the four report-only disclosures, which is
    /// every id on the list below.
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

    /// THE STATE OF THIS BUILD, pinned so that changing it is deliberate.
    /// There is no privacy page yet: App.xaml.cs builds exactly two findings
    /// pages, IsHealth and IsPerformance, and both exclude these ids, so a
    /// privacy finding reaches no row on either. The finding planted below
    /// also carries no Headline, so RevelationPicker — and the report card,
    /// which picks through it — skip THAT one as well. That is a fact about
    /// this planted finding and no longer a fact about privacy findings: the
    /// report-only disclosures ship a Headline when they have a reading to
    /// lead with, and RevelationPicker takes findings that carry one, so on a
    /// real machine a privacy finding can reach the overview band and the
    /// report card's picked list while still reaching no row on either page.
    /// The band shows the number and the claim, and withholds one control in
    /// itself: no "see the evidence" link, because no page hosts the finding.
    /// That is not the empty band's silence — the empty band hides its whole
    /// DockPanel. OpenFinding_OverAPrivacyRevelation_OffersNoLinkAtAll holds
    /// that end.
    ///
    /// What this guarantees, exactly: these two view models, wired the way
    /// App.xaml.cs wires them, put a privacy finding on neither page. It is
    /// NOT a tripwire for the privacy page being built — it constructs its own
    /// two HealthViewModels over IsHealth and IsPerformance and asserts only
    /// about those, so a third page can be added beside them and every
    /// assertion here still passes untouched. What it does catch is the
    /// regression: either filter widening to take a privacy finding back onto
    /// a page that grades the machine. Whoever builds the privacy page still
    /// has to walk the comments that say "will" by hand — nothing here does
    /// it for them.
    ///
    /// What it does NOT claim: that a privacy finding is invisible. The
    /// "{n} findings" figure on the overview and the flyout counts every
    /// finding in the snapshot, privacy included — only the row, the title
    /// and the evidence have nowhere to appear.
    [Fact]
    public async Task OnThisBuild_APrivacyFinding_ReachesNeitherFindingsPage()
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

        await state.ScanAsync();

        foreach (var (name, vm) in new[] { ("Saglik", health), ("Performans", perf) })
        {
            var shown = vm.Rows.Concat(vm.AdviseRows).Concat(vm.NoticeRows)
                .Select(r => r.RuleId).ToArray();
            Assert.False(shown.Contains("advertising-id"),
                $"advertising-id reached the {name} page, which grades the " +
                "machine and must not carry a privacy finding");
        }
        // The control: the same wiring does show a non-privacy finding, so a
        // page that rendered nothing at all could not pass this by accident.
        Assert.Contains("power-plan",
            perf.Rows.Concat(perf.AdviseRows).Concat(perf.NoticeRows)
                .Select(r => r.RuleId));

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
}
