using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using BriskEngine;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.RealProbes;
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
            new RealProcessInfoProbe(), sensors,
            new RealDiskInfoProbe(), new RealFileProbe(),
            new RealProcessLister(), dataDir);
        var log = new ActionLog(Path.Combine(dataDir, "action-log.jsonl"));
        var fixRunner = new FixRunner(new FixJournal(Path.Combine(dataDir, "fix-journal.jsonl")), log);
        var scanner = new Scanner(CleanupTargetRegistry.All, new RealProcessLister());
        bool IsElevated() => new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);
        var cleanRunner = new CleanRunner(new SafetyValidator(), new WindowsRecycler(),
            log, runner, IsElevated);

        try
        {
            return cmd.Verb switch
            {
                "scan" => Scan(cmd, ctx, scanner),
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

    private static int Scan(CliCommand cmd, DiagnosticContext ctx, Scanner scanner)
    {
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
                        skipped = t.SkippedReason,
                    }),
                    totalBytes = scan.TotalBytes,
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

        long SafeBytes(CleanupLevel level) => scan.Targets
            .Where(t => t.Target.Level == level)
            .Sum(t => t.TotalBytes);

        Console.WriteLine(
            $"Reclaimable — Safe: {Fmt.Bytes(SafeBytes(CleanupLevel.Safe))}, " +
            $"Developer: {Fmt.Bytes(SafeBytes(CleanupLevel.Developer))}, " +
            $"Deep: {Fmt.Bytes(SafeBytes(CleanupLevel.Deep))} (run 'brisk clean')");
        return 0;
    }

    private static int Fix(CliCommand cmd, DiagnosticContext ctx, FixRunner fixRunner)
    {
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
            var outcome = fixRunner.Undo(rule, ctx);
            Console.WriteLine(outcome.Message);
            return outcome.Ok ? 0 : 1;
        }

        if (cmd.All)
        {
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
            }
            return 0;
        }

        if (cmd.RuleId is not null)
        {
            var rule = DiagnosticRuleRegistry.All.FirstOrDefault(r => r.Id == cmd.RuleId);
            if (rule is null)
            {
                Console.Error.WriteLine($"brisk: unknown rule '{cmd.RuleId}'");
                return 2;
            }
            if (!cmd.Yes)
            {
                var finding = Safe(() => rule.Detect(ctx));
                if (finding is not null)
                {
                    Console.WriteLine($"[{rule.Id}] {finding.Title}");
                    Console.WriteLine($"    {finding.Evidence}");
                }
                else
                {
                    Console.WriteLine($"{rule.Id}: no finding");
                }
                Console.WriteLine("add --yes to apply");
                return 0;
            }
            var applyOutcome = fixRunner.Apply(rule, ctx);
            Console.WriteLine(applyOutcome.Message);
            return applyOutcome.Ok ? 0 : 1;
        }

        Console.Error.WriteLine("brisk: fix requires --all or --rule <id>");
        return 2;
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
        var targets = scan.Targets.Where(t => t.Target.Level == level).ToList();
        var selected = targets
            .Where(t => !t.Target.RequiresIndividualSelection && !t.Target.RequiresExplicitOptIn)
            .ToList();

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
                }
            }
        }
        Console.WriteLine($"recycled: {recycledCount} items, {Fmt.Bytes(recycledBytes)}");
        return 0;
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
        Console.WriteLine("    --yes                    actually mutate (otherwise dry-run)");
        Console.WriteLine("  clean                      reclaim disk space");
        Console.WriteLine("    --level <safe|developer|deep>  which cleanup level to run");
        Console.WriteLine("    --yes                    actually delete (otherwise print plan)");
        Console.WriteLine("  targets                    list cleanup targets");
        Console.WriteLine("  rules                      list diagnostic rules");
        Console.WriteLine("  version                    print the engine version");
    }
}
