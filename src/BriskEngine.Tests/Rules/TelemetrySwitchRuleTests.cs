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

/// The six switches this wave gives brisk: four that cost the user nothing
/// visible, and two — location and activity-history — that cost Find my
/// device and Timeline. One thing makes them unlike every other fixable rule
/// brisk ships, and it is asserted here rather than described: a value that is
/// ABSENT reads as ON — not because brisk knows what Windows does with an
/// unwritten value, but because it cannot read one as off. So Detect has to
/// fire on an empty registry, and the undo of that fix has to DELETE the value
/// again. Writing the off number where there was no value at all is not a
/// restoration; it is a second change wearing an undo's name.
///
/// Five of the six store a number. location stores a WORD — "Allow" or "Deny"
/// — so it carries no RegistryValue at all and reads, writes and restores its
/// own state; the theories driven by NumberValuedSwitches leave it out and the
/// Location_* facts below cover it instead.
public class TelemetrySwitchRuleTests
{
    private static readonly string[] Ids =
    {
        "advertising-id", "diagnostic-level",
        "tailored-experiences", "speech-typing",
        "location", "activity-history",
    };

    /// Every switch whose state brisk reads as a number, which is every one of
    /// them except location. The theories that walk a rule's Values collection
    /// take this list: over location they would walk an EMPTY collection and
    /// pass while asserting nothing at all.
    private static readonly string[] NumberValuedIds =
        Ids.Where(id => id != "location").ToArray();

    public static TheoryData<string> AllSwitches()
    {
        var data = new TheoryData<string>();
        foreach (var id in Ids) data.Add(id);
        return data;
    }

    public static TheoryData<string> NumberValuedSwitches()
    {
        var data = new TheoryData<string>();
        foreach (var id in NumberValuedIds) data.Add(id);
        return data;
    }

    private static TelemetrySwitchRule Rule(string id) => id switch
    {
        "advertising-id" => new AdvertisingIdRule(),
        "diagnostic-level" => new DiagnosticLevelRule(),
        "tailored-experiences" => new TailoredExperiencesRule(),
        "speech-typing" => new SpeechTypingRule(),
        "location" => new LocationRule(),
        "activity-history" => new ActivityHistoryRule(),
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

    /// The trap, per rule: nothing written anywhere, and every one of them
    /// still fires. location has the same trap and its own test for it, below.
    [Theory]
    [MemberData(nameof(NumberValuedSwitches))]
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
    [MemberData(nameof(NumberValuedSwitches))]
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
    [MemberData(nameof(NumberValuedSwitches))]
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
    /// grades it. This reads the real Kind of every switch, over states this
    /// file plants itself.
    ///
    /// It is one of four tests that read a real Kind, and the widest of them
    /// is not in this assembly. PrivacyDisclosureRuleTests and
    /// DeliveryOptimizationRuleTests cover the other families the way this
    /// one covers the switches — each from an id list it keeps itself —
    /// while PrivacyRedLineTests' EveryPrivacyRule_ReportsANoticeAndCosts
    /// TheScoreNothing walks DiagnosticRuleRegistry.All against
    /// PrivacyRuleIds.All. That is the one reading that GROWS when a privacy
    /// rule is added; the three id lists do not, and BriskEngine cannot see
    /// PrivacyRuleIds at all. What stays here is the family's own coverage:
    /// these six under the states this file knows how to build.
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
        // The rule's own consent level, whatever it is — the finding must not
        // report a different one from the rule that produced it. Which level
        // each rule ships is pinned by TheConsentLevel_MatchesWhatTheSwitchCosts.
        Assert.Equal(Rule(id).Category, finding.Category);
        Assert.True(finding.CanFix, $"{id}: a switch brisk offers to flip has to be fixable");
        Assert.False(string.IsNullOrWhiteSpace(finding.FixDescription),
            $"{id}: nothing describes what the fix does");
        Assert.Null(finding.Headline);
    }

    /// A rule brisk never runs is a rule that never fires. The id is matched
    /// as a literal because the list the privacy page routes on lives in the
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

