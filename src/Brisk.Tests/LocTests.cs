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

    /// The reassurance-round keys LOAD — which is less than "EN+TR parity",
    /// and the difference is the whole reason the test above exists.
    ///
    /// Loc's indexer answers through ResourceManager (Loc.cs:17), and
    /// ResourceManager falls back to the NEUTRAL resource. So this theory
    /// fails on one gap only: a key missing from Strings.resx, where the
    /// indexer's `?? key` hands back the key itself and the en pass below
    /// fails on it. A key present in English and missing from Turkish renders
    /// the English sentence in a Turkish GUI — silently, never the raw key —
    /// and both passes below stay green over it. ResxFiles_ExposeTheSameKeySet
    /// is what sees that one, and 7e64eb4 put it on this branch's record:
    /// the missing Turkish caption keys were caught by the key-set test and
    /// waved through by this one.
    ///
    /// Measured both ways by deleting `nav.overview` from one file at a time:
    /// gone from Strings.resx, this theory failed its `en` pass with
    /// Actual: "nav.overview"; gone from Strings.tr.resx, it stayed green and
    /// only the key-set test failed.
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
    [InlineData("rule.thermals.evidence.cpu-unread")]
    [InlineData("rule.thermals.evidence.gpu-unread")]
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
    // The report card's own keys. The card is a PNG people post, and Loc's
    // indexer answers a miss with the key itself — so a typo in ReportCard.xaml
    // does not fail, it prints "report.section.findings" onto the shareable
    // picture in place of a heading. The three section headings are read
    // straight out of the XAML and had nothing holding them at all; the unread
    // sentences and the button's four lines are pinned by value elsewhere and
    // are listed here so the whole wave is guarded in one place.
    [InlineData("report.section.findings")]
    [InlineData("report.section.unread")]
    [InlineData("report.section.fixes")]
    [InlineData("report.fixes.more")]
    [InlineData("report.unread.none")]
    [InlineData("report.unread.gpu")]
    [InlineData("report.unread.cpu")]
    [InlineData("report.unread.cpu.integrity-on")]
    [InlineData("report.unread.cpu.integrity-off")]
    [InlineData("report.unread.neither")]
    [InlineData("report.unread.neither.integrity-on")]
    [InlineData("report.unread.neither.integrity-off")]
    [InlineData("overview.report.card")]
    [InlineData("overview.report.card.saved")]
    [InlineData("overview.report.card.saved.fileonly")]
    [InlineData("overview.report.card.failed")]
    // The heading over the findings brisk can only report. It labels a whole
    // band on two pages, so a miss in the ENGLISH resx would print the raw key
    // above the cards instead of the sentence that explains why they carry no
    // button. A Turkish-only gap prints the English sentence there instead and
    // this row stays green over it — see the theory's comment above.
    [InlineData("health.notice.section")]
    // The window's own caption controls. Windows used to name these buttons
    // in the user's language; brisk draws them itself now, and both the
    // AutomationProperties.Name and the tooltip read through this indexer — so
    // a miss in the ENGLISH resx puts "chrome.close" into the name a screen
    // reader speaks and into the tooltip a mouse user sees. (The private-use-
    // area glyph a reader used to announce was the state BEFORE 7e64eb4 bound
    // a name at all; with a name bound, a miss is a raw key, not a glyph.) A
    // Turkish-only gap gives both the English name, silently, and this row
    // cannot see it.
    // Restore is listed beside Maximize because the middle button carries both
    // names, one per window state.
    [InlineData("chrome.minimize")]
    [InlineData("chrome.maximize")]
    [InlineData("chrome.restore")]
    [InlineData("chrome.close")]
    // The first nav tile's name. It used to borrow [app.name] and show
    // "brisk", so the entry could not miss — there was nothing to miss. Now
    // that it says where it goes, a gap in the ENGLISH resx would put
    // "nav.overview" at the top of the nav, above four tiles that read fine,
    // and this row is what fails. A Turkish gap alone puts "Overview" there
    // instead — English above four Turkish tiles, and green here; the key-set
    // test is the one that fails on it, measured with this very key.
    [InlineData("nav.overview")]
    // The sixth tile, added with the Gizlilik page. A gap in the ENGLISH resx
    // puts "nav.privacy" between Depolama and Ayarlar, above and below tiles
    // that read fine.
    [InlineData("nav.privacy")]
    // The read-back's four lines. ReadBackTests reads these keys off disk from
    // both files, which proves they are THERE; only this row proves the
    // English one LOADS through ResourceManager, and the two are different
    // gaps — see this theory's comment above. The Gizlilik page renders these
    // four, one per ReadBackState, so a miss in the English resx now puts
    // "readback.held" on screen where the sentence goes.
    [InlineData("readback.held")]
    [InlineData("readback.reverted")]
    [InlineData("readback.ignored")]
    [InlineData("readback.unverified")]
    // The Gizlilik page's own strings. Every one of them is a heading, a
    // button caption or a sentence with no other guard: Loc answers a miss
    // with the key itself, so a gap prints "privacy.switches.section" over
    // the switches instead of the sentence naming them.
    [InlineData("privacy.title")]
    [InlineData("privacy.hero.note")]
    [InlineData("privacy.disclosure.section")]
    [InlineData("privacy.switches.section")]
    [InlineData("privacy.switches.note")]
    [InlineData("privacy.switches.safe.note")]
    [InlineData("privacy.switches.costly.section")]
    [InlineData("privacy.switches.none")]
    [InlineData("privacy.turnoff.safe")]
    [InlineData("privacy.turnoff.refused")]
    [InlineData("privacy.readback.section")]
    // The USB fold's two keys, added with the amended red line 2. The title
    // is a heading like the eleven above it. The row is not: it is a FORMAT
    // string with three holes in it, and Loc answers a miss with the key
    // itself — so a gap in the English resx does not print
    // "privacy.usb.device" once, it prints it once per device, with the
    // model name and both dates dropped on the floor. There is no louder
    // failure; the row key also has a second guard after all —
    // PrivacyViewModelTests renders its real English value through a real
    // Loc, so a break there lands twice.
    [InlineData("privacy.usb.devices.title")]
    [InlineData("privacy.usb.device")]
    // The `privacy.*` key this list went without while the page's headings
    // were added: what the page says when Windows refuses to open its own
    // Settings app. It is not a heading — it is the whole of the message the
    // row's only action leaves behind — so a gap in the ENGLISH resx prints
    // "privacy.setting.failed" where the reason goes, on the one path a
    // reader reaches by clicking something that then did nothing.
    // WhenWindowsSettingsDoNotOpen_ThePageSaysSo already refuses an echoed
    // key there, but only in the language it builds (en) and only on that one
    // command's path; this row holds the same value from the load list, where
    // the rest of the page's copy is held. Neither reads the TURKISH value —
    // see this theory's comment above: a Turkish-only gap renders the English
    // sentence and stays green here, and ResxFiles_ExposeTheSameKeySet is
    // what fails on it. (Both directions were watched: deleting the key from
    // Strings.resx failed this row with Actual: "privacy.setting.failed";
    // deleting it from Strings.tr.resx left it green.)
    [InlineData("privacy.setting.failed")]
    // The named loss beside each of the two switches that cost the user
    // something. FindingRow reads these through the indexer and treats a miss
    // as "this rule declares no cost" — so a gap here does not print a key,
    // it silently removes the warning from the card. There is no louder
    // failure available: the mechanism that lets every other rule have no
    // cost line is the same one that would swallow these.
    [InlineData("rule.location.cost")]
    [InlineData("rule.activity-history.cost")]
    // The six switches' past-tense labels. DoneLabel falls back to
    // "Fixed: {title}" when a rule has no .done key, and a finding title is
    // a sentence about the switch being ON — so a gap here puts "Fixed: The
    // advertising ID is not switched off" on a read-back line whose own
    // sentence says it has been off for three days. Read by the read-back
    // block, the journal report and the report card alike.
    [InlineData("rule.advertising-id.done")]
    [InlineData("rule.diagnostic-level.done")]
    [InlineData("rule.tailored-experiences.done")]
    [InlineData("rule.speech-typing.done")]
    [InlineData("rule.location.done")]
    [InlineData("rule.activity-history.done")]
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
        // and by MemorySpeedRuleTests, which pins what the engine actually
        // emits into {0} — an assertion this comment claimed before it
        // existed, back when the rule test asserted only that EvidenceArgs
        // was non-null.
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

    /// The card's fixes list overflows into its own line, and it must not
    /// borrow the overview's. "overview.revelation.more" is "and {0} more" in
    /// English and "ve {0} bulgu daha" — and {0} more FINDINGS — in Turkish,
    /// so borrowing it printed the wrong noun under "Uygulanan düzeltmeler" on
    /// a shareable PNG. The two keys read identically in English, which is the
    /// only reason it survived review, so this pins them apart by value.
    [Fact]
    public void FixesOverflowLine_CountsFixes_AndTheRevelationLineStillCountsFindings()
    {
        var loc = new Loc();

        loc.SetLanguage("en");
        Assert.Equal("and 3 more", loc.F("report.fixes.more", 3));

        loc.SetLanguage("tr");
        Assert.Equal("ve 3 düzeltme daha", loc.F("report.fixes.more", 3));
        Assert.DoesNotContain("bulgu", loc["report.fixes.more"]);
        // The borrowed key keeps its own noun — it is correct where it belongs.
        Assert.Contains("bulgu", loc["overview.revelation.more"]);
    }

    /// The caption buttons, pinned by value in both languages.
    ///
    /// Maximize is not fullscreen — a maximized window keeps its taskbar and
    /// its title bar — so "Tam ekran yap" would name something this button
    /// does not do, which is the same class of defect as a findings card
    /// claiming a measurement it never took. Windows' own Turkish calls this
    /// control "Ekranı kapla", and a caption button is the one control where
    /// a user arrives with muscle memory from every other app on the machine:
    /// matching the platform IS the requirement, not a preference. Pinned by
    /// value because a plausible-sounding rewrite is exactly how a wrong word
    /// gets in, and nothing else in the suite would notice.
    [Theory]
    [InlineData("en", "Minimize", "Maximize", "Restore", "Close")]
    [InlineData("tr", "Küçült", "Ekranı kapla", "Önceki boyuta getir", "Kapat")]
    public void CaptionButtonNames_SayWhatTheButtonsDo(string language,
        string minimize, string maximize, string restore, string close)
    {
        var loc = new Loc();
        loc.SetLanguage(language);

        Assert.Equal(minimize, loc["chrome.minimize"]);
        Assert.Equal(maximize, loc["chrome.maximize"]);
        Assert.Equal(restore, loc["chrome.restore"]);
        Assert.Equal(close, loc["chrome.close"]);

        // The claim the middle button may never make.
        foreach (var fullscreen in new[] { "Tam ekran", "Fullscreen", "Full screen" })
            Assert.DoesNotContain(fullscreen, loc["chrome.maximize"],
                StringComparison.OrdinalIgnoreCase);
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

    /// TASK 5. brisk knows exactly one reason a CPU temperature comes back
    /// unread — WinRing0 on Microsoft's vulnerable-driver blocklist, which a
    /// machine running memory integrity will not load — and it does NOT know
    /// that this is the reason on the machine in front of it. The engine's
    /// English is pinned by AdviseRulesTests; this pins the templates the GUI
    /// renders, in both languages, because a Turkish rewrite into a flat "your
    /// machine has memory integrity on" would assert something brisk never
    /// read, render its argument, leave no stray brace, and pass every other
    /// test in this suite.
    [Theory]
    [InlineData("en", "Usually", "cannot confirm from here",
        "will not switch that protection off",
        new[] { "turn off", "turn it off", "switch it off", "disable",
                "Windows Security", "you should" })]
    [InlineData("tr", "genelde", "buradan doğrulayamıyor", "korumayı kapatmaz",
        new[] { "kapatıp", "kapatın", "kapatarak", "kapatmayı", "kapatman",
                "devre dışı", "Windows Güvenlik" })]
    public void ThermalsCpuUnreadEvidence_NamesTheUsualCause_WithoutClaimingIt(
        string language, string usually, string hedge, string refusal, string[] forbidden)
    {
        var loc = new Loc();
        loc.SetLanguage(language);

        var evidence = loc.F("rule.thermals.evidence.cpu-unread", "GPU 78°C");
        Assert.Contains("GPU 78°C", evidence);
        Assert.Contains(usually, evidence);     // the usual cause, not this machine's
        Assert.Contains(hedge, evidence);
        Assert.Contains(refusal, evidence);
        Assert.DoesNotContain("{", evidence);

        // The refusal alone is not a guard. Appending "if you want the reading,
        // turn memory integrity off in Windows Security and try again" leaves
        // korumayı kapatmaz and buradan doğrulayamıyor both present: the test
        // passes while the paragraph contradicts itself and orders the reader
        // into a setting brisk cannot see, verify or undo. So the imperative
        // stems are forbidden beside the refusal being required — kapatmaz
        // stays legal, kapatıp / kapatın / kapatarak do not.
        foreach (var order in forbidden)
            Assert.DoesNotContain(order, evidence, StringComparison.OrdinalIgnoreCase);
    }

    /// The mirror template has no cause to name and must not borrow the one
    /// above: a blocked kernel driver is not why a GPU sensor goes quiet, and
    /// translating the two into one paragraph is the cheapest way to end up
    /// saying it does.
    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    public void ThermalsGpuUnreadEvidence_StopsAtTheFact(string language)
    {
        var loc = new Loc();
        loc.SetLanguage(language);

        var evidence = loc.F("rule.thermals.evidence.gpu-unread", "CPU 88°C");
        Assert.Contains("CPU 88°C", evidence);
        Assert.DoesNotContain("{", evidence);
        // Stems, not sentences. Turkish Windows calls Core Isolation "çekirdek
        // yalıtımı", and "sürücü listesi" is not "sürücüler listesinde", so a
        // rewrite borrowing the CPU cause steps around any list spelled as the
        // phrases actually used above. The bare stems hold the line.
        foreach (var cause in new[] { "blocklist", "WinRing0", "memory integrity",
                                      "bellek bütünlüğü", "çekirdek yalıtımı",
                                      "Core Isolation", "driver", "sürücü" })
            Assert.DoesNotContain(cause, evidence, StringComparison.OrdinalIgnoreCase);
    }

    /// The engine's English explains the bracketed count. The resx sentences
    /// are what the GUI actually renders, and the count arrives inside an arg
    /// — so a template that never mentions it leaves "(1/8)" sitting beside a
    /// program name with nothing saying what it counts.
    [Theory]
    [InlineData("en", "bracketed figure is how many of those boots")]
    [InlineData("tr", "Parantez içindeki sayı")]
    public void BootDegradation_Evidence_ExplainsTheBracketedCount(string lang, string phrase)
    {
        var loc = new Loc();
        loc.SetLanguage(lang);

        var evidence = loc.F("rule.boot-degradation.evidence",
            "57 s", "8", "Spotify 37 s (1/8)");

        Assert.Contains(phrase, evidence);
    }
}
