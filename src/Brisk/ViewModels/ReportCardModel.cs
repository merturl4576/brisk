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
    public required IReadOnlyList<CardLine> Findings { get; init; }
    public required string FindingsEmptyText { get; init; }
    public required IReadOnlyList<string> Unread { get; init; }
    public required IReadOnlyList<string> Fixes { get; init; }
    public bool HasFixes => Fixes.Count > 0;
    public string RepoLine => "github.com/merturl4576/brisk";

    public static ReportCardModel Build(ScanSnapshot snapshot,
        IReadOnlyList<UndoableFix> undoable, Loc loc)
    {
        var picked = RevelationPicker.Pick(snapshot.Findings);
        var findings = picked
            .Select(f => new CardLine(
                LocalizedText.Headline(f.Headline!, loc).Value,
                loc.Title(f.TitleKey, f.Title)))
            .ToList();

        // Old snapshots (and bare test fixtures) may predate SensorStatus;
        // "both answered" is the only reading that adds no claim.
        var sensors = snapshot.Sensors ?? new SensorStatus(true, true, null);

        return new ReportCardModel
        {
            DateText = snapshot.CompletedUtc.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            VersionText = EngineInfo.Version,
            Health = snapshot.Health,
            Findings = findings,
            FindingsEmptyText = findings.Count > 0 ? "" :
                loc.F("overview.revelation.none", DiagnosticRuleRegistry.All.Count),
            Unread = new[] { UnreadLine(sensors, loc) },
            Fixes = undoable
                .OrderByDescending(f => f.FixedAtUtc)
                .Select(f => loc.Title($"rule.{f.RuleId}.title", f.RuleId)
                    + " · " + f.FixedAtUtc.ToLocalTime()
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .ToList(),
        };
    }

    /// One line, always. The variant logic mirrors the CLI's SensorNotice —
    /// the same three-state honesty about memory integrity, in resx form.
    private static string UnreadLine(SensorStatus sensors, Loc loc) =>
        (sensors.CpuRead, sensors.GpuRead) switch
        {
            (true, true) => loc["report.unread.none"],
            (true, false) => loc["report.unread.gpu"],
            (false, _) when !sensors.GpuRead => loc["report.unread.neither"],
            (false, _) => sensors.MemoryIntegrityOn switch
            {
                true => loc["report.unread.cpu.integrity-on"],
                false => loc["report.unread.cpu.integrity-off"],
                null => loc["report.unread.cpu"],
            },
        };
}
