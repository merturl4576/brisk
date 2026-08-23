using System;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

public class RevelationPickerTests
{
    private static DiagnosticFinding F(string id, Severity sev = Severity.Warning,
        int stars = 3, bool withHeadline = true) => new(
        id, $"rule.{id}.title", $"Title {id}", $"Evidence {id}",
        sev, RuleCategory.Advise, stars, CanFix: false, FixDescription: null,
        Headline: withHeadline
            ? new Headline("1", "cap",
                $"rule.{id}.headline.value", new[] { "1" },
                $"rule.{id}.headline.caption", new[] { "1" })
            : null);

    [Fact]
    public void DeclaredOrder_DecidesAmongListedRules()
    {
        var picked = RevelationPicker.Pick(new[]
        {
            F("memory-speed"), F("disk-breakdown"), F("boot-degradation"),
        });
        Assert.Equal(new[] { "boot-degradation", "disk-breakdown", "memory-speed" },
            picked.Select(f => f.RuleId).ToArray());
    }

    [Fact]
    public void FindingsWithoutHeadlines_AreNotPicked()
    {
        var picked = RevelationPicker.Pick(new[]
        {
            F("boot-degradation", withHeadline: false), F("startup-bloat"),
        });
        Assert.Equal(new[] { "startup-bloat" }, picked.Select(f => f.RuleId).ToArray());
    }

    [Fact]
    public void UnlistedRules_SortAfterListed_BySeverityImpactThenId()
    {
        var picked = RevelationPicker.Pick(new[]
        {
            F("zz-custom", Severity.Critical, stars: 5),
            F("memory-speed"),
            F("bb-custom", Severity.Warning, stars: 5),
            F("aa-custom", Severity.Warning, stars: 5),
        });
        Assert.Equal(new[] { "memory-speed", "zz-custom", "aa-custom", "bb-custom" },
            picked.Select(f => f.RuleId).ToArray());
    }

    [Fact]
    public void EmptyInput_EmptyOutput() =>
        Assert.Empty(RevelationPicker.Pick(Array.Empty<DiagnosticFinding>()));

    /// The declared order IS the product decision — pinned so a change to it
    /// is a deliberate edit here, never a drive-by.
    [Fact]
    public void Priority_IsExactlyTheOptingRules() =>
        Assert.Equal(new[]
        {
            "boot-degradation", "display-refresh", "startup-bloat",
            "disk-breakdown", "memory-speed",
        }, RevelationPicker.Priority);
}
