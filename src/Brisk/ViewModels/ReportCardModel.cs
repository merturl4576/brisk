using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Brisk.Localization;
using Brisk.Services;
using BriskEngine;
using BriskEngine.Diagnostics;
using BriskEngine.Models;

namespace Brisk.ViewModels;

public sealed record CardLine(string Lead, string Text);

/// Everything the report card says, as plain strings, built once per render.
/// The privacy rule is structural: this model reads headlines and titles and
/// nothing else a finding carries — evidence, fix descriptions, and every
/// engine-authored sentence that could name a program, a path, or the
/// machine simply have no route in. A test pins the ban on the output.
public sealed class ReportCardModel
{
    public required string DateText { get; init; }
    public required string VersionText { get; init; }
    public required int Health { get; init; }
    /// Which band the score falls in, as the app's own theme key. The card
    /// paints its ring from this, so a machine at 35 cannot leave here
    /// wearing the healthy green — and the banding is the one the overview
    /// and the health page already use, not a second opinion about health.
    ///
    /// Named ScoreBrushKey because that is what Theming/Shared.xaml's
    /// score styles bind, and what OverviewViewModel and HealthViewModel
    /// already call it. Under the old name the card's numeral carried a
    /// style whose triggers bound a property this model did not expose: a
    /// binding that failed silently, rendered white, and would have begun
    /// colouring the numeral the first time somebody renamed anything.
    public required string ScoreBrushKey { get; init; }
    public required IReadOnlyList<CardLine> Findings { get; init; }
    public required string FindingsMoreText { get; init; }
    public required string FindingsEmptyText { get; init; }
    public required IReadOnlyList<string> Unread { get; init; }
    public required IReadOnlyList<string> Fixes { get; init; }
    public bool HasFixes => Fixes.Count > 0;
    public string RepoLine => "github.com/merturl4576/brisk";

    /// The card is a fixed 1600x900 frame, and the right-hand column does not
    /// scroll, wrap or shrink. What happens to a column taller than its Grid
    /// is WPF's layout clip: the column is cut at the Grid's edge and the rows
    /// past it are never drawn. Silently — no exception, no warning, and
    /// nothing on the picture to say a row is missing, which is exactly the
    /// kind of thing a person posts without noticing. It is also the failure
    /// no pixel count can see, because a clipped card and a card that fits
    /// look equally tidy; the render test weighs the column's desired height
    /// against the height the Grid gives it instead.
    ///
    /// THE BUDGET IS SHARED, because the frame does not grow. Three sections
    /// compete for the column and they do not compete on equal terms.
    ///
    /// The findings are capped and count what they dropped. The fix list is
    /// budgeted and counts what it dropped. The section between them does
    /// NEITHER: the spec's fourth red line says an unreadable probe is never a
    /// silent zero, so every line it has is printed, and the fix list is what
    /// pays for them — one row given up per line taken, since those two rows
    /// are the same height.
    ///
    /// THAT TRADE HAS A FLOOR AND IS THEREFORE NOT A BOUND. FixBudget will not
    /// take the fix list below one row, so once the unread section passes nine
    /// lines nothing is left to pay and the column grows past the frame again.
    /// Unreachable on the shipped rules — the ceiling there is the sensor line
    /// plus one per report-only disclosure — and stated rather than assumed:
    /// TheTrade_HasHeadroomForEveryUnreadLineTheShippedRulesCanProduce derives
    /// that ceiling from the registry and fails when it stops fitting. Said
    /// plainly because the last sentence in this file that claimed a mechanism
    /// it did not have hid a live clipping defect for a whole wave.
    ///
    /// This constant is the fix list's CEILING: what it gets on a card whose
    /// sections above took the least they can — one unread sentence, and no
    /// findings overflow. It gives way rather than the sections above because
    /// it comes from the journal, is uncapped, and is the footnote on a card
    /// whose subject is the numbers above it.
    public const int MaxFixRows = 9;

    /// How many measured numbers the card leads with; the rest are counted on
    /// a line of their own. Five is the most the card could show before this
    /// cap existed — not by design but because exactly five shipped rules
    /// carried a headline, which is what "the picker takes five at most" used
    /// to describe. This wave's disclosures brought that count to nine, and a
    /// sixth row is 52px the frame does not always have: a card with a full
    /// fix list measured 758px against the 715px the Grid gives it, and the
    /// rows past the edge are cut away in silence.
    public const int MaxFindingRows = 5;

    public static ReportCardModel Build(ScanSnapshot snapshot,
        IReadOnlyList<UndoableFix> undoable, Loc loc)
    {
        var picked = RevelationPicker.Pick(snapshot.Findings);
        var findings = picked
            .Take(MaxFindingRows)
            .Select(f => new CardLine(
                LocalizedText.Headline(f.Headline!, loc).Value,
                loc.Title(f.TitleKey, f.Title)))
            .ToList();
        var hiddenFindings = picked.Count - findings.Count;
        var unread = UnreadLines(snapshot, loc);

        return new ReportCardModel
        {
            DateText = snapshot.CompletedUtc.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            VersionText = EngineInfo.Version,
            Health = snapshot.Health,
            ScoreBrushKey = HealthBrush.KeyFor(snapshot.Health),
            Findings = findings,
            // The overview's key, and the right one here: its Turkish counts
            // FINDINGS out loud, which under this heading is the noun the line
            // is about. That same Turkish is what made it the wrong key for
            // the fix list below.
            FindingsMoreText = hiddenFindings > 0
                ? loc.F("overview.revelation.more", hiddenFindings) : "",
            FindingsEmptyText = findings.Count > 0 ? "" :
                loc.F("overview.revelation.none", DiagnosticRuleRegistry.All.Count),
            Unread = unread,
            Fixes = FixRows(undoable, loc,
                FixBudget(unread.Count, hiddenFindings > 0)),
        };
    }

