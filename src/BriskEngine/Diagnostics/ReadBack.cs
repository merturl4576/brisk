using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Diagnostics.Rules.Privacy;

namespace BriskEngine.Diagnostics;

/// What brisk found when it looked again at the switches it turned off.
///
/// There is no scheduler here, no background service and no store, and that
/// is a decision rather than an omission: the read-back rides the scan the
/// user already runs. FixRunner.Apply journals every fix it applies and is
/// the only thing in brisk that applies one — the GUI and the CLI both reach
/// a rule's Fix through it — and every telemetry switch re-reads its own
/// state. This is the join between the two.
///
/// Nothing here reads a clock either. FixedAtUtc is carried through from the
/// journal untouched, so "23 days ago" is arithmetic for whatever renders the
/// line — and nothing renders one yet. The Privacy page that will show these
/// rows is a later task of this wave; until it lands, For is called by its
/// tests and by nothing else.
///
/// WHAT IT DELIBERATELY DOES NOT DO: it does not walk a rule's Values
/// collection. That was the obvious way to ask "is what I wrote still
/// written?", and it is a trap — LocationRule carries no RegistryValue at
/// all, because its state is a word rather than a number, so a Values walk
/// answers "nothing wrong here" for the one switch the user was warned costs
/// them Find my device. Every question below goes through a rule's own
/// virtual reads instead, which location overrides, so location is answered
/// by the same code path as the other five and by the same code path a
/// seventh switch would get.
public static class ReadBack
{
    /// One row per journal entry brisk can re-read, in the order the journal
    /// hands them over, carrying the journal's own stamp.
    ///
    /// A journal entry gets NO row when nothing in `rules` matches its id, and
    /// when what matches is not a telemetry switch — the journal records
    /// fixes against rules of every kind, and none of the states below would
    /// mean anything about a power plan. That is an absence rather than a claim:
    /// the read-back speaks about what it re-read and about nothing else.
    ///
    /// Ids are matched without regard to case, because a journal file
    /// outlives the build that wrote it; the app's privacy list matches ids
    /// the same way, which PrivacyRuleIds_MatchWithoutRegardToCase pins from
    /// the project that can see it. The row carries the RULE's id, not the
    /// journal's spelling of it, so whatever routes on it gets the canonical
    /// one.
    public static IReadOnlyList<ReadBackResult> For(
        DiagnosticContext ctx,
        IReadOnlyList<UndoableFix> journal,
        IReadOnlyList<IDiagnosticRule> rules)
    {
        var rows = new List<ReadBackResult>();
        foreach (var entry in journal)
        {
            var rule = rules.OfType<TelemetrySwitchRule>().FirstOrDefault(
                r => string.Equals(r.Id, entry.RuleId, StringComparison.OrdinalIgnoreCase));
            if (rule is null) continue;
            rows.Add(new ReadBackResult(rule.Id, StateOf(ctx, rule), entry.FixedAtUtc));
        }
        return rows;
    }

    /// The two reads, in the order that keeps each one inside what it
    /// measured.
    ///
    /// FIRST: does the switch still read as off? That is the rule's own live
    /// read — the same one that decides whether it reports a finding — so a
    /// switch that is back on is exactly a switch brisk is reporting again,
    /// and the two surfaces cannot disagree. It reads as on, so something
    /// took brisk's write away: Reverted. brisk does not say what did, or
    /// when, because it read neither.
    ///
    /// SECOND, and only once the switch reads as off: is there anything else
    /// brisk can read that says this edition of Windows is not acting on what
    /// was written? Only a policy can be written and not acted on; two of the
    /// six switches are policies, and only one of those two has a second
    /// value brisk can read for it. That question belongs to the rule, which
    /// is the only thing that knows its own second value, so it is asked of
    /// the rule.
    private static ReadBackState StateOf(DiagnosticContext ctx, TelemetrySwitchRule rule)
    {
        if (rule.IsOn(ctx)) return ReadBackState.Reverted;

        return rule.EffectOfTheWrite(ctx) switch
        {
            // Not a policy: brisk wrote the very value the setting is kept
            // in, so re-reading it is the whole answer and there is nothing
            // left to hedge.
            WriteEffect.NotAPolicy => ReadBackState.Held,
            // A policy, and a second value brisk never writes agrees with it.
            WriteEffect.ActedOn => ReadBackState.Held,
            WriteEffect.Ignored => ReadBackState.WrittenButIgnored,
            WriteEffect.Unread => ReadBackState.WrittenButUnverified,
            // Not reachable over the four members above, and it throws rather
            // than picking one: a fifth answer added to WriteEffect without a
            // line here is a machine brisk would otherwise describe with a
            // state it never established.
            var unknown => throw new ArgumentOutOfRangeException(
                nameof(rule), unknown,
                $"'{rule.Id}' reported something about its own write that the " +
                "read-back has no state for"),
        };
    }
}

/// The four things brisk can find when it looks again at a switch it turned
/// off. They are a closed set here and exhaustive over what StateOf can
/// return — EveryStateTheEnumCarries_IsOneSomeMachineActuallyReaches plants a
/// machine for each — but they are NOT four equally strong readings. Which
/// ones a given switch can reach depends on what brisk can read for it, and
/// WhatBriskCanTellAboutItsOwnWrite_PerSwitch is where that is written down.
public enum ReadBackState
{
    /// brisk turned it off, and it still reads as off. For the four switches
    /// whose value IS the setting, that is the complete answer. For a policy,
    /// it additionally means a second value brisk reads and never writes
    /// agrees — a policy with no such second value never lands here.
    Held,

    /// brisk turned it off and it reads as on again. Something took the write
    /// away — either by writing the on state over it or by removing it
    /// outright, and absence reads as on for this whole family. brisk does
    /// not name what did it and does not know when: it has the date it wrote,
    /// and today's read, and nothing in between.
    Reverted,

    /// The switch reads as off, and a second value this machine keeps for the
    /// same setting says it is not being acted on. This is brisk reporting
    /// that its own fix did not take.
    ///
    /// "Reads as off" and not "brisk's write is still there": the two are the
    /// same statement for four of the six switches and not for the other two
    /// — see EffectOfTheWrite — so this state is defined by the read, which
    /// brisk made, rather than by whose write is there, which it did not
    /// check.
    ///
    /// What was measured is two local values that disagree. What is said is
    /// that this edition of Windows is not acting on the policy. Nothing here
    /// is a claim about what any company receives — brisk read no such thing
    /// and the copy for this state says no such thing.
    WrittenButIgnored,

    /// The switch reads as off, and brisk has no second reading that could
    /// tell an edition that acted on the policy from one that did not. Either
    /// no second value was ever established for this setting, or the one that
    /// exists could not be read. Defined by the read and not by whose write
    /// is there, for the reason given on WrittenButIgnored above.
    ///
    /// This is not a weaker Held, it is a different answer. Held on a policy
    /// nobody confirmed would be brisk telling a user their switch is off on
    /// exactly the machine where it is not — the failure this wave exists to
    /// refuse. Reporting the gap is the whole point.
    WrittenButUnverified,
}

/// One switch brisk turned off, and what looking again found. RuleId is the
/// rule's own id; FixedAtUtc is the journal's stamp, unchanged.
public sealed record ReadBackResult(
    string RuleId, ReadBackState State, DateTime FixedAtUtc);
