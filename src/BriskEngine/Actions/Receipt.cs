using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BriskEngine.Actions.Rules;
using BriskEngine.Diagnostics;
using BriskEngine.Live;

namespace BriskEngine.Actions;

public sealed record StartupRam(string RuleId, string Name, IReadOnlyList<string> Processes, long PrivateBytes);

public sealed record Receipt(
    string RunId, DateTime StartedUtc, DateTime? FinishedUtc,
    long FreeBytesBefore, long? FreeBytesAfter, long DeferredBytes,
    int StartupEnabledBefore, int? StartupEnabledAfter,
    IReadOnlyList<StartupRam> StartupRam, IReadOnlyList<string> StillStarted, bool StartupRamConfirmed,
    int? BootMsBefore, int? BootMsAfter,
    int PrivacyOff, int PrivacyIgnored,
    int? TelemetryBefore, int? TelemetryAfter)
{
    /// The real change in free space — what Explorer will show — not a sum of
    /// file sizes.
    public long? FreedBytes => FreeBytesAfter is { } after ? Math.Max(0, after - FreeBytesBefore) : null;

    /// Claimed only once the next boot has shown which programs stayed off.
    public long? RamFreedBytes => StartupRamConfirmed
        ? StartupRam.Where(s => !StillStarted.Contains(s.Name)).Sum(s => s.PrivateBytes)
        : null;
}

public sealed class ReceiptStore
{
    private readonly string _dir;

    /// Creates nothing until the first Save, like RunStore.
    public ReceiptStore(string directory) { _dir = directory; }

    public void Save(Receipt receipt)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, $"{receipt.RunId}.json"), JsonSerializer.Serialize(receipt));
    }

    public Receipt? Load(string runId)
    {
        var path = Path.Combine(_dir, $"{runId}.json");
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path)); }
        catch (JsonException) { return null; }
    }

    public Receipt? Latest() => !Directory.Exists(_dir) ? null : Directory.EnumerateFiles(_dir, "*.json")
        .OrderByDescending(f => f, StringComparer.Ordinal)
        .Select(f => Load(Path.GetFileNameWithoutExtension(f)))
        .FirstOrDefault(r => r is not null);
}

public static class ReceiptCalculator
{
    public const int BootsBefore = 5;
    public static readonly TimeSpan SettleAfterBoot = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan TelemetrySettle = TimeSpan.FromMinutes(2);

    public static int? MedianBootMs(IReadOnlyList<BootRecord> boots, DateTime beforeUtc)
    {
        var ms = boots.Where(b => b.When < beforeUtc)
            .OrderByDescending(b => b.When).Take(BootsBefore)
            .Select(b => b.BootMs).OrderBy(x => x).ToList();
        return ms.Count == 0 ? null : ms[(ms.Count - 1) / 2];
    }

    public static Receipt Complete(Receipt r, IReadOnlyList<BootRecord> boots,
        IReadOnlyList<ProcSample> processes, int? telemetryNow, DateTime nowUtc)
    {
        if (r.FinishedUtc is not { } finished) return r;
        var result = r;

        var firstBoot = boots.Where(b => b.When > finished && b.When <= nowUtc)
            .OrderBy(b => b.When).FirstOrDefault();
        if (firstBoot is not null)
        {
            result = result with { BootMsAfter = firstBoot.BootMs };
            if (!result.StartupRamConfirmed && nowUtc >= firstBoot.When + SettleAfterBoot)
            {
                var running = processes.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var still = r.StartupRam.Where(s => s.Processes.Any(running.Contains))
                    .Select(s => s.Name).ToList();
                result = result with { StillStarted = still, StartupRamConfirmed = true };
            }
        }

        if (result.TelemetryAfter is null && telemetryNow is not null && nowUtc >= finished + TelemetrySettle)
            result = result with { TelemetryAfter = telemetryNow };
        return result;
    }
}

public sealed class ReceiptRecorder : IReceiptRecorder
{
    private readonly DiagnosticContext _ctx;
    private readonly StartupManager _startup;
    private readonly IProcessSnapshotProbe _processes;
    private readonly ReceiptStore _store;
    private readonly Func<int?> _telemetryCount;
    private readonly Func<DateTime> _now;
    private Receipt? _current;

    public ReceiptRecorder(DiagnosticContext ctx, StartupManager startup, IProcessSnapshotProbe processes,
        ReceiptStore store, Func<int?> telemetryCount, Func<DateTime>? now = null)
    {
        _ctx = ctx;
        _startup = startup;
        _processes = processes;
        _store = store;
        _telemetryCount = telemetryCount;
        _now = now ?? (() => DateTime.UtcNow);
    }

    private long FreeBytes()
    {
        try { return _ctx.Disk.FreeBytes(Path.GetPathRoot(Environment.SystemDirectory)!); }
        catch (Exception) { return 0; }
    }

    private int? Telemetry()
    {
        try { return _telemetryCount(); }
        catch (Exception) { return null; }
    }

    public void Before(string runId, ActionPlan plan, ActionSelection selection)
    {
        var entries = _startup.List();
        var processes = _processes.Snapshot();
        var ram = new List<StartupRam>();
        foreach (var item in plan.Items.Where(i => selection.RowIds.Contains(i.Id)))
        {
            if (!StartupEntryRule.TryParseId(item.Id, out var hive, out var name)) continue;
            var entry = entries.FirstOrDefault(e =>
                string.Equals(e.Hive, hive, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (entry is null) continue;
            var matched = processes.Where(p => StartupProcessMatch.Matches(entry, p.Name)).ToList();
            ram.Add(new StartupRam(item.Id, item.Title,
                matched.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                matched.Sum(p => p.PrivateBytes)));
        }

        var started = _now();
        _current = new Receipt(runId, started, null, FreeBytes(), null, 0,
            entries.Count(e => e.Enabled), null, ram, Array.Empty<string>(), false,
            ReceiptCalculator.MedianBootMs(_ctx.EventLog.RecentBoots(20), started), null,
            0, 0, Telemetry(), null);
        _store.Save(_current);
    }

    public void After(RunRecord run)
    {
        if (_current is null || _current.RunId != run.RunId) return;
        var privacy = run.Rows.Where(r => r.RowId.StartsWith("privacy.", StringComparison.Ordinal)).ToList();
        _current = _current with
        {
            FinishedUtc = run.FinishedUtc,
            FreeBytesAfter = FreeBytes(),
            DeferredBytes = run.Rows.Sum(r => r.BytesDeferred),
            StartupEnabledAfter = _startup.List().Count(e => e.Enabled),
            // A row Windows put back on, or ignored, is not "off": the ignored
            // ones have their own number.
            PrivacyOff = privacy.Count(r => r.Outcome is RowOutcome.Done or RowOutcome.PendingRestart
                && r.ReadBack is not (ReadBackState.Reverted or ReadBackState.WrittenButIgnored)),
            PrivacyIgnored = privacy.Count(r => r.ReadBack == ReadBackState.WrittenButIgnored),
        };
        _store.Save(_current);
    }

    /// Fills in what has become measurable since the run (the next boot, the
    /// telemetry count two minutes on) and saves it.
    public Receipt? Refresh(string runId)
    {
        var r = _store.Load(runId);
        if (r is null) return null;
        r = ReceiptCalculator.Complete(r, _ctx.EventLog.RecentBoots(20), _processes.Snapshot(), Telemetry(), _now());
        _store.Save(r);
        return r;
    }
}
