using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using Xunit;

namespace BriskEngine.Tests;

/// The read-back: brisk looking again at the switches it turned off, and
/// saying what it found. It is a join between two things brisk already has —
/// the journal of fixes it applied, and each switch's own live read — so
/// there is no scheduler here and no store, and nothing below waits for
/// anything.
///
/// The states are not equally knowable across the six switches, and that is
/// what most of this file is about. Four of them are SETTINGS: brisk writes
/// the very value the setting is kept in, so re-reading it answers the
/// question completely. Two are POLICIES under HKLM\SOFTWARE\Policies, and a
/// policy is the one kind of value an edition of Windows can have written
/// down and still not act on. Of those two only diagnostic-level has a second
/// value brisk reads and never writes, so only diagnostic-level can tell an
/// edition that acted on the policy from one that did not. activity-history
/// cannot, and the state it reports says so instead of guessing.
///
/// location is the trap this file was written around. Its state is a WORD, so
/// it carries no RegistryValue at all and its Values collection is empty. A
/// read-back built by walking that collection to ask "is what I wrote still
/// written?" would walk nothing for location, decide nothing, and report the
/// one switch whose loss the user was warned about as one it had checked and
/// found fine. Every theory here runs over location, and
/// Location_IsReadBackThroughItsOwnWord_NotThroughAValuesWalk is the fact
/// that fails first if anybody rebuilds it that way.
public class ReadBackTests
{
    private static readonly string[] Ids =
    {
        "advertising-id", "diagnostic-level",
        "tailored-experiences", "speech-typing",
        "location", "activity-history",
    };

    /// The four that are SETTINGS rather than policies: brisk writes the value
    /// the setting itself is kept in, so nothing sits between the write and
    /// the setting for an edition of Windows to ignore, and Held is the strong
    /// form for all four.
    ///
    /// NOT "what brisk wrote is what is there" — a different claim, and false
    /// for location, which is on this list: it matches its word without regard
    /// to case, so "deny" reads as off where the fix wrote "Deny". What this
    /// list is, is the switches that are not policies.
    /// WhichSwitchesReadAsOff_AtAStateTheirOwnFixDidNotWrite is where the
    /// other question is answered, and it answers it differently.
    private static readonly string[] SettingIds =
        { "advertising-id", "tailored-experiences", "speech-typing", "location" };

    public static TheoryData<string> AllSwitches()
    {
        var data = new TheoryData<string>();
        foreach (var id in Ids) data.Add(id);
        return data;
    }

    public static TheoryData<string> SettingSwitches()
    {
        var data = new TheoryData<string>();
        foreach (var id in SettingIds) data.Add(id);
        return data;
    }

