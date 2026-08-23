using System;
using System.Collections.Generic;
using System.Linq;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

public class ReportCardModelTests
{
    private static Loc Loc(string lang)
    {
        var loc = new Loc();
        loc.SetLanguage(lang);
        return loc;
    }

    private static Headline H(string value) => new(value, "cap",
        "rule.fake.headline.value", new[] { value },
        "rule.fake.headline.caption", Array.Empty<string>());

    [Fact]
    public void Findings_AreHeadlinePlusTitle_InPickerOrder_NeverEvidence()
    {
        var snapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("aa-fake", cat: RuleCategory.Advise, canFix: false,
                headline: H("13")),
            TestData.Finding("zz-fake", sev: Severity.Critical,
                cat: RuleCategory.Advise, canFix: false, headline: H("57 s")),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
        }, new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Equal(2, card.Findings.Count);                    // headline-less thermals excluded
        Assert.Equal("57 s", card.Findings[0].Lead);             // Critical outranks Warning
        Assert.Equal("Title zz-fake", card.Findings[0].Text);    // the TITLE, never the evidence
        Assert.Equal("13", card.Findings[1].Lead);
        Assert.Equal("", card.FindingsEmptyText);
        Assert.DoesNotContain(card.Findings, l => l.Text.Contains("Evidence"));
    }

    [Fact]
    public void NoHeadlines_KeepsTheSectionWithTheHonestEmptyLine()
    {
        var snapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
        }, new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Empty(card.Findings);
        Assert.Equal(
            $"All {DiagnosticRuleRegistry.All.Count} rules looked — nothing on this machine leads with a number.",
            card.FindingsEmptyText);
    }

    [Theory]
    [InlineData(true, true, null, "en", "Everything brisk tried to read, answered.")]
    [InlineData(true, true, null, "tr", "brisk'in okumaya çalıştığı her şey cevap verdi.")]
    [InlineData(true, false, null, "en", "GPU temperature — not read; brisk cannot tell from here why.")]
    // A GPU-only silence carries no reason, on purpose: a blocklisted kernel
    // driver is not why a GPU sensor is quiet, so the card does not say it is.
    [InlineData(true, false, true, "en", "GPU temperature — not read; brisk cannot tell from here why.")]
    public void UnreadSection_NeverDrops_AndSpeaksTheVariant(
        bool cpu, bool gpu, bool? integrity, string lang, string expected)
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(cpu, gpu, integrity));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc(lang));

        Assert.Equal(new[] { expected }, card.Unread);
    }

    [Theory]
    [InlineData(true, "en", "CPU temperature — not read. Memory integrity is on; the driver that reads it is on Microsoft's vulnerable-driver blocklist.")]
    [InlineData(true, "tr", "CPU sıcaklığı — okunamadı. Bellek bütünlüğü açık; onu okuyan sürücü Microsoft'un güvenlik açığı listesinde.")]
    [InlineData(false, "en", "CPU temperature — not read. Memory integrity is off here, so the usual reason is ruled out; brisk cannot tell what did it.")]
    public void CpuUnread_CarriesTheMeasuredIntegrityVariant(
        bool? integrity, string lang, string expected)
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(false, true, integrity));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc(lang));

        Assert.Equal(new[] { expected }, card.Unread);
    }

    [Fact]
    public void CpuUnread_UnknownIntegrity_KeepsTheHedge()
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(false, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Equal(
            new[] { "CPU temperature — not read. The driver that reads it will not load "
                + "while memory integrity is on; brisk could not confirm that is the reason here." },
            card.Unread);
    }

    /// The mirror of the test above, and the defect it was written against:
    /// the neither-answered line used to drop the measured reason entirely, so
    /// on an HVCI machine with no readable GPU sensor `brisk scan` explained
    /// the blocklisted driver and the card explained nothing — two surfaces of
    /// one product disagreeing about the same silent sensor. The CPU went
    /// unread in both cases, so the CPU's reason belongs on both lines.
    [Theory]
    [InlineData(true, "en", "Temperatures — neither sensor answered. Memory integrity is on; the driver that reads CPU temperature is on Microsoft's vulnerable-driver blocklist.")]
    [InlineData(true, "tr", "Sıcaklıklar — iki sensör de cevap vermedi. Bellek bütünlüğü açık; CPU sıcaklığını okuyan sürücü Microsoft'un güvenlik açığı listesinde.")]
    [InlineData(false, "en", "Temperatures — neither sensor answered. Memory integrity is off here, so the usual reason is ruled out; brisk cannot tell what did it.")]
    public void NeitherAnswered_CarriesTheMeasuredIntegrityVariantToo(
        bool? integrity, string lang, string expected)
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(false, false, integrity));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc(lang));

        Assert.Equal(new[] { expected }, card.Unread);
    }

    [Fact]
    public void NeitherAnswered_UnknownIntegrity_KeepsTheHedge()
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(false, false, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Equal(
            new[] { "Temperatures — neither sensor answered. The driver that reads CPU "
                + "temperature will not load while memory integrity is on; brisk could not "
                + "confirm that is the reason here." },
            card.Unread);
    }

    [Fact]
    public void Fixes_AreTitleAndDate_AndTheSectionDropsWhenEmpty()
    {
        var fixes = new[]
        {
            new UndoableFix("power-plan", new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc)),
        };
        var snapshot = TestData.Snapshot(null, new SensorStatus(true, true, null));

        var with = ReportCardModel.Build(snapshot, fixes, Loc("en"));
        var without = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.True(with.HasFixes);
        Assert.Single(with.Fixes);
        // The localized rule title, never the raw id — the exact resx text is
        // pinned elsewhere; here the contract is "not the id, plus the date".
        Assert.DoesNotContain("power-plan", with.Fixes[0]);
        Assert.False(string.IsNullOrWhiteSpace(with.Fixes[0]));
        Assert.Contains("2026-08-20", with.Fixes[0]);
        Assert.False(without.HasFixes);
    }

    /// The privacy ban, enforced on output rather than on good intentions:
    /// plant the user's name, the machine name, and a profile path into every
    /// engine-authored string a finding carries, and prove none of them can
    /// reach the card.
    [Fact]
    public void PrivacyBan_EvidenceNamesAndPathsNeverReachTheCard()
    {
        // The markers live ONLY in the fields that carry user data in real
        // findings (evidence, fix description) — the title is rule-authored
        // static text and legitimately appears on the card.
        var poisoned = new DiagnosticFinding(
            "zz-fake", "rule.zz-fake.title", "Too many programs run at start",
            @"C:\Users\SECRETUSER\Desktop leaks from DESKTOP-SECRETPC via SecretApp.exe",
            Severity.Warning, RuleCategory.Advise, 3, CanFix: false,
            FixDescription: @"delete C:\Users\SECRETUSER\file",
            Headline: H("47"));
        var snapshot = TestData.Snapshot(new[] { poisoned },
            new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        var everything = string.Join("\n",
            card.Findings.Select(l => l.Lead + " " + l.Text)
                .Concat(card.Unread).Concat(card.Fixes)
                .Append(card.FindingsEmptyText).Append(card.DateText)
                .Append(card.VersionText).Append(card.RepoLine));
        Assert.Contains("47", everything);                       // the number survives
        Assert.DoesNotContain("SECRETUSER", everything);         // the user never does
        Assert.DoesNotContain("DESKTOP-SECRETPC", everything);
        Assert.DoesNotContain("SecretApp", everything);
        Assert.DoesNotContain(@"C:\Users", everything);
    }

    [Fact]
    public void TopStrip_CarriesLocalDateAndEngineVersion()
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Equal(EngineInfo.Version, card.VersionText);
        Assert.Contains("2026-08-15", card.DateText);            // TestData's CompletedUtc date
        Assert.Equal("github.com/merturl4576/brisk", card.RepoLine);
        Assert.Equal(72, card.Health);
    }

    /// The card paints its ring from this key, so drift between it and the
    /// app's banding would let a screenshot claim health the machine does
    /// not have. The boundaries are the assertion: 90 and 70 are where
    /// HealthBrush turns, and the card must turn on the same numbers.
    [Theory]
    [InlineData(100, "Good")]
    [InlineData(90, "Good")]
    [InlineData(89, "SeverityWarning")]
    [InlineData(72, "SeverityWarning")]
    [InlineData(70, "SeverityWarning")]
    [InlineData(69, "SeverityCritical")]
    [InlineData(35, "SeverityCritical")]
    [InlineData(0, "SeverityCritical")]
    public void HealthBrushKey_BandsTheScoreTheWayTheRestOfTheAppDoes(
        int health, string expected)
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(true, true, null))
            with { Health = health };

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Equal(expected, card.HealthBrushKey);
        Assert.Equal(HealthBrush.KeyFor(health), card.HealthBrushKey);
    }
}
