using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

public class HealthScoreTests
{
    // Measured findings are the ones that carry the stars x severity formula;
    // the arithmetic tests below are about that formula.
    private static DiagnosticFinding F(Severity sev, int stars) => new(
        "r", "rule.r.title", "T", "E", sev, RuleCategory.Auto, stars, true, null,
        ImpactClass: ImpactClass.Measured);

    private static DiagnosticFinding H(string id, Severity sev, int stars) => new(
        id, $"rule.{id}.title", "T", "E", sev, RuleCategory.Auto, stars, true, null);

    [Fact]
    public void NoFindings_Is100() =>
        Assert.Equal(100, HealthScore.Compute(Array.Empty<DiagnosticFinding>()));

    /// A finding that says nothing about its class is Hygiene. The default
    /// leans towards under-charging: a rule someone adds later and forgets to
    /// classify cannot inflate the score.
    [Fact]
    public void AFindingThatSaysNothing_IsHygiene() =>
        Assert.Equal(ImpactClass.Hygiene, H("r", Severity.Warning, 4).ImpactClass);

    /// A setting brisk flips without a number that says anyone felt it costs
    /// a flat 2 points, whatever its stars. Five Critical stars used to cost
    /// 25 — the power plan alone moved a quarter of the gauge.
    [Fact]
    public void HygieneFinding_ChargesTwoPoints_WhateverItsStars()
    {
        Assert.Equal(98, HealthScore.Compute(new[] { H("power-plan", Severity.Critical, 5) }));
        Assert.Equal(98, HealthScore.Compute(new[] { H("visual-effects", Severity.Warning, 2) }));
    }

    [Fact]
    public void HygieneInfo_ChargesOnePoint() =>
        Assert.Equal(99, HealthScore.Compute(new[] { H("stale-dev-caches", Severity.Info, 2) }));

    /// A real machine, replayed. Power plan (Critical 5), web results in the
    /// start menu (Warning 4) and visual effects (Warning 2) are hygiene;
    /// startup bloat (Warning 3) is measured, because the next boots will say
    /// whether it mattered. The old formula charged 25+12+6+9 and showed 48;
    /// applying the fixes took it to 90 with nothing measured behind the
    /// jump. Now the same machine reads 85 before and 100 after, a 15-point
    /// promise with most of it on the one item brisk will measure.
    [Fact]
    public void SettingsHeavyMachine_ReadsEightyFive_NotFortyEight()
    {
        var findings = new[]
        {
            H("power-plan", Severity.Critical, 5),
            H("search-web-results", Severity.Warning, 4),
            H("visual-effects", Severity.Warning, 2),
            new DiagnosticFinding("startup-bloat", "rule.startup-bloat.title", "T", "E",
                Severity.Warning, RuleCategory.Confirm, 3, true, null,
                ImpactClass: ImpactClass.Measured),
        };
        Assert.Equal(85, HealthScore.Compute(findings));
    }

    [Fact]
    public void Warning4Stars_Subtracts12() =>
        Assert.Equal(88, HealthScore.Compute(new List<DiagnosticFinding>
            { F(Severity.Warning, 4) }));

    [Fact]
    public void MixedSeverities_SumPenalties()
    {
        // Critical 5*5=25, Warning 3*3=9, Info 2*1=2 -> 100-36=64
        var findings = new List<DiagnosticFinding>
            { F(Severity.Critical, 5), F(Severity.Warning, 3), F(Severity.Info, 2) };
        Assert.Equal(64, HealthScore.Compute(findings));
    }

    [Fact]
    public void ManyFindings_FloorsAt5()
    {
        var findings = Enumerable.Repeat(F(Severity.Critical, 5), 30).ToList();
        Assert.Equal(5, HealthScore.Compute(findings));
    }

    /// A notice is a fact brisk can only report — 47 USB devices, memory
    /// below its rating on a board that will not change. Charging the score
    /// for it tells the user to fix hardware brisk itself says it cannot.
    [Fact]
    public void Notices_DoNotLowerTheScore()
    {
        var problem = new DiagnosticFinding("a", "rule.a.title", "A", "ev",
            Severity.Warning, RuleCategory.Advise, 4, false, null);
        var notice = new DiagnosticFinding("b", "rule.b.title", "B", "ev",
            Severity.Warning, RuleCategory.Advise, 4, false, null,
            Kind: FindingKind.Notice);

        Assert.Equal(
            HealthScore.Compute(new[] { problem }),
            HealthScore.Compute(new[] { problem, notice }));
    }

    /// The spec's stated reason for the enum: 100 stays reachable, so a
    /// user is never permanently penalised for what they cannot change.
    [Fact]
    public void AllNotices_ScoreIsAHundred()
    {
        var notices = new[]
        {
            new DiagnosticFinding("a", "rule.a.title", "A", "ev",
                Severity.Critical, RuleCategory.Advise, 5, false, null,
                Kind: FindingKind.Notice),
            new DiagnosticFinding("b", "rule.b.title", "B", "ev",
                Severity.Warning, RuleCategory.Advise, 4, false, null,
                Kind: FindingKind.Notice),
        };
        Assert.Equal(100, HealthScore.Compute(notices));
    }

    /// The skip is a skip, not a stop. Rule order puts boot-degradation (a
    /// notice) ahead of startup-bloat (a problem) on a real machine, so a
    /// walk that STOPPED at the first notice would drop every penalty behind
    /// it and hand back a score with nothing charged for problems that are
    /// still there.
    [Fact]
    public void ANoticeAheadOfAProblem_StillChargesTheProblem()
    {
        var notice = new DiagnosticFinding("a", "rule.a.title", "A", "ev",
            Severity.Critical, RuleCategory.Advise, 5, false, null,
            Kind: FindingKind.Notice);

        // notice first, then Warning 3*3=9 -> 91
        Assert.Equal(91, HealthScore.Compute(
            new[] { notice, F(Severity.Warning, 3) }));
    }

    /// Nothing opts in by accident: every rule that says nothing about Kind
    /// keeps costing the score exactly what it did before.
    [Fact]
    public void TheDefaultKind_IsProblem() =>
        Assert.Equal(FindingKind.Problem,
            new DiagnosticFinding("a", "rule.a.title", "A", "ev",
                Severity.Info, RuleCategory.Auto, 1, false, null).Kind);
}
