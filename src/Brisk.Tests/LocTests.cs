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
    [InlineData("rule.memory-speed.advice")]
    [InlineData("rule.memory-speed.evidence")]
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

    /// The one claim this rule exists to not make: DegradationTime is how late
    /// a program was, never what it added to the boot. The engine's English is
    /// pinned by BootDegradationRuleTests — this pins the templates the GUI
    /// actually renders, in BOTH languages, because a Turkish template rewritten
    /// into the false framing would render every argument, leave no stray brace,
    /// and pass every other test in the suite.
    ///
    /// The forbidden strings are shapes of the claim rather than one sentence
    /// somebody might write: an English share ("of it", "belongs to", "accounts
    /// for", "share of") and a Turkish one ("kadarı" — that much of it, "ait" —
    /// belongs to, "payı" — its share).
    [Theory]
    [InlineData("en", "not time it added to your boot")]
    [InlineData("tr", "ne kadar eklediğini değil")]
    public void BootDegradationEvidence_KeepsTheDisclaimer_AndRefusesTheSum(
        string language, string disclaimer)
    {
        var loc = new Loc();
        loc.SetLanguage(language);

        var blamed = loc.F("rule.boot-degradation.evidence",
            "57 s", "8", "Microsoft Edge WebView2 37 s, brisk-app.exe 26 s");
        Assert.Contains(disclaimer, blamed);

        var nobody = loc.F("rule.boot-degradation.evidence.nobody", "57 s", "8");
        foreach (var sum in new[] { "of it", "belongs to", "accounts for", "share of",
                                    "kadarı", "ait", "payı" })
        {
            Assert.DoesNotContain(sum, blamed, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(sum, nobody, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// The advice line is the row body — HealthViewModel folds the hedged
    /// evidence behind "Details" and shows this instead — so an unhedged promise
    /// here is the one the user actually reads. A blamed program is often a
    /// service or a scheduled task (MsMpEng, TiWorker, mscorsvw on the verified
    /// machine), and brisk deliberately does not map an executable name to a
    /// startup entry, so the advice may never assert that a blamed program IS
    /// switchable. It may only say where to look.
    [Theory]
    [InlineData("en", "turn up under Startup programs on the Performance page")]
    [InlineData("tr", "Açılış programları listesinde yer alanları")]
    public void BootDegradationAdvice_PointsAtTheRealPlace_WithoutPromising(
        string language, string hedgedPointer)
    {
        var loc = new Loc();
        loc.SetLanguage(language);

        var advice = loc["rule.boot-degradation.advice"];
        Assert.Contains(hedgedPointer, advice);

        // brisk has four pages and "Startup" is not one of them: the list is a
        // section on the Performance page. Naming a page the app does not have
        // leaves a Turkish reader nothing on screen to match against, and this
        // is the one clause they are meant to act on.
        Assert.Contains(loc["nav.performance"], advice);
        Assert.DoesNotContain("Startup page", advice);
        Assert.DoesNotContain("Başlangıç sayfas", advice);
    }

    /// Same pointer, same hedge, in the evidence — which is what the CLI prints
    /// and what sits behind the GUI's Details fold.
    [Theory]
    [InlineData("en", "look for the rest under Startup programs on the Performance page")]
    [InlineData("tr", "Performans sayfasındaki Açılış programları listesinden bak")]
    public void BootDegradationEvidence_PointsAtTheRealPlace(
        string language, string pointer)
    {
        var loc = new Loc();
        loc.SetLanguage(language);

        var blamed = loc.F("rule.boot-degradation.evidence",
            "57 s", "8", "Microsoft Edge WebView2 37 s");
        Assert.Contains(pointer, blamed);
        Assert.DoesNotContain("Startup page", blamed);
        Assert.DoesNotContain("Başlangıç sayfas", blamed);
    }

    /// Offenders under 500 ms are dropped, so "no program stood out" has to hold
    /// for a boot Windows did name somebody on. The approximation hedge is what
    /// keeps that sentence true, and the Turkish had lost it.
    [Theory]
    [InlineData("en", "about as fast as it expected")]
    [InlineData("tr", "aşağı yukarı beklendiği kadar hızlı")]
    public void BootDegradationNobodyCopy_KeepsTheApproximationHedge(
        string language, string hedge)
    {
        var loc = new Loc();
        loc.SetLanguage(language);
        Assert.Contains(hedge, loc.F("rule.boot-degradation.evidence.nobody", "57 s", "8"));
    }

    /// "the last 8 boots" claimed a contiguity the probe does not give:
    /// RealEventLogProbe skips an ID 100 record it cannot read and keeps
    /// walking, so the sample is the most recent boots brisk could READ.
    [Theory]
    [InlineData("en", "most recent boots brisk could read")]
    [InlineData("tr", "okuyabildiği son")]
    public void BootDegradationEvidence_ClaimsOnlyWhatItCouldRead(
        string language, string phrasing)
    {
        var loc = new Loc();
        loc.SetLanguage(language);

        Assert.Contains(phrasing,
            loc.F("rule.boot-degradation.evidence", "57 s", "8", "Spotify 37 s"));
        Assert.Contains(phrasing,
            loc.F("rule.boot-degradation.evidence.nobody", "57 s", "8"));
    }

    /// The memory rule is defined by what it refuses to say. WMI exposes
    /// neither the memory controller's maximum nor whether an XMP/EXPO profile
    /// exists, so the gap it measures does not identify its own cause: the copy
    /// names both explanations and picks neither. A Turkish rewrite into
    /// "BIOS'tan XMP'yi aç" would render its argument, leave no stray brace and
    /// pass every other test in this suite — while sending the one reader who
    /// uses the app in Turkish into a BIOS over a reading that may be his
    /// platform's ceiling.
    ///
    /// The unit is pinned here too. DDR transfers twice per clock, so labelling
    /// this figure MHz would state double the real clock — the correction the
    /// source thread upvoted above every other reply.
    [Theory]
    [InlineData("en", "does not support", "cannot tell", "out of")]
    [InlineData("tr", "desteklemeyen", "ayırt edemiyor", "üzerinden")]
    public void MemorySpeedCopy_NamesBothCauses_AndPrescribesNeither(
        string language, string unsupported, string hedge, string relation)
    {
        var loc = new Loc();
        loc.SetLanguage(language);

        var evidence = loc.F("rule.memory-speed.evidence",
            "ChannelA-DIMM0 2133 MT/s / 3200 MT/s");
        var advice = loc["rule.memory-speed.advice"];

        foreach (var line in new[] { evidence, advice })
        {
            Assert.Contains("XMP", line);            // one explanation
            Assert.Contains(unsupported, line);      // the other
            Assert.Contains(hedge, line);            // and neither is claimed
        }

        // The reading is a "configured out of rated" pair, and which of the two
        // numbers is the shortfall is carried by the template, not by the
        // argument — a template that flattened the relation into "and" would
        // still render both figures and still look right.
        Assert.Contains(relation, evidence);

        // This pins that {0} is rendered whole rather than dropped, reordered
        // or truncated. It does NOT pin the unit: the MT/s in it was supplied
        // by this test. The unit is pinned by the DoesNotContain below — a
        // template that spelled the figure out as MHz beside the argument —
        // and by MemorySpeedRuleTests and HardwareProbeTests, which pin what
        // the engine actually emits into {0}.
        Assert.Contains("ChannelA-DIMM0 2133 MT/s / 3200 MT/s", evidence);
        Assert.DoesNotContain("MHz", evidence);
        Assert.DoesNotContain("MHz", advice);

        // No imperative into a setting brisk cannot see, verify or undo.
        foreach (var order in new[] { "enable XMP", "turn on XMP", "enable the profile",
                                      "XMP'yi aç", "profili aç", "etkinleştir", "BIOS'a gir" })
        {
            Assert.DoesNotContain(order, evidence, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(order, advice, StringComparison.OrdinalIgnoreCase);
        }
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
