using Brisk.Localization;
using Xunit;

namespace Brisk.Tests;

public class LocTests
{
    [Fact]
    public void English_ByDefault()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        Assert.Equal("Health", loc["nav.health"]);
    }

    [Fact]
    public void Turkish_AfterSwitch_AndBackToEnglish()
    {
        var loc = new Loc();
        loc.SetLanguage("tr");
        Assert.Equal("Sağlık", loc["nav.health"]);
        loc.SetLanguage("en");
        Assert.Equal("Health", loc["nav.health"]);
    }

    [Fact]
    public void MissingKey_ReturnsKeyItself()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        Assert.Equal("nope.missing", loc["nope.missing"]);
    }

    [Fact]
    public void Format_UsesLocalizedTemplate()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        Assert.Equal("3 findings · 2 one-click fixable", loc.F("flyout.findings", 3, 2));
    }

    [Fact]
    public void Title_FallsBackToEngineEnglish()
    {
        var loc = new Loc();
        loc.SetLanguage("tr");
        Assert.Equal("Güç planı hızı kısıtlıyor",
            loc.Title("rule.power-plan.title", "Power plan is limiting speed"));
        Assert.Equal("Engine English",
            loc.Title("rule.not-a-rule.title", "Engine English"));
    }

    /// EN+TR parity for the reassurance-round keys: the indexer returns the
    /// key itself when a culture is missing a value, so this fails loudly if
    /// either resx falls behind.
    [Theory]
    [InlineData("overview.status.advise")]
    [InlineData("overview.recent.hint")]
    [InlineData("overview.report.summary")]
    [InlineData("overview.report.part.freed")]
    [InlineData("overview.report.part.startup")]
    [InlineData("overview.report.part.fixes")]
    [InlineData("rule.power-plan.done")]
    [InlineData("rule.browser-gpu.done")]
    [InlineData("rule.hw-acceleration.done")]
    [InlineData("rule.startup-bloat.done")]
    [InlineData("rule.visual-effects.done")]
    [InlineData("rule.storage-sense.done")]
    [InlineData("overview.live.cpu")]
    [InlineData("overview.live.ram")]
    [InlineData("overview.live.temp")]
    [InlineData("overview.live.disk")]
    [InlineData("finding.details")]
    [InlineData("finding.action.storage")]
    [InlineData("rule.thermals.advice")]
    [InlineData("rule.ram-pressure.advice")]
    [InlineData("rule.disk-breakdown.advice")]
    [InlineData("rule.disk-forecast.advice")]
    [InlineData("rule.orphaned-data.advice")]
    [InlineData("rule.stale-dev-caches.advice")]
    [InlineData("performance.optimized.title")]
    [InlineData("startup.system.hint")]
    [InlineData("startup.app.teams")]
    [InlineData("startup.app.onedrive")]
    [InlineData("startup.app.spotify")]
    [InlineData("startup.app.discord")]
    [InlineData("startup.app.steam")]
    [InlineData("startup.app.epicgameslauncher")]
    [InlineData("startup.app.skype")]
    [InlineData("startup.app.cortana")]
    [InlineData("startup.app.dockerdesktop")]
    [InlineData("startup.app.whatsapp")]
    [InlineData("startup.app.bluestacks")]
    [InlineData("startup.app.wallpaperengine")]
    public void ReassuranceKeys_ExistInBothLanguages(string key)
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        Assert.NotEqual(key, loc[key]);
        loc.SetLanguage("tr");
        Assert.NotEqual(key, loc[key]);
    }

    [Fact]
    public void SetLanguage_RaisesIndexerChange()
    {
        var loc = new Loc();
        string? raised = null;
        loc.PropertyChanged += (_, e) => raised = e.PropertyName;
        loc.SetLanguage("tr");
        Assert.Equal("Item[]", raised);
    }
}