    /// Every switch crossed with the two ways brisk's write can stop being
    /// there: something wrote the on state over it, or something removed it.
    /// Absence reads as on for this whole family, so both are the switch
    /// being back on — and a read-back that handled only the first would miss
    /// the machine where something deleted the key outright.
    public static TheoryData<string, string> AllSwitchesAndHowTheWriteWentAway()
    {
        var data = new TheoryData<string, string>();
        foreach (var id in Ids)
        foreach (var how in new[] { "written back on", "deleted" })
            data.Add(id, how);
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

    /// A fixed stamp, never DateTime.UtcNow: nothing in ReadBack reads a
    /// clock, and a test that fed it one could not prove that.
    private static readonly DateTime FixedAt =
        new(2026, 8, 12, 9, 30, 0, DateTimeKind.Utc);

    private static UndoableFix[] Journal(params string[] ids) =>
        ids.Select(id => new UndoableFix(id, FixedAt)).ToArray();

    private static IReadOnlyList<ReadBackResult> Rows(
        DiagnosticContext ctx, UndoableFix[] journal) =>
        ReadBack.For(ctx, journal, DiagnosticRuleRegistry.All);

    private static ReadBackResult Row(
        DiagnosticContext ctx, UndoableFix[] journal, string id)
    {
        var rows = Rows(ctx, journal);
        var row = rows.FirstOrDefault(
            r => string.Equals(r.RuleId, id, StringComparison.Ordinal));
        Assert.True(row is not null,
            $"the journal carries '{id}' and the read-back produced no row for " +
            $"it — rows: [{Describe(rows)}]");
        return row!;
    }

    private static string Describe(IReadOnlyList<ReadBackResult> rows) =>
        rows.Count == 0 ? "none"
            : string.Join(", ", rows.Select(r => $"{r.RuleId}={r.State}"));

    /// Puts the switch back the way something other than brisk would. The
    /// five numbered switches take their own on number; location takes a
    /// WORD, because it has no number to take — which is the whole reason
    /// this helper is a switch statement and not a loop over Values.
    private static void WriteBackOn(FakeRegistry reg, string id)
    {
        if (id == "location")
        {
            reg.SetString(LocationRule.KeyPath, LocationRule.ValueName, "Allow");
            return;
        }
        foreach (var v in Rule(id).Values) reg.SetInt(v.KeyPath, v.ValueName, v.OnValue);
    }

    private static void Delete(FakeRegistry reg, string id)
    {
        if (id == "location")
        {
            reg.DeleteValue(LocationRule.KeyPath, LocationRule.ValueName);
            return;
        }
        foreach (var v in Rule(id).Values) reg.DeleteValue(v.KeyPath, v.ValueName);
    }

    private static void TakeTheWriteAway(FakeRegistry reg, string id, string how)
    {
        switch (how)
        {
            case "written back on": WriteBackOn(reg, id); break;
            case "deleted": Delete(reg, id); break;
            default:
                throw new ArgumentOutOfRangeException(nameof(how), how,
                    "the two ways this file plants a write that went away");
        }
    }

    // ---- Held --------------------------------------------------------

    /// The quiet line. brisk's own fix, still exactly where it left it, on the
    /// four switches whose value IS the setting: there is nothing further to
    /// read and nothing further to hedge.
    ///
    /// The fix is applied by calling the rule's own Fix rather than by
    /// planting numbers here, so what the read-back re-reads is what brisk
    /// actually writes — location's word included.
    [Theory]
    [MemberData(nameof(SettingSwitches))]
    public void ASettingBriskTurnedOff_AndNothingTouchedSince_ReadsAsHeld(string id)
    {
        var (ctx, _) = Context();
        var rule = Rule(id);
        rule.Fix(ctx);

        Assert.True(rule.Detect(ctx) is null,
            $"{id}: the rule's own fix did not leave the switch reading as off, " +
            "so this test never reached the state it means to check");

        var row = Row(ctx, Journal(id), id);
        Assert.True(row.State == ReadBackState.Held,
            $"{id}: brisk's own write is still there, the switch reads as off, " +
            $"and the read-back says {row.State}");
    }

    // ---- Reverted ----------------------------------------------------

    /// Something put it back. Run over all six and over both ways a write
    /// stops being there — including location, whose word cannot be reached
    /// by any loop over the family's numbers.
    [Theory]
    [MemberData(nameof(AllSwitchesAndHowTheWriteWentAway))]
    public void AFixThatIsNotThereAnyMore_ReadsAsReverted(string id, string how)
    {
        var (ctx, reg) = Context();
        var rule = Rule(id);
        rule.Fix(ctx);
        TakeTheWriteAway(reg, id, how);

        Assert.True(rule.Detect(ctx) is not null,
            $"{id}: the write was {how} and the rule still reads the switch as " +
            "off, so this test never reached the state it means to check");

        var row = Row(ctx, Journal(id), id);
        Assert.True(row.State == ReadBackState.Reverted,
            $"{id}: brisk's write was {how} and the read-back says {row.State}");
    }

    /// The one switch a Values walk cannot see, asserted on its own so the
    /// failure names the hazard rather than showing up as one row of a
    /// theory. LocationRule.Values is empty by design: a read-back that asked
    /// "is what I wrote still written?" by walking it would walk nothing
    /// here, find nothing wrong, and report the switch the user was warned
    /// costs them Find my device as one it had checked.
    [Fact]
    public void Location_IsReadBackThroughItsOwnWord_NotThroughAValuesWalk()
    {
        Assert.True(new LocationRule().Values.Count == 0,
            "LocationRule grew a RegistryValue — this test's premise is that it " +
            "has none, and the read-back's cover for it rests on that");

        var (heldCtx, _) = Context();
        new LocationRule().Fix(heldCtx);
        Assert.True(Row(heldCtx, Journal("location"), "location").State
                == ReadBackState.Held,
            "location's consent reads as denied and the read-back did not say held");

        var (backCtx, backReg) = Context();
        new LocationRule().Fix(backCtx);
        backReg.SetString(LocationRule.KeyPath, LocationRule.ValueName, "Allow");
        var row = Row(backCtx, Journal("location"), "location");
        Assert.True(row.State == ReadBackState.Reverted,
            $"location's consent reads Allow again and the read-back says {row.State} " +
            "— a read-back that decided by walking LocationRule.Values would walk " +
            "an empty collection and answer exactly this way");
    }

    // ---- Written but ignored -----------------------------------------

    /// The state this whole wave is for: brisk reporting that its own fix did
    /// not take. The policy brisk wrote is still there — the switch reads as
    /// off — and the second value this machine keeps for the same setting
    /// still records a level above it. What brisk measured is two values that
    /// disagree; what it says is that this edition is not acting on the
    /// policy. It says nothing about what any company receives, because it
    /// read nothing about that.
    [Fact]
    public void DiagnosticLevel_PolicyWritten_ButTheMachineRecordsAHigherLevel_ReadsAsIgnored()
    {
        var (ctx, reg) = Context();
        reg.SetInt(DiagnosticLevelRule.KeyPath, DiagnosticLevelRule.ValueName, 1);
        reg.SetInt(DiagnosticLevelRule.EffectiveKeyPath,
            DiagnosticLevelRule.ValueName, 3);

        Assert.True(new DiagnosticLevelRule().Detect(ctx) is null,
            "the policy reads as held at basic and the rule still reports a " +
            "finding — the premise of this test is that the switch reads off");

        var row = Row(ctx, Journal("diagnostic-level"), "diagnostic-level");
        Assert.True(row.State == ReadBackState.WrittenButIgnored,
            "the policy says basic, the level this machine records says 3, and " +
            $"the read-back says {row.State}");
    }

    /// The same two values agreeing. This is the strongest held brisk can
    /// produce for a policy: it wrote one value, and a second value it never
    /// writes says the machine is where the policy asks.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DiagnosticLevel_PolicyWritten_AndTheRecordedLevelAgrees_ReadsAsHeld(
        int recorded)
    {
        var (ctx, reg) = Context();
        reg.SetInt(DiagnosticLevelRule.KeyPath, DiagnosticLevelRule.ValueName, 1);
        reg.SetInt(DiagnosticLevelRule.EffectiveKeyPath,
            DiagnosticLevelRule.ValueName, recorded);

        var row = Row(ctx, Journal("diagnostic-level"), "diagnostic-level");
        Assert.True(row.State == ReadBackState.Held,
            $"the policy says basic, the recorded level says {recorded}, which is " +
            $"not above it, and the read-back says {row.State}");
    }

