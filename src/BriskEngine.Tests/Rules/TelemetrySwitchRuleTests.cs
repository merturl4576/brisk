using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

/// The four switches the Privacy page turns off with one button. One thing
/// makes them unlike every other fixable rule brisk ships, and it is asserted
/// here rather than described: a value that is ABSENT reads as ON. A machine
/// nobody has touched has nothing written at any of these paths, and Windows
/// treats that absence as the permissive default — so Detect has to fire on
/// an empty registry, and the undo of that fix has to DELETE the value again.
/// Writing the off number where there was no value at all is not a
/// restoration; it is a second change wearing an undo's name.
public class TelemetrySwitchRuleTests
{
    private static readonly string[] Ids =
    {
        "advertising-id", "diagnostic-level",
        "tailored-experiences", "speech-typing",
    };

    public static TheoryData<string> AllSwitches()
    {
        var data = new TheoryData<string>();
        foreach (var id in Ids) data.Add(id);
        return data;
    }

    private static TelemetrySwitchRule Rule(string id) => id switch
    {
        "advertising-id" => new AdvertisingIdRule(),
        "diagnostic-level" => new DiagnosticLevelRule(),
        "tailored-experiences" => new TailoredExperiencesRule(),
        "speech-typing" => new SpeechTypingRule(),
        _ => throw new ArgumentOutOfRangeException(
            nameof(id), id, "not one of this wave's telemetry switches"),
    };

    private static (DiagnosticContext ctx, FakeRegistry reg) Context()
    {
        var reg = new FakeRegistry();
        return (TestContext.Empty() with { Registry = reg }, reg);
    }

    private static string Read(FakeRegistry reg, TelemetrySwitchRule.RegistryValue v) =>
        reg.GetInt(v.KeyPath, v.ValueName)?.ToString(CultureInfo.InvariantCulture)
        ?? "absent";

    /// The trap, per rule: nothing written anywhere, and all four still fire.
    [Theory]
    [MemberData(nameof(AllSwitches))]
    public void UntouchedMachine_IsAFinding_AndFixThenUndoLeavesEveryValueAbsent(string id)
    {
        var (ctx, reg) = Context();
        var rule = Rule(id);

        Assert.True(rule.IsOn(ctx),
            $"{id}: nothing is written at any of its paths and IsOn still reads false");
        Assert.NotNull(rule.Detect(ctx));

        var prior = rule.Fix(ctx);
        Assert.Null(rule.Detect(ctx));

        rule.Undo(ctx, prior);
        foreach (var v in rule.Values)
            Assert.True(reg.GetInt(v.KeyPath, v.ValueName) is null,
                $"{id}: undo left {Read(reg, v)} at {v.KeyPath}\\{v.ValueName}, " +
                "where nothing existed before the fix");
    }

    /// Every value explicitly at the number the fix writes: the switch reads
    /// as off and brisk has nothing to say about it.
    [Theory]
    [MemberData(nameof(AllSwitches))]
    public void EveryValueAlreadyOff_IsNoFinding(string id)
    {
        var (ctx, reg) = Context();
        var rule = Rule(id);
        foreach (var v in rule.Values) reg.SetInt(v.KeyPath, v.ValueName, v.OffValue);

        Assert.False(rule.IsOn(ctx),
            $"{id}: every value reads {string.Join(", ", rule.Values.Select(v => Read(reg, v)))} " +
            "— the number the fix itself writes — and IsOn still reads true");
        Assert.Null(rule.Detect(ctx));
    }

    /// The switch explicitly on. The round trip has to hand back the number
    /// that was there, not the absence the untouched case restores.
    [Theory]
    [MemberData(nameof(AllSwitches))]
    public void EveryValueOn_IsAFinding_AndUndoRestoresTheNumberThatWasThere(string id)
    {
        var (ctx, reg) = Context();
        var rule = Rule(id);
        foreach (var v in rule.Values) reg.SetInt(v.KeyPath, v.ValueName, v.OnValue);

        Assert.NotNull(rule.Detect(ctx));
        var prior = rule.Fix(ctx);
        Assert.Null(rule.Detect(ctx));

        rule.Undo(ctx, prior);
        foreach (var v in rule.Values)
            Assert.True(reg.GetInt(v.KeyPath, v.ValueName) == v.OnValue,
                $"{id}: {v.KeyPath}\\{v.ValueName} came back as {Read(reg, v)}, " +
                $"not the {v.OnValue} that was there before the fix");
    }

