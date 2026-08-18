using System;
using System.Collections.Generic;
using BriskEngine.Diagnostics.Rules;
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
}
