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
    /// The findings section is already bounded: the picker takes five at
    /// most, and the render test covers that maximum. The fix list is not —
    /// it comes from the journal, it is uncapped, and a machine that has run
    /// fix-all can carry eight or ten entries. So this is the budget, in
    /// ROWS: the last row is spent on "and N more" whenever there are more
    /// fixes than fit, so nothing is dropped without being counted.
    public const int MaxFixRows = 9;

    public static ReportCardModel Build(ScanSnapshot snapshot,
        IReadOnlyList<UndoableFix> undoable, Loc loc)
    {
        var picked = RevelationPicker.Pick(snapshot.Findings);
        var findings = picked
            .Select(f => new CardLine(
                LocalizedText.Headline(f.Headline!, loc).Value,
                loc.Title(f.TitleKey, f.Title)))
            .ToList();

        return new ReportCardModel
        {
            DateText = snapshot.CompletedUtc.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            VersionText = EngineInfo.Version,
            Health = snapshot.Health,
            ScoreBrushKey = HealthBrush.KeyFor(snapshot.Health),
            Findings = findings,
            FindingsEmptyText = findings.Count > 0 ? "" :
                loc.F("overview.revelation.none", DiagnosticRuleRegistry.All.Count),
            Unread = new[] { UnreadLine(snapshot.Sensors, loc) },
            Fixes = FixRows(undoable, loc),
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
        IReadOnlyList<UndoableFix> undoable, Loc loc)
    {
        var rows = undoable
            .OrderByDescending(f => f.FixedAtUtc)
            .Select(f => loc.Title($"rule.{f.RuleId}.title", f.RuleId)
                + " · " + f.FixedAtUtc.ToLocalTime()
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToList();
        if (rows.Count <= MaxFixRows) return rows;

        // One row short of the budget, because the count needs a row of its
        // own. Truncating to the budget and appending the line would put
        // MaxFixRows + 1 rows on the card — the overflow this exists to stop.
        var shown = rows.Take(MaxFixRows - 1).ToList();
        shown.Add(loc.F("report.fixes.more", rows.Count - shown.Count));
        return shown;
    }

    /// One line, always, naming which sensor stayed silent. Whenever the CPU
    /// temperature went unread — alone or alongside the GPU — the line also
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