    // ---- Written but unverified --------------------------------------

    /// The second value absent — or there and unreadable, which brisk cannot
    /// tell apart from absent. The policy brisk wrote is still there and
    /// nothing brisk can read says whether this edition acts on it, so it
    /// reports exactly that and does not borrow held.
    [Fact]
    public void DiagnosticLevel_PolicyWritten_AndNoSecondValueToRead_ReadsAsUnverified()
    {
        var (ctx, reg) = Context();
        reg.SetInt(DiagnosticLevelRule.KeyPath, DiagnosticLevelRule.ValueName, 1);

        Assert.True(new DiagnosticLevelRule().EffectiveLevel(ctx) is null,
            "something is written at the second key, so this test is not the " +
            "unreadable case it says it is");

        var row = Row(ctx, Journal("diagnostic-level"), "diagnostic-level");
        Assert.True(row.State == ReadBackState.WrittenButUnverified,
            "brisk has no second reading for this machine's diagnostic data " +
            $"level and the read-back says {row.State}");
    }

    /// activity-history, and the honest gap this wave chose over inventing a
    /// path. Its policy is written, the switch reads as off, and brisk has NO
    /// second value for it — Task 3 declined to name one it could not vouch
    /// for, precisely so that this state would not be built on a read that
    /// means nothing. So the read-back reports it as written and unverified
    /// on every machine, and never as held.
    ///
    /// Held here would be the exact failure the wave exists to refuse: a Home
    /// machine where the policy is ignored and Timeline is still running,
    /// being told the switch it paid for is still off.
    [Fact]
    public void ActivityHistory_PolicyWritten_ReadsAsUnverified_AndNeverAsHeld()
    {
        var (ctx, _) = Context();
        var rule = new ActivityHistoryRule();
        rule.Fix(ctx);

        Assert.True(rule.Detect(ctx) is null,
            "the fix did not leave the policy reading as off, so this test never " +
            "reached the state it means to check");

        var row = Row(ctx, Journal("activity-history"), "activity-history");
        Assert.True(row.State == ReadBackState.WrittenButUnverified,
            "brisk has no second value for activity history and the read-back " +
            $"says {row.State}");
    }

    /// What brisk can find out about its own write, per switch, on a machine
    /// where nothing but the fix has been written. This table is the answer
    /// to "are the states equally knowable across the six" — they are not,
    /// and this is where that is written down rather than described.
    [Theory]
    [InlineData("advertising-id", WriteEffect.NotAPolicy)]
    [InlineData("tailored-experiences", WriteEffect.NotAPolicy)]
    [InlineData("speech-typing", WriteEffect.NotAPolicy)]
    [InlineData("location", WriteEffect.NotAPolicy)]
    [InlineData("diagnostic-level", WriteEffect.Unread)]
    [InlineData("activity-history", WriteEffect.Unread)]
    public void WhatBriskCanTellAboutItsOwnWrite_PerSwitch(string id, WriteEffect expected)
    {
        var (ctx, _) = Context();
        var rule = Rule(id);
        rule.Fix(ctx);

        Assert.True(rule.EffectOfTheWrite(ctx) == expected,
            $"{id}: brisk reports {rule.EffectOfTheWrite(ctx)} about its own write " +
            $"where the shipped answer is {expected}");
    }

    /// activity-history answers Unread whatever is on the machine, because
    /// there is no second value to make it answer anything else. Planted with
    /// the policy at four different states; the answer does not move.
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void ActivityHistory_AnswersUnread_WhateverIsOnTheMachine(int? planted)
    {
        var (ctx, reg) = Context();
        if (planted is not null)
            foreach (var v in new ActivityHistoryRule().Values)
                reg.SetInt(v.KeyPath, v.ValueName, planted.Value);

        Assert.True(new ActivityHistoryRule().EffectOfTheWrite(ctx) == WriteEffect.Unread,
            "activity-history reported something other than Unread about its own " +
            "write, and no second value exists on this machine for it to have read");
    }

