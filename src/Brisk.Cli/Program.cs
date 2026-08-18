using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using BriskEngine;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.RealProbes;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Logging;
using BriskEngine.Models;
using BriskEngine.Safety;

namespace Brisk.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        var cmd = CliParser.Parse(args);
        if (cmd.Verb == "error") { Console.Error.WriteLine($"brisk: {cmd.Error}"); return 2; }
        if (cmd.Verb is "help") { PrintHelp(); return 0; }
        if (cmd.Verb is "version") { Console.WriteLine(EngineInfo.Version); return 0; }

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "brisk");
        var runner = new RealProcessRunner();
        using var sensors = new RealSensorProbe();
        var ctx = new DiagnosticContext(
            new RealPowercfgProbe(runner), new RealRegistryProbe(),
            new RealProcessInfoProbe(), sensors, new RealDisplayProbe(),
            new RealEventLogProbe(), new RealHardwareProbe(),
            new RealDiskInfoProbe(), new RealFileProbe(),
            new RealProcessLister(), dataDir);
        var log = new ActionLog(Path.Combine(dataDir, "action-log.jsonl"));
        var fixRunner = new FixRunner(new FixJournal(Path.Combine(dataDir, "fix-journal.jsonl")), log);
        var scanner = new Scanner(CleanupTargetRegistry.All, new RealProcessLister(),
            new DeleteLockProbe());
        bool IsElevated() => new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);
        var cleanRunner = new CleanRunner(new SafetyValidator(), new WindowsRecycler(),
            log, runner, IsElevated, new DeleteLockProbe());

        try
        {
            return cmd.Verb switch
            {
                "scan" => Scan(cmd, ctx, scanner, IsElevated()),
                "fix" => Fix(cmd, ctx, fixRunner),
                "clean" => Clean(cmd, scanner, cleanRunner),
                "targets" => PrintTargets(),
                "rules" => PrintRules(),
                _ => 2,
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"brisk: {ex.Message}");
            return 1;
        }
    }

    private static DiagnosticFinding? Safe(Func<DiagnosticFinding?> detect)
    {
        try { return detect(); }
        catch { return null; }
    }

    public static (List<TargetScanResult> Selected, string? Error) SelectTargets(
        ScanResult scan, string? targetId, CleanupLevel level)
    {
        if (targetId is null)
            return (scan.Targets
                .Where(t => t.Target.Level == level)
                .Where(t => !t.Target.RequiresIndividualSelection
                         && !t.Target.RequiresExplicitOptIn)
                .ToList(), null);

        var match = scan.Targets.FirstOrDefault(t => t.Target.Id == targetId);
        if (match is null)
            return (new List<TargetScanResult>(), $"unknown target '{targetId}'");
        if (match.Target.RequiresIndividualSelection)
            return (new List<TargetScanResult>(),
                $"target '{targetId}' needs per-item selection — use the app");
        return (new List<TargetScanResult> { match }, null);
    }

    /// The sentence to print when no sensor answered, or null when they did.
    ///
    /// The GUI ships an elevation manifest; the CLI deliberately does not — a
    /// command-line tool that raises UAC on every invocation is worse than one
    /// that cannot read a temperature. But silently omitting the thermals
    /// finding makes "brisk scan" look like it checked and found nothing
    /// wrong, which is the same lie the manifest was added to stop in the GUI.
    /// So the CLI says which of the two it is.
    public static string? SensorNotice(ISensorProbe sensors, bool elevated)
    {
        if (sensors.CpuTempC() is not null || sensors.GpuTempC() is not null)
            return null;
        return elevated
            ? "temperature: no readable sensor on this machine — thermals not checked"
            : "temperature: not checked — sensor access needs administrator. "
              + "Run this from an elevated prompt, or open the brisk app.";
    }

    private static int Scan(CliCommand cmd, DiagnosticContext ctx, Scanner scanner,
        bool elevated)
    {
        var sensorNotice = SensorNotice(ctx.Sensors, elevated);
        var findings = DiagnosticRuleRegistry.All
            .Select(r => Safe(() => r.Detect(ctx)))
            .Where(f => f != null)
            .Select(f => f!)
            .ToList();
        var scan = scanner.Scan();

        if (cmd.Json)
        {
            var payload = new
            {
                findings,
                cleaner = new
                {
                    targets = scan.Targets.Select(t => new
                    {
                        id = t.Target.Id,
                        bytes = t.TotalBytes,
                        reclaimableBytes = t.ReclaimableBytes,
                        skipped = t.SkippedReason,
                    }),
                    totalBytes = scan.TotalBytes,
                    reclaimableBytes = scan.ReclaimableBytes,
                },
                // Absent thermals must be distinguishable from healthy
                // thermals by anything parsing this, not just by a human
                // reading the text output.
                sensors = new { available = sensorNotice is null, notice = sensorNotice },
            };
            Console.WriteLine(JsonSerializer.Serialize(payload));
            return 0;
        }

        foreach (var f in findings)
        {
            var marker = f.Severity switch
            {
                Severity.Critical => "!!",
                Severity.Warning => "! ",
                _ => "i ",
            };
            Console.WriteLine($"[{marker}] {f.Title} (impact {new string('*', f.ImpactStars)})");
            Console.WriteLine($"    {f.Evidence}");
        }

        if (sensorNotice is not null) Console.WriteLine($"[i ] {sensorNotice}");

        // ReclaimableBytes, not TotalBytes: the printed promise counts only
        // what 'brisk clean' can actually take right now (running-app and
        // delete-locked content stays out — the round-11 honesty rule).
        long SafeBytes(CleanupLevel level) => scan.Targets
            .Where(t => t.Target.Level == level)
            .Sum(t => t.ReclaimableBytes);

        Console.WriteLine(
            $"Reclaimable — Safe: {Fmt.Bytes(SafeBytes(CleanupLevel.Safe))}, " +
            $"Developer: {Fmt.Bytes(SafeBytes(CleanupLevel.Developer))}, " +
            $"Deep: {Fmt.Bytes(SafeBytes(CleanupLevel.Deep))} (run 'brisk clean')");
        return 0;
    }

    public static int Fix(CliCommand cmd, DiagnosticContext ctx, FixRunner fixRunner)
    {
        // --keep is the console's answer to the GUI's "is the picture back?".
        // It runs before any detect below on purpose: by the time a person can
        // answer, the display is already at its best rate, so there is no
        // finding left and the branches further down would decline to act.
        if (cmd.Keep)
        {
            if (cmd.RuleId != DisplayRefreshRule.RuleId)
            {
                Console.Error.WriteLine(
                    $"brisk: --keep applies to --rule {DisplayRefreshRule.RuleId}");
                return 2;
            }
            if (!cmd.Yes)
            {
                Console.WriteLine("would keep: the display mode now on screen (add --yes)");
                return 0;
            }
            ctx.Displays.PersistCurrentModes();
            Console.WriteLine($"{DisplayRefreshRule.RuleId}: kept — the mode now on " +
                              "screen will survive a restart");
            return 0;
        }

        if (cmd.Undo)
        {
            if (cmd.RuleId is null)
            {
                Console.Error.WriteLine("brisk: --undo requires --rule <id>");
                return 2;
            }
            var rule = DiagnosticRuleRegistry.All.FirstOrDefault(r => r.Id == cmd.RuleId);
            if (rule is null)
            {
                Console.Error.WriteLine($"brisk: unknown rule '{cmd.RuleId}'");
                return 2;
            }
            if (!cmd.Yes)
            {
                Console.WriteLine($"would undo: {rule.Id} (add --yes to apply)");
                return 0;
            }
            var outcome = fixRunner.Undo(rule, ctx);
            Console.WriteLine(outcome.Message);
            return outcome.Ok ? 0 : 1;
        }

        if (cmd.All)
        {
            var anyFailed = false;
            foreach (var rule in DiagnosticRuleRegistry.All.Where(r => r.Category == RuleCategory.Auto))
            {
                var finding = Safe(() => rule.Detect(ctx));
                if (finding is null) continue;

                if (!cmd.Yes)
                {
                    Console.WriteLine($"would fix: {rule.Id} — {finding.Title}");
                    continue;
                }
                var outcome = fixRunner.Apply(rule, ctx);
                Console.WriteLine(outcome.Message);
                if (outcome.Ok) NoteIfProvisional(rule);
                if (!outcome.Ok) anyFailed = true;
            }
            return anyFailed ? 1 : 0;
        }

        if (cmd.RuleId is not null)
        {
            var rule = DiagnosticRuleRegistry.All.FirstOrDefault(r => r.Id == cmd.RuleId);
            if (rule is null)
            {
                Console.Error.WriteLine($"brisk: unknown rule '{cmd.RuleId}'");
                return 2;
            }
            var finding = Safe(() => rule.Detect(ctx));
            if (finding is null)
            {
                Console.WriteLine($"{rule.Id}: no live finding — nothing to fix");
                return 0;
            }
            if (!cmd.Yes)
            {
                Console.WriteLine($"[{rule.Id}] {finding.Title}");
                Console.WriteLine($"    {finding.Evidence}");
                Console.WriteLine("add --yes to apply");
                return 0;
            }
            var applyOutcome = fixRunner.Apply(rule, ctx);
            Console.WriteLine(applyOutcome.Message);
            if (applyOutcome.Ok) NoteIfProvisional(rule);
            return applyOutcome.Ok ? 0 : 1;
        }

        Console.Error.WriteLine("brisk: fix requires --all or --rule <id>");
        return 2;
    }

    /// The display fix is applied for this session only, because the mode that
    /// blanks a screen must not also be the mode the machine boots into. The
    /// GUI makes it permanent when the user confirms the picture is back; the
    /// console has no such prompt, so it says which of the two this was rather
    /// than let a change the next restart undoes read as finished.
    private static void NoteIfProvisional(IDiagnosticRule rule)
    {
        if (rule.Id != DisplayRefreshRule.RuleId) return;
        Console.WriteLine("    this session only — a restart brings the previous " +
                          "refresh rate back");
        Console.WriteLine($"    to keep it: brisk fix --rule {DisplayRefreshRule.RuleId} " +
                          "--keep --yes");
    }

    private static int Clean(CliCommand cmd, Scanner scanner, CleanRunner cleanRunner)
    {
        var levelName = cmd.Level ?? "safe";
        var level = levelName switch
        {
            "safe" => CleanupLevel.Safe,
            "developer" => CleanupLevel.Developer,
            "deep" => CleanupLevel.Deep,
            _ => CleanupLevel.Safe,
        };

        var scan = scanner.Scan();
        var (selected, selectError) = SelectTargets(scan, cmd.Target, level);
        if (selectError is not null)
        {
            Console.Error.WriteLine($"brisk: {selectError}");
            return 2;
        }

        if (!cmd.Yes)
        {
            Console.WriteLine("PLAN (nothing deleted)");
            long planBytes = 0;
            foreach (var t in selected)
            {
                if (t.SkippedReason is not null)
                {
                    Console.WriteLine($"  {t.Target.Id}: skipped — {t.SkippedReason}");
                    continue;
                }
                var report = cleanRunner.Clean(t, dryRun: true);
                foreach (var entry in report.Entries)
                {
                    if (entry.Action == "dry-run")
                    {
                        Console.WriteLine($"  {entry.Path}  ({Fmt.Bytes(entry.Bytes)})");
                        planBytes += entry.Bytes;
                    }
                    else if (entry.Action == "refused")
                    {
                        Console.WriteLine($"  refused: {entry.Path} — {entry.Reason}");
                    }
                }
            }
            Console.WriteLine($"Total: {Fmt.Bytes(planBytes)}");
            return 0;
        }

        long recycledBytes = 0;
        int recycledCount = 0;
        var anyErrors = false;
        foreach (var t in selected)
        {
            if (t.SkippedReason is not null)
            {
                Console.WriteLine($"  skipped: {t.Target.Id} — {t.SkippedReason}");
                continue;
            }
            var report = cleanRunner.Clean(t, dryRun: false);
            foreach (var entry in report.Entries)
            {
                if (entry.Action == "recycled")
                {
                    recycledBytes += entry.Bytes;
                    recycledCount++;
                }
                else if (entry.Action is "refused" or "error")
                {
                    Console.WriteLine($"  {entry.Action}: {entry.Path} — {entry.Reason}");
                    if (entry.Action == "error") anyErrors = true;
                }
            }
        }
        Console.WriteLine($"recycled: {recycledCount} items, {Fmt.Bytes(recycledBytes)}");
        return anyErrors ? 1 : 0;
    }

    private static int PrintTargets()
    {
        foreach (var t in CleanupTargetRegistry.All)
            Console.WriteLine($"{t.Id,-24} {t.Level,-10} {t.DisplayName}");
        return 0;
    }

    private static int PrintRules()
    {
        foreach (var r in DiagnosticRuleRegistry.All)
            Console.WriteLine($"{r.Id,-24} {r.Category,-10} {Humanize(r.Id)}");
        return 0;
    }

    private static string Humanize(string id) =>
        string.Join(' ', id.Split('-').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static void PrintHelp()
    {
        Console.WriteLine("brisk — Windows performance diagnostics and cleanup");
        Console.WriteLine();
        Console.WriteLine("Usage: brisk <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  scan                       run diagnostics + cleaner scan");
        Console.WriteLine("    --json                   emit JSON instead of text");
        Console.WriteLine("  fix                        apply diagnostic rule fixes");
        Console.WriteLine("    --all                    apply every Auto rule with a finding");
        Console.WriteLine("    --rule <id>               apply/undo a single rule");
        Console.WriteLine("    --undo                   undo the named rule's last fix");
        Console.WriteLine("    --keep                   commit the display mode currently on screen");
        Console.WriteLine("    --yes                    actually mutate (otherwise dry-run)");
        Console.WriteLine("  clean                      reclaim disk space");
        Console.WriteLine("    --level <safe|developer|deep>  which cleanup level to run");
        Console.WriteLine("    --target <id>            clean a single target by id (see 'brisk targets')");
        Console.WriteLine("    --yes                    actually delete (otherwise print plan)");
        Console.WriteLine("  targets                    list cleanup targets");
        Console.WriteLine("  rules                      list diagnostic rules");
        Console.WriteLine("  version                    print the engine version");
    }
}
