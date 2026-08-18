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
    [InlineData("clean.simple.title")]
    [InlineData("clean.simple.hint")]
    [InlineData("clean.advanced")]
    [InlineData("clean.group.system")]
    [InlineData("clean.group.browser")]
    [InlineData("clean.group.app")]
    [InlineData("clean.group.other")]
    [InlineData("clean.simple.locked.app")]
    [InlineData("clean.simple.locked.inuse")]
    [InlineData("clean.report.skipped.appheld")]
    [InlineData("clean.report.summary.freed")]
    [InlineData("clean.report.binleft")]
    [InlineData("clean.preparing")]
    [InlineData("clean.purging")]
    [InlineData("overview.cleanspace")]
    [InlineData("overview.cleanspace.none")]
    [InlineData("overview.actions.hint")]
    [InlineData("health.fixall")]
    [InlineData("flyout.fixall")]
    public void ReassuranceKeys_ExistInBothLanguages(string key)
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        Assert.NotEqual(key, loc[key]);
        loc.SetLanguage("tr");
        Assert.NotEqual(key, loc[key]);
    }

    /// ROUND 12: the simple clean purges its own recycled items immediately,
    /// so the footer's old "everything goes to the Recycle Bin first" would
    /// be a lie — pin the truthful wording in both languages.
    [Fact]
    public void SimpleHint_NoLongerPromisesTheRecycleBin()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        Assert.DoesNotContain("Recycle Bin", loc["clean.simple.hint"]);
        loc.SetLanguage("tr");
        Assert.DoesNotContain("Geri Dönüşüm", loc["clean.simple.hint"]);
    }

    /// ROUND 13: "the next clean or Windows will take care of it" was false
    /// — only deterministically-named files are ever recycled again, so a
    /// randomly-named leftover never gets a second pass. Only the truthful
    /// half survives.
    [Fact]
    public void BinLeft_PromisesNoSecondPass()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        Assert.Equal("{0} stayed in the Recycle Bin", loc["clean.report.binleft"]);
        loc.SetLanguage("tr");
        Assert.Equal("{0} Geri Dönüşüm Kutusu'nda kaldı", loc["clean.report.binleft"]);
    }

    /// ROUND 13: the overview footer was the last place still promising
    /// the Recycle Bin, but the button right beside it now purges what it
    /// recycles — same one-step flow as the Depolama page. The reassurance
    /// keeps only what stayed true: these files rebuild themselves.
    [Fact]
    public void ActionsHint_NoLongerPromisesTheRecycleBin()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        Assert.DoesNotContain("Recycle Bin", loc["overview.actions.hint"]);
        loc.SetLanguage("tr");
        Assert.DoesNotContain("Geri Dönüşüm", loc["overview.actions.hint"]);
    }

    /// The boot rule ships three readings, and a template that quietly dropped
    /// {2} would print a sentence naming nobody — in the one language the
    /// maintainer actually reads the app in. Both templates are rendered here,
    /// including the one for a slow boot Windows blamed nobody for.
    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    public void BootDegradationEvidence_RendersEveryReading(string language)
    {
        var loc = new Loc();
        loc.SetLanguage(language);
        const string blamedNames = "Microsoft Edge WebView2 37 s, brisk-app.exe 26 s";

        var blamed = loc.F("rule.boot-degradation.evidence", "57 s", "8", blamedNames);
        Assert.Contains("57 s", blamed);
        Assert.Contains("8", blamed);
        Assert.Contains(blamedNames, blamed);
        Assert.DoesNotContain("{", blamed);

        var nobody = loc.F("rule.boot-degradation.evidence.nobody", "57 s", "8");
        Assert.Contains("57 s", nobody);
        Assert.DoesNotContain("{", nobody);

        Assert.NotEqual("rule.boot-degradation.title", loc["rule.boot-degradation.title"]);
        Assert.NotEqual("rule.boot-degradation.advice", loc["rule.boot-degradation.advice"]);
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
