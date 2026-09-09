using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

/// The first outside tester's laptop (2026-09-09) wore a red McAfee shield
/// in its tray, and brisk's launch died with "cancelled by the user" for a
/// reason nobody could name. A third-party antivirus is the commonest thing
/// on a consumer laptop that both slows every file open and blocks an
/// unsigned program without a word — and brisk had no sentence for it.
/// Report-only, free: a product someone chose is a fact, not a fault.
public class ThirdPartyAntivirusRuleTests
{
    private static DiagnosticContext With(params AntivirusProduct[] products)
    {
        var defender = new FakeDefender();
        defender.Products.AddRange(products);
        return TestContext.Empty() with { Defender = defender };
    }

    private static AntivirusProduct Defender() =>
        new("Windows Defender", IsDefender: true, Enabled: true, DefinitionsOutdated: false);

    private static AntivirusProduct McAfee(bool enabled = true, bool outdated = false) =>
        new("McAfee LiveSafe", IsDefender: false, Enabled: enabled, DefinitionsOutdated: outdated);

    [Fact]
    public void NothingRegistered_IsNotAFinding()
    {
        Assert.Null(new ThirdPartyAntivirusRule().Detect(With()));
    }

    [Fact]
    public void OnlyDefender_IsNotAFinding()
    {
        Assert.Null(new ThirdPartyAntivirusRule().Detect(With(Defender())));
    }

    [Fact]
    public void ThirdPartyOn_IsAFreeInfoNotice_ThatNamesIt()
    {
        var finding = new ThirdPartyAntivirusRule().Detect(With(McAfee(), Defender()));

        Assert.NotNull(finding);
        Assert.Equal("third-party-av", finding!.RuleId);
        Assert.Equal(RuleCategory.Advise, finding.Category);
        Assert.False(finding.CanFix);
        Assert.Equal(FindingKind.Notice, finding.Kind);
        Assert.Equal(ImpactClass.Hygiene, finding.ImpactClass);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("rule.third-party-av.evidence", finding.EvidenceKey);
        Assert.Equal(new[] { "McAfee LiveSafe" }, finding.EvidenceArgs);
        Assert.Contains("McAfee LiveSafe", finding.Evidence);
        Assert.Contains("Defender", finding.Evidence);
        Assert.Contains("brisk changes nothing", finding.Evidence);
        // A product someone chose is a fact, not a fault: the gauge stays at 100.
        Assert.Equal(100, HealthScore.Compute(new[] { finding }));
    }

    /// The expired-trial shape: listed as the machine's antivirus, reporting
    /// itself off. That is a warning even as a free notice, because nothing
    /// may be scanning at all, and the sentence names the likeliest cause.
    [Fact]
    public void ThirdPartyOff_IsAWarningNotice_ThatNamesTheExpiredTrial()
    {
        var finding = new ThirdPartyAntivirusRule().Detect(With(McAfee(enabled: false)));

        Assert.NotNull(finding);
        Assert.Equal(Severity.Warning, finding!.Severity);
        Assert.Equal(FindingKind.Notice, finding.Kind);
        Assert.Equal("rule.third-party-av.evidence.off", finding.EvidenceKey);
        Assert.Contains("expired trial", finding.Evidence);
        Assert.Equal(100, HealthScore.Compute(new[] { finding }));
    }

    [Fact]
    public void ThirdPartyOutdated_IsAWarningNotice_ThatSaysOutOfDate()
    {
        var finding = new ThirdPartyAntivirusRule().Detect(With(McAfee(outdated: true)));

        Assert.NotNull(finding);
        Assert.Equal(Severity.Warning, finding!.Severity);
        Assert.Equal("rule.third-party-av.evidence.outdated", finding.EvidenceKey);
        Assert.Contains("out of date", finding.Evidence);
    }

    /// Two products, one of them off: the worse state decides the sentence,
    /// and both are named — the reader sees every scanner Windows lists.
    [Fact]
    public void TwoThirdParties_TakeTheWorseState_AndNameBoth()
    {
        var finding = new ThirdPartyAntivirusRule().Detect(With(
            new("Avast Free Antivirus", IsDefender: false, Enabled: true, DefinitionsOutdated: false),
            McAfee(enabled: false)));

        Assert.NotNull(finding);
        Assert.Equal("rule.third-party-av.evidence.off", finding!.EvidenceKey);
        Assert.Equal(new[] { "Avast Free Antivirus, McAfee LiveSafe" }, finding.EvidenceArgs);
    }

    [Fact]
    public void Registered_ExactlyOnce()
    {
        Assert.Equal(1, DiagnosticRuleRegistry.All.Count(r => r.Id == "third-party-av"));
    }
}

/// Security Center's productState is three bytes: product type, then the
/// on/off byte (0x10 set = on), then the signature byte (0x10 set = out of
/// date). The values below are the ones scripts across the industry decode
/// the same way; the first is what this machine's Defender reported live
/// (2026-09-09, root\SecurityCenter2, productState 397568).
public class AntivirusStateTests
{
    [Theory]
    [InlineData(397568, true, false)]    // 0x061100: Defender on, up to date
    [InlineData(0x041000, true, false)]  // third-party on, up to date
    [InlineData(0x040000, false, false)] // off — the expired-trial shape
    [InlineData(0x041010, true, true)]   // on, definitions out of date
    [InlineData(0x040010, false, true)]  // off and out of date
    public void Decode_ReadsTheOnAndSignatureBits(int state, bool enabled, bool outdated)
    {
        var (on, old) = AntivirusState.Decode(state);

        Assert.Equal(enabled, on);
        Assert.Equal(outdated, old);
    }

    /// Defender is known by its registration path first — "windowsdefender://"
    /// on this machine, and not a thing a third party would claim — and by
    /// its name second, for a Windows that lists it under a localized name
    /// with the path missing.
    [Theory]
    [InlineData("Windows Defender", "windowsdefender://", true)]
    [InlineData("Microsoft Defender Antivirus", "", true)]
    [InlineData("McAfee LiveSafe", @"C:\Program Files\McAfee\WPS\mcapexe.exe", false)]
    [InlineData("", "", false)]
    public void IsDefender_KnowsItByPathThenName(string name, string path, bool expected)
    {
        Assert.Equal(expected, AntivirusState.IsDefender(name, path));
    }
}