    /// The consent level, per switch, and the reason it differs. RuleCategory
    /// is what the CLI's `fix --all` selects on — it applies every Auto rule
    /// it finds a finding for — so this is not a label, it is the thing that
    /// decides whether somebody who typed `--all` and was shown no consequence
    /// can lose Find my device. CliFixAll_NeverReachesASwitchThatCostsThe
    /// UserSomething, in ProgramFixTests, asserts the other end of the same
    /// wire.
    [Theory]
    [InlineData("advertising-id", RuleCategory.Auto)]
    [InlineData("diagnostic-level", RuleCategory.Auto)]
    [InlineData("tailored-experiences", RuleCategory.Auto)]
    [InlineData("speech-typing", RuleCategory.Auto)]
    [InlineData("location", RuleCategory.Confirm)]
    [InlineData("activity-history", RuleCategory.Confirm)]
    public void TheConsentLevel_MatchesWhatTheSwitchCosts(string id, RuleCategory expected)
    {
        Assert.True(Rule(id).Category == expected,
            $"{id} ships as {Rule(id).Category}, not {expected} — and a costly " +
            "switch shipped as Auto is one a CLI `fix --all` would apply with " +
            "no consequence named");
    }

    /// The whole point of the two costly switches: the loss is IN the copy,
    /// in both languages, or the switch is a trap. Read off disk from both
    /// resx files, and from the ENGINE's own prose too — `brisk scan` prints
    /// every finding's title and evidence, and `brisk fix --rule <id>` prints
    /// them again before it asks for --yes; neither prints advice, and
    /// nothing in Brisk.Cli reads a rule.*.advice string at all —
    /// HealthViewModel's adviceKey is the only reader of one in the repo.
    /// A loss named only in the advice is a loss no CLI user is ever shown.
    ///
    /// This pins a phrasing, which usually goes stale on the next reword.
    /// Here the phrasing IS the requirement: these two sentences are what the
    /// task exists to put in front of the user, so a reword that drops one
    /// should fail rather than pass quietly.
    [Theory]
    [InlineData("location", "Find my device stops working.", "'Cihazımı bul' çalışmaz.")]
    [InlineData("activity-history", "Timeline ends.", "Timeline biter.")]
    public void TheCostlySwitch_NamesTheLoss_InBothLanguages(
        string id, string english, string turkish)
    {
        var (ctx, _) = Context();
        var finding = Rule(id).Detect(ctx)!;
        Assert.True(finding.Evidence.Contains(english, StringComparison.Ordinal),
            $"the engine's evidence for {id} never says \"{english}\" — and the " +
            "CLI prints evidence, never advice");

        foreach (var (file, sentence) in new[]
                 { ("Strings.resx", english), ("Strings.tr.resx", turkish) })
        {
            var strings = Resx(file);
            foreach (var key in new[] { $"rule.{id}.evidence", $"rule.{id}.advice" })
            {
                Assert.True(strings.TryGetValue(key, out var text),
                    $"{key} is missing from {file}");
                Assert.True(text!.Contains(sentence, StringComparison.Ordinal),
                    $"{key} in {file} never says \"{sentence}\", so the switch " +
                    "costs something the copy does not name");
            }
        }
    }

