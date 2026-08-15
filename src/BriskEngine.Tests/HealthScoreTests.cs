using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

public class HealthScoreTests
{
    private static DiagnosticFinding F(Severity sev, int stars) => new(
        "r", "rule.r.title", "T", "E", sev, RuleCategory.Auto, stars, true, null);

    [Fact]
    public void NoFindings_Is100() =>
        Assert.Equal(100, HealthScore.Compute(Array.Empty<DiagnosticFinding>()));

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
}