    /// The line between the two answers is not a taste: it is whether the
    /// value brisk writes sits under a Policies key, which is the one kind of
    /// value an edition of Windows can have recorded and still not act on.
    /// Asserted from the paths themselves, so a seventh switch cannot be
    /// classified by whoever adds it and be wrong.
    ///
    /// location is named rather than walked, for the reason this whole file
    /// exists: it has no RegistryValue for a loop to find.
    [Theory]
    [MemberData(nameof(AllSwitches))]
    public void OnlyTheSwitchesUnderAPoliciesKey_ReportAPolicyAtAll(string id)
    {
        var (ctx, _) = Context();
        var rule = Rule(id);
        var paths = id == "location"
            ? new[] { LocationRule.KeyPath }
            : rule.Values.Select(v => v.KeyPath).ToArray();

        Assert.True(paths.Length > 0,
            $"{id}: no registry path was found for this switch, so this test " +
            "checked nothing at all");

        var underPolicies = paths.Any(p =>
            p.Contains(@"\Policies\", StringComparison.OrdinalIgnoreCase));
        var reportsAPolicy = rule.EffectOfTheWrite(ctx) != WriteEffect.NotAPolicy;

        Assert.True(underPolicies == reportsAPolicy,
            $"{id}: its values live at [{string.Join(", ", paths)}], which " +
            $"{(underPolicies ? "is" : "is not")} under a Policies key, and it " +
            $"reports {rule.EffectOfTheWrite(ctx)}");
    }

    // ---- What the read-back does not speak about ---------------------

    /// The journal carries every fix brisk has applied, not only the privacy
    /// switches. brisk has no re-read of a power plan that would mean any of
    /// the four states, so it produces no row for it — an absence, not a
    /// claim. The control in the same journal proves the call was not simply
    /// returning nothing.
    [Fact]
    public void AJournalledFixThatIsNotATelemetrySwitch_GetsNoRow()
    {
        var (ctx, _) = Context();
        new AdvertisingIdRule().Fix(ctx);
        var rows = Rows(ctx, Journal("power-plan", "advertising-id"));

        Assert.True(rows.All(r => r.RuleId != "power-plan"),
            "the read-back spoke about power-plan, which it cannot re-read: " +
            $"[{Describe(rows)}]");
        Assert.True(rows.Any(r => r.RuleId == "advertising-id"),
            "the control switch got no row either, so this test proves nothing: " +
            $"[{Describe(rows)}]");
    }

    /// A journal written by an older build can name a rule this one does not
    /// ship. brisk cannot re-read what it cannot find, so it says nothing.
    [Fact]
    public void AJournalledIdNoShippedRuleCarries_GetsNoRow()
    {
        var (ctx, _) = Context();
        var rows = Rows(ctx, Journal("a-rule-this-build-does-not-ship"));

        Assert.True(rows.Count == 0,
            $"the read-back produced a row for an id no rule carries: [{Describe(rows)}]");
    }

    /// The read-back speaks about fixes brisk applied and nothing else. A
    /// switch that is ON and was never journalled is a finding for the rule
    /// to report, not something brisk turned off and should now check on.
    [Theory]
    [MemberData(nameof(AllSwitches))]
    public void ASwitchBriskNeverFixed_GetsNoRow(string id)
    {
        var (ctx, _) = Context();

        Assert.True(Rule(id).Detect(ctx) is not null,
            $"{id}: an untouched machine did not read as on, so this test is not " +
            "the never-fixed case it says it is");

        var rows = Rows(ctx, Array.Empty<UndoableFix>());
        Assert.True(rows.Count == 0,
            $"the journal is empty and the read-back produced [{Describe(rows)}]");
    }

    // ---- The shape of what comes back --------------------------------

    /// One row per journal entry it can re-read, in the journal's own order,
    /// with the journal's own stamp. ListUndoable already collapses a rule's
    /// history to its last undoable fix and orders what it returns; this adds
    /// no second opinion about either.
    [Fact]
    public void TheRows_FollowTheJournalsOwnOrder_AndCarryItsOwnStamp()
    {
        var (ctx, _) = Context();
        var journal = new[]
        {
            new UndoableFix("speech-typing",
                new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc)),
            new UndoableFix("location",
                new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc)),
            new UndoableFix("advertising-id",
                new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)),
        };

        var rows = Rows(ctx, journal);

