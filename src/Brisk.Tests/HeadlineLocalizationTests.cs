using Brisk.Localization;
using Brisk.ViewModels;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

/// The headline twin of EvidenceLocalizationTests: keys resolve in both
/// languages, a missing key falls back to the engine's English, and the
/// finding row exposes exactly what the card binds.
public class HeadlineLocalizationTests
{
    private static Loc Loc(string lang)
    {
        var loc = new Loc();
        loc.SetLanguage(lang);
        return loc;
    }

    private static Headline Boot() => new("57 s", "boot time — the middle of the last 8 boots",
        "rule.boot-degradation.headline.value", new[] { "57" },
        "rule.boot-degradation.headline.caption", new[] { "8" });

    [Fact]
    public void BootHeadline_SpeaksBothLanguages()
    {
        var (trValue, trCaption) = LocalizedText.Headline(Boot(), Loc("tr"));
        Assert.Equal("57 sn", trValue);
        Assert.Equal("açılış süresi — son 8 açılışın ortası", trCaption);

        var (enValue, enCaption) = LocalizedText.Headline(Boot(), Loc("en"));
        Assert.Equal("57 s", enValue);
        Assert.Equal("boot time — the middle of the last 8 boots", enCaption);
    }

    [Theory]
    [InlineData("rule.display-refresh.headline.value", "60", "60 Hz", "60 Hz")]
    [InlineData("rule.display-refresh.headline.caption", "144",
        "the display supports 144 Hz", "ekran 144 Hz destekliyor")]
    [InlineData("rule.startup-bloat.headline.value", "13", "13", "13")]
    [InlineData("rule.startup-bloat.headline.caption", "x",
        "programs start with Windows", "program Windows ile başlıyor")]
    [InlineData("rule.disk-breakdown.headline.value", "57.7 GB", "57.7 GB", "57.7 GB")]
    [InlineData("rule.disk-breakdown.headline.caption", "Desktop",
        "Desktop — the largest measured folder", "Desktop — ölçülen en büyük klasör")]
    [InlineData("rule.memory-speed.headline.value", "2933", "2933 MT/s", "2933 MT/s")]
    [InlineData("rule.memory-speed.headline.caption", "3200",
        "rated for 3200 MT/s", "anma hızı 3200 MT/s")]
    public void EveryHeadlineKey_ExistsInBothLanguages(
        string key, string arg, string en, string tr)
    {
        Assert.Equal(en, Loc("en").F(key, arg));
        Assert.Equal(tr, Loc("tr").F(key, arg));
    }

    [Fact]
    public void UnknownKeys_FallBackToTheEnginesEnglish()
    {
        var h = new Headline("42 things", "of some kind",
            "rule.custom-x.headline.value", new[] { "42" },
            "rule.custom-x.headline.caption", new[] { "x" });
        var (value, caption) = LocalizedText.Headline(h, Loc("tr"));
        Assert.Equal("42 things", value);
        Assert.Equal("of some kind", caption);
    }

    [Fact]
    public void FindingRow_ExposesTheHeadline_OrSaysItHasNone()
    {
        var loc = Loc("en");
        var with = new FindingRow(TestData.Finding("boot-degradation",
            cat: RuleCategory.Advise, canFix: false, headline: Boot()),
            loc, canUndo: false, _ => { }, _ => { });
        Assert.True(with.HasHeadline);
        Assert.Equal("57 s", with.HeadlineValue);

        var without = new FindingRow(TestData.Finding("thermals",
            cat: RuleCategory.Advise, canFix: false),
            loc, canUndo: false, _ => { }, _ => { });
        Assert.False(without.HasHeadline);
        Assert.Equal("", without.HeadlineValue);
    }
}
