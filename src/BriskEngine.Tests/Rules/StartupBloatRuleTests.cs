using System;
using System.Collections.Generic;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Models;
using System.Linq;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class StartupBloatRuleTests
{
    private const string RunKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private static (BriskEngine.Diagnostics.DiagnosticContext, FakeRegistry) Ctx(params string[] runItems)
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        foreach (var item in runItems) reg.SetString(RunKey, item, $@"C:\apps\{item}.exe");
        return (ctx, reg);
    }

    [Fact]
    public void HeavyStartupItem_IsAFinding()
    {
        var (ctx, _) = Ctx("Steam");
        var finding = new StartupBloatRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Contains("Steam", finding!.Evidence);
        // Localizable twin of the prose: stable key + the data a GUI needs
        // to rebuild the sentence in the user's language.
        Assert.Equal("rule.startup-bloat.evidence.heavy", finding.EvidenceKey);
        Assert.Equal(new[] { "1", "Steam" }, finding.EvidenceArgs);
        // The next boots are measured against this fix (boot trend), so the
        // score may charge it in full.
        Assert.Equal(ImpactClass.Measured, finding.ImpactClass);
    }

    [Fact]
    public void DisabledHeavyItem_NoFinding()
    {
        var (ctx, reg) = Ctx("Steam");
        reg.SetBytes(ApprovedKey, "Steam", new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        Assert.Null(new StartupBloatRule().Detect(ctx));
    }

    [Fact]
    public void FewLightItems_NoFinding()
    {
        var (ctx, _) = Ctx("MyTool", "OtherTool");
        Assert.Null(new StartupBloatRule().Detect(ctx));
    }

    [Fact]
    public void ManyItems_IsAFinding_EvenWithoutHeavyOnes()
    {
        var (ctx, _) = Ctx("A", "B", "C", "D", "E", "F");
        var finding = new StartupBloatRule().Detect(ctx);
        Assert.NotNull(finding);
        // No heavy tail → the shorter evidence template, count only.
        Assert.Equal("rule.startup-bloat.evidence", finding!.EvidenceKey);
        Assert.Equal(new[] { "6" }, finding.EvidenceArgs);
        // This branch also fires just AFTER a successful fix has taken the
        // heavy programs out, so it must not read like the fix did nothing:
        // with nothing left that brisk would touch, the remaining programs
        // are the reader's judgement to make.
        //
        // Pinned whole rather than sampled. This exact sentence is what the
        // CLI prints, and Strings.resx restates it for the GUI under the key
        // asserted above — EvidenceLocalizationTests pins that render against
        // the same literal. Two full pins on one sentence are what keeps the
        // terminal reader and the GUI reader from being told different
        // things; a sampled assert would let a reworded tail through.
        //
        // Note where it points: at a named surface, not "below". The CLI
        // prints this into a terminal with nothing below it, so deixis that
        // reads fine on the Performance page would be a lie in the shell.
        Assert.Equal("6 programs start with Windows. None of them is on brisk's " +
            "heavy list, so which ones you actually need is your call — review " +
            "them under Startup programs on the Performance page.", finding.Evidence);
    }

    [Fact]
    public void Fix_DisablesOnlyHeavyItems_AndUndoRestores()
    {
        var (ctx, reg) = Ctx("Steam", "MyTool");
        var rule = new StartupBloatRule();
        var prior = rule.Fix(ctx);
        Assert.Equal(0x03, reg.GetBytes(ApprovedKey, "Steam")![0]);
        Assert.Null(reg.GetBytes(ApprovedKey, "MyTool")); // untouched
        rule.Undo(ctx, prior);
        Assert.Null(reg.GetBytes(ApprovedKey, "Steam")); // was absent before
    }

    [Fact]
    public void Fix_DeniedWrite_IsNotRecordedInPrior()
    {
        // One HKCU heavy item (writable) + one HKLM heavy item (denied)
        var (ctx, reg) = Ctx("Steam");
        const string HklmRunKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string HklmApprovedKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        reg.SetString(HklmRunKey, "Discord", @"C:\apps\Discord.exe");
        reg.DenyWriteKeys.Add(HklmApprovedKey);

        var rule = new StartupBloatRule();
        var prior = rule.Fix(ctx);

        // HKCU Steam should be disabled
        Assert.Equal(0x03, reg.GetBytes(ApprovedKey, "Steam")![0]);
        // HKLM Discord should NOT be disabled (write was denied)
        Assert.Null(reg.GetBytes(HklmApprovedKey, "Discord"));
        // Prior should only contain HKCU entry, not HKLM
        var priorMap = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(prior)!;
        Assert.True(priorMap.ContainsKey($"{ApprovedKey}|Steam"));
        Assert.False(priorMap.ContainsKey($"{HklmApprovedKey}|Discord"));
    }

    /// The Startup page listed Store apps while this rule did not, so Spotify
    /// — the entry Windows' own boot log blames for 37 seconds — showed as
    /// enabled and heavy on one page and was invisible to the finding and to
    /// Fix All on the other.
    [Fact]
    public void HeavyStoreApp_IsAFinding_AndFixMovesEveryTask_AndUndoRestores()
    {
        var (ctx, reg) = Ctx();
        const string Pfn = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0";
        StoreRegistry.Task(reg, Pfn, "Spotify", 2);
        StoreRegistry.Task(reg, Pfn, "SpotifyLauncher", 2);
        var root = BriskEngine.Diagnostics.StartupManager.StoreRoot;
        var spotify = $@"{root}\{Pfn}\Spotify";
        var launcher = $@"{root}\{Pfn}\SpotifyLauncher";

        var rule = new StartupBloatRule();
        var finding = rule.Detect(ctx);

        Assert.NotNull(finding);
        Assert.Contains("SpotifyMusic", finding!.Evidence);
        Assert.True(finding.CanFix);

        var prior = rule.Fix(ctx);
        Assert.Equal(0, reg.GetInt(spotify, "State"));
        Assert.Equal(0, reg.GetInt(launcher, "State"));

        rule.Undo(ctx, prior);
        Assert.Equal(2, reg.GetInt(spotify, "State"));
        Assert.Equal(2, reg.GetInt(launcher, "State"));
    }

    /// Undo restores the value that was there, not a generic "enabled" — a
    /// task that was EnabledByPolicy is not the same as one set to Enabled.
    [Fact]
    public void Undo_RestoresTheExactPriorState()
    {
        var (ctx, reg) = Ctx();
        const string Pfn = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0";
        StoreRegistry.Task(reg, Pfn, "Spotify", 4);   // EnabledByPolicy
        var key = $@"{BriskEngine.Diagnostics.StartupManager.StoreRoot}\{Pfn}\Spotify";

        var rule = new StartupBloatRule();
        rule.Undo(ctx, rule.Fix(ctx));

        Assert.Equal(4, reg.GetInt(key, "State"));
    }

    /// The undo journal has no expiry — an undoable fix sits in "What brisk
    /// did" until someone clicks it — so uninstalling between Fix and Undo is
    /// not a corner case, it is a Tuesday. SetInt goes through CreateSubKey on
    /// the real registry, so restoring a task whose package is gone RECREATES
    /// the key, and StoreTasks reads that table back: brisk would grow a
    /// startup row for an app that cannot start, out of a key brisk itself
    /// wrote, and offer to disable it.
    ///
    /// The assertion is on the value's absence rather than the key's, because
    /// no registry double in this repo can express "this key does not exist" —
    /// which is exactly why 645 tests could not see this.
    [Fact]
    public void Undo_PackageUninstalledSinceFix_WritesNothingBack()
    {
        var (ctx, reg) = Ctx();
        const string Pfn = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0";
        StoreRegistry.Task(reg, Pfn, "Spotify", 2);
        var taskKey = $@"{BriskEngine.Diagnostics.StartupManager.StoreRoot}\{Pfn}\Spotify";

        var rule = new StartupBloatRule();
        var prior = rule.Fix(ctx);
        Assert.Equal(0, reg.GetInt(taskKey, "State"));

        // Spotify is uninstalled: Windows takes the whole entry with it.
        reg.Values.Remove($"{taskKey}::State");
        reg.SubKeys[BriskEngine.Diagnostics.StartupManager.StoreRoot].Remove(Pfn);
        reg.SubKeys.Remove($@"{BriskEngine.Diagnostics.StartupManager.StoreRoot}\{Pfn}");

        rule.Undo(ctx, prior);

        Assert.Null(reg.GetInt(taskKey, "State"));
        Assert.Empty(new BriskEngine.Diagnostics.StartupManager(ctx.Registry, null).List());
    }

    [Fact]
    public void DisabledHeavyStoreApp_IsNotCounted()
    {
        var (ctx, reg) = Ctx();
        StoreRegistry.Task(reg, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "Spotify", 0);

        Assert.Null(new StartupBloatRule().Detect(ctx));
    }

    [Fact]
    public void Fix_AllWritesDenied_Throws()
    {
        var (ctx, reg) = Ctx("Steam");
        reg.DenyWriteKeys.Add(ApprovedKey);

        var rule = new StartupBloatRule();
        var ex = Assert.Throws<InvalidOperationException>(() => rule.Fix(ctx));
        Assert.Contains("administrator", ex.Message);
    }

    [Fact]
    public void Headline_IsTheTotalCount()
    {
        var (ctx, _) = Ctx("A", "B", "C", "D", "E", "F");

        var h = new StartupBloatRule().Detect(ctx)!.Headline;

        Assert.NotNull(h);
        Assert.Equal("6", h!.Value);
        Assert.Equal("programs start with Windows", h.Caption);
        Assert.Equal("rule.startup-bloat.headline.value", h.ValueKey);
        Assert.Equal(new[] { "6" }, h.ValueArgs);
        Assert.Equal("rule.startup-bloat.headline.caption", h.CaptionKey);
        Assert.Empty(h.CaptionArgs);
    }

    private static BootRecord Blaming(int day, params (string Exe, int LateMs)[] offenders) =>
        new(new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc).AddDays(day), 70_000, 25_000,
            offenders.Select(o => new BootOffender(o.Exe, "", $@"C:\apps\{o.Exe}", o.LateMs)).ToList());

    /// The friend's laptop (2026-09-09): fourteen startup programs, none on
    /// brisk's list, and Windows' own boot log naming the late ones. A Run
    /// entry Windows blamed 5 s+ on two of the sampled boots is heavy: the
    /// finding names it, says who blamed it and on how many boots, and the
    /// fix disables it like any known-heavy entry.
    [Fact]
    public void AProgramWindowsBlamedOnTwoBoots_IsHeavy()
    {
        var (ctx, reg) = Ctx("Wondershare");
        var log = (FakeEventLog)ctx.EventLog;
        log.Boots.Add(Blaming(0, ("Wondershare.exe", 12_000)));
        log.Boots.Add(Blaming(-1, ("wondershare.EXE", 8_000)));
        log.Boots.Add(Blaming(-2));

        var finding = new StartupBloatRule().Detect(ctx);

        Assert.NotNull(finding);
        Assert.True(finding!.CanFix);
        Assert.Equal("rule.startup-bloat.evidence.blamed", finding.EvidenceKey);
        Assert.Equal(new[] { "1", "Wondershare", "Wondershare (2/3 boots)" }, finding.EvidenceArgs);
        Assert.Equal("1 programs start with Windows. Heavy ones that can be started " +
            "manually instead: Wondershare. Windows itself blamed these for starting " +
            "late on recent boots: Wondershare (2/3 boots).", finding.Evidence);

        new StartupBloatRule().Fix(ctx);
        Assert.Equal(0x03, reg.GetBytes(ApprovedKey, "Wondershare")![0]);
    }

    /// One late start is a one-off, not a habit — and a delay under 5 s is
    /// not one anybody felt.
    [Fact]
    public void OneBlamedBoot_OrASmallDelay_IsNotHeavy()
    {
        var (ctx, _) = Ctx("Wondershare");
        var log = (FakeEventLog)ctx.EventLog;
        log.Boots.Add(Blaming(0, ("Wondershare.exe", 12_000)));
        log.Boots.Add(Blaming(-1, ("Wondershare.exe", 3_000)));
        log.Boots.Add(Blaming(-2));

        Assert.Null(new StartupBloatRule().Detect(ctx));
    }

    /// OneDrive not starting means files silently not syncing; Windows
    /// blaming it does not make that brisk's call.
    [Fact]
    public void OneDrive_IsNeverHeavyOnWindowsWordAlone()
    {
        var (ctx, _) = Ctx("OneDrive");
        var log = (FakeEventLog)ctx.EventLog;
        log.Boots.Add(Blaming(0, ("OneDrive.exe", 20_000)));
        log.Boots.Add(Blaming(-1, ("OneDrive.exe", 20_000)));

        Assert.Null(new StartupBloatRule().Detect(ctx));
    }

    /// A known-heavy name stays on the older template: the blame sentence
    /// is only for entries heavy on Windows' word rather than brisk's list.
    [Fact]
    public void AKnownHeavyName_KeepsTheHeavyTemplate_EvenWhenWindowsBlamesItToo()
    {
        var (ctx, _) = Ctx("Steam");
        var log = (FakeEventLog)ctx.EventLog;
        log.Boots.Add(Blaming(0, ("Steam.exe", 20_000)));
        log.Boots.Add(Blaming(-1, ("Steam.exe", 20_000)));

        var finding = new StartupBloatRule().Detect(ctx)!;

        Assert.Equal("rule.startup-bloat.evidence.heavy", finding.EvidenceKey);
    }

    /// The command decides, not the value name: a Run entry called
    /// "Updater" that starts C:\Program Files\Foo\foo.exe matches Windows'
    /// blame of foo.exe — quoted or not, whatever follows the path.
    [Theory]
    [InlineData("\"C:\\Program Files\\Foo\\foo.exe\" --minimized", "foo.exe")]
    [InlineData("C:\\Program Files\\Foo\\foo.exe /background", "foo.exe")]
    [InlineData("C:\\apps\\bar.EXE", "bar.EXE")]
    [InlineData("rundll32.dll,Entry", null)]
    [InlineData("", null)]
    public void ExecutableName_ReadsTheProgramOutOfAnyRunCommand(string command, string? exe) =>
        Assert.Equal(exe, BriskEngine.Diagnostics.StartupManager.ExecutableName(command));
}