    /// A policy is the one kind of setting an edition of Windows can ignore,
    /// and activity-history is a policy whose fix brisk cannot verify: it
    /// re-reads the two values it wrote and nothing else, so after a
    /// successful fix the finding ALWAYS clears, on an edition that honoured
    /// the policy and on one that did not. Copy that said only "Timeline
    /// ends" would be brisk reporting a consequence it did not achieve.
    ///
    /// So the hedge is required, and it is required in the EVIDENCE and not
    /// only in the advice, for the same reason the loss is: the CLI prints
    /// evidence and never prints advice. The closing clause is word for word
    /// the one rule.diagnostic-level.advice already uses, which is what makes
    /// a grep for it find every place brisk makes this admission.
    ///
    /// diagnostic-level is here too, and for one round it was not: it carried
    /// the sentence in its advice and not in its evidence, which is the same
    /// CLI gap — a user who only ever runs `brisk scan` or `brisk fix --rule
    /// diagnostic-level` was never shown it. Both policy switches now say it
    /// in both places, so the theory takes both and a rule that drops it from
    /// either fails.
    ///
    /// The two rules are not equally placed behind that sentence, and the
    /// sentence is not a substitute for the read. diagnostic-level also reads
    /// a second, consumer-side key, so the read-back HAS two numbers that can
    /// disagree and says "written but ignored" when they do; activity-history
    /// has no counterpart established for it, says so in its own class doc,
    /// and reads back as written-but-unverified on every machine rather than
    /// borrowing the held line. The admission is the floor for both.
    [Theory]
    [InlineData("activity-history",
        "whether this edition of Windows acts on that policy is not something " +
        "that read can tell you",
        "bu Windows sürümünün o ilkeye uyup uymadığını bu okuma söyleyemez")]
    [InlineData("diagnostic-level",
        "whether this edition of Windows acts on that policy is not something " +
        "that read can tell you",
        "bu Windows sürümünün o ilkeye uyup uymadığını bu okuma söyleyemez")]
    public void ThePolicySwitch_DoesNotPromiseTheEditionObeyedIt(
        string id, string english, string turkish)
    {
        var (ctx, _) = Context();
        var finding = Rule(id).Detect(ctx)!;
        Assert.True(finding.Evidence.Contains(english, StringComparison.Ordinal),
            $"the engine's evidence for {id} promises the loss and never admits " +
            "brisk re-reads only the policy it wrote — and the CLI prints " +
            "evidence, never advice");

        foreach (var (file, sentence) in new[]
                 { ("Strings.resx", english), ("Strings.tr.resx", turkish) })
        {
            var strings = Resx(file);
            foreach (var key in new[] { $"rule.{id}.evidence", $"rule.{id}.advice" })
            {
                Assert.True(strings.TryGetValue(key, out var text),
                    $"{key} is missing from {file}");
                Assert.True(text!.Contains(sentence, StringComparison.Ordinal),
                    $"{key} in {file} never says \"{sentence}\", so brisk " +
                    "promises a policy took effect that it cannot see took effect");
            }
        }
    }

    /// location is the one switch whose state is a word. "Deny" is the only
    /// thing that reads as off; the cases below — "Allow", a word brisk does
    /// not recognise, an empty string and nothing written at all — read as on,
    /// because what cannot be read as off is not reported as off. The fifth
    /// case, a value of a type brisk cannot read as text, has its own fact
    /// below it.
    [Theory]
    [InlineData(null)]
    [InlineData("Allow")]
    [InlineData("Prompt")]
    [InlineData("")]
    public void Location_AnythingButDenied_IsAFinding_AndUndoPutsItBack(string? state)
    {
        var (ctx, reg) = Context();
        var rule = new LocationRule();
        if (state is not null)
            reg.SetString(LocationRule.KeyPath, LocationRule.ValueName, state);

        Assert.True(rule.IsOn(ctx),
            $"the location consent reads {state ?? "absent"}, which is not " +
            $"{LocationRule.Denied}, and IsOn reported the switch as off");
        Assert.NotNull(rule.Detect(ctx));

        var prior = rule.Fix(ctx);
        Assert.Equal(LocationRule.Denied,
            reg.GetString(LocationRule.KeyPath, LocationRule.ValueName));
        Assert.Null(rule.Detect(ctx));

        rule.Undo(ctx, prior);
        var after = reg.GetString(LocationRule.KeyPath, LocationRule.ValueName);
        Assert.True(after == state,
            $"undo left {after ?? "absent"} at {LocationRule.KeyPath}\\" +
            $"{LocationRule.ValueName}, not the {state ?? "absent"} that was there");
    }

    /// The same word the fix writes, already there: brisk has nothing to say.
    [Fact]
    public void Location_AlreadyDenied_IsNoFinding()
    {
        var (ctx, reg) = Context();
        reg.SetString(LocationRule.KeyPath, LocationRule.ValueName, LocationRule.Denied);

        Assert.False(new LocationRule().IsOn(ctx),
            $"the location consent reads {LocationRule.Denied} — the word the " +
            "fix itself writes — and IsOn still reads true");
        Assert.Null(new LocationRule().Detect(ctx));
    }

