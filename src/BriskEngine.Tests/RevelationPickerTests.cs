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

    /// The controller ranked usb-history third; the maintainer's machine
    /// then showed what that buys — a count of 1 above real findings on
    /// surfaces built to be read and shared — and he overturned the ranking
    /// on the first live data (2026-08-26). The count now lives on the
    /// Gizlilik page, which reads Headline itself and never asks Pick.
    [Fact]
    public void UsbHistory_IsNeverPicked_HoweverStrongItsNumber()
    {
        var picked = RevelationPicker.Pick(new[]
        {
            F("usb-history", Severity.Warning, stars: 5, value: "47"),
            F("disk-breakdown", Severity.Info, stars: 1, value: "58.1 GB"),
        });

        Assert.DoesNotContain(picked, f => f.RuleId == "usb-history");
        Assert.Contains(picked, f => f.RuleId == "disk-breakdown");
    }

    /// The report-only disclosures stay OFF the declared list, which is the
    /// tail rank — this theory drives two of the three that can lead at all,
    /// recall-status being the third. That is as much a decision as
    /// usb-history's old third place was: a count of program records and a total of uploaded bytes are not
    /// numbers this project wants a scan to open with, and leaving them
    /// unlisted is what puts them last. (usb-history has since gone
    /// further — NeverLeads — on the maintainer's call; these two stay
    /// merely unlisted, able to lead a machine where nothing else speaks.)
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

    /// A named 23.5 GB file outranks the total of the folder holding it.
    /// That is the whole argument for the large-files rule, made once more
    /// where the order is decided: "Desktop: 58.8 GB" and "23.5 GB — the
    /// largest single file in your profile" describe the same disk, and only
    /// one of them can be acted on.
    [Fact]
    public void ANamedFile_LeadsAheadOfTheFolderTotal()
    {
        var picked = RevelationPicker.Pick(new[]
        {
            F("disk-breakdown", value: "58.8 GB"),
            F("large-files", value: "23.5 GB"),
        });

        Assert.Equal(new[] { "large-files", "disk-breakdown" },
            picked.Select(f => f.RuleId).ToArray());
    }

    /// The declared order IS the product decision — pinned so a change to it
    /// is a deliberate edit here, never a drive-by.
    [Fact]
    public void Priority_IsExactlyTheOptingRules() =>
        Assert.Equal(new[]
        {
            "boot-degradation", "display-refresh",
            "startup-bloat", "large-files", "disk-breakdown", "memory-speed",
        }, RevelationPicker.Priority);

    /// The ban list is as much a product decision as the order — pinned the
    /// same way, for the same reason.
    [Fact]
    public void NeverLeads_IsExactlyTheOverturnedRule() =>
        Assert.Equal(new[] { "usb-history" }, RevelationPicker.NeverLeads);
}
