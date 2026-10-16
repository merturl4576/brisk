using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BriskEngine.Actions.Rules;
using BriskEngine.Diagnostics;

namespace BriskEngine.Live;

public sealed record AppLoad(string Key, string DisplayName, double CpuPercent, long PrivateBytes,
    bool IsSystem, string? NoteKey, string? Note, IReadOnlyList<int> Pids, bool HasWindow);

/// What is slowing the machine down now: CPU over a 10-second window (of the
/// whole machine, all cores) and private memory, grouped by app.
public sealed class LoadSampler
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    public const double CpuThresholdPercent = 10;
    public const long RamThresholdBytes = 500L * 1024 * 1024;
    public const int MaxRows = 5;

    private static readonly Dictionary<string, (string Key, string English)> Notes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["MsMpEng"] = ("action.load.note.defender", "Defender is scanning"),
            ["TiWorker"] = ("action.load.note.update", "Windows is installing updates"),
            ["TrustedInstaller"] = ("action.load.note.update", "Windows is installing updates"),
            ["MoUsoCoreWorker"] = ("action.load.note.update", "Windows is installing updates"),
            ["SearchIndexer"] = ("action.load.note.search", "Windows is indexing for search"),
            ["Memory Compression"] = ("action.load.note.memory", "Windows is compressing memory"),
            ["CompatTelRunner"] = ("action.load.note.compat", "Windows is collecting compatibility data"),
            ["dwm"] = ("action.load.note.dwm", "Windows is drawing the screen"),
            ["System"] = ("action.load.note.system", "Windows itself"),
            ["svchost"] = ("action.load.note.services", "Windows services"),
        };

    private static readonly HashSet<string> Ignored =
        new(StringComparer.OrdinalIgnoreCase) { "Idle", "brisk", "brisk-app" };

    private readonly IProcessSnapshotProbe _probe;
    private readonly IAppNamer _namer;
    private readonly Action<TimeSpan> _wait;

    public LoadSampler(IProcessSnapshotProbe probe, IAppNamer namer, Action<TimeSpan>? wait = null)
    {
        _probe = probe;
        _namer = namer;
        _wait = wait ?? Thread.Sleep;
    }

    public IReadOnlyList<AppLoad> Sample()
    {
        var first = _probe.Snapshot().GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First());
        _wait(Window);
        var second = _probe.Snapshot();
        var capacityMs = Window.TotalMilliseconds * Math.Max(1, _probe.ProcessorCount);

        return second
            .Where(p => !Ignored.Contains(p.Name))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                double cpuMs = 0;
                foreach (var p in g)
                    if (p.Cpu is { } now && first.TryGetValue(p.Pid, out var before)
                        && before.Cpu is { } then && now >= then)
                        cpuMs += (now - then).TotalMilliseconds;
                string? noteKey = null, note = null;
                if (Notes.TryGetValue(g.Key, out var n)) { noteKey = n.Key; note = n.English; }
                var exe = g.Select(p => p.ExePath).FirstOrDefault(e => e is not null);
                return new AppLoad(g.Key, _namer.Name(g.Key, exe),
                    Math.Round(cpuMs / capacityMs * 100, 1), g.Sum(p => p.PrivateBytes),
                    g.All(p => p.SessionId == 0) || noteKey is not null, noteKey, note,
                    g.Select(p => p.Pid).ToList(), g.Any(p => p.HasWindow));
            })
            .Where(a => a.CpuPercent >= CpuThresholdPercent || a.PrivateBytes >= RamThresholdBytes)
            .OrderByDescending(a => a.CpuPercent)
            .ThenByDescending(a => a.PrivateBytes)
            .Take(MaxRows)
            .ToList();
    }

    public static bool CanClose(AppLoad load) => !load.IsSystem;

    /// The startup entry that starts this app, so its row can offer a ticked
    /// "remove from startup" box.
    public static string? StartupIdFor(AppLoad load, IReadOnlyList<StartupEntry> entries) =>
        entries.Where(e => e.Enabled && StartupProcessMatch.Matches(e, load.Key))
            .Select(e => StartupEntryRule.IdFor(e.Hive, e.Name))
            .FirstOrDefault();
}

public static class StartupProcessMatch
{
    /// A Run command names the exe ("Spotify.exe") or hands it to an updater
    /// ("Update.exe --processStart Discord.exe"); a shortcut is named after
    /// the app ("Wispr Flow.lnk").
    public static bool Matches(StartupEntry entry, string processName)
    {
        var stem = RealAppNamer.Stem(processName);
        if (stem.Length == 0) return false;
        var name = entry.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? entry.Name[..^4] : entry.Name;
        if (string.Equals(name, stem, StringComparison.OrdinalIgnoreCase)) return true;
        if (StartupManager.ExecutableName(entry.Command) is { } exe
            && string.Equals(RealAppNamer.Stem(exe), stem, StringComparison.OrdinalIgnoreCase)) return true;
        return entry.Command?.Contains(stem + ".exe", StringComparison.OrdinalIgnoreCase) ?? false;
    }
}
