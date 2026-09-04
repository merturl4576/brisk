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
