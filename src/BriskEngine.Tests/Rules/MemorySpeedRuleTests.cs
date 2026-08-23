using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class MemorySpeedRuleTests
{
    private static DiagnosticContext With(params MemoryModule[] modules)
    {
        var hw = new FakeHardware();
        hw.Modules.AddRange(modules);
        return TestContext.Empty() with { Hardware = hw };
    }

    [Fact]
    public void ProfileNeverEnabled_IsAFinding()
    {
        var ctx = With(new MemoryModule("DIMM0", 3200, 2133, 16L << 30));
        var finding = new MemorySpeedRule().Detect(ctx);

        Assert.NotNull(finding);
        Assert.Equal(RuleCategory.Advise, finding!.Category);
        Assert.False(finding.CanFix);
        Assert.Contains("MT/s", finding.Evidence);
        Assert.DoesNotContain("MHz", finding.Evidence);
    }

    // The maintainer's own machine: 3200-rated modules at 2933. That is this
    // platform's ceiling, not a disabled profile, and WMI cannot tell them
    // apart — so brisk must not send anyone into a BIOS over it.
    [Fact]
    public void PlatformCeiling_IsNotAFinding()
    {
        Assert.Null(new MemorySpeedRule().Detect(
            With(new MemoryModule("DIMM0", 3200, 2933, 16L << 30))));
    }

    [Fact]
    public void RunningAtRatedSpeed_IsNotAFinding()
    {
        Assert.Null(new MemorySpeedRule().Detect(
            With(new MemoryModule("DIMM0", 3200, 3200, 16L << 30))));
    }

    // Soldered laptop memory legitimately reports equal or zero values.
    [Fact]
    public void UnavailableData_IsNotAFinding()
    {
        Assert.Null(new MemorySpeedRule().Detect(
            With(new MemoryModule("DIMM0", 0, 0, 8L << 30))));
        Assert.Null(new MemorySpeedRule().Detect(TestContext.Empty()));
    }

    [Fact]
    public void Evidence_NamesBothExplanations_AndClaimsNeither()
    {
        var ctx = With(new MemoryModule("DIMM0", 3200, 2133, 16L << 30));
        var evidence = new MemorySpeedRule().Detect(ctx)!.Evidence;

        Assert.Contains("3200", evidence);
        Assert.Contains("2133", evidence);
        Assert.Contains("XMP", evidence);        // one explanation
        Assert.Contains("support", evidence);    // the other: the board may not support it
    }

    /// The exact pair of modules WMI reports on the maintainer's machine —
    /// two different kits, both rated 3200, both configured at the platform's
    /// 2933. The whole rule exists in the shape it does because of this
    /// reading, so it is pinned as data rather than left to the single-module
    /// case above.
    [Fact]
    public void MaintainersRealModules_StaySilent()
    {
        Assert.Null(new MemorySpeedRule().Detect(With(
            new MemoryModule("ChannelA-DIMM0", 3200, 2933, 17179869184L),
            new MemoryModule("ChannelB-DIMM0", 3200, 2933, 17179869184L))));
    }

    /// "At or below 80%" is inclusive on purpose: 2560 out of 3200 fires,
    /// one step above it does not. A > that drifted to >= would move the
    /// line by a whole JEDEC step.
    [Fact]
    public void EightyPercent_IsTheLine()
    {
        Assert.NotNull(new MemorySpeedRule().Detect(
            With(new MemoryModule("DIMM0", 3200, 2560, 16L << 30))));
        Assert.Null(new MemorySpeedRule().Detect(
            With(new MemoryModule("DIMM0", 3200, 2561, 16L << 30))));
    }

    /// A module reporting only half its numbers is unknown, not slow: a rated
    /// speed with no configured reading beside it would otherwise read as a
    /// module running at zero — the maximal possible overstatement.
    [Fact]
    public void HalfAReading_IsNotAFinding()
    {
        Assert.Null(new MemorySpeedRule().Detect(
            With(new MemoryModule("DIMM0", 3200, 0, 16L << 30))));
        Assert.Null(new MemorySpeedRule().Detect(
            With(new MemoryModule("DIMM0", 0, 2133, 16L << 30))));
    }

    /// One slow module beside one that is fine names the slow one and only
    /// the slow one — a healthy slot must never be listed as a problem.
    [Fact]
    public void OnlySlowModulesAreNamed()
    {
        var finding = new MemorySpeedRule().Detect(With(
            new MemoryModule("ChannelA-DIMM0", 3200, 2133, 16L << 30),
            new MemoryModule("ChannelB-DIMM0", 2400, 2400, 16L << 30)));

        Assert.NotNull(finding);
        Assert.Contains("ChannelA-DIMM0", finding!.Evidence);
        Assert.DoesNotContain("ChannelB-DIMM0", finding.Evidence);
        // And in the localized arg, not only in the English fallback: the
        // Turkish sentence is {0} plus prose, so EvidenceArgs[0] is everything
        // the maintainer reads about which slot and which numbers.
        Assert.Equal("ChannelA-DIMM0 2133 MT/s / 3200 MT/s",
            Assert.Single(finding.EvidenceArgs!));
    }

    /// Severity, weight and the localization contract, so a later edit cannot
    /// quietly turn an advisory reading into a critical one.
    [Fact]
    public void Finding_CarriesItsIdSeverityAndLocalizationKeys()
    {
        var finding = new MemorySpeedRule().Detect(
            With(new MemoryModule("DIMM0", 3200, 2133, 16L << 30)))!;

        Assert.Equal("memory-speed", finding.RuleId);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(4, finding.ImpactStars);
        Assert.Equal("rule.memory-speed.title", finding.TitleKey);
        Assert.Equal("rule.memory-speed.evidence", finding.EvidenceKey);
        // NotNull was the whole assertion here, and swapping the arg for
        // "MUTANT" passed the suite while the Turkish GUI rendered
        // "MUTANT — her modülün ayarlı hızı…". The English fallback every
        // other test reads is a different local.
        Assert.Equal("DIMM0 2133 MT/s / 3200 MT/s",
            Assert.Single(finding.EvidenceArgs!));
    }
}