    /// A registry brisk did not write is not one brisk gets to assume the
    /// shape of. Reading "deny" as on would have brisk offer to switch off
    /// something already off, and then report a change it did not make.
    [Fact]
    public void Location_DeniedInAnyCase_IsNoFinding()
    {
        var (ctx, reg) = Context();
        reg.SetString(LocationRule.KeyPath, LocationRule.ValueName, "deny");

        Assert.False(new LocationRule().IsOn(ctx),
            "the location consent reads deny and IsOn read the case, not the word");
    }

    /// The unreadable case, and the wave's rule for it: an unreadable probe
    /// reports unreadable, never protection. A number sitting where the
    /// consent word belongs cannot be read as text at all — the real probe
    /// returns null for it exactly as the fake does — and brisk did NOT read
    /// that as denied, so it does not report it as denied.
    [Fact]
    public void Location_AValueOfATypeBriskCannotRead_ReadsAsOn_NotAsProtection()
    {
        var (ctx, reg) = Context();
        reg.SetInt(LocationRule.KeyPath, LocationRule.ValueName, 0);

        Assert.True(new LocationRule().IsOn(ctx),
            "a number sits where the location consent word belongs, brisk " +
            "could not read it as text, and IsOn reported the switch as off");
        Assert.NotNull(new LocationRule().Detect(ctx));
    }

    /// location carries no RegistryValue, and that is a hazard worth failing
    /// over rather than discovering. Anything that walks the family's Values
    /// collections to compare what brisk wrote against what is there now
    /// walks nothing at all for this rule and would report a switch it never
    /// checked as one it checked and found fine. The rule's own consts are
    /// where its surface lives.
    ///
    /// The read-back that would have been the first caller to do exactly that
    /// was built not to: ReadBack asks IsOn and EffectOfTheWrite, both
    /// virtual, so location answers for itself. ReadBackTests'
    /// Location_IsReadBackThroughItsOwnWord_NotThroughAValuesWalk is what
    /// fails if that ever changes.
    [Fact]
    public void Location_CarriesNoRegistryValue_BecauseItsStateIsAWord()
    {
        Assert.True(new LocationRule().Values.Count == 0,
            "LocationRule grew a RegistryValue: RegistryValue carries an on " +
            "number and an off number, and this switch has neither");
    }

    /// One of the two switches that carry two values (activity-history is the
    /// other), and the machine a single-value rule would get wrong is the
    /// half-set one: somebody restricted text collection and never touched
    /// ink. The undo has to restore a 1 to one value and remove the other.
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

    /// A number that is neither the on value nor the off value — the THIRD
    /// state, and the one an exact match on OnValue used to read as
    /// protection and say nothing about. That is the silent direction:
    /// brisk cannot tell what an unrecognised number means, and "I could not
    /// read this as off" is not "this is off".
    ///
    /// Run over all four flag switches, not just advertising-id, because a
    /// subclass can narrow the read back by overriding ReadsAsOn and one of
    /// them already does. Two switches are left out on purpose:
    /// diagnostic-level, whose override is a threshold, so every number it
    /// can read is either at or above Enhanced (on) or below it (off) and no
    /// third state exists for it to mishandle; and location, whose state is a
    /// word rather than a number — Location_AnythingButDenied_IsAFinding_
    /// AndUndoPutsItBack is the same assertion in its own alphabet, and it
    /// runs an unrecognised word through it.
    [Theory]
    [InlineData("advertising-id")]
    [InlineData("tailored-experiences")]
    [InlineData("speech-typing")]
    [InlineData("activity-history")]
    public void AnUnrecognisedNumber_ReadsAsOn_NotAsProtection(string id)
    {
        var (ctx, reg) = Context();
        var rule = Rule(id);
        // 7 is neither OnValue nor OffValue for any of the four: their pairs
        // are (1, 0) and (0, 1).
        foreach (var v in rule.Values) reg.SetInt(v.KeyPath, v.ValueName, 7);

        Assert.True(rule.IsOn(ctx),
            $"{id}: every value reads 7, which is neither its on number nor " +
            "its off number, and IsOn reported the switch as off");
        Assert.NotNull(rule.Detect(ctx));

        rule.Undo(ctx, rule.Fix(ctx));
        foreach (var v in rule.Values)
            Assert.True(reg.GetInt(v.KeyPath, v.ValueName) == 7,
                $"{id}: {v.KeyPath}\\{v.ValueName} came back as " +
                $"{Read(reg, v)}, not the unrecognised 7 that was there");
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
    /// and the read-back's "written but ignored" state depends on their being
    /// able to disagree. So brisk reads the second one and never writes it: a
    /// fix that wrote the number it later reads back would leave the
    /// read-back comparing brisk's number with itself. Absent reads as null,
    /// never as 0.
    ///
    /// DiagnosticLevelRule.EffectOfTheWrite is what reads this value, and
    /// ReadBackTests plants the two numbers against each other. The Privacy
    /// page's read-back block prints the resulting line — WrittenButIgnored,
    /// the state only this rule can reach, and the one the whole wave was
    /// built around.
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
            "the fix wrote the value it reads back — the read-back would " +
            "then be comparing brisk's number with itself");
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