    /// Privacy is a second axis: brisk shows it and acts on it but never
    /// grades it. PrivacyRedLineTests proves HealthScore skips a Notice it
    /// plants itself and says in as many words that no real rule's Kind is
    /// ever read there. This reads them.
    [Theory]
    [MemberData(nameof(AllSwitches))]
    public void TheFinding_IsANotice_AndCostsTheHealthScoreNothing(string id)
    {
        var (ctx, _) = Context();
        var finding = Rule(id).Detect(ctx);

        Assert.NotNull(finding);
        Assert.True(finding!.Kind == FindingKind.Notice,
            $"{id} ships as {finding.Kind}; every finding in this wave is a Notice");
        Assert.True(HealthScore.Compute(new[] { finding! }) == 100,
            $"{id} moved the health score to {HealthScore.Compute(new[] { finding! })}");
    }

    /// The keys the GUI renders from, and the absence of one it must not
    /// render: these four measure no number, so they lead with no headline.
    [Theory]
    [MemberData(nameof(AllSwitches))]
    public void TheFinding_CarriesTheRulesOwnKeys_AndNoHeadline(string id)
    {
        var (ctx, _) = Context();
        var finding = Rule(id).Detect(ctx)!;

        Assert.Equal(id, finding.RuleId);
        Assert.Equal($"rule.{id}.title", finding.TitleKey);
        Assert.Equal($"rule.{id}.evidence", finding.EvidenceKey);
        Assert.Equal(RuleCategory.Auto, finding.Category);
        Assert.True(finding.CanFix, $"{id}: the one-button group has to be fixable");
        Assert.False(string.IsNullOrWhiteSpace(finding.FixDescription),
            $"{id}: nothing describes what the fix does");
        Assert.Null(finding.Headline);
    }

    /// A rule brisk never runs is a rule that never fires. The id is matched
    /// as a literal because the list the Privacy page routes on lives in the
    /// Brisk project, which BriskEngine cannot see — PrivacyRedLineTests
    /// reads the shipped rules against that list from the other side.
    [Theory]
    [MemberData(nameof(AllSwitches))]
    public void EachSwitch_IsRegisteredExactlyOnce(string id)
    {
        Assert.True(DiagnosticRuleRegistry.All.Count(r => r.Id == id) == 1,
            $"'{id}' appears {DiagnosticRuleRegistry.All.Count(r => r.Id == id)} times " +
            "in DiagnosticRuleRegistry.All");
    }

    /// speech-typing is the only switch with two values, and the machine a
    /// single-value rule would get wrong is the half-set one: somebody
    /// restricted text collection and never touched ink. The undo has to
    /// restore a 1 to one value and remove the other.
    [Fact]
    public void SpeechTyping_TextRestrictedAndInkAbsent_IsStillAFinding_AndUndoRestoresBothStates()
    {
        var (ctx, reg) = Context();
        var rule = new SpeechTypingRule();
        reg.SetInt(SpeechTypingRule.KeyPath, SpeechTypingRule.TextValueName, 1);

        Assert.NotNull(rule.Detect(ctx));
        rule.Undo(ctx, rule.Fix(ctx));

        var text = reg.GetInt(SpeechTypingRule.KeyPath, SpeechTypingRule.TextValueName);
        var ink = reg.GetInt(SpeechTypingRule.KeyPath, SpeechTypingRule.InkValueName);
        Assert.True(text == 1,
            $"{SpeechTypingRule.TextValueName} came back as " +
            $"{text?.ToString(CultureInfo.InvariantCulture) ?? "absent"}, not the 1 that was there");
        Assert.True(ink is null,
            $"{SpeechTypingRule.InkValueName} came back as " +
            $"{ink?.ToString(CultureInfo.InvariantCulture) ?? "absent"}, " +
            "where nothing existed before the fix");
    }

