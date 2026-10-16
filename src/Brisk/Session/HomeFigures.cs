using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Actions;
using BriskEngine.Actions.Rules;
using BriskEngine.Diagnostics;
using Brisk.Localization;

namespace Brisk.Session;

public enum Tone { Normal, Good, Warn, Bad }

public enum HomeButton { None, RunAll, Undo, Rescan, Retry }

public sealed record Satellite(string Value, string Caption, bool Done);

/// Every number and word on the home screen, worked out from the session in
/// one place so the view only places them.
public sealed record HomeFigures(
    string HeroLabel, long? HeroBytes, double GaugeFraction, bool GaugeGreen,
    string Found, string Startup, string Online,
    Satellite Speed, Satellite Space, Satellite Privacy,
    string Stateline, Tone StatelineTone,
    string Button, bool ButtonEnabled, HomeButton ButtonAction, bool ShowRescanLink,
    string? Previous, bool ShowPreviousUndo)
{
    public static readonly TimeSpan PreviousRunShownFor = TimeSpan.FromDays(7);

    /// The runner's order; a satellite turns green once the run has moved past its group.
    private static readonly ActionGroupKind[] RunOrder =
        { ActionGroupKind.Privacy, ActionGroupKind.Speed, ActionGroupKind.Space };

    private const string Sep = " · ";

    public static HomeFigures From(ActionSession s, Loc loc, int enabledStartup, int? onlineApps,
        DateTime nowUtc, IReadOnlyList<string>? deferredApps = null)
    {
        var c = loc.Culture;
        string Size(long bytes) => Sizes.Format(bytes, c);
        var items = s.Items.ToList();
        var byId = items.GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        string TitleOf(ActionItem i) => i.TitleKey is null ? i.Title : loc.Title(i.TitleKey, i.Title);

        // ---- space
        long found, ticked;
        if (!s.SpaceReady) (found, ticked) = PartialSpace.Estimate(s.SizedSoFar);
        else
        {
            var rows = s.SpaceGroup?.Items ?? Array.Empty<ActionItem>();
            found = rows.Sum(i => i.Bytes ?? 0);
            ticked = rows.Where(s.WillRun).Sum(s.SelectedBytes);
        }
        var freedSoFar = s.Results.Values.Sum(r => r.BytesFreed);

        var (label, hero, green) = s.State switch
        {
            SessionState.Looking => (loc["gui.home.label.looking"], (long?)ticked, false),
            SessionState.Ready => (loc["gui.home.label.ready"], ticked, false),
            SessionState.Running => (loc["gui.home.label.freed"], freedSoFar, true),
            SessionState.Done => (loc["gui.home.label.freed"], s.Receipt?.FreedBytes ?? freedSoFar, true),
            _ => (loc["gui.home.label.failed"], (long?)null, false),
        };
        var fraction = found > 0 && hero is { } h ? Math.Clamp(h / (double)found, 0, 1) : 0;

        // ---- satellites
        var fastIn = s.FastGroups.Count > 0 || s.Plan is not null;
        bool IsStartup(ActionItem i) => StartupEntryRule.TryParseId(i.Id, out _, out _);
        var startupRows = items.Where(i => i.Group == ActionGroupKind.Speed && IsStartup(i)).ToList();
        var tickedStartup = startupRows.Count(s.WillRun);
        var startupDone = startupRows.Count(i => s.Results.TryGetValue(i.Id, out var r) && r.Outcome == RowOutcome.Done);
        var privacyResults = s.Results.Values
            .Where(r => byId.TryGetValue(r.RowId, out var i) && i.Group == ActionGroupKind.Privacy).ToList();
        var privacyOff = s.Receipt?.PrivacyOff ?? privacyResults.Count(r =>
            r.Outcome is RowOutcome.Done or RowOutcome.PendingRestart
            && r.ReadBack is not (ReadBackState.Reverted or ReadBackState.WrittenButIgnored));

        var currentIndex = s.CurrentRowId is { } cur && byId.TryGetValue(cur, out var curItem)
            ? Array.IndexOf(RunOrder, curItem.Group) : -1;
        bool Finished(ActionGroupKind g) =>
            (s.RunGroup is null || s.RunGroup == g)
            && s.Results.Keys.Any(k => byId.TryGetValue(k, out var i) && i.Group == g)
            && (s.State == SessionState.Done
                || (s.State == SessionState.Running && currentIndex > Array.IndexOf(RunOrder, g)));

        var pending = loc["gui.sat.pending"];
        var speed = Finished(ActionGroupKind.Speed)
            ? new Satellite(loc.F("gui.sat.speed.done", s.Receipt?.StartupEnabledAfter
                ?? Math.Max(0, (s.Receipt?.StartupEnabledBefore ?? enabledStartup) - startupDone)),
                loc["gui.sat.speed"], true)
            : new Satellite(fastIn ? loc.F("gui.sat.speed.value", enabledStartup, enabledStartup - tickedStartup) : pending,
                loc["gui.sat.speed"], false);
        var privacy = Finished(ActionGroupKind.Privacy)
            ? new Satellite(loc.F("gui.sat.privacy.done", privacyOff), loc["gui.sat.privacy"], true)
            : new Satellite(fastIn
                    ? loc.F("gui.sat.privacy.value", items.Count(i => i.Group == ActionGroupKind.Privacy && s.WillRun(i)))
                    : pending,
                loc["gui.sat.privacy"], false);
        var space = Finished(ActionGroupKind.Space) && hero is { } freed
            ? new Satellite(loc.F("gui.sat.space.done", Size(freed)), loc["gui.sat.space"], true)
            : new Satellite(Size(ticked), loc["gui.sat.space"], false);

        // ---- stateline and button
        var tickedRows = items.Count(s.WillRun);
        var tone = Tone.Normal;
        string stateline;
        switch (s.State)
        {
            case SessionState.Looking:
                stateline = s.SizedSoFar.Count == 0
                    ? loc["gui.state.looking.start"]
                    : loc.F("gui.state.looking", LookingAt(s.SizedSoFar[^1].Target, loc),
                        s.SizedSoFar.Count, s.TargetCount);
                break;
            case SessionState.Ready:
                if (s.UndoResults is { } undone)
                {
                    stateline = loc.F("gui.state.undone", undone.Count(r => r.Outcome == RowOutcome.Done));
                    tone = Tone.Good;
                }
                else stateline = tickedRows == 0 ? loc["gui.state.nothing"] : loc.F("gui.state.ready", tickedRows);
                break;
            case SessionState.Running:
                stateline = s.CurrentRowId is { } id && byId.TryGetValue(id, out var row)
                    ? (row.LinkIds.Contains("component-store")
                        ? loc.F("gui.state.running.slow", TitleOf(row))
                        : loc.F("gui.state.running", TitleOf(row)))
                    : loc["gui.button.running"];
                break;
            case SessionState.Done:
            {
                var parts = new List<string> { loc.F("gui.state.done.freed", Size(hero ?? 0)) };
                if (startupDone > 0) parts.Add(loc.F("gui.state.done.startup", startupDone));
                if (privacyOff > 0) parts.Add(loc.F("gui.state.done.privacy", privacyOff));
                if (s.Receipt is { DeferredBytes: > 0 } r && deferredApps is { Count: > 0 })
                    parts.Add(loc.F("gui.state.done.deferred", Size(r.DeferredBytes), string.Join(", ", deferredApps)));
                var failed = s.Results.Values.Count(x => x.Outcome == RowOutcome.Failed);
                if (failed > 0) parts.Add(loc.F("gui.state.done.failed", failed));
                if (s.Results.Values.Any(x => x.Outcome == RowOutcome.PendingRestart))
                    parts.Add(loc["gui.state.done.restart"]);
                stateline = string.Join(Sep, parts);
                tone = failed > 0 ? Tone.Warn : Tone.Good;
                break;
            }
            default:
                stateline = loc.F("gui.state.failed", s.Error ?? "");
                tone = Tone.Bad;
                break;
        }

        var (button, enabled, action, rescanLink) = s.State switch
        {
            SessionState.Looking => (loc["gui.button.looking"], false, HomeButton.None, false),
            SessionState.Ready => (loc["gui.button.run-all"], tickedRows > 0, HomeButton.RunAll, false),
            SessionState.Running => (loc["gui.button.running"], false, HomeButton.None, false),
            SessionState.Done => s.CanUndo
                ? (loc["gui.button.undo"], true, HomeButton.Undo, true)
                : (loc["gui.button.rescan"], true, HomeButton.Rescan, false),
            _ => (loc["gui.button.retry"], true, HomeButton.Retry, false),
        };

        // ---- the last run, when this session has not made one
        string? previous = null;
        if (s.State == SessionState.Ready && !s.RanThisSession && s.UndoResults is null
            && s.LastRun is { UndoneUtc: null } last && nowUtc - last.FinishedUtc < PreviousRunShownFor
            && s.Receipt?.FreedBytes is { } before)
        {
            var parts = new List<string> { loc.F("gui.state.done.freed", Size(before)) };
            if (s.Receipt.BootMsBefore is { } b0 && s.Receipt.BootMsAfter is { } b1)
                parts.Add(loc.F("gui.state.previous.boot", b0 / 1000, b1 / 1000));
            if (s.Receipt.RamFreedBytes is { } ram) parts.Add(loc.F("gui.state.previous.ram", Size(ram)));
            previous = loc.F("gui.state.previous", string.Join(Sep, parts));
        }

        return new HomeFigures(label, hero, fraction, green,
            Size(found), enabledStartup.ToString(c), onlineApps?.ToString(c) ?? "—",
            speed, space, privacy, stateline, tone, button, enabled, action, rescanLink,
            previous, previous is not null && s.CanUndo);
    }

    /// What the stateline names while a target is sized: the row it feeds,
    /// in the window's language.
    private static string LookingAt(BriskEngine.Models.CleanupTarget target, Loc loc)
    {
        var rowId = target.Id == "hibernation-file" ? "space.hibernation" : null;
        var row = ActionCatalog.Rows.FirstOrDefault(r => r.Id == rowId || r.LinkIds.Contains(target.Id));
        return row is null ? target.DisplayName : loc.Title(row.TitleKey, row.Title);
    }
}
