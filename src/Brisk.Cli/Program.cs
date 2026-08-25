using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
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
        ConfigureConsole();
        return Run(args);
    }

    /// A Turkish (or Polish, or Japanese) console defaults to a legacy code
    /// page, which turns brisk's own help text into mojibake before the user
    /// has read a single finding.
    ///
    /// Kept out of Run because setting this discards Console.Out, which is the
    /// stream a test hands in to read what brisk printed.
    private static void ConfigureConsole()
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch (IOException) { /* nothing attached to configure */ }
    }

    public static int Run(string[] args)
    {
        // "--help" and "--version" are switches, and the parser knows verbs.
        // Translating here rather than at the window's entry point is what
        // makes both executables answer them the same way.
        var normalized = EntryRouter.Normalize(args);
        // Ahead of the parser on purpose. The verb is in CliParser.Verbs so
        // that 'report' is not an unknown command, but the parser knows none
        // of report's flags — so 'report --out card.png' answered "bad
        // argument '--out'", which is true about the flag and the same lie
        // about why brisk.exe will not draw a card. Every report line, flags
        // or not, gets the one reason that is actually the reason.
        if (normalized.Length > 0 && normalized[0] == "report") return Refuse();
        var cmd = CliParser.Parse(normalized);
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
            new RealProcessLister(), new RealMemoryIntegrityProbe(), dataDir);
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
                // Unreachable while the early return above stands, and kept
                // for the day someone moves it: without this arm 'report'
                // would fall through to the silent `_ => 2` and refuse with
                // no message at all — worse than the unknown-command error
                // that recognizing the verb was meant to replace.
                "report" => Refuse(),
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

    /// The sentence to print about a temperature brisk did not read, or null
    /// when both sensors answered.
    ///
    /// The GUI ships an elevation manifest; the CLI deliberately does not — a
    /// command-line tool that raises UAC on every invocation is worse than one
    /// that cannot read a temperature. But silently omitting the thermals
    /// finding makes "brisk scan" look like it checked and found nothing
    /// wrong, which is the same lie the manifest was added to stop in the GUI.
    /// So the CLI says which of the two it is.
    ///
    /// It used to fire only when NOTHING answered, and to promise that
    /// elevation would fix it. Measuring the thermals rule made both halves
    /// wrong. GPU temperature reads unelevated, so a GPU-only machine printed
    /// no notice at all and a scan looked complete when the CPU had gone
    /// unread. And CPU temperature reads at NO privilege level on a machine
    /// running memory integrity, because the driver that reads it is on
    /// Microsoft's vulnerable-driver blocklist — so the elevation advice
    /// promised a remedy this codebase documents as ineffective. Elevation is
    /// still worth naming, as one thing that can matter and often will not.
    /// memoryIntegrityOn is required rather than defaulted: the caller that
    /// forgets it is exactly the caller that would keep printing the hedged
    /// reason on a machine brisk could have measured.
    public static string? SensorNotice(ISensorProbe sensors, bool elevated,
        bool? memoryIntegrityOn)
    {
        // `is not null` alone counted a NaN as an answer, so this notice
        // stayed silent about a sensor the report card — same product, same
        // machine, same second — reported as unread. One predicate decides.
        var cpu = SensorReading.IsReal(sensors.CpuTempC());
        var gpu = SensorReading.IsReal(sensors.GpuTempC());
        if (cpu && gpu) return null;
        if (cpu) return "temperature: GPU not read — CPU only. brisk cannot tell from here why.";
        var unread = gpu
            ? "temperature: CPU not read — GPU only."
            : "temperature: not checked — neither sensor answered.";
        // The CPU half of the reason is the same in both, so it is said once
        // — but WHICH reason is available depends on a setting brisk can read
        // without a driver, and the rule and this notice must not disagree
        // about it. null is not folded into off: a Device Guard query that
        // failed is not a machine with memory integrity switched off.
        var why = memoryIntegrityOn switch
        {
            true => " Memory integrity is on here, and the driver that reads CPU "
                + "temperature is on Microsoft's vulnerable-driver blocklist, so Windows "
                + "will not load it at any privilege level. brisk does not switch that "
                + "off, and cannot prove it is the only reason here.",
            false => " Memory integrity is off here, so the usual reason — a driver "
                + "Windows refuses to load — is not what happened, and brisk cannot tell "
                + "from here what did.",
            null => " The driver that reads CPU temperature is on Microsoft's "
                + "vulnerable-driver blocklist and will not load while memory integrity "
                + "is on. brisk does not switch that off, and cannot confirm from here "
                + "that it is the reason on this machine.",
        };
        return elevated
            ? unread + why
            : unread + why + " Running as administrator can help other sensors.";
    }

    private static int Scan(CliCommand cmd, DiagnosticContext ctx, Scanner scanner,
        bool elevated)
    {
        var sensorNotice = SensorNotice(ctx.Sensors, elevated, ctx.MemoryIntegrity.IsOn());
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
                // Two nullable numbers instead of one "available" flag, which
                // said true as soon as EITHER sensor answered — a parser was
                // being told thermals were checked on a machine where the CPU
                // never was.
                sensors = new
                {
                    cpuC = ctx.Sensors.CpuTempC(),
                    gpuC = ctx.Sensors.GpuTempC(),
                    notice = sensorNotice,
                },
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
            foreach (var rule in FixAllRules())
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

    /// Every rule `brisk fix --all` considers, and no other. Of these it
    /// applies the ones whose Detect fires, and only with --yes; a rule that
    /// is not here is not reachable by that command at all. Extracted from the
    /// loop so a test can read the selection instead of inferring it from what
    /// the command printed.
    ///
    /// This is NOT the GUI's fix-all. That one lives in Brisk's FixAllService,
    /// which this project does not reference and cannot see, and it excludes
    /// the whole privacy topic by rule id. This selects on the consent level
    /// alone, so it does reach the four privacy switches that cost the user
    /// nothing visible, and is meant to: nothing anybody relies on stops
    /// working when an advertising ID goes off.
    ///
    /// The line it must never cross is the two switches that DO cost
    /// something. `--all` names no consequence, so it may not take Find my
    /// device or Timeline away; those rules ship as Confirm, and
    /// ProgramFixTests asserts they stay out of what this returns.
    public static IReadOnlyList<IDiagnosticRule> FixAllRules() =>
        DiagnosticRuleRegistry.All
            .Where(r => r.Category == RuleCategory.Auto)
            .ToList();

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

    /// The card needs the visual engine, which ships only in brisk-app.exe.
    /// The verb is recognized so the refusal can be precise — an
    /// unknown-command error, or a complaint about a flag, would both lie
    /// about why.
    private static int Refuse()
    {
        Console.Error.WriteLine(
            "brisk: the report card needs the visual engine that ships in "
            + "brisk-app.exe — run: brisk-app.exe report");
        return 2;
    }

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
        Console.WriteLine("  report                     save the scan as a shareable PNG (brisk-app.exe only)");
        Console.WriteLine("  version                    print the engine version");
    }
}