        Assert.Equal(journal.Select(j => j.RuleId), rows.Select(r => r.RuleId));
        Assert.Equal(journal.Select(j => j.FixedAtUtc), rows.Select(r => r.FixedAtUtc));
    }

    /// Nothing in the read-back reads a clock. A stamp in the future is
    /// carried through exactly as the journal has it, and the state is decided
    /// by the registry alone — "23 days ago" is arithmetic for whatever
    /// renders the line. ReadBackRow is what renders it, and what a future
    /// stamp does on SCREEN is settled there rather than here:
    /// AStampInTheFuture_RendersAsToday_RatherThanAsANegative.
    [Fact]
    public void AStampInTheFuture_IsCarriedThrough_BecauseNothingHereReadsAClock()
    {
        var (ctx, _) = Context();
        new AdvertisingIdRule().Fix(ctx);
        var stamp = new DateTime(2999, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var row = Row(ctx, new[] { new UndoableFix("advertising-id", stamp) },
            "advertising-id");

        Assert.Equal(stamp, row.FixedAtUtc);
        Assert.True(row.State == ReadBackState.Held,
            $"the stamp moved the state to {row.State}; the registry decides it");
    }

    /// Rule ids reach this from a journal file, and the journal outlives the
    /// build that wrote it. The app's privacy list matches ids without regard
    /// to case for the same reason, and the row hands back the rule's own id
    /// rather than whatever spelling the journal had.
    [Fact]
    public void AJournalledIdInAnotherCase_StillMatches_AndTheRowCarriesTheRulesOwnId()
    {
        var (ctx, _) = Context();
        new AdvertisingIdRule().Fix(ctx);

        var rows = Rows(ctx, Journal("Advertising-ID"));

        Assert.True(rows.Count == 1,
            $"a journal entry spelled Advertising-ID produced [{Describe(rows)}]");
        Assert.Equal("advertising-id", rows[0].RuleId);
    }

    /// This feature IS a closed set of states, which is the one shape this
    /// branch has learned to distrust. So the set is checked against machines
    /// rather than described: every member of the enum has to be reachable by
    /// something plantable here, and a member added without a machine that
    /// reaches it fails on the next run.
    [Fact]
    public void EveryStateTheEnumCarries_IsOneSomeMachineActuallyReaches()
    {
        var reached = new List<ReadBackState>();

        var (held, _) = Context();
        new AdvertisingIdRule().Fix(held);
        reached.Add(Row(held, Journal("advertising-id"), "advertising-id").State);

        var (reverted, revertedReg) = Context();
        new LocationRule().Fix(reverted);
        revertedReg.SetString(LocationRule.KeyPath, LocationRule.ValueName, "Allow");
        reached.Add(Row(reverted, Journal("location"), "location").State);

        var (ignored, ignoredReg) = Context();
        ignoredReg.SetInt(DiagnosticLevelRule.KeyPath, DiagnosticLevelRule.ValueName, 1);
        ignoredReg.SetInt(DiagnosticLevelRule.EffectiveKeyPath,
            DiagnosticLevelRule.ValueName, 3);
        reached.Add(Row(ignored, Journal("diagnostic-level"), "diagnostic-level").State);

        var (unverified, _) = Context();
        new ActivityHistoryRule().Fix(unverified);
        reached.Add(Row(unverified, Journal("activity-history"), "activity-history").State);

        var missing = Enum.GetValues<ReadBackState>().Except(reached).ToArray();
        Assert.True(missing.Length == 0,
            $"nothing in this file reaches {string.Join(", ", missing)} — a state " +
            "no machine can produce is a sentence brisk would never print");
    }

    // ---- The sentences -----------------------------------------------

    /// The four sentences, in both languages, keyed the way the wave keys
    /// everything else. The Privacy page's read-back block renders all four,
    /// one per ReadBackState. This file reads them off disk to pin what they
    /// SAY; that the renderer hands each one the argument it takes is a
    /// different claim and is pinned on the other side, by
    /// PrivacyViewModelTests.EachRow_RendersItsOwnSentenceWithItsOwnArgument.
    [Theory]
    [InlineData("readback.held")]
    [InlineData("readback.reverted")]
    [InlineData("readback.ignored")]
    [InlineData("readback.unverified")]
    public void EachSentence_IsInBothResxFiles(string key)
    {
        foreach (var file in new[] { "Strings.resx", "Strings.tr.resx" })
        {
            Assert.True(Resx(file).TryGetValue(key, out var text),
                $"{key} is missing from {file}");
            Assert.False(string.IsNullOrWhiteSpace(text),
                $"{key} is empty in {file}");
        }
    }

    /// The Turkish wording, pinned as literals because the wording IS the
    /// requirement here — these are the sentences the wave was planned
    /// around. Contains rather than Equal, so a sentence may carry the
    /// evidence for itself after the pinned clause without failing.
    [Theory]
    [InlineData("readback.held", "{0} gün önce kapattın, hâlâ kapalı")]
    [InlineData("readback.reverted",
        "Bunu {0} tarihinde kapatmıştın; şu an yeniden açık.")]
    [InlineData("readback.ignored",
        "Ayar kapalı yazıyor ama bu Windows sürümü onu dikkate almıyor.")]
    public void TheTurkishSentences_AreTheOnesThePlanNames(string key, string sentence)
    {
        Assert.True(
            Resx("Strings.tr.resx")[key].Contains(sentence, StringComparison.Ordinal),
            $"{key} in Strings.tr.resx never says \"{sentence}\"");
    }

    /// The fourth sentence is the one this task added, and it does not invent
    /// its own admission: it closes with the clause both policy rules already
    /// close with, word for word, so one grep finds every place brisk admits
    /// it cannot see whether an edition acted on a policy.
    [Theory]
    [InlineData("Strings.resx", "rule.diagnostic-level.advice",
        "whether this edition of Windows acts on that policy is not something " +
        "that read can tell you")]
    [InlineData("Strings.tr.resx", "rule.diagnostic-level.advice",
        "bu Windows sürümünün o ilkeye uyup uymadığını bu okuma söyleyemez")]
    public void TheUnverifiedSentence_ClosesWithTheAdmissionTheRulesAlreadyMake(
        string file, string neighbourKey, string clause)
    {
        var strings = Resx(file);
        Assert.True(strings[neighbourKey].Contains(clause, StringComparison.Ordinal),
            $"{neighbourKey} in {file} no longer carries the clause this test " +
            "matches the read-back against, so the two have drifted apart");
        Assert.True(strings["readback.unverified"].Contains(clause,
                StringComparison.Ordinal),
            $"readback.unverified in {file} never says \"{clause}\"");
    }

    /// The arguments each sentence takes, pinned because they differ and a
    /// renderer handed the wrong one prints nonsense: held and unverified take
    /// a NUMBER OF DAYS, reverted takes a DATE, and ignored takes nothing at
    /// all — brisk has no date for when an edition stopped acting on a policy,
    /// and does not leave a hole where one would go.
    [Theory]
    [InlineData("readback.held", true)]
    [InlineData("readback.reverted", true)]
    [InlineData("readback.unverified", true)]
    [InlineData("readback.ignored", false)]
    public void EachSentence_TakesTheArgumentItsRendererWillHaveToPass(
        string key, bool takesOne)
    {
        foreach (var file in new[] { "Strings.resx", "Strings.tr.resx" })
        {
            var text = Resx(file)[key];
            Assert.True(text.Contains("{0}", StringComparison.Ordinal) == takesOne,
                $"{key} in {file} " +
                (takesOne ? "takes no argument and its renderer has one to pass"
                          : "takes an argument no caller has one for"));
            Assert.False(text.Contains("{1}", StringComparison.Ordinal),
                $"{key} in {file} takes a second argument no caller has one for");
        }
    }

    /// The red line, over this task's own four keys. brisk measured a local
    /// policy and a local setting; it measured nothing about what any company
    /// receives, and the read-back is exactly where that claim would be
    /// tempting to make. A wider version of this over every privacy string is
    /// a later task of this wave; this one covers what this task added.
    ///
    /// The Turkish entries are written with the real letters — göremez, not
    /// goremez — because a banned phrase spelled in ASCII can never match the
    /// text it is meant to ban, and a list like that returns zero for the
    /// wrong reason. TheBannedPhraseCheck_ActuallyReachesTheTurkishFile is the
    /// control that proves this list is matched against real text.
    [Theory]
    [InlineData("readback.held")]
    [InlineData("readback.reverted")]
    [InlineData("readback.ignored")]
    [InlineData("readback.unverified")]
    public void NoSentence_ClaimsAnythingAboutWhatLeavesTheMachine(string key)
    {
        string[] banned =
        {
            "Microsoft", "no longer see", "cannot see", "can't see",
            "stops sending", "leaves your machine", "leaving your machine",
            "göremez", "görmüyor", "gitmiyor", "gönderilmiyor",
        };

        foreach (var file in new[] { "Strings.resx", "Strings.tr.resx" })
        {
            var text = Resx(file)[key];
            foreach (var phrase in banned)
                Assert.False(text.Contains(phrase, StringComparison.OrdinalIgnoreCase),
                    $"{key} in {file} says \"{phrase}\" — brisk read a setting on " +
                    "this machine and nothing about what any company receives");
        }
    }

    /// The control for the theory above: its banned list is matched against
    /// real text, so a Turkish word with a Turkish letter in it has to be
    /// findable in the same file by the same comparison. Without this, a list
    /// that could never match anything would look exactly like a clean one.
    [Fact]
    public void TheBannedPhraseCheck_ActuallyReachesTheTurkishFile()
    {
        Assert.True(Resx("Strings.tr.resx")["readback.unverified"]
                .Contains("söyleyemez", StringComparison.OrdinalIgnoreCase),
            "the Turkish read-back sentence does not contain the Turkish word " +
            "this control looks for, so the banned-phrase theory above may be " +
            "matching nothing at all");
    }

    /// The machine the unverified sentence used to be wrong about, and it is
    /// reachable rather than theoretical. brisk journalled its fix at
    /// diagnostic-level; something else later wrote AllowTelemetry 0 —
    /// Security, STRICTER than the 1 brisk writes, and exactly what another
    /// debloat tool would set — and the second value cannot be read.
    ///
    /// The state is right: the switch reads as off and brisk has no reading
    /// that says whether this edition acts on the policy. What is NOT true on
    /// this machine is that the value brisk wrote is still there. It is 0,
    /// and brisk writes 1.
    ///
    /// Only diagnostic-level can reach this STATE, and the reason is its
    /// read: a THRESHOLD, so any number below Enhanced reads as off, brisk's
    /// or not. It is not the only rule whose read and whose write come apart,
    /// though — location matches its word without regard to case, so "deny"
    /// reads as off where the fix wrote "Deny" — and
    /// WhichSwitchesReadAsOff_AtAStateTheirOwnFixDidNotWrite is where both
    /// are pinned. FOUR of the six compare against the exact value their own
    /// fix writes, and for those four "reads as off" and "my write is still
    /// there" coincide, which is why a sentence that named brisk's write
    /// passed every test in this file until this one.
    [Fact]
    public void DiagnosticLevel_AStricterLevelThanBriskWrote_ReadsAsUnverified_AndTheSentenceDoesNotSayBriskWroteIt()
    {
        var (ctx, reg) = Context();
        var rule = new DiagnosticLevelRule();
        var written = rule.Values.Single().OffValue;
        const int stricterThanBriskWrites = 0;
        reg.SetInt(DiagnosticLevelRule.KeyPath, DiagnosticLevelRule.ValueName,
            stricterThanBriskWrites);

        Assert.True(stricterThanBriskWrites != written,
            $"this test plants {stricterThanBriskWrites} to be a number brisk's " +
            $"fix does not write, and the fix writes {written}");
        Assert.True(rule.EffectiveLevel(ctx) is null,
            "something is readable at the second key, so this is not the " +
            "unverified case it says it is");

        var row = Row(ctx, Journal("diagnostic-level"), "diagnostic-level");
        Assert.True(row.State == ReadBackState.WrittenButUnverified,
            $"the policy reads {stricterThanBriskWrites}, which is not on and " +
            $"is not brisk's {written} either, and the read-back says {row.State}");

        foreach (var (file, clause) in new[]
                 {
                     ("Strings.resx", "the value brisk wrote is still there"),
                     ("Strings.tr.resx", "brisk'in yazdığı değer hâlâ yerinde"),
                 })
            Assert.False(Resx(file)["readback.unverified"]
                    .Contains(clause, StringComparison.OrdinalIgnoreCase),
                $"readback.unverified in {file} says \"{clause}\" — on the " +
                "machine planted above that is false, and what brisk read is " +
                "that the switch still reads as off, not whose write is there");
    }

    /// The structural fact every "reads as off" sentence in this family now
    /// rests on, pinned so it cannot drift back into an assumption.
    ///
    /// "The switch reads as off" and "the value brisk wrote is the value
    /// there" are the same statement only for a rule that keeps BOTH default
    /// reads — the default IsOn, which walks Values, and the default
    /// ReadsAsOn, which treats exactly OffValue as off. One rule has replaced
    /// each, and they are not the same rule:
    ///
    ///   diagnostic-level  overrides READSASON with a threshold, so 0
    ///                     (Security) reads as off while the fix writes 1
    ///   location          keeps the default ReadsAsOn and parts them anyway:
    ///                     it overrides ISON to match a word without regard to
    ///                     case, so "deny" reads as off while the fix wrote
    ///                     "Deny", and its Values is empty
    ///
    /// So "did it keep ReadsAsOn?" is the WRONG question — it answers "safe"
    /// for location. The question is whether it kept both.
    ///
    /// WHAT THIS THEORY GIVES AND WHAT IT DOES NOT. It runs over every
    /// TelemetrySwitchRule DiagnosticRuleRegistry ships, not over a list kept
    /// here, so a seventh switch arrives as a row with no recorded answer and
    /// fails until somebody works one out. And a switch that NONE of this
    /// file's planted states could make read as off fails too, rather than
    /// reporting "no witness" for a rule the search never got into the state
    /// it is about. That is decided by the rule's own read rather than by a
    /// property of this file's writes: it used to be the proxy
    /// `Values.Count == 0`, and a word-valued rule shipping a NON-EMPTY
    /// Values — one override away from the shape location already is —
    /// slipped past the proxy into the numeric branch and was recorded as a
    /// silent false.
    ///
    /// What it does NOT do is prove a rule cannot part them. The search is
    /// BOUNDED — six candidate numbers, six candidate words — so a recorded
    /// false means "none among the states this file plants", never "none
    /// exists". A rule whose off-reading set were 0 and 42 would be missed.
    [Theory]
    [MemberData(nameof(EverySwitchThisBuildShips))]
    public void WhichSwitchesReadAsOff_AtAStateTheirOwnFixDidNotWrite(string id)
    {
        var rule = ShippedSwitch(id);

        Assert.True(DivergesFromItsOwnWrite.TryGetValue(id, out var recorded),
            $"'{id}' is a telemetry switch this build ships and nothing here " +
            "records whether it can read as off at a state its own fix did not " +
            "write. Read TelemetrySwitchRule.EffectOfTheWrite's contract, work " +
            "the answer out for this rule, and add a row — an unrecorded switch " +
            "is one the read-back's documented criterion has never been checked " +
            "against.");

        var (outcome, witness) =
            SearchForAStateThatReadsAsOffWithoutBeingBrisksWrite(rule);

        Assert.True(outcome != StateSearch.NoPlantedStateEverReadAsOff,
            $"'{id}': nothing this file planted ever made this rule read as off, " +
            "so the search never reached the state it exists to ask about. " +
            "Either this file cannot write the state this rule reads — add a " +
            "fork beside the LocationRule one — or the state it does read as " +
            "off sits outside the bounded candidates here. Reporting no " +
            "witness for a rule that was never seen reading as off is the " +
            "silent answer this test exists to refuse.");

        var found = outcome == StateSearch.FoundOne;
        Assert.True(found == recorded, found
            ? $"{id}: {witness} reads as off and is not what the fix writes, so " +
              "\"reads as off\" and \"my write is still there\" have come apart " +
              "for a rule recorded as keeping them together"
            : $"{id}: no state this file can plant reads as off without being " +
              "what the fix writes, and this rule is recorded as having one — " +
              "the docs that say \"the switch reads as off\" rather than " +
              "\"brisk's write is there\" were written for it");
    }

    /// Every telemetry switch the BUILD ships, read from the registry rather
    /// than from a list in this file. That is the whole point: a seventh
    /// switch appears here on its own and fails the theory above until
    /// somebody records what it does.
    public static TheoryData<string> EverySwitchThisBuildShips()
    {
        var data = new TheoryData<string>();
        foreach (var rule in DiagnosticRuleRegistry.All.OfType<TelemetrySwitchRule>())
            data.Add(rule.Id);
        return data;
    }

    private static TelemetrySwitchRule ShippedSwitch(string id) =>
        DiagnosticRuleRegistry.All.OfType<TelemetrySwitchRule>()
            .Single(r => string.Equals(r.Id, id, StringComparison.Ordinal));

    /// What this file has ESTABLISHED, per switch, by looking. Not a
    /// description of the family and not a substitute for the search — the
    /// theory runs the search and compares against this — but the record that
    /// somebody worked the answer out for that rule and wrote it down. An id
    /// shipped without a row here fails rather than being assumed either way.
    private static readonly Dictionary<string, bool> DivergesFromItsOwnWrite =
        new(StringComparer.Ordinal)
        {
            ["advertising-id"] = false,
            ["tailored-experiences"] = false,
            ["speech-typing"] = false,
            ["activity-history"] = false,
            // Overrides ReadsAsOn with a threshold.
            ["diagnostic-level"] = true,
            // Overrides IsOn and matches its word without regard to case.
            ["location"] = true,
        };

    /// Three outcomes, and the third is the one that matters: a rule the
    /// search never got INTO the state it is about is NOT a rule with no
    /// witness. Collapsing those two into one null is exactly how a future
    /// word-valued rule would be waved through, so they are separate answers
    /// and the caller fails on the third.
    private enum StateSearch
    {
        FoundOne,
        NoneInTheStatesThisFileCanPlant,
        NoPlantedStateEverReadAsOff,
    }

    /// Looks for a state that reads as off while NOT being what the rule's fix
    /// writes. Forked on LocationRule by TYPE rather than by id, so a rename
    /// cannot quietly send it down the numeric branch.
    ///
    /// The third outcome is decided by WHAT THE RULE READ, not by what this
    /// file wrote. Every branch records whether a state it planted actually
    /// made the rule read as off; if none ever did, the search never reached
    /// the state it exists to ask about, and it says so instead of answering
    /// "no witness". That used to be decided on the proxy `Values.Count == 0`,
    /// and the proxy is not the property: a word-valued rule shipping a
    /// NON-EMPTY Values takes the numeric branch, gets six SetInts it never
    /// reads, never once reads as off, and was recorded as a silent false.
    /// That shape was planted and watched pass before this was changed.
    ///
    /// What the third outcome now covers is therefore WIDER than "this file
    /// cannot write this rule's state", and the failure message says both
    /// halves: a rule whose state this file cannot write at all lands here,
    /// and so does one whose off-reading state simply sits outside the
    /// bounded candidates below.
    private static (StateSearch Outcome, string? Witness)
        SearchForAStateThatReadsAsOffWithoutBeingBrisksWrite(TelemetrySwitchRule rule)
    {
        var everReadAsOff = false;

        if (rule is LocationRule)
        {
            foreach (var word in new[] { "deny", "DENY", "dEnY", "Allow", "Prompt", "" })
            {
                var (ctx, reg) = Context();
                reg.SetString(LocationRule.KeyPath, LocationRule.ValueName, word);
                if (rule.IsOn(ctx)) continue;
                everReadAsOff = true;
                if (!string.Equals(word, LocationRule.Denied, StringComparison.Ordinal))
                    return (StateSearch.FoundOne, $"the word \"{word}\"");
            }
            return (Outcome(everReadAsOff), null);
        }

        foreach (var candidate in new[] { -1, 0, 1, 2, 3, 7 })
        {
            var (ctx, reg) = Context();
            foreach (var v in rule.Values) reg.SetInt(v.KeyPath, v.ValueName, candidate);
            if (rule.IsOn(ctx)) continue;
            everReadAsOff = true;
            if (rule.Values.Any(v => v.OffValue != candidate))
                return (StateSearch.FoundOne, $"the number {candidate}");
        }
        return (Outcome(everReadAsOff), null);
    }

    /// The two answers a search that found no witness can honestly give,
    /// told apart by whether the rule was ever seen reading as off at all.
    private static StateSearch Outcome(bool everReadAsOff) => everReadAsOff
        ? StateSearch.NoneInTheStatesThisFileCanPlant
        : StateSearch.NoPlantedStateEverReadAsOff;

    /// The positive half of the correction above: the sentence names the read
    /// brisk made. Pinned as a literal for the same reason the other three
    /// are — here the wording IS the requirement, because the wording is what
    /// was wrong.
    [Theory]
    [InlineData("Strings.resx", "the switch still reads as off")]
    [InlineData("Strings.tr.resx", "ayar hâlâ kapalı okunuyor")]
    public void TheUnverifiedSentence_NamesTheReadItMade(string file, string clause)
    {
        Assert.True(Resx(file)["readback.unverified"]
                .Contains(clause, StringComparison.Ordinal),
            $"readback.unverified in {file} never says \"{clause}\", so it does " +
            "not say what brisk actually read");
    }

    /// "Still" is a claim about a previous look. brisk has one for its OWN
    /// write — the journal records that it turned the switch off — and none
    /// at all for the second value, which it reads for the first time on the
    /// scan that produces this sentence. So the ignored sentence reports what
    /// that value reads and does not say it has read it before.
    ///
    /// The held and unverified sentences DO say "still", and legitimately:
    /// what they say it about is brisk's own act, which the journal backs.
    [Theory]
    [InlineData("Strings.resx", "still reads as on")]
    [InlineData("Strings.tr.resx", "hâlâ açık diyor")]
    public void TheIgnoredSentence_DoesNotClaimAPriorReadingOfTheSecondValue(
        string file, string clause)
    {
        Assert.False(Resx(file)["readback.ignored"]
                .Contains(clause, StringComparison.OrdinalIgnoreCase),
            $"readback.ignored in {file} says \"{clause}\" — brisk read that " +
            "value once, on this scan, and has no earlier reading of it to " +
            "call this one a continuation of");
    }

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
}
