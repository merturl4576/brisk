using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

/// UAC inverts the switch family's standing rule: absence is Windows' DEFAULT
/// (prompting), so an unwritten value cannot be read as "never prompt" and
/// raises nothing. Only a written 0 in EnableLUA or ConsentPromptBehaviorAdmin
/// is the bad state. PromptOnSecureDesktop is restored by the fix but never
/// decides the finding: the third position of Windows' own UAC slider sets
/// it to 0 on purpose.
public class UacPromptRuleTests
{
    private static (DiagnosticContext ctx, FakeRegistry reg) Context()
    {
        var reg = new FakeRegistry();
        return (TestContext.Empty() with { Registry = reg }, reg);
    }

    private static void Plant(FakeRegistry reg, int? lua, int? admin, int? secure)
    {
        if (lua is { } l) reg.SetInt(UacPromptRule.KeyPath, UacPromptRule.EnableLua, l);
        if (admin is { } a) reg.SetInt(UacPromptRule.KeyPath, UacPromptRule.ConsentPromptBehaviorAdmin, a);
        if (secure is { } s) reg.SetInt(UacPromptRule.KeyPath, UacPromptRule.PromptOnSecureDesktop, s);
    }

    private static int? Read(FakeRegistry reg, string name) =>
        reg.GetInt(UacPromptRule.KeyPath, name);

    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(1, 0, 1, true)]
    [InlineData(0, 5, 1, true)]
    [InlineData(null, 0, null, true)]
    [InlineData(1, 5, 1, false)]
    [InlineData(1, 2, 1, false)]
    [InlineData(1, 5, 0, false)]
    [InlineData(null, null, null, false)]
    [InlineData(1, null, null, false)]
    public void FiresOnlyOnAWrittenZero_ThatMeansNeverPrompt(int? lua, int? admin, int? secure, bool fires)
    {
        var (ctx, reg) = Context();
        Plant(reg, lua, admin, secure);
        var finding = new UacPromptRule().Detect(ctx);
        Assert.Equal(fires, finding is not null);
    }

    [Fact]
    public void Fix_WritesWindowsDefaults_AndUndo_PutsTheZerosBack()
    {
        var (ctx, reg) = Context();
        Plant(reg, 1, 0, 0);
        var rule = new UacPromptRule();

        var prior = rule.Fix(ctx);
        Assert.Equal(1, Read(reg, UacPromptRule.EnableLua));
        Assert.Equal(5, Read(reg, UacPromptRule.ConsentPromptBehaviorAdmin));
        Assert.Equal(1, Read(reg, UacPromptRule.PromptOnSecureDesktop));
        Assert.Null(rule.Detect(ctx));

        rule.Undo(ctx, prior);
        Assert.Equal(1, Read(reg, UacPromptRule.EnableLua));
        Assert.Equal(0, Read(reg, UacPromptRule.ConsentPromptBehaviorAdmin));
        Assert.Equal(0, Read(reg, UacPromptRule.PromptOnSecureDesktop));
    }

    [Fact]
    public void Undo_DeletesWhatTheFixCreated()
    {
        var (ctx, reg) = Context();
        Plant(reg, null, 0, null);
        var rule = new UacPromptRule();

        var prior = rule.Fix(ctx);
        rule.Undo(ctx, prior);

        Assert.Null(Read(reg, UacPromptRule.EnableLua));
        Assert.Equal(0, Read(reg, UacPromptRule.ConsentPromptBehaviorAdmin));
        Assert.Null(Read(reg, UacPromptRule.PromptOnSecureDesktop));
    }

    [Fact]
    public void ReadBack_SaysHeldAfterTheFix_AndRevertedWhenSomethingWritesZeroAgain()
    {
        var (ctx, reg) = Context();
        Plant(reg, 1, 0, 0);
        var rule = new UacPromptRule();
        rule.Fix(ctx);
        var journal = new[] { new UndoableFix(UacPromptRule.RuleId,
            new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc)) };

        var held = ReadBack.For(ctx, journal, DiagnosticRuleRegistry.All).Single();
        Assert.Equal(ReadBackState.Held, held.State);

        reg.SetInt(UacPromptRule.KeyPath, UacPromptRule.ConsentPromptBehaviorAdmin, 0);
        var reverted = ReadBack.For(ctx, journal, DiagnosticRuleRegistry.All).Single();
        Assert.Equal(ReadBackState.Reverted, reverted.State);
    }

    [Fact]
    public void TheFinding_IsConfirm_Warning_Notice_AndMovesNoScore()
    {
        var (ctx, reg) = Context();
        Plant(reg, 1, 0, 0);
        var rule = new UacPromptRule();
        var finding = rule.Detect(ctx)!;

        Assert.Equal(RuleCategory.Confirm, rule.Category);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(FindingKind.Notice, finding.Kind);
        Assert.True(finding.CanFix);
        Assert.Null(finding.Headline);
        Assert.Equal(100, HealthScore.Compute(new[] { finding }));
        Assert.Equal(1, DiagnosticRuleRegistry.All.Count(r => r.Id == UacPromptRule.RuleId));
    }

    [Fact]
    public void EveryOtherShippedSwitch_StaysInfo()
    {
        var (ctx, _) = Context();
        foreach (var rule in DiagnosticRuleRegistry.All.OfType<TelemetrySwitchRule>()
                     .Where(r => r.Id != UacPromptRule.RuleId))
            Assert.True(rule.Detect(ctx)!.Severity == Severity.Info,
                $"{rule.Id} raised its severity; only uac-prompts does that");
    }

    /// THE SECOND THING THIS RULE INVERTS: its fix turns a setting ON. Every
    /// other switch turns one off, and the read-back's sentences on the
    /// Privacy page are written that way — "You switched this off 3 days ago;
    /// it still reads as off" over a row where brisk switched prompting ON
    /// would say the opposite of what happened.
    ///
    /// Declared on the rule and asserted over the whole registry, so an
    /// eighth switch that also turns something on is a row somebody has to
    /// look at rather than a page that quietly renders the wrong line.
    [Fact]
    public void TheFixTurnsThisOn_AndEveryOtherSwitchStillTurnsSomethingOff()
    {
        Assert.True(new UacPromptRule().FixTurnsItOn);
        foreach (var rule in DiagnosticRuleRegistry.All.OfType<TelemetrySwitchRule>()
                     .Where(r => r.Id != UacPromptRule.RuleId))
            Assert.False(rule.FixTurnsItOn,
                $"{rule.Id} says its fix turns something on; only uac-prompts " +
                "does that, and only its two states have an on-direction line");
    }

    [Theory]
    [InlineData("Strings.resx")]
    [InlineData("Strings.tr.resx")]
    public void TheCopy_ExistsInBothLanguages_AndNamesTheCost(string file)
    {
        var resx = Resx(file);
        foreach (var suffix in new[] { "title", "evidence", "advice", "cost", "done" })
            Assert.True(resx.ContainsKey($"rule.uac-prompts.{suffix}"),
                $"rule.uac-prompts.{suffix} is missing from {file}");
        Assert.False(string.IsNullOrWhiteSpace(resx["rule.uac-prompts.cost"]));
    }

    /// THE DONE LINE NAMES THE WRITE AND NOT THE BEHAVIOUR, and the wording
    /// IS the requirement here, so it is pinned as a literal in both files.
    ///
    /// Restoring EnableLUA from a written 0 takes effect at the next restart.
    /// A done line that said "Windows asks again before a program takes
    /// control" would therefore be false on exactly the machine the user
    /// checks first: fix, elevate something, see no prompt. What the write
    /// makes true the instant it lands is that UAC's three values are back at
    /// Windows' defaults, and that is all this line claims. It is also the
    /// read-back row's TITLE on the Privacy page (DoneLabel.For), so it reads
    /// as a short outcome label like every other rule.&lt;id&gt;.done entry.
    ///
    /// The restart caveat is not dropped, it is moved: rule.uac-prompts.advice
    /// carries it as a static conditional sentence, shown before the user
    /// chooses. The second assertion is what keeps it there — a done line that
    /// promises nothing and an advice line that says nothing would together
    /// leave the caveat nowhere.
    [Theory]
    [InlineData("Strings.resx", "UAC values restored to Windows' defaults",
        "If EnableLUA was the value that read 0, the prompts return after a restart.")]
    [InlineData("Strings.tr.resx", "UAC değerleri Windows'un varsayılanlarına geri getirildi",
        "0 okunan değer EnableLUA idiyse sorular yeniden başlatmadan sonra döner.")]
    public void TheDoneLine_NamesTheWrite_AndTheAdviceKeepsTheRestartCaveat(
        string file, string done, string caveat)
    {
        var resx = Resx(file);
        Assert.Equal(done, resx["rule.uac-prompts.done"]);
        Assert.True(
            resx["rule.uac-prompts.advice"].Contains(caveat, StringComparison.Ordinal),
            $"rule.uac-prompts.advice in {file} no longer says \"{caveat}\", so " +
            "nothing brisk shows this user mentions the restart");
    }

    [Fact]
    public void TheEnglishTitle_MatchesTheResxEntry()
    {
        var (ctx, reg) = Context();
        Plant(reg, 1, 0, 0);
        var finding = new UacPromptRule().Detect(ctx)!;
        Assert.Equal(Resx("Strings.resx")["rule.uac-prompts.title"], finding.Title);
        Assert.Equal(Resx("Strings.resx")["rule.uac-prompts.evidence"], finding.Evidence);
    }

    private static Dictionary<string, string> Resx(string fileName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null;
             dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return XDocument
                    .Load(Path.Combine(dir.FullName, "src", "Brisk", "Localization", fileName))
                    .Root!.Elements("data")
                    .ToDictionary(e => (string)e.Attribute("name")!,
                        e => (string)e.Element("value")!);
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }
}