    /// AllowTelemetry 0 is Security — stricter than the 1 the fix writes. A
    /// rule that read "anything but 1" as on would loosen the machine it
    /// claims to be tightening, so 0 is left alone and said nothing about.
    [Fact]
    public void DiagnosticLevel_SecurityLevel_IsNoFinding()
    {
        var (ctx, reg) = Context();
        reg.SetInt(DiagnosticLevelRule.KeyPath, DiagnosticLevelRule.ValueName, 0);

        Assert.False(new DiagnosticLevelRule().IsOn(ctx),
            "AllowTelemetry 0 (Security) reads as on, and the fix would raise it to 1");
        Assert.Null(new DiagnosticLevelRule().Detect(ctx));
    }

    /// The one switch that is a level and not a flag, so "on" is a threshold.
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void DiagnosticLevel_AtOrAboveEnhanced_IsAFinding(int level)
    {
        var (ctx, reg) = Context();
        reg.SetInt(DiagnosticLevelRule.KeyPath, DiagnosticLevelRule.ValueName, level);

        Assert.NotNull(new DiagnosticLevelRule().Detect(ctx));
    }

    /// The edition trap. A diagnostic data level is recorded under two keys,
    /// and the read-back's "written but ignored" sentence exists because they
    /// can disagree. So brisk reads the second one and never writes it: a fix
    /// that wrote the number it later reads back would turn the read-back
    /// into a mirror. Absent reads as null, never as 0.
    [Fact]
    public void DiagnosticLevel_ReadsTheConsumerValue_AndTheFixNeverWritesIt()
    {
        var (ctx, reg) = Context();
        var rule = new DiagnosticLevelRule();

        Assert.True(rule.EffectiveLevel(ctx) is null,
            "an unwritten consumer value read as a number instead of as nothing");

        reg.SetInt(DiagnosticLevelRule.EffectiveKeyPath,
            DiagnosticLevelRule.ValueName, 3);
        Assert.Equal(3, rule.EffectiveLevel(ctx));

        rule.Fix(ctx);
        Assert.True(rule.EffectiveLevel(ctx) == 3,
            "the fix wrote the value it reads back — the read-back would then " +
            "be reading brisk's own number back to itself");
        Assert.Equal(1, reg.GetInt(DiagnosticLevelRule.KeyPath,
            DiagnosticLevelRule.ValueName));
    }

    /// One claim, two sources: the engine ships English prose that the CLI
    /// prints verbatim, next to a resx key the GUI renders instead. This
    /// reads the English resx off disk the way LocTests does and refuses to
    /// let the two drift. The Turkish file is held to the same key set by
    /// LocTests' ResxFiles_ExposeTheSameKeySet — its wording is not, and
    /// nothing here reads it.
    [Theory]
    [MemberData(nameof(AllSwitches))]
    public void TheEnglishResx_SaysWhatTheEngineSays(string id)
    {
        var (ctx, _) = Context();
        var finding = Rule(id).Detect(ctx)!;
        var en = EnglishStrings();

        Assert.True(en.TryGetValue(finding.TitleKey, out var title),
            $"{finding.TitleKey} is missing from Strings.resx");
        Assert.Equal(finding.Title, title);

        Assert.True(en.TryGetValue(finding.EvidenceKey!, out var evidence),
            $"{finding.EvidenceKey} is missing from Strings.resx");
        Assert.Equal(finding.Evidence, evidence);

        Assert.True(en.ContainsKey($"rule.{id}.advice"),
            $"rule.{id}.advice is missing from Strings.resx");
    }

    private static Dictionary<string, string> EnglishStrings()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null;
             dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return XDocument
                    .Load(Path.Combine(dir.FullName, "src", "Brisk", "Localization",
                        "Strings.resx")).Root!
                    .Elements("data")
                    .ToDictionary(e => (string)e.Attribute("name")!,
                        e => (string)e.Element("value")!);
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }

    /// The paths, pinned as literals. Every other test in this file reads the
    /// rule's own Values collection, so a rule pointed at the wrong key would
    /// pass all of them by being consistently wrong.
    [Fact]
    public void ThePaths_AreTheOnesTheSpecNames()
    {
        Assert.Equal(
            new[]
            {
                (@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled"),
                (@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry"),
                (@"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy",
                    "TailoredExperiencesWithDiagnosticDataEnabled"),
                (@"HKCU\Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection"),
                (@"HKCU\Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection"),
            },
            Ids.SelectMany(id => Rule(id).Values).Select(v => (v.KeyPath, v.ValueName)));
    }
}