    /// Newest first, capped at what the frame holds, and honest about the
    /// remainder — in a sentence about the right things.
    ///
    /// This borrowed "overview.revelation.more", which reads "and {0} more" in
    /// English and looks like the same line. It is not: the Turkish is "ve {0}
    /// bulgu daha" — and {0} more FINDINGS — because that key belongs to the
    /// overview's revelation band. Under "Uygulanan düzeltmeler" on a shared
    /// PNG it counted the wrong things out loud, in the one language the
    /// maintainer actually reads the app in. English parity is what hid it, so
    /// the fixes list has its own key and its own Turkish noun.
    private static IReadOnlyList<string> FixRows(
        IReadOnlyList<UndoableFix> undoable, Loc loc, int budget)
    {
        var rows = undoable
            .OrderByDescending(f => f.FixedAtUtc)
            .Select(f => loc.Title($"rule.{f.RuleId}.title", f.RuleId)
                + " · " + f.FixedAtUtc.ToLocalTime()
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToList();
        if (rows.Count <= budget) return rows;

        // One row short of the budget, because the count needs a row of its
        // own. Truncating to the budget and appending the line would put
        // budget + 1 rows on the card — the overflow this exists to stop.
        var shown = rows.Take(budget - 1).ToList();
        shown.Add(loc.F("report.fixes.more", rows.Count - shown.Count));
        return shown;
    }

    /// What the fix list may spend on this particular card. An unread
    /// sentence, a fix line and the findings' overflow line are the same
    /// height here — 29px each on the real control, against 52px for a
    /// finding row — so a line taken by either section above costs the fix
    /// list exactly one row, and the arithmetic is a trade rather than an
    /// estimate.
    ///
    /// Never below one. A card carrying fixes that showed none of them and
    /// said nothing about it would be the silent drop this whole budget
    /// exists to stop; at a budget of one the single row IS the count.
    private static int FixBudget(int unreadLines, bool findingsOverflowed) =>
        Math.Max(1, MaxFixRows - (unreadLines - 1) - (findingsOverflowed ? 1 : 0));

    /// What brisk could not read, from the sensors and from the findings, in
    /// that order. The sensor line is always here; a disclosure that reached
    /// its source and came back with nothing joins it below.
    ///
    /// ONE CHANNEL. The disclosures arrive by the same predicate the Gizlilik
    /// page bands its unreadable rows with, so the card and the page cannot
    /// come to disagree about which probe went unread — the alternative was a
    /// second list of "the unreadable ones" kept here, which is exactly the
    /// drift the page refused to introduce.
    ///
    /// The rule's TITLE, which is rule-authored static text and carries no
    /// reading, so the section obeys the same ban as the rest of the card:
    /// this model reads headlines and titles and nothing else a finding
    /// carries. Ordered by rule id, ordinal — the order the page's unreadable
    /// band already falls into, since every row in it shares the floor that
    /// page sorts on and its tie-break is all that decides them.
    private static IReadOnlyList<string> UnreadLines(ScanSnapshot snapshot, Loc loc)
    {
        var lines = new List<string> { UnreadLine(snapshot.Sensors, loc) };
        lines.AddRange(snapshot.Findings
            .Where(PrivacyViewModel.IsUnreadableDisclosure)
            .OrderBy(f => f.RuleId, StringComparer.Ordinal)
            .Select(f => loc.Title(f.TitleKey, f.Title)));
        return lines;
    }

    /// The SENSOR line — one of them, always, naming which sensor stayed
    /// silent. Whenever the CPU temperature went unread — alone or alongside
    /// the GPU — the line also
    /// carries the measured memory-integrity state, because that is the one
    /// reason brisk can actually name for a silent CPU sensor. A GPU-only
    /// silence carries no reason: a blocked kernel driver is not why a GPU
    /// sensor is quiet, and inventing a cause would be worse than admitting
    /// there is none.
    ///
    /// Narrower than the CLI's SensorNotice, which also weighs elevation. The
    /// two must not DISAGREE about memory integrity — they read the same three
    /// states off the same probe, and a scan that explains a blocklisted
    /// driver beside a card that explains nothing is one product contradicting
    /// itself — but the card is not a copy of the notice, and the wording
    /// differs on purpose.
    private static string UnreadLine(SensorStatus sensors, Loc loc) =>
        (sensors.CpuRead, sensors.GpuRead) switch
        {
            (true, true) => loc["report.unread.none"],
            (true, false) => loc["report.unread.gpu"],
            (false, false) => WithReason("report.unread.neither", sensors, loc),
            (false, true) => WithReason("report.unread.cpu", sensors, loc),
        };

    /// The three-state reason, chosen among three whole sentences rather than
    /// glued on as a clause — Turkish does not take an English sentence with a
    /// tail bolted to it. null is never folded into off: a Device Guard query
    /// that failed is not a machine with memory integrity switched off, and
    /// the hedged sentence says exactly that.
    private static string WithReason(string baseKey, SensorStatus sensors, Loc loc) =>
        sensors.MemoryIntegrityOn switch
        {
            true => loc[baseKey + ".integrity-on"],
            false => loc[baseKey + ".integrity-off"],
            null => loc[baseKey],
        };
}