    private static Dictionary<string, string> EnglishStrings() => Resx("Strings.resx");

    private static Dictionary<string, string> Resx(string fileName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null;
             dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return XDocument
                    .Load(Path.Combine(dir.FullName, "src", "Brisk", "Localization",
                        fileName)).Root!
                    .Elements("data")
                    .ToDictionary(e => (string)e.Attribute("name")!,
                        e => (string)e.Element("value")!);
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }

    /// The whole table, pinned as literals — paths AND numbers. Every other
    /// test in this file reads the rule's own Values collection, on/off
    /// numbers included, so a rule pointed at the wrong key or writing the
    /// wrong number would pass all of them by being consistently wrong. Only
    /// diagnostic-level's 2 and 1 appear as literals anywhere else, in the
    /// Security and Enhanced tests.
    ///
    /// Five rules, six values: location is not here because it has no
    /// RegistryValue to be here with. TheLocationSurface_IsTheOneTheSpecNames
    /// pins its path the only way it can be pinned.
    [Fact]
    public void TheRegistrySurfaces_AreTheOnesTheSpecNames()
    {
        Assert.Equal(
            new[]
            {
                (@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
                    "Enabled", 1, 0),
                (@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                    "AllowTelemetry", 2, 1),
                (@"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy",
                    "TailoredExperiencesWithDiagnosticDataEnabled", 1, 0),
                (@"HKCU\Software\Microsoft\InputPersonalization",
                    "RestrictImplicitTextCollection", 0, 1),
                (@"HKCU\Software\Microsoft\InputPersonalization",
                    "RestrictImplicitInkCollection", 0, 1),
                (@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System",
                    "PublishUserActivities", 1, 0),
                (@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System",
                    "UploadUserActivities", 1, 0),
            },
            Ids.SelectMany(id => Rule(id).Values)
                .Select(v => (v.KeyPath, v.ValueName, v.OnValue, v.OffValue)));
    }

    /// location's row of the same table. Its state is a word, so the pair the
    /// other rules keep as OnValue/OffValue is a single word here: the one
    /// that reads as off, and the one the fix writes, are the same "Deny".
    /// There is no "Allow" literal to pin — the rule never tests for one.
    [Fact]
    public void TheLocationSurface_IsTheOneTheSpecNames()
    {
        // Asserted field by field rather than as one tuple: a tuple comparison
        // elides the middle of a long path and reports two strings that read
        // identically, which is the one thing a path test must not do.
        foreach (var (name, expected, actual) in new[]
                 {
                     ("key path",
                         @"HKCU\Software\Microsoft\Windows\CurrentVersion" +
                         @"\CapabilityAccessManager\ConsentStore\location",
                         LocationRule.KeyPath),
                     ("value name", "Value", LocationRule.ValueName),
                     ("the word that reads as off", "Deny", LocationRule.Denied),
                 })
            Assert.True(expected == actual,
                $"location's {name} is \"{actual}\", not \"{expected}\"");
    }
}
