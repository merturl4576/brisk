using System.Linq;
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
    /// own suite, where a DiagnosticContext can be built — as of this wave it
    /// covers the four telemetry switches, which are the only privacy rules
    /// that exist. The six the wave has yet to ship are still uncovered.
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
    /// what turns that silence into a failure. It reaches the four telemetry
    /// switches; the six privacy rules still to be written need the same line
    /// extended to whatever base class or marker they arrive with.
    [Fact]
    public void EveryTelemetrySwitchRule_ShipsAnIdThePrivacyPageRoutes()
    {
        var switches = DiagnosticRuleRegistry.All
            .OfType<TelemetrySwitchRule>().ToList();

        Assert.Equal(4, switches.Count);
        foreach (var rule in switches)
            Assert.True(FindingSections.IsPrivacy(rule.Id),
                $"rule '{rule.Id}' is a telemetry switch, but no id in " +
                "PrivacyRuleIds.All matches it — its findings would route to Sağlık");
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
