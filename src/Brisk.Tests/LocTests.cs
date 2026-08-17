using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Brisk.Localization;
using Xunit;

namespace Brisk.Tests;

public class LocTests
{
    /// EN and TR resx expose the SAME key set, checked from source — a key
    /// added to one file only would surface as raw English (or a raw key)
    /// in the other language's GUI. This is the structural guard; the
    /// per-key theory below additionally proves the values actually load.
    [Fact]
    public void ResxFiles_ExposeTheSameKeySet()
    {
        static string[] Keys(string file) =>
            XDocument.Load(file).Root!
                .Elements("data")
                .Select(e => (string)e.Attribute("name")!)
                .ToArray();

        var dir = LocalizationDir();
        var en = Keys(Path.Combine(dir, "Strings.resx"));
        var tr = Keys(Path.Combine(dir, "Strings.tr.resx"));
        Assert.Equal(en.OrderBy(k => k, StringComparer.Ordinal),
            tr.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(en.Length, en.Distinct(StringComparer.Ordinal).Count());
    }

    private static string LocalizationDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null;
             dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return Path.Combine(dir.FullName, "src", "Brisk", "Localization");
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }

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
    [InlineData("overview.report.live")]
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
    [InlineData("overview.gauge.label")]
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
    [InlineData("rule.power-plan.evidence")]
    [InlineData("rule.browser-gpu.evidence")]
    [InlineData("rule.hw-acceleration.evidence")]
    [InlineData("rule.startup-bloat.evidence")]
    [InlineData("rule.startup-bloat.evidence.heavy")]
    [InlineData("rule.visual-effects.evidence")]
    [InlineData("rule.storage-sense.evidence")]
    [InlineData("rule.ram-pressure.evidence")]
    [InlineData("rule.thermals.evidence")]
    [InlineData("rule.disk-forecast.evidence")]
    [InlineData("clean.skipped.apprunning")]
    [InlineData("clean.target.user-temp")]
    [InlineData("clean.target.windows-temp")]
    [InlineData("tray.tooltip")]
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
