using System;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

public class RevelationPickerTests
{
    /// The headline's VALUE is a parameter because two of this wave's rules
    /// lead with something that is not a number, and a fixture that could
    /// only produce "1" could not say so.
    private static DiagnosticFinding F(string id, Severity sev = Severity.Warning,
        int stars = 3, bool withHeadline = true, string value = "1") => new(
        id, $"rule.{id}.title", $"Title {id}", $"Evidence {id}",
        sev, RuleCategory.Advise, stars, CanFix: false, FixDescription: null,
        Headline: withHeadline
            ? new Headline(value, "cap",
                $"rule.{id}.headline.value", new[] { value },
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

    /// WHERE THE USB COUNT SITS, one pair at a time rather than as a single
    /// expected list, so a failure names the two rules that swapped.
    ///
    /// Third, and the reason is the whole of the decision: a slower boot and a
    /// display running below its refresh rate are measurements the user can
    /// act on today, and brisk leads with those. The count of USB devices
    /// Windows has recorded is the strongest number brisk owns that the user
    /// can do nothing about, so it leads the moment nothing actionable
    /// outranks it.
    ///
    /// In every row the expected leader is the SECOND argument, so a picker
    /// that had stopped sorting and returned its input could not pass this.
    [Theory]
    [InlineData("usb-history", "boot-degradation", "boot-degradation")]
    [InlineData("usb-history", "display-refresh", "display-refresh")]
    [InlineData("startup-bloat", "usb-history", "usb-history")]
    [InlineData("disk-breakdown", "usb-history", "usb-history")]
    [InlineData("memory-speed", "usb-history", "usb-history")]
    public void TheUsbCount_SitsThirdInTheDeclaredOrder(
        string first, string second, string leads)
    {
        var picked = RevelationPicker.Pick(new[] { F(first), F(second) });

        Assert.True(picked[0].RuleId == leads,
            $"{first} against {second} led with {picked[0].RuleId}");
    }

    /// The other two report-only disclosures stay OFF the declared list, which
    /// is the tail rank. That is as much a decision as usb-history's third
    /// place: a count of program records and a total of uploaded bytes are not
    /// numbers this project wants a scan to open with, and leaving them
    /// unlisted is what puts them last.
    ///
    /// What this holds is the CONSEQUENCE — behind memory-speed, the last rule
    /// on the list. Appending either id to the end of Priority leaves it green,
    /// which was watched: the list's exact membership is
    /// Priority_IsExactlyTheOptingRules' end to hold, not this one's.
    [Theory]
    [InlineData("run-history")]
    [InlineData("delivery-optimization")]
    public void TheOtherTwoDisclosures_StayBehindEveryNamedRule(string id)
    {
        var picked = RevelationPicker.Pick(new[] { F(id), F("memory-speed") });

        Assert.True(picked[^1].RuleId == id,
            $"{id} outranked memory-speed, the last rule on the declared list");
    }

    /// THE ORDER NEVER READS A HEADLINE VALUE, and this wave is what makes the
    /// guard worth its lines: recall-status leads with the WORD "Off" and
    /// delivery-optimization with "302 MB". A rank that parsed the value would
    /// have to decide what those two are worth against 47 devices, and there
    /// is no answer to that — so it never asks. Same rules, same severities,
    /// same stars, values that share no shape at all, one order.
    [Fact]
    public void TheOrder_ReadsTheDeclaredList_NotWhatTheHeadlineSays()
    {
        var ids = new[]
        {
            "usb-history", "run-history", "recall-status",
            "delivery-optimization", "startup-bloat",
        };
        var plain = ids.Select(id => F(id)).ToArray();
        var asShipped = new[]
        {
            F("usb-history", value: "47"), F("run-history", value: "1284"),
            F("recall-status", value: "Off"),
            F("delivery-optimization", value: "302 MB"),
            F("startup-bloat", value: "9"),
        };

        Assert.Equal(
            RevelationPicker.Pick(plain).Select(f => f.RuleId),
            RevelationPicker.Pick(asShipped).Select(f => f.RuleId));
    }

    /// The declared order IS the product decision — pinned so a change to it
    /// is a deliberate edit here, never a drive-by.
    [Fact]
    public void Priority_IsExactlyTheOptingRules() =>
        Assert.Equal(new[]
        {
            "boot-degradation", "display-refresh", "usb-history",
            "startup-bloat", "disk-breakdown", "memory-speed",
        }, RevelationPicker.Priority);
}
