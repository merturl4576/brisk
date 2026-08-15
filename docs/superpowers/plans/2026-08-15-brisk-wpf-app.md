# brisk Plan B: WPF Tray App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `Brisk` WPF app — tray icon + flyout + compact detail window ("panel is the face, window is the kitchen") — on top of the finished BriskEngine, plus the small engine additions the GUI needs.

**Architecture:** Engine additions first (health score, scan progress, journal/log queries, startup manager, CLI `--target`), then the WPF app: a composition root wires real probes exactly like the CLI; every view model talks only to an `IEngineHost` facade so all VM logic is unit-testable with fakes. Three surfaces share one visual language (Windows 11 panel material) defined in theme resource dictionaries.

**Tech Stack:** .NET 8 WPF (`net8.0-windows`, x64), WinForms `NotifyIcon` for the tray (in-box, no NuGet), xUnit. No new package references anywhere.

**Spec:** `docs/superpowers/specs/2026-08-14-brisk-design.md` — sections "UI / UX" (rev 2026-08-15, tray flyout + detail window) and "Engine additions for the GUI (Plan B)". Read it first.

## Global Constraints

- Work in `C:\Users\MERT\Desktop\brisk`; ALWAYS `git -C C:\Users\MERT\Desktop\brisk ...` (the Desktop folder belongs to an unrelated outer repo — never commit there).
- Branch: create `feat/wpf-app` from `feat/engine-cli` before Task 1: `git -C C:\Users\MERT\Desktop\brisk checkout -b feat/wpf-app feat/engine-cli`.
- TargetFramework `net8.0-windows`, `<Platforms>x64</Platforms>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in every csproj (new ones too).
- NO new NuGet packages. Tray = WinForms NotifyIcon (`UseWindowsForms`), COM via late binding.
- The app NEVER runs wholesale as admin, no elevation manifest. Elevation is per-action: shell out to `Brisk.Cli.exe` with `Verb = "runas"`.
- No mutation without explicit consent; every mutation goes through the engine (FixRunner / CleanRunner / StartupManager) — view models never touch registry/filesystem directly, only `IEngineHost`.
- No background work: no timers, no scheduled scans, no telemetry. Scans run only from a user action (including app launch, which is a user action).
- Engine strings stay English with stable `TitleKey`s; GUI strings live in `Strings.resx` (EN) + `Strings.tr.resx` (TR) — never hardcode user-visible text in XAML/VMs.
- Run tests with `dotnet test` from the repo root; it must stay green after every task.
- Commit after every task (Conventional Commits, English).
- This machine is tr-TR: never use culture-sensitive case-folding on identifiers (the Turkish-İ problem) — always `StringComparison.OrdinalIgnoreCase`.

## File Structure

```
src/BriskEngine/Diagnostics/HealthScore.cs          score formula (Task 1)
src/BriskEngine/Models/ScanModels.cs                + ScanProgress record (Task 2)
src/BriskEngine/Cleaning/Scanner.cs                 + progress reporting (Task 2)
src/BriskEngine/Diagnostics/FixJournal.cs           + ListUndoable (Task 3)
src/BriskEngine/Logging/ActionLogReader.cs          read/parse action log (Task 3)
src/BriskEngine/Diagnostics/StartupManager.cs       per-item startup toggling (Task 4)
src/BriskEngine/Diagnostics/Rules/StartupBloatRule.cs  delegates shared tables (Task 4)
src/Brisk.Cli/CliParser.cs + Program.cs             clean --target (Task 5)
src/Brisk/Brisk.csproj, App.xaml(.cs)               WPF app shell (Tasks 6, 17)
src/Brisk/ViewModels/ViewModelBase.cs               INPC + RelayCommand (Task 6)
src/Brisk/Services/Settings.cs                      settings.json store (Task 7)
src/Brisk/Services/StartupLauncher.cs               HKCU Run toggle for brisk itself (Task 7)
src/Brisk/Theming/ThemeResolver.cs, ThemeManager.cs, Dark.xaml, Light.xaml, Shared.xaml (Tasks 8, 15)
src/Brisk/Localization/Loc.cs, Strings.resx, Strings.tr.resx (Task 9)
src/Brisk/Services/ScanSnapshot.cs, IEngineHost.cs, EngineHost.cs, CleanService.cs, AppServices.cs (Task 10)
src/Brisk/ViewModels/AppState.cs, FlyoutViewModel.cs (Task 11)
src/Brisk/ViewModels/HealthViewModel.cs             (Task 12)
src/Brisk/Services/IRecycleBinSession.cs, ShellRecycleBinSession.cs (Task 13)
src/Brisk/ViewModels/CleanViewModel.cs              (Task 13)
src/Brisk/ViewModels/LogViewModel.cs, SettingsViewModel.cs (Task 14)
src/Brisk/Windows/FlyoutWindow.xaml(.cs)            (Task 15)
src/Brisk/ViewModels/MainViewModel.cs, Windows/MainWindow.xaml(.cs), Views/*.xaml (Task 16)
src/Brisk/Tray/TrayIcon.cs                          (Task 17)
src/Brisk.Tests/                                    xUnit for everything under src/Brisk
```

---

### Task 1: HealthScore (engine)

**Files:**
- Create: `src/BriskEngine/Diagnostics/HealthScore.cs`
- Test: `src/BriskEngine.Tests/HealthScoreTests.cs`

**Interfaces:**
- Consumes: `DiagnosticFinding`, `Severity` (existing).
- Produces: `static int HealthScore.Compute(IReadOnlyList<DiagnosticFinding> findings)` — 0–100, floor 5.

- [ ] **Step 1: Write the failing tests**

`src/BriskEngine.Tests/HealthScoreTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

public class HealthScoreTests
{
    private static DiagnosticFinding F(Severity sev, int stars) => new(
        "r", "rule.r.title", "T", "E", sev, RuleCategory.Auto, stars, true, null);

    [Fact]
    public void NoFindings_Is100() =>
        Assert.Equal(100, HealthScore.Compute(Array.Empty<DiagnosticFinding>()));

    [Fact]
    public void Warning4Stars_Subtracts12() =>
        Assert.Equal(88, HealthScore.Compute(new List<DiagnosticFinding>
            { F(Severity.Warning, 4) }));

    [Fact]
    public void MixedSeverities_SumPenalties()
    {
        // Critical 5*5=25, Warning 3*3=9, Info 2*1=2 -> 100-36=64
        var findings = new List<DiagnosticFinding>
            { F(Severity.Critical, 5), F(Severity.Warning, 3), F(Severity.Info, 2) };
        Assert.Equal(64, HealthScore.Compute(findings));
    }

    [Fact]
    public void ManyFindings_FloorsAt5()
    {
        var findings = Enumerable.Repeat(F(Severity.Critical, 5), 30).ToList();
        Assert.Equal(5, HealthScore.Compute(findings));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests --filter HealthScoreTests`
Expected: build FAILS — `HealthScore` missing.

- [ ] **Step 3: Implement**

`src/BriskEngine/Diagnostics/HealthScore.cs`:

```csharp
using System;
using System.Collections.Generic;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics;

public static class HealthScore
{
    /// 0-100. Every live finding subtracts ImpactStars x severity weight
    /// (Critical 5, Warning 3, Info 1). Floor 5 so the gauge never reads "dead".
    public static int Compute(IReadOnlyList<DiagnosticFinding> findings)
    {
        var penalty = 0;
        foreach (var f in findings)
            penalty += f.ImpactStars * f.Severity switch
            {
                Severity.Critical => 5,
                Severity.Warning => 3,
                _ => 1,
            };
        return Math.Max(5, 100 - penalty);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/BriskEngine.Tests --filter HealthScoreTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: health score calculator"
```

---

### Task 2: Scanner progress reporting (engine)

**Files:**
- Modify: `src/BriskEngine/Models/ScanModels.cs` (add `ScanProgress`)
- Modify: `src/BriskEngine/Cleaning/Scanner.cs` (report per-target completion)
- Test: `src/BriskEngine.Tests/ScannerProgressTests.cs`

**Interfaces:**
- Produces:
  - `sealed record ScanProgress(int Completed, int Total, string TargetId)`
  - `Scanner.Scan(CancellationToken ct = default, IProgress<ScanProgress>? progress = null)` — existing callers (`Scan()`, `Scan(ct)`) keep compiling; `progress.Report` fires once per finished target with a running completed count.

- [ ] **Step 1: Write the failing tests**

`src/BriskEngine.Tests/ScannerProgressTests.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

file sealed class CollectingProgress : IProgress<ScanProgress>
{
    public ConcurrentBag<ScanProgress> Reports { get; } = new();
    public void Report(ScanProgress value) => Reports.Add(value);
}

file sealed class NoProcesses : IProcessLister
{
    public bool IsRunning(string processName) => false;
}

public sealed class ScannerProgressTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-prog-").FullName;

    private CleanupTarget Target(string id)
    {
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        return new CleanupTarget(id, id, CleanupLevel.Safe,
            new List<string> { dir }, "Test");
    }

    [Fact]
    public void ReportsOncePerTarget_WithStableTotal()
    {
        var targets = new[] { Target("t1"), Target("t2"), Target("t3") };
        var progress = new CollectingProgress();
        new Scanner(targets, new NoProcesses()).Scan(default, progress);

        Assert.Equal(3, progress.Reports.Count);
        Assert.All(progress.Reports, r => Assert.Equal(3, r.Total));
        Assert.Equal(new[] { 1, 2, 3 },
            progress.Reports.Select(r => r.Completed).OrderBy(c => c).ToArray());
        Assert.Equal(new[] { "t1", "t2", "t3" },
            progress.Reports.Select(r => r.TargetId).OrderBy(t => t).ToArray());
    }

    [Fact]
    public void NullProgress_StillScans()
    {
        var result = new Scanner(new[] { Target("t1") }, new NoProcesses()).Scan();
        Assert.Single(result.Targets);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests --filter ScannerProgressTests`
Expected: build FAILS — `ScanProgress` missing / no matching `Scan` overload.

- [ ] **Step 3: Implement**

Append to `src/BriskEngine/Models/ScanModels.cs`:

```csharp
public sealed record ScanProgress(int Completed, int Total, string TargetId);
```

In `src/BriskEngine/Cleaning/Scanner.cs`, replace the `Scan` method with:

```csharp
    public ScanResult Scan(CancellationToken ct = default,
        IProgress<ScanProgress>? progress = null)
    {
        var results = new TargetScanResult[_targets.Count];
        var completed = 0;
        Parallel.For(0, _targets.Count,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            i =>
            {
                results[i] = ScanTarget(_targets[i], ct);
                var done = Interlocked.Increment(ref completed);
                progress?.Report(new ScanProgress(done, _targets.Count, _targets[i].Id));
            });
        return new ScanResult(results);
    }
```

(`using System.Threading;` is already present.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/BriskEngine.Tests`
Expected: PASS — new tests plus the whole existing suite (97+) stay green.

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: scanner progress reporting"
```

---

### Task 3: FixJournal.ListUndoable + ActionLogReader (engine)

**Files:**
- Modify: `src/BriskEngine/Diagnostics/FixJournal.cs`
- Create: `src/BriskEngine/Logging/ActionLogReader.cs`
- Test: `src/BriskEngine.Tests/JournalQueryTests.cs`

**Interfaces:**
- Consumes: existing journal line format `{"RuleId","Action","PriorState","Ts"}`; action-log lines written by `FixRunner` (`{ts, ruleId, action}`) and `CleanRunner` (`{ts, targetId, path, bytes, action, reason}`).
- Produces:
  - `sealed record UndoableFix(string RuleId, DateTime FixedAtUtc)` (top level in FixJournal.cs)
  - `IReadOnlyList<UndoableFix> FixJournal.ListUndoable()` — newest first; a later `undo` removes the rule from the list.
  - `sealed record ActionLogEntry(DateTime TsUtc, string Kind, string Summary, string Raw)` — `Kind` is `"fix"`, `"clean"` or `"other"`.
  - `static IReadOnlyList<ActionLogEntry> ActionLogReader.ReadTail(string path, int max = 200)` — newest first, malformed lines skipped, missing file → empty list.
  - `static long ActionLogReader.TotalRecycledBytes(string path)` — sum of `bytes` over every `"recycled"` clean line in the WHOLE file (the spec's "lifetime total"; the action log is its single source of truth).

- [ ] **Step 1: Write the failing tests**

`src/BriskEngine.Tests/JournalQueryTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using Xunit;

namespace BriskEngine.Tests;

public sealed class JournalQueryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-jq-").FullName;

    [Fact]
    public void ListUndoable_TracksFixThenUndo()
    {
        var journal = new FixJournal(Path.Combine(_root, "j.jsonl"));
        journal.RecordFix("power-plan", "{}");
        journal.RecordFix("visual-effects", "{}");
        journal.RecordUndo("power-plan");

        var undoable = journal.ListUndoable();
        Assert.Single(undoable);
        Assert.Equal("visual-effects", undoable[0].RuleId);
    }

    [Fact]
    public void ListUndoable_RefixAfterUndo_IsUndoableAgain()
    {
        var journal = new FixJournal(Path.Combine(_root, "j2.jsonl"));
        journal.RecordFix("power-plan", "{}");
        journal.RecordUndo("power-plan");
        journal.RecordFix("power-plan", "{}");
        Assert.Equal("power-plan", Assert.Single(journal.ListUndoable()).RuleId);
    }

    [Fact]
    public void ListUndoable_EmptyJournal_IsEmpty() =>
        Assert.Empty(new FixJournal(Path.Combine(_root, "j3.jsonl")).ListUndoable());

    [Fact]
    public void ReadTail_ParsesFixAndCleanLines_NewestFirst()
    {
        var path = Path.Combine(_root, "log.jsonl");
        File.WriteAllLines(path, new[]
        {
            """{"ts":"2026-08-15T10:00:00Z","ruleId":"power-plan","action":"fix"}""",
            "not json at all",
            """{"ts":"2026-08-15T11:00:00Z","targetId":"user-temp","path":"C:\\t\\x.tmp","bytes":2048,"action":"recycled","reason":null}""",
        });

        var entries = ActionLogReader.ReadTail(path);
        Assert.Equal(2, entries.Count);
        Assert.Equal("clean", entries[0].Kind);
        Assert.Contains("user-temp", entries[0].Summary);
        Assert.Contains("2 KB", entries[0].Summary);
        Assert.Equal("fix", entries[1].Kind);
        Assert.Contains("power-plan", entries[1].Summary);
    }

    [Fact]
    public void ReadTail_MissingFile_IsEmpty() =>
        Assert.Empty(ActionLogReader.ReadTail(Path.Combine(_root, "nope.jsonl")));

    [Fact]
    public void TotalRecycledBytes_SumsOnlyRecycledLines()
    {
        var path = Path.Combine(_root, "life.jsonl");
        File.WriteAllLines(path, new[]
        {
            """{"ts":"2026-08-15T10:00:00Z","targetId":"user-temp","path":"C:\\a","bytes":100,"action":"recycled","reason":null}""",
            """{"ts":"2026-08-15T10:01:00Z","targetId":"user-temp","path":"C:\\b","bytes":900,"action":"dry-run","reason":null}""",
            """{"ts":"2026-08-15T10:02:00Z","targetId":"npm-cache","path":"C:\\c","bytes":50,"action":"recycled","reason":null}""",
            """{"ts":"2026-08-15T10:03:00Z","ruleId":"power-plan","action":"fix"}""",
        });
        Assert.Equal(150, ActionLogReader.TotalRecycledBytes(path));
        Assert.Equal(0, ActionLogReader.TotalRecycledBytes(Path.Combine(_root, "no.jsonl")));
    }

    [Fact]
    public void ReadTail_RespectsMax()
    {
        var path = Path.Combine(_root, "big.jsonl");
        File.WriteAllLines(path, Enumerable.Range(0, 50).Select(i =>
            $$"""{"ts":"2026-08-15T10:00:{{i:00}}Z","ruleId":"r{{i}}","action":"fix"}"""));
        Assert.Equal(10, ActionLogReader.ReadTail(path, 10).Count);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
```

Note: the `{{i:00}}` interpolation inside a raw string literal — if the compiler
version complains, build the line with plain string concatenation instead; the
content is what matters, not the syntax sugar.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests --filter JournalQueryTests`
Expected: build FAILS — `ListUndoable` / `ActionLogReader` missing.

- [ ] **Step 3: Implement**

In `src/BriskEngine/Diagnostics/FixJournal.cs`: add `using System.Linq;` at the top, add this record above the class, and this method inside the class:

```csharp
public sealed record UndoableFix(string RuleId, System.DateTime FixedAtUtc);
```

```csharp
    public IReadOnlyList<UndoableFix> ListUndoable()
    {
        var last = new Dictionary<string, System.DateTime>();
        foreach (var entry in ReadAll())
        {
            if (entry.Action == "fix" && entry.PriorState is not null)
                last[entry.RuleId] = entry.Ts;
            else if (entry.Action == "undo")
                last.Remove(entry.RuleId);
        }
        return last.Select(kv => new UndoableFix(kv.Key, kv.Value))
            .OrderByDescending(u => u.FixedAtUtc).ToList();
    }
```

`src/BriskEngine/Logging/ActionLogReader.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BriskEngine.Logging;

public sealed record ActionLogEntry(DateTime TsUtc, string Kind, string Summary, string Raw);

public static class ActionLogReader
{
    /// Newest first. Malformed lines are skipped; a missing file is an empty log.
    public static IReadOnlyList<ActionLogEntry> ReadTail(string path, int max = 200)
    {
        if (!File.Exists(path)) return Array.Empty<ActionLogEntry>();
        var entries = new List<ActionLogEntry>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = TryParse(line);
            if (entry is not null) entries.Add(entry);
        }
        entries.Reverse();
        return entries.Take(max).ToList();
    }

    /// Lifetime reclaimed total — every "recycled" clean line ever logged.
    public static long TotalRecycledBytes(string path)
    {
        if (!File.Exists(path)) return 0;
        long total = 0;
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("action", out var a)
                    && a.GetString() == "recycled"
                    && root.TryGetProperty("bytes", out var b)
                    && b.ValueKind == JsonValueKind.Number)
                    total += b.GetInt64();
            }
            catch (JsonException) { }
        }
        return total;
    }

    private static ActionLogEntry? TryParse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var ts = root.TryGetProperty("ts", out var tsEl) && tsEl.TryGetDateTime(out var t)
                ? t.ToUniversalTime() : DateTime.MinValue;
            var action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "?" : "?";

            if (root.TryGetProperty("ruleId", out var rule))
                return new ActionLogEntry(ts, "fix", $"{action}: {rule.GetString()}", line);

            if (root.TryGetProperty("targetId", out var target))
            {
                var itemPath = root.TryGetProperty("path", out var p) ? p.GetString() : null;
                var bytes = root.TryGetProperty("bytes", out var b)
                    && b.ValueKind == JsonValueKind.Number ? b.GetInt64() : 0;
                var reason = root.TryGetProperty("reason", out var r)
                    && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                var summary = $"{action}: {target.GetString()} {itemPath} ({Fmt.Bytes(bytes)})";
                if (reason is not null) summary += $" — {reason}";
                return new ActionLogEntry(ts, "clean", summary, line);
            }

            return new ActionLogEntry(ts, "other", action, line);
        }
        catch (JsonException) { return null; }
        catch (FormatException) { return null; }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/BriskEngine.Tests`
Expected: PASS, full suite green.

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: undoable-fix listing and action log reader"
```

---

### Task 4: StartupManager (engine) — per-item startup toggling

**Files:**
- Create: `src/BriskEngine/Diagnostics/StartupManager.cs`
- Modify: `src/BriskEngine/Diagnostics/Rules/StartupBloatRule.cs` (delegate shared tables to StartupManager)
- Test: `src/BriskEngine.Tests/StartupManagerTests.cs`

**Interfaces:**
- Consumes: `IRegistryProbe`, `ActionLog` (existing).
- Produces:
  - `sealed record StartupEntry(string Hive, string Name, bool Enabled, bool KnownHeavy)` — `Hive` is `"HKCU"` or `"HKLM"`.
  - `class StartupManager`:
    - `StartupManager(IRegistryProbe registry, ActionLog? log)`
    - `IReadOnlyList<StartupEntry> List()` — registry Run entries from both hives with their StartupApproved enabled state.
    - `bool SetEnabled(string hive, string name, bool enabled)` — writes StartupApproved bytes (`0x03…` = disabled, `0x02…` = enabled, 12 bytes); returns `false` on `UnauthorizedAccessException` (HKLM unelevated); appends `{ts, startup, hive, action}` to the log when it succeeds.
  - `StartupManager.Hives` (`internal static`), `public static bool IsHeavy(string name)`, `public static readonly IReadOnlySet<string> KnownHeavy` — moved here from `StartupBloatRule`; the rule's own copies are DELETED and its code now references `StartupManager.*`.

- [ ] **Step 1: Write the failing tests**

`src/BriskEngine.Tests/StartupManagerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using Xunit;

namespace BriskEngine.Tests;

file sealed class FakeStartupRegistry : IRegistryProbe
{
    public Dictionary<string, Dictionary<string, object>> Keys { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DeniedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, object> Key(string k) =>
        Keys.TryGetValue(k, out var d) ? d : Keys[k] = new(StringComparer.OrdinalIgnoreCase);

    public string? GetString(string k, string v) => Key(k).TryGetValue(v, out var o) ? o as string : null;
    public void SetString(string k, string v, string value) { Deny(k); Key(k)[v] = value; }
    public void DeleteValue(string k, string v) { Deny(k); Key(k).Remove(v); }
    public byte[]? GetBytes(string k, string v) => Key(k).TryGetValue(v, out var o) ? o as byte[] : null;
    public void SetBytes(string k, string v, byte[] value) { Deny(k); Key(k)[v] = value; }
    public int? GetInt(string k, string v) => Key(k).TryGetValue(v, out var o) ? o as int? : null;
    public void SetInt(string k, string v, int value) { Deny(k); Key(k)[v] = value; }
    public IReadOnlyList<string> GetValueNames(string k) => Key(k).Keys.ToList();
    public IReadOnlyList<string> GetSubKeyNames(string k) => Array.Empty<string>();
    private void Deny(string k) { if (DeniedKeys.Contains(k)) throw new UnauthorizedAccessException(k); }
}

public sealed class StartupManagerTests : IDisposable
{
    private const string HkcuRun = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string HkcuApproved =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string HklmRun = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string HklmApproved =
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly string _root = Directory.CreateTempSubdirectory("brisk-sm-").FullName;
    private readonly FakeStartupRegistry _reg = new();

    private StartupManager Manager() =>
        new(_reg, new ActionLog(Path.Combine(_root, "log.jsonl")));

    [Fact]
    public void List_ReportsEnabledStateAndHeavyFlag()
    {
        _reg.SetString(HkcuRun, "Discord", "x.exe");
        _reg.SetString(HkcuRun, "MyTool", "y.exe");
        _reg.SetBytes(HkcuApproved, "MyTool",
            new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        var items = Manager().List();
        var discord = items.Single(i => i.Name == "Discord");
        var mytool = items.Single(i => i.Name == "MyTool");
        Assert.True(discord.Enabled);
        Assert.True(discord.KnownHeavy);
        Assert.False(mytool.Enabled);
        Assert.False(mytool.KnownHeavy);
        Assert.Equal("HKCU", discord.Hive);
    }

    [Fact]
    public void SetEnabled_False_WritesDisabledBytes_AndLogs()
    {
        _reg.SetString(HkcuRun, "Spotify", "s.exe");
        Assert.True(Manager().SetEnabled("HKCU", "Spotify", false));
        var bytes = _reg.GetBytes(HkcuApproved, "Spotify")!;
        Assert.Equal(12, bytes.Length);
        Assert.Equal(1, bytes[0] & 1);
        var log = File.ReadAllText(Path.Combine(_root, "log.jsonl"));
        Assert.Contains("Spotify", log);
        Assert.Contains("disable", log);
    }

    [Fact]
    public void SetEnabled_True_WritesEnabledBytes()
    {
        _reg.SetString(HkcuRun, "Spotify", "s.exe");
        var mgr = Manager();
        mgr.SetEnabled("HKCU", "Spotify", false);
        Assert.True(mgr.SetEnabled("HKCU", "Spotify", true));
        Assert.Equal(0, _reg.GetBytes(HkcuApproved, "Spotify")![0] & 1);
    }

    [Fact]
    public void SetEnabled_DeniedHive_ReturnsFalse()
    {
        _reg.SetString(HklmRun, "Svc", "s.exe");
        _reg.DeniedKeys.Add(HklmApproved);
        Assert.False(Manager().SetEnabled("HKLM", "Svc", false));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests --filter StartupManagerTests`
Expected: build FAILS — `StartupManager` missing.

- [ ] **Step 3: Implement StartupManager**

`src/BriskEngine/Diagnostics/StartupManager.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Logging;

namespace BriskEngine.Diagnostics;

public sealed record StartupEntry(string Hive, string Name, bool Enabled, bool KnownHeavy);

/// Owner of the Run/StartupApproved tables. StartupBloatRule detects/fixes in
/// bulk; this class gives the GUI per-item listing and toggling. Both share
/// the same hive table and heavy-app list so they can never disagree.
public sealed class StartupManager
{
    public static readonly IReadOnlySet<string> KnownHeavy = new HashSet<string>(
        new[] { "Steam", "Discord", "Spotify", "Docker Desktop", "EpicGamesLauncher",
                "WhatsApp", "Teams", "BlueStacks", "WallpaperEngine" },
        StringComparer.OrdinalIgnoreCase);

    internal static readonly (string Hive, string Run, string Approved)[] Hives =
    {
        ("HKCU", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                 @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
        ("HKLM", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                 @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
    };

    public static bool IsHeavy(string name) => KnownHeavy.Any(h =>
        name.Contains(h, StringComparison.OrdinalIgnoreCase));

    private static readonly byte[] DisabledBytes = { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    private static readonly byte[] EnabledBytes = { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    private readonly IRegistryProbe _registry;
    private readonly ActionLog? _log;

    public StartupManager(IRegistryProbe registry, ActionLog? log)
    {
        _registry = registry;
        _log = log;
    }

    public IReadOnlyList<StartupEntry> List()
    {
        var items = new List<StartupEntry>();
        foreach (var (hive, run, approved) in Hives)
        foreach (var name in _registry.GetValueNames(run))
        {
            var bytes = _registry.GetBytes(approved, name);
            var disabled = bytes is { Length: > 0 } && (bytes[0] & 1) == 1;
            items.Add(new StartupEntry(hive, name, !disabled, IsHeavy(name)));
        }
        return items;
    }

    /// Returns false when the hive denies the write (HKLM without elevation).
    public bool SetEnabled(string hive, string name, bool enabled)
    {
        var approved = Hives.FirstOrDefault(h => h.Hive == hive).Approved;
        if (approved is null) return false;
        try
        {
            _registry.SetBytes(approved, name, enabled ? EnabledBytes : DisabledBytes);
        }
        catch (UnauthorizedAccessException) { return false; }
        _log?.Append(new { ts = DateTime.UtcNow, startup = name, hive,
            action = enabled ? "enable" : "disable" });
        return true;
    }
}
```

- [ ] **Step 4: Point StartupBloatRule at the shared tables**

In `src/BriskEngine/Diagnostics/Rules/StartupBloatRule.cs`:
- Delete the rule's `KnownHeavy` set, its `Hives` array, and its private `IsHeavy` method.
- Replace every use inside the rule: `Hives` → `StartupManager.Hives`, `IsHeavy(` → `StartupManager.IsHeavy(`.
- Search the whole repo for other references to the deleted members (`StartupBloatRule.KnownHeavy` appears in RamPressureRule or tests?): run `git -C C:\Users\MERT\Desktop\brisk grep -n "StartupBloatRule.KnownHeavy"` and switch any hit to `StartupManager.KnownHeavy`.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS — StartupManagerTests plus the untouched StartupBloatRuleTests all green (rule behavior is unchanged).

- [ ] **Step 6: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: startup manager with per-item toggling"
```

---

### Task 5: CLI `clean --target <id>` (per-target elevation vehicle for the GUI)

**Files:**
- Modify: `src/Brisk.Cli/CliParser.cs` (add `Target`)
- Modify: `src/Brisk.Cli/Program.cs` (target selection + help text)
- Test: extend `src/BriskEngine.Tests/CliParserTests.cs`, create `src/BriskEngine.Tests/CleanSelectionTests.cs`

**Interfaces:**
- Produces:
  - `CliCommand` gains `string? Target = null`.
  - `public static (List<TargetScanResult> Selected, string? Error) Program.SelectTargets(ScanResult scan, string? targetId, CleanupLevel level)` — extracted pure selection logic used by `Clean`:
    - `targetId == null` → previous behavior (level filter, skip `RequiresIndividualSelection` and `RequiresExplicitOptIn`).
    - `targetId` set → exactly that target regardless of level; naming it counts as explicit opt-in, but `RequiresIndividualSelection` targets are refused (`Error` contains `"per-item"`); unknown id → `Error` contains `"unknown target"`.
  - GUI usage (Task 10): `Brisk.Cli.exe clean --target windows-temp --yes` under `runas`.

- [ ] **Step 1: Write the failing tests**

Append to the test class in `src/BriskEngine.Tests/CliParserTests.cs` (keep its existing conventions):

```csharp
    [Fact]
    public void Clean_ParsesTarget()
    {
        var cmd = CliParser.Parse(new[] { "clean", "--target", "windows-temp", "--yes" });
        Assert.Equal("clean", cmd.Verb);
        Assert.Equal("windows-temp", cmd.Target);
        Assert.True(cmd.Yes);
    }

    [Fact]
    public void Clean_TargetWithoutValue_IsError()
    {
        Assert.Equal("error", CliParser.Parse(new[] { "clean", "--target" }).Verb);
    }
```

(`--target` with no value falls through to the existing `bad argument` error path.)

`src/BriskEngine.Tests/CleanSelectionTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using Brisk.Cli;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

public class CleanSelectionTests
{
    private static TargetScanResult Scan(string id, CleanupLevel level,
        bool pick = false, bool optIn = false) => new(
        new CleanupTarget(id, id, level, new List<string> { @"C:\x" }, "Test",
            RequiresIndividualSelection: pick, RequiresExplicitOptIn: optIn),
        Array.Empty<ResolvedItem>(), null);

    private static ScanResult Result(params TargetScanResult[] targets) => new(targets);

    [Fact]
    public void NoTarget_FiltersByLevel_ExcludesPickAndOptIn()
    {
        var scan = Result(
            Scan("user-temp", CleanupLevel.Safe),
            Scan("docker-prune", CleanupLevel.Developer, optIn: true),
            Scan("old-installers", CleanupLevel.Deep, pick: true),
            Scan("windows-temp", CleanupLevel.Deep));
        var (selected, error) = Program.SelectTargets(scan, null, CleanupLevel.Deep);
        Assert.Null(error);
        Assert.Equal("windows-temp", Assert.Single(selected).Target.Id);
    }

    [Fact]
    public void Target_SelectsRegardlessOfLevel_AllowsOptIn()
    {
        var scan = Result(Scan("docker-prune", CleanupLevel.Developer, optIn: true));
        var (selected, error) = Program.SelectTargets(scan, "docker-prune", CleanupLevel.Safe);
        Assert.Null(error);
        Assert.Equal("docker-prune", Assert.Single(selected).Target.Id);
    }

    [Fact]
    public void Target_IndividualSelection_IsRefused()
    {
        var scan = Result(Scan("old-installers", CleanupLevel.Deep, pick: true));
        var (selected, error) = Program.SelectTargets(scan, "old-installers", CleanupLevel.Deep);
        Assert.Empty(selected);
        Assert.Contains("per-item", error);
    }

    [Fact]
    public void Target_Unknown_IsError()
    {
        var (selected, error) = Program.SelectTargets(Result(), "nope", CleanupLevel.Safe);
        Assert.Empty(selected);
        Assert.Contains("unknown target", error);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests --filter "CliParserTests|CleanSelectionTests"`
Expected: build FAILS — `Target` / `SelectTargets` missing.

- [ ] **Step 3: Implement**

`CliParser.cs`: add `string? Target = null` to the `CliCommand` record parameters, and this case to the switch:

```csharp
                case "--target" when i + 1 < args.Length:
                    cmd = cmd with { Target = args[++i] }; break;
```

`Program.cs`: add the selection function to the class:

```csharp
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
```

In `Clean(...)`, replace the three lines building `scan`/`targets`/`selected` with:

```csharp
        var scan = scanner.Scan();
        var (selected, selectError) = SelectTargets(scan, cmd.Target, level);
        if (selectError is not null)
        {
            Console.Error.WriteLine($"brisk: {selectError}");
            return 2;
        }
```

(everything below keeps using `selected`). Add to `PrintHelp()` under the clean options:

```csharp
        Console.WriteLine("    --target <id>            clean a single target by id (see 'brisk targets')");
```

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: cli clean --target for per-target runs"
```

---

### Task 6: Brisk WPF project scaffold + MVVM base + Brisk.Tests

**Files:**
- Create: `src/Brisk/Brisk.csproj`, `src/Brisk/App.xaml`, `src/Brisk/App.xaml.cs`, `src/Brisk/ViewModels/ViewModelBase.cs`, `src/Brisk.Tests/Brisk.Tests.csproj`, `src/Brisk.Tests/ViewModelBaseTests.cs`
- Modify: `brisk.sln` (add both projects)

**Interfaces:**
- Produces:
  - `abstract class ViewModelBase : INotifyPropertyChanged` with `protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)` and `protected void Raise(string name)`.
  - `sealed class RelayCommand : ICommand` — `RelayCommand(Action execute, Func<bool>? canExecute = null)`, `void RaiseCanExecuteChanged()`. Own `event EventHandler? CanExecuteChanged` (NO `CommandManager` — it needs a WPF dispatcher and would break plain unit tests).
  - `Brisk` builds as WinExe with `UseWPF` + `UseWindowsForms`; references `BriskEngine` AND `Brisk.Cli` (so `Brisk.Cli.exe` lands in Brisk's output folder — the elevation vehicle).

- [ ] **Step 1: Scaffold projects**

```powershell
cd C:\Users\MERT\Desktop\brisk
dotnet new wpf -o src/Brisk -n Brisk -f net8.0
dotnet new xunit -o src/Brisk.Tests -n Brisk.Tests -f net8.0
dotnet sln add src/Brisk src/Brisk.Tests
dotnet add src/Brisk reference src/BriskEngine
dotnet add src/Brisk reference src/Brisk.Cli
dotnet add src/Brisk.Tests reference src/Brisk
```

Delete templates: `src/Brisk/MainWindow.xaml` + `.xaml.cs` (real windows come later, under `Windows/`), `src/Brisk.Tests/UnitTest1.cs`.

`src/Brisk/Brisk.csproj` — replace content with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <AssemblyName>brisk-app</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\BriskEngine\BriskEngine.csproj" />
    <ProjectReference Include="..\Brisk.Cli\Brisk.Cli.csproj" />
  </ItemGroup>
</Project>
```

`src/Brisk.Tests/Brisk.Tests.csproj` — same shape as `BriskEngine.Tests.csproj` (copy its xunit/test-sdk PackageReference versions verbatim) plus:

```xml
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
```

and `<ProjectReference Include="..\Brisk\Brisk.csproj" />`.

`src/Brisk/App.xaml` (no `StartupUri` — startup is manual in Task 17):

```xml
<Application x:Class="Brisk.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary />
    </Application.Resources>
</Application>
```

`src/Brisk/App.xaml.cs` (placeholder until Task 17):

```csharp
using System.Windows;

namespace Brisk;

public partial class App : Application
{
}
```

- [ ] **Step 2: Write the failing tests**

`src/Brisk.Tests/ViewModelBaseTests.cs`:

```csharp
using System.Collections.Generic;
using Brisk.ViewModels;
using Xunit;

namespace Brisk.Tests;

file sealed class SampleVm : ViewModelBase
{
    private int _count;
    public int Count { get => _count; set => Set(ref _count, value); }
}

public class ViewModelBaseTests
{
    [Fact]
    public void Set_RaisesPropertyChanged_OnlyOnRealChange()
    {
        var vm = new SampleVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Count = 5;
        vm.Count = 5;
        Assert.Equal(new[] { "Count" }, raised);
    }

    [Fact]
    public void RelayCommand_ExecutesAndGates()
    {
        var ran = 0;
        var allowed = false;
        var cmd = new RelayCommand(() => ran++, () => allowed);

        Assert.False(cmd.CanExecute(null));
        allowed = true;
        Assert.True(cmd.CanExecute(null));
        cmd.Execute(null);
        Assert.Equal(1, ran);
    }

    [Fact]
    public void RelayCommand_RaiseCanExecuteChanged_FiresEvent()
    {
        var cmd = new RelayCommand(() => { });
        var fired = 0;
        cmd.CanExecuteChanged += (_, _) => fired++;
        cmd.RaiseCanExecuteChanged();
        Assert.Equal(1, fired);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test src/Brisk.Tests`
Expected: build FAILS — `ViewModelBase` missing.

- [ ] **Step 4: Implement**

`src/Brisk/ViewModels/ViewModelBase.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Brisk.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name!);
        return true;
    }

    protected void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// Deliberately does NOT use CommandManager: that requires a WPF dispatcher
/// and makes view models untestable from plain xUnit.
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS — both test projects green.

- [ ] **Step 6: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: wpf app scaffold with mvvm base"
```

---

### Task 7: Settings store + StartupLauncher

**Files:**
- Create: `src/Brisk/Services/Settings.cs`, `src/Brisk/Services/StartupLauncher.cs`
- Test: `src/Brisk.Tests/SettingsTests.cs`

**Interfaces:**
- Consumes: `IRegistryProbe` (engine — reused for testability).
- Produces:
  - `sealed class Settings` — properties `string Language` (`"system"|"en"|"tr"`, default `"system"`), `string Theme` (`"system"|"light"|"dark"`, default `"system"`), `bool DryRun` (default false), `bool StartWithWindows` (default false; spec: a tool that criticizes startup bloat does not put itself there). `static Settings Load(string path)` (missing/corrupt → defaults), `void Save(string path)` (creates directory, indented JSON).
  - `sealed class StartupLauncher(IRegistryProbe registry, string exePath)` — `bool IsOn()`, `void Apply(bool on)`: writes/removes value `brisk` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with data `"<exePath>" --tray` (quoted).

- [ ] **Step 1: Write the failing tests**

`src/Brisk.Tests/SettingsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brisk.Services;
using BriskEngine.Diagnostics;
using Xunit;

namespace Brisk.Tests;

file sealed class MemRegistry : IRegistryProbe
{
    public Dictionary<string, Dictionary<string, object>> Keys { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, object> Key(string k) =>
        Keys.TryGetValue(k, out var d) ? d : Keys[k] = new(StringComparer.OrdinalIgnoreCase);

    public string? GetString(string k, string v) => Key(k).TryGetValue(v, out var o) ? o as string : null;
    public void SetString(string k, string v, string value) => Key(k)[v] = value;
    public void DeleteValue(string k, string v) => Key(k).Remove(v);
    public byte[]? GetBytes(string k, string v) => Key(k).TryGetValue(v, out var o) ? o as byte[] : null;
    public void SetBytes(string k, string v, byte[] value) => Key(k)[v] = value;
    public int? GetInt(string k, string v) => Key(k).TryGetValue(v, out var o) ? o as int? : null;
    public void SetInt(string k, string v, int value) => Key(k)[v] = value;
    public IReadOnlyList<string> GetValueNames(string k) => Key(k).Keys.ToList();
    public IReadOnlyList<string> GetSubKeyNames(string k) => Array.Empty<string>();
}

public sealed class SettingsTests : IDisposable
{
    private const string RunKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-set-").FullName;

    [Fact]
    public void Load_MissingFile_GivesDefaults()
    {
        var s = Settings.Load(Path.Combine(_root, "nope", "settings.json"));
        Assert.Equal("system", s.Language);
        Assert.Equal("system", s.Theme);
        Assert.False(s.DryRun);
        Assert.False(s.StartWithWindows);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(_root, "sub", "settings.json");
        new Settings { Language = "tr", Theme = "dark", DryRun = true }.Save(path);
        var s = Settings.Load(path);
        Assert.Equal("tr", s.Language);
        Assert.Equal("dark", s.Theme);
        Assert.True(s.DryRun);
    }

    [Fact]
    public void Load_CorruptFile_GivesDefaults()
    {
        var path = Path.Combine(_root, "bad.json");
        File.WriteAllText(path, "{{{ nope");
        Assert.Equal("system", Settings.Load(path).Language);
    }

    [Fact]
    public void StartupLauncher_OnWritesQuotedCommand_OffRemoves()
    {
        var reg = new MemRegistry();
        var launcher = new StartupLauncher(reg, @"C:\Apps\brisk-app.exe");

        launcher.Apply(true);
        Assert.True(launcher.IsOn());
        Assert.Equal("\"C:\\Apps\\brisk-app.exe\" --tray", reg.GetString(RunKey, "brisk"));

        launcher.Apply(false);
        Assert.False(launcher.IsOn());
        Assert.Null(reg.GetString(RunKey, "brisk"));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Brisk.Tests --filter SettingsTests`
Expected: build FAILS — `Settings` / `StartupLauncher` missing.

- [ ] **Step 3: Implement**

`src/Brisk/Services/Settings.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;

namespace Brisk.Services;

public sealed class Settings
{
    public string Language { get; set; } = "system"; // system | en | tr
    public string Theme { get; set; } = "system";    // system | light | dark
    public bool DryRun { get; set; }
    public bool StartWithWindows { get; set; }       // default OFF, on principle

    public static Settings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))
                    ?? new Settings();
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new Settings();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
```

`src/Brisk/Services/StartupLauncher.cs`:

```csharp
using BriskEngine.Diagnostics;

namespace Brisk.Services;

/// Registers brisk itself under HKCU Run. Default is OFF: a tool that
/// criticizes startup bloat earns trust by staying out of startup unless asked.
public sealed class StartupLauncher
{
    private const string RunKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "brisk";

    private readonly IRegistryProbe _registry;
    private readonly string _exePath;

    public StartupLauncher(IRegistryProbe registry, string exePath)
    {
        _registry = registry;
        _exePath = exePath;
    }

    public bool IsOn() => _registry.GetString(RunKey, ValueName) is not null;

    public void Apply(bool on)
    {
        if (on) _registry.SetString(RunKey, ValueName, $"\"{_exePath}\" --tray");
        else _registry.DeleteValue(RunKey, ValueName);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: settings store and start-with-windows toggle"
```

---

### Task 8: ThemeResolver + theme dictionaries + ThemeManager

**Files:**
- Create: `src/Brisk/Theming/ThemeResolver.cs`, `src/Brisk/Theming/ThemeManager.cs`, `src/Brisk/Theming/Dark.xaml`, `src/Brisk/Theming/Light.xaml`
- Test: `src/Brisk.Tests/ThemeResolverTests.cs`

**Interfaces:**
- Produces:
  - `static string ThemeResolver.Resolve(string setting, Func<int?> appsUseLightTheme)` → `"light"` or `"dark"`. `"light"`/`"dark"` pass through; `"system"` asks the delegate (registry value `AppsUseLightTheme`): `1`→light, `0`→dark, `null` (value missing) → light (Windows default).
  - `static System.Windows.Media.Color ThemeResolver.AccentFrom(int? colorizationColor)` — parses the DWM `ColorizationColor` DWORD as `0xAARGBB…` → Color (alpha forced to `FF`); `null` → default `#FF4CC2FF`.
  - `sealed class ThemeManager` — `void Apply(string setting)`: resolves via real registry (`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`, `HKCU\Software\Microsoft\Windows\DWM\ColorizationColor`), swaps `Dark.xaml`/`Light.xaml` into `Application.Current.Resources.MergedDictionaries`, sets `AccentBrush`. `string Current { get; }` (`"light"`/`"dark"`).
  - Brush/resource keys every later XAML task binds to: `Bg`, `BgElevated`, `BgHover`, `BorderBrushKey`, `Divider`, `Text`, `TextMuted`, `TextFaint`, `AccentBrush`, `AccentTextBrush`, `SeverityInfo`, `SeverityWarning`, `SeverityCritical`, `Good`.

- [ ] **Step 1: Write the failing tests**

`src/Brisk.Tests/ThemeResolverTests.cs`:

```csharp
using System.Windows.Media;
using Brisk.Theming;
using Xunit;

namespace Brisk.Tests;

public class ThemeResolverTests
{
    [Fact]
    public void ExplicitSettings_PassThrough()
    {
        Assert.Equal("dark", ThemeResolver.Resolve("dark", () => 1));
        Assert.Equal("light", ThemeResolver.Resolve("light", () => 0));
    }

    [Fact]
    public void System_FollowsRegistryValue()
    {
        Assert.Equal("light", ThemeResolver.Resolve("system", () => 1));
        Assert.Equal("dark", ThemeResolver.Resolve("system", () => 0));
        Assert.Equal("light", ThemeResolver.Resolve("system", () => null));
    }

    [Fact]
    public void AccentFrom_ParsesDword_ForcesOpaque()
    {
        var color = ThemeResolver.AccentFrom(unchecked((int)0xC40078D4));
        Assert.Equal(Color.FromArgb(0xFF, 0x00, 0x78, 0xD4), color);
    }

    [Fact]
    public void AccentFrom_Null_IsDefaultBlue()
    {
        Assert.Equal(Color.FromArgb(0xFF, 0x4C, 0xC2, 0xFF), ThemeResolver.AccentFrom(null));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Brisk.Tests --filter ThemeResolverTests`
Expected: build FAILS — `ThemeResolver` missing.

- [ ] **Step 3: Implement resolver**

`src/Brisk/Theming/ThemeResolver.cs`:

```csharp
using System;
using System.Windows.Media;

namespace Brisk.Theming;

public static class ThemeResolver
{
    public static string Resolve(string setting, Func<int?> appsUseLightTheme) =>
        setting switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => appsUseLightTheme() == 0 ? "dark" : "light",
        };

    /// DWM ColorizationColor is an ARGB dword; alpha carries blur opacity,
    /// so it is forced to FF for use as a UI accent.
    public static Color AccentFrom(int? colorizationColor)
    {
        if (colorizationColor is not { } raw)
            return Color.FromArgb(0xFF, 0x4C, 0xC2, 0xFF);
        var v = unchecked((uint)raw);
        return Color.FromArgb(0xFF, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 5: Theme dictionaries + manager (build-verified)**

`src/Brisk/Theming/Dark.xaml` — the mockup's palette:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="Bg" Color="#202024" />
    <SolidColorBrush x:Key="BgElevated" Color="#2B2B30" />
    <SolidColorBrush x:Key="BgHover" Color="#35353B" />
    <SolidColorBrush x:Key="BorderBrushKey" Color="#26FFFFFF" />
    <SolidColorBrush x:Key="Divider" Color="#12FFFFFF" />
    <SolidColorBrush x:Key="Text" Color="#FFFFFF" />
    <SolidColorBrush x:Key="TextMuted" Color="#A6A6A6" />
    <SolidColorBrush x:Key="TextFaint" Color="#8A8A8A" />
    <SolidColorBrush x:Key="AccentBrush" Color="#4CC2FF" />
    <SolidColorBrush x:Key="AccentTextBrush" Color="#0B0B0B" />
    <SolidColorBrush x:Key="SeverityInfo" Color="#4CC2FF" />
    <SolidColorBrush x:Key="SeverityWarning" Color="#F2C14E" />
    <SolidColorBrush x:Key="SeverityCritical" Color="#F26D6D" />
    <SolidColorBrush x:Key="Good" Color="#5BD97E" />
</ResourceDictionary>
```

`src/Brisk/Theming/Light.xaml` — same keys, light values:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="Bg" Color="#F3F3F3" />
    <SolidColorBrush x:Key="BgElevated" Color="#FFFFFF" />
    <SolidColorBrush x:Key="BgHover" Color="#E9E9EC" />
    <SolidColorBrush x:Key="BorderBrushKey" Color="#1F000000" />
    <SolidColorBrush x:Key="Divider" Color="#14000000" />
    <SolidColorBrush x:Key="Text" Color="#1B1B1B" />
    <SolidColorBrush x:Key="TextMuted" Color="#616161" />
    <SolidColorBrush x:Key="TextFaint" Color="#8A8A8A" />
    <SolidColorBrush x:Key="AccentBrush" Color="#0067C0" />
    <SolidColorBrush x:Key="AccentTextBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="SeverityInfo" Color="#0067C0" />
    <SolidColorBrush x:Key="SeverityWarning" Color="#B45309" />
    <SolidColorBrush x:Key="SeverityCritical" Color="#C42B2B" />
    <SolidColorBrush x:Key="Good" Color="#16A34A" />
</ResourceDictionary>
```

Both files need Build Action `Page` (the default for `.xaml` under `UseWPF` — no csproj edit required).

`src/Brisk/Theming/ThemeManager.cs`:

```csharp
using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Brisk.Theming;

public sealed class ThemeManager
{
    private const string Personalize =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Dwm = @"Software\Microsoft\Windows\DWM";

    public string Current { get; private set; } = "dark";

    public void Apply(string setting)
    {
        Current = ThemeResolver.Resolve(setting, () =>
            Registry.CurrentUser.OpenSubKey(Personalize)
                ?.GetValue("AppsUseLightTheme") as int?);
        var accent = ThemeResolver.AccentFrom(
            Registry.CurrentUser.OpenSubKey(Dwm)?.GetValue("ColorizationColor") as int?);

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries.Clear();
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Theming/{(Current == "dark" ? "Dark" : "Light")}.xaml"),
        });
        // Real system accent wins over the dictionary's fallback value. In dark
        // mode a light accent needs dark text on it and vice versa.
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush(accent);
        var luminance = 0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B;
        Application.Current.Resources["AccentTextBrush"] = new SolidColorBrush(
            luminance > 140 ? Color.FromRgb(0x0B, 0x0B, 0x0B) : Colors.White);
    }
}
```

Run: `dotnet build`
Expected: whole solution builds with zero warnings.

- [ ] **Step 6: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: theme system with os dark-mode and accent"
```

---

### Task 9: Localization — Loc + Strings.resx (EN) + Strings.tr.resx (TR)

**Files:**
- Create: `src/Brisk/Localization/Loc.cs`, `src/Brisk/Localization/Strings.resx`, `src/Brisk/Localization/Strings.tr.resx`
- Test: `src/Brisk.Tests/LocTests.cs`

**Interfaces:**
- Produces:
  - `sealed class Loc : INotifyPropertyChanged` — singleton `Loc.Instance`; indexer `string this[string key]` (missing key → the key itself, so a typo is visible, never a crash); `string F(string key, params object[] args)` (string.Format over the localized template); `string Title(string titleKey, string english)` (localized rule title, engine English as fallback); `void SetLanguage(string setting)` (`"en"`/`"tr"` explicit, anything else → OS UI culture) — raises `PropertyChanged("Item[]")` so every XAML binding refreshes.
  - XAML binds like: `{Binding [nav.health], Source={x:Static loc:Loc.Instance}}`.
- Key inventory (later tasks use EXACTLY these): `app.name`, `nav.health`, `nav.clean`, `nav.log`, `nav.settings`, `flyout.health`, `flyout.findings` (`{0} findings · {1} one-click fixable`), `flyout.reclaimable` (`{0}`), `flyout.lastscan` (`{0}`), `flyout.scanning`, `flyout.scan`, `flyout.fixall`, `flyout.clean`, `flyout.details`, `health.title`, `health.fixall`, `health.fix`, `health.undo`, `health.restorepoint`, `health.noscan`, `health.advise`, `startup.title`, `clean.selected` (`{0}`), `clean.clean`, `clean.level.safe`, `clean.level.developer`, `clean.level.deep`, `clean.recycled` (`{0}`, `{1}`), `clean.undo`, `clean.reclaim`, `clean.dismiss`, `clean.elevation`, `clean.skipped`, `clean.lifetime` (`{0}`), `log.actions`, `log.undoable`, `log.empty`, `settings.language`, `settings.theme`, `settings.dryrun`, `settings.startwithwindows`, `settings.startwithwindows.hint`, `settings.value.system`, `settings.value.en`, `settings.value.tr`, `settings.value.light`, `settings.value.dark`, plus one `rule.<id>.title` per rule id: `power-plan`, `browser-gpu`, `hw-acceleration`, `startup-bloat`, `ram-pressure`, `thermals`, `disk-breakdown`, `disk-forecast`, `orphaned-data`, `stale-dev-caches`, `visual-effects`, `storage-sense`.

- [ ] **Step 1: Write the failing tests**

`src/Brisk.Tests/LocTests.cs`:

```csharp
using Brisk.Localization;
using Xunit;

namespace Brisk.Tests;

public class LocTests
{
    [Fact]
    public void English_ByDefault()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        Assert.Equal("Health", loc["nav.health"]);
    }

    [Fact]
    public void Turkish_AfterSwitch_AndBackToEnglish()
    {
        var loc = new Loc();
        loc.SetLanguage("tr");
        Assert.Equal("Sağlık", loc["nav.health"]);
        loc.SetLanguage("en");
        Assert.Equal("Health", loc["nav.health"]);
    }

    [Fact]
    public void MissingKey_ReturnsKeyItself()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        Assert.Equal("nope.missing", loc["nope.missing"]);
    }

    [Fact]
    public void Format_UsesLocalizedTemplate()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        Assert.Equal("3 findings · 2 one-click fixable", loc.F("flyout.findings", 3, 2));
    }

    [Fact]
    public void Title_FallsBackToEngineEnglish()
    {
        var loc = new Loc();
        loc.SetLanguage("tr");
        Assert.Equal("Güç planı hızı kısıtlıyor",
            loc.Title("rule.power-plan.title", "Power plan is limiting speed"));
        Assert.Equal("Engine English",
            loc.Title("rule.not-a-rule.title", "Engine English"));
    }

    [Fact]
    public void SetLanguage_RaisesIndexerChange()
    {
        var loc = new Loc();
        string? raised = null;
        loc.PropertyChanged += (_, e) => raised = e.PropertyName;
        loc.SetLanguage("tr");
        Assert.Equal("Item[]", raised);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Brisk.Tests --filter LocTests`
Expected: build FAILS — `Loc` missing.

- [ ] **Step 3: Implement Loc**

`src/Brisk/Localization/Loc.cs`:

```csharp
using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Brisk.Localization;

public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private readonly ResourceManager _resources =
        new("Brisk.Localization.Strings", typeof(Loc).Assembly);
    private CultureInfo _culture = CultureInfo.GetCultureInfo("en");

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => _resources.GetString(key, _culture) ?? key;

    public string F(string key, params object[] args) =>
        string.Format(_culture, this[key], args);

    /// Rule titles come from the engine with a stable TitleKey; the engine's
    /// English is the fallback when a translation is missing.
    public string Title(string titleKey, string english) =>
        _resources.GetString(titleKey, _culture) ?? english;

    public void SetLanguage(string setting)
    {
        _culture = setting switch
        {
            "en" => CultureInfo.GetCultureInfo("en"),
            "tr" => CultureInfo.GetCultureInfo("tr"),
            _ => CultureInfo.CurrentUICulture,
        };
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
```

- [ ] **Step 4: Write the resx files**

`src/Brisk/Localization/Strings.resx` (neutral = English). The four `resheader` entries are mandatory resx plumbing:

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <data name="app.name" xml:space="preserve"><value>brisk</value></data>
  <data name="nav.health" xml:space="preserve"><value>Health</value></data>
  <data name="nav.clean" xml:space="preserve"><value>Clean</value></data>
  <data name="nav.log" xml:space="preserve"><value>Log</value></data>
  <data name="nav.settings" xml:space="preserve"><value>Settings</value></data>
  <data name="flyout.health" xml:space="preserve"><value>health</value></data>
  <data name="flyout.findings" xml:space="preserve"><value>{0} findings · {1} one-click fixable</value></data>
  <data name="flyout.reclaimable" xml:space="preserve"><value>{0} reclaimable</value></data>
  <data name="flyout.lastscan" xml:space="preserve"><value>Last scan: {0}</value></data>
  <data name="flyout.scanning" xml:space="preserve"><value>Scanning…</value></data>
  <data name="flyout.scan" xml:space="preserve"><value>Scan</value></data>
  <data name="flyout.fixall" xml:space="preserve"><value>Fix all (safe)</value></data>
  <data name="flyout.clean" xml:space="preserve"><value>Clean</value></data>
  <data name="flyout.details" xml:space="preserve"><value>Open details →</value></data>
  <data name="health.title" xml:space="preserve"><value>System health</value></data>
  <data name="health.fixall" xml:space="preserve"><value>Fix all (safe)</value></data>
  <data name="health.fix" xml:space="preserve"><value>Fix</value></data>
  <data name="health.undo" xml:space="preserve"><value>Undo</value></data>
  <data name="health.restorepoint" xml:space="preserve"><value>Create a restore point first</value></data>
  <data name="health.noscan" xml:space="preserve"><value>Run a scan to see findings</value></data>
  <data name="health.advise" xml:space="preserve"><value>advice</value></data>
  <data name="startup.title" xml:space="preserve"><value>Startup programs</value></data>
  <data name="clean.selected" xml:space="preserve"><value>{0} selected</value></data>
  <data name="clean.clean" xml:space="preserve"><value>Clean</value></data>
  <data name="clean.level.safe" xml:space="preserve"><value>Safe</value></data>
  <data name="clean.level.developer" xml:space="preserve"><value>Developer</value></data>
  <data name="clean.level.deep" xml:space="preserve"><value>Deep</value></data>
  <data name="clean.recycled" xml:space="preserve"><value>{0} items moved to Recycle Bin ({1})</value></data>
  <data name="clean.undo" xml:space="preserve"><value>Undo</value></data>
  <data name="clean.reclaim" xml:space="preserve"><value>Reclaim space now</value></data>
  <data name="clean.dismiss" xml:space="preserve"><value>Dismiss</value></data>
  <data name="clean.elevation" xml:space="preserve"><value>Administrator required</value></data>
  <data name="clean.skipped" xml:space="preserve"><value>skipped</value></data>
  <data name="clean.lifetime" xml:space="preserve"><value>{0} reclaimed so far</value></data>
  <data name="log.actions" xml:space="preserve"><value>Actions</value></data>
  <data name="log.undoable" xml:space="preserve"><value>Undoable fixes</value></data>
  <data name="log.empty" xml:space="preserve"><value>Nothing here yet</value></data>
  <data name="settings.language" xml:space="preserve"><value>Language</value></data>
  <data name="settings.theme" xml:space="preserve"><value>Theme</value></data>
  <data name="settings.dryrun" xml:space="preserve"><value>Dry run (report only, never change anything)</value></data>
  <data name="settings.startwithwindows" xml:space="preserve"><value>Start with Windows</value></data>
  <data name="settings.startwithwindows.hint" xml:space="preserve"><value>Off by default — brisk practices what it preaches about startup bloat.</value></data>
  <data name="settings.value.system" xml:space="preserve"><value>System</value></data>
  <data name="settings.value.en" xml:space="preserve"><value>English</value></data>
  <data name="settings.value.tr" xml:space="preserve"><value>Türkçe</value></data>
  <data name="settings.value.light" xml:space="preserve"><value>Light</value></data>
  <data name="settings.value.dark" xml:space="preserve"><value>Dark</value></data>
  <data name="rule.power-plan.title" xml:space="preserve"><value>Power plan is limiting speed</value></data>
  <data name="rule.browser-gpu.title" xml:space="preserve"><value>Browsers are not using the fast GPU</value></data>
  <data name="rule.hw-acceleration.title" xml:space="preserve"><value>Browser hardware acceleration is off</value></data>
  <data name="rule.startup-bloat.title" xml:space="preserve"><value>Too many programs start with Windows</value></data>
  <data name="rule.ram-pressure.title" xml:space="preserve"><value>Memory pressure is high</value></data>
  <data name="rule.thermals.title" xml:space="preserve"><value>Running hot at idle</value></data>
  <data name="rule.disk-breakdown.title" xml:space="preserve"><value>Where your disk space goes</value></data>
  <data name="rule.disk-forecast.title" xml:space="preserve"><value>Disk is filling up</value></data>
  <data name="rule.orphaned-data.title" xml:space="preserve"><value>Data left behind by uninstalled apps</value></data>
  <data name="rule.stale-dev-caches.title" xml:space="preserve"><value>Stale developer caches</value></data>
  <data name="rule.visual-effects.title" xml:space="preserve"><value>Visual effects are slowing this PC</value></data>
  <data name="rule.storage-sense.title" xml:space="preserve"><value>Storage Sense is off</value></data>
</root>
```

`src/Brisk/Localization/Strings.tr.resx` — same resheader block, Turkish values for every key:

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <data name="app.name" xml:space="preserve"><value>brisk</value></data>
  <data name="nav.health" xml:space="preserve"><value>Sağlık</value></data>
  <data name="nav.clean" xml:space="preserve"><value>Temizlik</value></data>
  <data name="nav.log" xml:space="preserve"><value>Günlük</value></data>
  <data name="nav.settings" xml:space="preserve"><value>Ayarlar</value></data>
  <data name="flyout.health" xml:space="preserve"><value>sağlık</value></data>
  <data name="flyout.findings" xml:space="preserve"><value>{0} bulgu · {1} tanesi tek tıkla düzelir</value></data>
  <data name="flyout.reclaimable" xml:space="preserve"><value>{0} geri kazanılabilir</value></data>
  <data name="flyout.lastscan" xml:space="preserve"><value>Son tarama: {0}</value></data>
  <data name="flyout.scanning" xml:space="preserve"><value>Taranıyor…</value></data>
  <data name="flyout.scan" xml:space="preserve"><value>Tara</value></data>
  <data name="flyout.fixall" xml:space="preserve"><value>Tümünü düzelt (güvenli)</value></data>
  <data name="flyout.clean" xml:space="preserve"><value>Temizle</value></data>
  <data name="flyout.details" xml:space="preserve"><value>Ayrıntıları aç →</value></data>
  <data name="health.title" xml:space="preserve"><value>Sistem sağlığı</value></data>
  <data name="health.fixall" xml:space="preserve"><value>Tümünü düzelt (güvenli)</value></data>
  <data name="health.fix" xml:space="preserve"><value>Düzelt</value></data>
  <data name="health.undo" xml:space="preserve"><value>Geri al</value></data>
  <data name="health.restorepoint" xml:space="preserve"><value>Önce geri yükleme noktası oluştur</value></data>
  <data name="health.noscan" xml:space="preserve"><value>Bulguları görmek için tarama çalıştır</value></data>
  <data name="health.advise" xml:space="preserve"><value>öneri</value></data>
  <data name="startup.title" xml:space="preserve"><value>Açılış programları</value></data>
  <data name="clean.selected" xml:space="preserve"><value>{0} seçili</value></data>
  <data name="clean.clean" xml:space="preserve"><value>Temizle</value></data>
  <data name="clean.level.safe" xml:space="preserve"><value>Güvenli</value></data>
  <data name="clean.level.developer" xml:space="preserve"><value>Geliştirici</value></data>
  <data name="clean.level.deep" xml:space="preserve"><value>Derin</value></data>
  <data name="clean.recycled" xml:space="preserve"><value>{0} öğe Geri Dönüşüm Kutusu'na taşındı ({1})</value></data>
  <data name="clean.undo" xml:space="preserve"><value>Geri al</value></data>
  <data name="clean.reclaim" xml:space="preserve"><value>Alanı şimdi boşalt</value></data>
  <data name="clean.dismiss" xml:space="preserve"><value>Kapat</value></data>
  <data name="clean.elevation" xml:space="preserve"><value>Yönetici gerekiyor</value></data>
  <data name="clean.skipped" xml:space="preserve"><value>atlandı</value></data>
  <data name="clean.lifetime" xml:space="preserve"><value>şimdiye dek {0} geri kazanıldı</value></data>
  <data name="log.actions" xml:space="preserve"><value>İşlemler</value></data>
  <data name="log.undoable" xml:space="preserve"><value>Geri alınabilir düzeltmeler</value></data>
  <data name="log.empty" xml:space="preserve"><value>Henüz bir şey yok</value></data>
  <data name="settings.language" xml:space="preserve"><value>Dil</value></data>
  <data name="settings.theme" xml:space="preserve"><value>Tema</value></data>
  <data name="settings.dryrun" xml:space="preserve"><value>Kuru çalıştırma (yalnızca raporla, hiçbir şeyi değiştirme)</value></data>
  <data name="settings.startwithwindows" xml:space="preserve"><value>Windows ile başlat</value></data>
  <data name="settings.startwithwindows.hint" xml:space="preserve"><value>Varsayılan olarak kapalı — brisk, açılış şişkinliği öğüdünü kendine de uygular.</value></data>
  <data name="settings.value.system" xml:space="preserve"><value>Sistem</value></data>
  <data name="settings.value.en" xml:space="preserve"><value>English</value></data>
  <data name="settings.value.tr" xml:space="preserve"><value>Türkçe</value></data>
  <data name="settings.value.light" xml:space="preserve"><value>Açık</value></data>
  <data name="settings.value.dark" xml:space="preserve"><value>Koyu</value></data>
  <data name="rule.power-plan.title" xml:space="preserve"><value>Güç planı hızı kısıtlıyor</value></data>
  <data name="rule.browser-gpu.title" xml:space="preserve"><value>Tarayıcılar hızlı GPU'yu kullanmıyor</value></data>
  <data name="rule.hw-acceleration.title" xml:space="preserve"><value>Tarayıcı donanım hızlandırması kapalı</value></data>
  <data name="rule.startup-bloat.title" xml:space="preserve"><value>Windows ile çok fazla program başlıyor</value></data>
  <data name="rule.ram-pressure.title" xml:space="preserve"><value>Bellek baskısı yüksek</value></data>
  <data name="rule.thermals.title" xml:space="preserve"><value>Boştayken yüksek sıcaklık</value></data>
  <data name="rule.disk-breakdown.title" xml:space="preserve"><value>Disk alanı nereye gidiyor</value></data>
  <data name="rule.disk-forecast.title" xml:space="preserve"><value>Disk doluyor</value></data>
  <data name="rule.orphaned-data.title" xml:space="preserve"><value>Kaldırılmış uygulamalardan kalan veriler</value></data>
  <data name="rule.stale-dev-caches.title" xml:space="preserve"><value>Bayat geliştirici önbellekleri</value></data>
  <data name="rule.visual-effects.title" xml:space="preserve"><value>Görsel efektler bilgisayarı yavaşlatıyor</value></data>
  <data name="rule.storage-sense.title" xml:space="preserve"><value>Depolama Alanı Algılayıcısı kapalı</value></data>
</root>
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS. (If `ResourceManager` cannot find `Brisk.Localization.Strings`, check the resx files sit under `src/Brisk/Localization/` — the default resource name is RootNamespace + folder path.)

- [ ] **Step 6: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: en/tr localization with runtime switch"
```

---

### Task 10: EngineHost facade + CleanService + AppServices + test fakes

**Files:**
- Create: `src/Brisk/Services/IEngineHost.cs`, `src/Brisk/Services/EngineHost.cs`, `src/Brisk/Services/CleanService.cs`, `src/Brisk/Services/AppServices.cs`
- Test: `src/Brisk.Tests/Fakes.cs`, `src/Brisk.Tests/EngineHostTests.cs`, `src/Brisk.Tests/CleanServiceTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–5 plus existing engine types.
- Produces (every later VM task depends on these EXACT signatures):

```csharp
public sealed record ScanSnapshot(
    IReadOnlyList<DiagnosticFinding> Findings,
    ScanResult Cleaner,
    int Health,
    DateTime CompletedUtc);

public interface IEngineHost
{
    Task<ScanSnapshot> ScanAsync(IProgress<string>? progress = null,
        CancellationToken ct = default);
    FixOutcome Fix(string ruleId);
    FixOutcome Undo(string ruleId);
    CleanReport Clean(TargetScanResult scan, bool dryRun);
    IReadOnlyList<UndoableFix> ListUndoable();
    IReadOnlyList<ActionLogEntry> ReadLog(int max = 200);
    IReadOnlyList<StartupEntry> ListStartup();
    bool SetStartupEnabled(string hive, string name, bool enabled);
    bool RunElevated(string cliArgs);   // runas Brisk.Cli.exe <args>; false on UAC-cancel
    bool CreateRestorePoint();          // runas powershell Checkpoint-Computer
    long FreeDiskBytes();
    long LifetimeReclaimedBytes();
    bool IsElevated();
}

public sealed record CleanOutcome(
    IReadOnlyList<string> RecycledPaths, long RecycledBytes,
    IReadOnlyList<string> Problems, bool WasDryRun);

public sealed class CleanService
{
    public CleanService(IEngineHost host, Settings settings);
    public CleanOutcome CleanTargets(IEnumerable<TargetScanResult> scans);
}

public sealed class AppComposition
{
    public required IEngineHost Host { get; init; }
    public required Settings Settings { get; init; }
    public required string SettingsPath { get; init; }
    public required StartupLauncher Launcher { get; init; }
}
public static class AppServices { public static AppComposition Build(); }
```

  - `Brisk.Tests/Fakes.cs` additionally exports `public sealed class FakeEngineHost : IEngineHost` and `public static class TestData` — REUSED by Tasks 11–14 tests.

- [ ] **Step 1: Write the shared fakes**

`src/Brisk.Tests/Fakes.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Brisk.Services;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using BriskEngine.Models;

namespace Brisk.Tests;

public static class TestData
{
    public static DiagnosticFinding Finding(string ruleId, Severity sev = Severity.Warning,
        RuleCategory cat = RuleCategory.Auto, int stars = 3, bool canFix = true) => new(
        ruleId, $"rule.{ruleId}.title", $"Title {ruleId}", $"Evidence {ruleId}",
        sev, cat, stars, canFix, canFix ? $"Fix {ruleId}" : null);

    public static TargetScanResult Target(string id, CleanupLevel level, long bytes,
        string? skipped = null, bool pick = false, bool optIn = false, bool admin = false)
    {
        var target = new CleanupTarget(id, id, level, new List<string> { @"C:\x\" + id },
            "Test", RequiresIndividualSelection: pick, RequiresExplicitOptIn: optIn,
            RequiresElevation: admin);
        var items = bytes == 0
            ? (IReadOnlyList<ResolvedItem>)Array.Empty<ResolvedItem>()
            : new[] { new ResolvedItem(id, @"C:\x\" + id + @"\item", bytes, null) };
        return new TargetScanResult(target, items, skipped);
    }

    public static ScanSnapshot Snapshot(IReadOnlyList<DiagnosticFinding>? findings = null,
        params TargetScanResult[] targets) => new(
        findings ?? Array.Empty<DiagnosticFinding>(),
        new ScanResult(targets), 72, new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc));
}

public sealed class FakeEngineHost : IEngineHost
{
    public ScanSnapshot NextSnapshot { get; set; } = TestData.Snapshot();
    public int ScanCalls { get; private set; }
    public List<string> Fixed { get; } = new();
    public List<string> Undone { get; } = new();
    public List<(string TargetId, bool DryRun)> Cleans { get; } = new();
    public Func<TargetScanResult, bool, CleanReport>? OnClean { get; set; }
    public List<UndoableFix> Undoable { get; } = new();
    public List<ActionLogEntry> LogEntries { get; } = new();
    public List<StartupEntry> Startup { get; } = new();
    public List<(string Hive, string Name, bool Enabled)> StartupToggles { get; } = new();
    public bool StartupToggleResult { get; set; } = true;
    public List<string> ElevatedRuns { get; } = new();
    public bool ElevatedResult { get; set; } = true;
    public bool RestorePointResult { get; set; } = true;
    public int RestorePointCalls { get; private set; }
    public bool Elevated { get; set; }

    public Task<ScanSnapshot> ScanAsync(IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        ScanCalls++;
        progress?.Report("fake");
        return Task.FromResult(NextSnapshot);
    }

    public FixOutcome Fix(string ruleId) { Fixed.Add(ruleId); return new(true, ruleId); }
    public FixOutcome Undo(string ruleId) { Undone.Add(ruleId); return new(true, ruleId); }

    public CleanReport Clean(TargetScanResult scan, bool dryRun)
    {
        Cleans.Add((scan.Target.Id, dryRun));
        if (OnClean is not null) return OnClean(scan, dryRun);
        var entries = scan.Items
            .Select(i => new CleanEntry(scan.Target.Id, i.Path, i.Bytes,
                dryRun ? "dry-run" : "recycled"))
            .ToList();
        return new CleanReport(entries);
    }

    public IReadOnlyList<UndoableFix> ListUndoable() => Undoable;
    public IReadOnlyList<ActionLogEntry> ReadLog(int max = 200) => LogEntries;
    public IReadOnlyList<StartupEntry> ListStartup() => Startup;

    public bool SetStartupEnabled(string hive, string name, bool enabled)
    {
        StartupToggles.Add((hive, name, enabled));
        return StartupToggleResult;
    }

    public bool RunElevated(string cliArgs) { ElevatedRuns.Add(cliArgs); return ElevatedResult; }
    public bool CreateRestorePoint() { RestorePointCalls++; return RestorePointResult; }
    public long FreeDiskBytes() => 122L << 30;
    public long Lifetime { get; set; }
    public long LifetimeReclaimedBytes() => Lifetime;
    public bool IsElevated() => Elevated;
}
```

- [ ] **Step 2: Write the failing tests**

`src/Brisk.Tests/EngineHostTests.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Services;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using BriskEngine.Models;
using BriskEngine.Safety;
using Xunit;

namespace Brisk.Tests;

file sealed class NullPowercfg : IPowercfgProbe
{
    public (Guid Id, string Name) GetActiveScheme() => (Guid.Empty, "High performance");
    public IReadOnlyList<(Guid Id, string Name)> ListSchemes() =>
        Array.Empty<(Guid, string)>();
    public void SetActive(Guid id) { }
}

file sealed class NullRegistry : IRegistryProbe
{
    public string? GetString(string k, string v) => null;
    public void SetString(string k, string v, string value) { }
    public void DeleteValue(string k, string v) { }
    public byte[]? GetBytes(string k, string v) => null;
    public void SetBytes(string k, string v, byte[] value) { }
    public int? GetInt(string k, string v) => null;
    public void SetInt(string k, string v, int value) { }
    public IReadOnlyList<string> GetValueNames(string k) => Array.Empty<string>();
    public IReadOnlyList<string> GetSubKeyNames(string k) => Array.Empty<string>();
}

file sealed class NullProcessInfo : IProcessInfoProbe
{
    public IReadOnlyList<(string Name, long WorkingSetBytes)> TopByMemory(int count) =>
        Array.Empty<(string, long)>();
    public double MemoryLoadPercent() => 10;
}

file sealed class NullSensors : ISensorProbe
{
    public double? CpuTempC() => null;
    public double? GpuTempC() => null;
    public int GpuCount() => 0;
}

file sealed class NullDisk : IDiskInfoProbe
{
    public long FreeBytes(string driveRoot) => 100L << 30;
    public long TotalBytes(string driveRoot) => 500L << 30;
}

file sealed class NullFiles : IFileProbe
{
    public string? ReadAllText(string path) => null;
    public void WriteAllText(string path, string content) { }
    public IReadOnlyList<string> ListFiles(string directory) => Array.Empty<string>();
    public DateTime? NewestWriteUtc(string path, int limit = 1500) => null;
}

file sealed class NothingRuns : IProcessLister
{
    public bool IsRunning(string processName) => false;
}

file sealed class FixedRule : IDiagnosticRule
{
    private readonly DiagnosticFinding? _finding;
    public FixedRule(string id, DiagnosticFinding? finding) { Id = id; _finding = finding; }
    public string Id { get; }
    public RuleCategory Category => RuleCategory.Auto;
    public DiagnosticFinding? Detect(DiagnosticContext ctx) => _finding;
    public string Fix(DiagnosticContext ctx) => "{}";
    public void Undo(DiagnosticContext ctx, string priorStateJson) { }
}

file sealed class BoomRule : IDiagnosticRule
{
    public string Id => "boom";
    public RuleCategory Category => RuleCategory.Auto;
    public DiagnosticFinding? Detect(DiagnosticContext ctx) =>
        throw new InvalidOperationException("probe exploded");
    public string Fix(DiagnosticContext ctx) => "{}";
    public void Undo(DiagnosticContext ctx, string priorStateJson) { }
}

public sealed class EngineHostTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-eh-").FullName;

    private EngineHost Host(params IDiagnosticRule[] rules)
    {
        var ctx = new DiagnosticContext(new NullPowercfg(), new NullRegistry(),
            new NullProcessInfo(), new NullSensors(), new NullDisk(), new NullFiles(),
            new NothingRuns(), _root);
        var logPath = Path.Combine(_root, "action-log.jsonl");
        var log = new ActionLog(logPath);
        var journal = new FixJournal(Path.Combine(_root, "fix-journal.jsonl"));
        var scanDir = Path.Combine(_root, "scan-me");
        Directory.CreateDirectory(scanDir);
        File.WriteAllBytes(Path.Combine(scanDir, "x.bin"), new byte[64]);
        var targets = new[] { new CleanupTarget("t1", "T1", CleanupLevel.Safe,
            new List<string> { scanDir }, "Test") };
        return new EngineHost(ctx, rules, new Scanner(targets, new NothingRuns()),
            new FixRunner(journal, log),
            new CleanRunner(new SafetyValidator(), new NullRecycler(), log,
                new RealProcessRunner(), () => false),
            journal, new StartupManager(new NullRegistry(), log), logPath,
            Path.Combine(_root, "Brisk.Cli.exe"));
    }

    private sealed class NullRecycler : IRecycler
    {
        public void Recycle(string path) { }
    }

    [Fact]
    public async Task ScanAsync_CollectsFindings_SkipsThrowingRule_ComputesHealth()
    {
        var finding = TestData.Finding("power-plan", Severity.Warning, stars: 4);
        var host = Host(new FixedRule("power-plan", finding),
            new FixedRule("quiet", null), new BoomRule());
        var progress = new ConcurrentBag<string>();

        var snapshot = await host.ScanAsync(
            new SyncProgress(progress.Add));

        Assert.Equal("power-plan", Assert.Single(snapshot.Findings).RuleId);
        Assert.Equal(88, snapshot.Health);
        Assert.Equal(64, snapshot.Cleaner.TotalBytes);
        Assert.NotEmpty(progress);
    }

    [Fact]
    public void Fix_UnknownRule_Fails()
    {
        var outcome = Host().Fix("nope");
        Assert.False(outcome.Ok);
        Assert.Contains("unknown", outcome.Message);
    }

    [Fact]
    public void FixThenUndo_RoundTrips_ThroughJournal()
    {
        var host = Host(new FixedRule("power-plan",
            TestData.Finding("power-plan")));
        Assert.True(host.Fix("power-plan").Ok);
        Assert.Equal("power-plan", Assert.Single(host.ListUndoable()).RuleId);
        Assert.True(host.Undo("power-plan").Ok);
        Assert.Empty(host.ListUndoable());
        Assert.Equal(2, host.ReadLog().Count);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}

/// IProgress that reports synchronously — Progress<T> posts to a sync context
/// and races with test assertions.
public sealed class SyncProgress : IProgress<string>
{
    private readonly Action<string> _handler;
    public SyncProgress(Action<string> handler) { _handler = handler; }
    public void Report(string value) => _handler(value);
}
```

`src/Brisk.Tests/CleanServiceTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Brisk.Services;
using BriskEngine.Cleaning;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

public class CleanServiceTests
{
    [Fact]
    public void CleanTargets_CollectsRecycledPathsAndBytes()
    {
        var host = new FakeEngineHost();
        var service = new CleanService(host, new Settings());
        var outcome = service.CleanTargets(new[]
        {
            TestData.Target("user-temp", CleanupLevel.Safe, 2048),
            TestData.Target("chrome-cache", CleanupLevel.Safe, 1024),
        });

        Assert.Equal(2, outcome.RecycledPaths.Count);
        Assert.Equal(3072, outcome.RecycledBytes);
        Assert.Empty(outcome.Problems);
        Assert.False(outcome.WasDryRun);
        Assert.All(host.Cleans, c => Assert.False(c.DryRun));
    }

    [Fact]
    public void CleanTargets_DryRunSetting_PassesThrough_NothingRecycled()
    {
        var host = new FakeEngineHost();
        var service = new CleanService(host, new Settings { DryRun = true });
        var outcome = service.CleanTargets(new[]
            { TestData.Target("user-temp", CleanupLevel.Safe, 2048) });

        Assert.True(outcome.WasDryRun);
        Assert.Empty(outcome.RecycledPaths);
        Assert.All(host.Cleans, c => Assert.True(c.DryRun));
    }

    [Fact]
    public void CleanTargets_CollectsRefusalsAndErrors_AsProblems()
    {
        var host = new FakeEngineHost
        {
            OnClean = (scan, _) => new CleanReport(new List<CleanEntry>
            {
                new(scan.Target.Id, @"C:\x\a", 0, "refused", "requires administrator"),
                new(scan.Target.Id, @"C:\x\b", 512, "recycled"),
                new(scan.Target.Id, @"C:\x\c", 0, "error", "locked"),
            }),
        };
        var outcome = new CleanService(host, new Settings())
            .CleanTargets(new[] { TestData.Target("windows-temp", CleanupLevel.Deep, 512) });

        Assert.Single(outcome.RecycledPaths);
        Assert.Equal(2, outcome.Problems.Count);
        Assert.Contains(outcome.Problems, p => p.Contains("administrator"));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test src/Brisk.Tests`
Expected: build FAILS — `IEngineHost`, `EngineHost`, `CleanService` missing.

- [ ] **Step 4: Implement**

`src/Brisk/Services/IEngineHost.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using BriskEngine.Models;

namespace Brisk.Services;

public sealed record ScanSnapshot(
    IReadOnlyList<DiagnosticFinding> Findings,
    ScanResult Cleaner,
    int Health,
    DateTime CompletedUtc);

/// The only door between view models and the engine. Everything here is
/// fakeable; nothing in ViewModels/ touches probes, registry or files.
public interface IEngineHost
{
    Task<ScanSnapshot> ScanAsync(IProgress<string>? progress = null,
        CancellationToken ct = default);
    FixOutcome Fix(string ruleId);
    FixOutcome Undo(string ruleId);
    CleanReport Clean(TargetScanResult scan, bool dryRun);
    IReadOnlyList<UndoableFix> ListUndoable();
    IReadOnlyList<ActionLogEntry> ReadLog(int max = 200);
    IReadOnlyList<StartupEntry> ListStartup();
    bool SetStartupEnabled(string hive, string name, bool enabled);
    bool RunElevated(string cliArgs);
    bool CreateRestorePoint();
    long FreeDiskBytes();
    long LifetimeReclaimedBytes();
    bool IsElevated();
}
```

`src/Brisk/Services/EngineHost.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using BriskEngine.Models;

namespace Brisk.Services;

public sealed class EngineHost : IEngineHost
{
    private readonly DiagnosticContext _ctx;
    private readonly IReadOnlyList<IDiagnosticRule> _rules;
    private readonly Scanner _scanner;
    private readonly FixRunner _fixes;
    private readonly CleanRunner _cleaner;
    private readonly FixJournal _journal;
    private readonly StartupManager _startup;
    private readonly string _actionLogPath;
    private readonly string _cliExePath;

    public EngineHost(DiagnosticContext ctx, IReadOnlyList<IDiagnosticRule> rules,
        Scanner scanner, FixRunner fixes, CleanRunner cleaner, FixJournal journal,
        StartupManager startup, string actionLogPath, string cliExePath)
    {
        _ctx = ctx;
        _rules = rules;
        _scanner = scanner;
        _fixes = fixes;
        _cleaner = cleaner;
        _journal = journal;
        _startup = startup;
        _actionLogPath = actionLogPath;
        _cliExePath = cliExePath;
    }

    public Task<ScanSnapshot> ScanAsync(IProgress<string>? progress = null,
        CancellationToken ct = default) => Task.Run(() =>
    {
        var findings = new List<DiagnosticFinding>();
        foreach (var rule in _rules)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(rule.Id);
            try
            {
                if (rule.Detect(_ctx) is { } finding) findings.Add(finding);
            }
            catch
            {
                // A broken probe must never kill the scan (spec: degrade gracefully).
            }
        }
        var cleaner = _scanner.Scan(ct, new SyncProgressAdapter(p =>
            progress?.Report(p.TargetId)));
        return new ScanSnapshot(findings, cleaner,
            HealthScore.Compute(findings), DateTime.UtcNow);
    }, ct);

    private sealed class SyncProgressAdapter : IProgress<ScanProgress>
    {
        private readonly Action<ScanProgress> _handler;
        public SyncProgressAdapter(Action<ScanProgress> handler) { _handler = handler; }
        public void Report(ScanProgress value) => _handler(value);
    }

    public FixOutcome Fix(string ruleId) => WithRule(ruleId, r => _fixes.Apply(r, _ctx));
    public FixOutcome Undo(string ruleId) => WithRule(ruleId, r => _fixes.Undo(r, _ctx));

    private FixOutcome WithRule(string ruleId, Func<IDiagnosticRule, FixOutcome> action)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
        return rule is null
            ? new FixOutcome(false, $"unknown rule '{ruleId}'")
            : action(rule);
    }

    public CleanReport Clean(TargetScanResult scan, bool dryRun) =>
        _cleaner.Clean(scan, dryRun);

    public IReadOnlyList<UndoableFix> ListUndoable() => _journal.ListUndoable();
    public IReadOnlyList<ActionLogEntry> ReadLog(int max = 200) =>
        ActionLogReader.ReadTail(_actionLogPath, max);
    public IReadOnlyList<StartupEntry> ListStartup() => _startup.List();
    public bool SetStartupEnabled(string hive, string name, bool enabled) =>
        _startup.SetEnabled(hive, name, enabled);

    /// Per-action UAC: run the CLI elevated for exactly one consented action.
    public bool RunElevated(string cliArgs) => RunAs(_cliExePath, cliArgs);

    public bool CreateRestorePoint() => RunAs("powershell.exe",
        "-NoProfile -Command Checkpoint-Computer -Description brisk " +
        "-RestorePointType MODIFY_SETTINGS");

    private static bool RunAs(string exe, string args)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, args)
            {
                Verb = "runas",
                UseShellExecute = true,
            });
            if (process is null) return false;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Win32Exception) { return false; }  // user cancelled the UAC prompt
        catch (FileNotFoundException) { return false; }
    }

    public long FreeDiskBytes() =>
        _ctx.Disk.FreeBytes(Path.GetPathRoot(Environment.SystemDirectory)!);

    public long LifetimeReclaimedBytes() =>
        ActionLogReader.TotalRecycledBytes(_actionLogPath);

    public bool IsElevated() => new WindowsPrincipal(WindowsIdentity.GetCurrent())
        .IsInRole(WindowsBuiltInRole.Administrator);
}
```

`src/Brisk/Services/CleanService.cs`:

```csharp
using System.Collections.Generic;
using BriskEngine.Models;

namespace Brisk.Services;

public sealed record CleanOutcome(
    IReadOnlyList<string> RecycledPaths, long RecycledBytes,
    IReadOnlyList<string> Problems, bool WasDryRun);

/// One clean pass over a set of scanned targets, shared by flyout and window.
public sealed class CleanService
{
    private readonly IEngineHost _host;
    private readonly Settings _settings;

    public CleanService(IEngineHost host, Settings settings)
    {
        _host = host;
        _settings = settings;
    }

    public CleanOutcome CleanTargets(IEnumerable<TargetScanResult> scans)
    {
        var paths = new List<string>();
        long bytes = 0;
        var problems = new List<string>();
        foreach (var scan in scans)
        {
            var report = _host.Clean(scan, _settings.DryRun);
            foreach (var entry in report.Entries)
            {
                if (entry.Action == "recycled") { paths.Add(entry.Path); bytes += entry.Bytes; }
                else if (entry.Action is "refused" or "error")
                    problems.Add($"{entry.Path} — {entry.Reason}");
            }
        }
        return new CleanOutcome(paths, bytes, problems, _settings.DryRun);
    }
}
```

`src/Brisk/Services/AppServices.cs` — the composition root, mirroring the CLI's wiring:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.RealProbes;
using BriskEngine.Logging;
using BriskEngine.Safety;

namespace Brisk.Services;

public sealed class AppComposition
{
    public required IEngineHost Host { get; init; }
    public required Settings Settings { get; init; }
    public required string SettingsPath { get; init; }
    public required StartupLauncher Launcher { get; init; }
}

public static class AppServices
{
    public static AppComposition Build()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "brisk");
        var runner = new RealProcessRunner();
        var registry = new RealRegistryProbe();
        // RealSensorProbe is IDisposable but lives for the whole app lifetime.
        var ctx = new DiagnosticContext(
            new RealPowercfgProbe(runner), registry,
            new RealProcessInfoProbe(), new RealSensorProbe(),
            new RealDiskInfoProbe(), new RealFileProbe(),
            new RealProcessLister(), dataDir);
        var logPath = Path.Combine(dataDir, "action-log.jsonl");
        var log = new ActionLog(logPath);
        var journal = new FixJournal(Path.Combine(dataDir, "fix-journal.jsonl"));
        var host = new EngineHost(ctx, DiagnosticRuleRegistry.All,
            new Scanner(CleanupTargetRegistry.All, new RealProcessLister()),
            new FixRunner(journal, log),
            new CleanRunner(new SafetyValidator(), new WindowsRecycler(), log, runner,
                () => new System.Security.Principal.WindowsPrincipal(
                    System.Security.Principal.WindowsIdentity.GetCurrent())
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)),
            journal, new StartupManager(registry, log), logPath,
            Path.Combine(AppContext.BaseDirectory, "Brisk.Cli.exe"));

        var settingsPath = Path.Combine(dataDir, "settings.json");
        return new AppComposition
        {
            Host = host,
            Settings = Settings.Load(settingsPath),
            SettingsPath = settingsPath,
            Launcher = new StartupLauncher(registry,
                Path.Combine(AppContext.BaseDirectory, "brisk-app.exe")),
        };
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS — both projects, full suite.

- [ ] **Step 6: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: engine host facade, clean service, composition root"
```

---

### Task 11: AppState + FlyoutViewModel

**Files:**
- Create: `src/Brisk/ViewModels/AppState.cs`, `src/Brisk/ViewModels/FlyoutViewModel.cs`
- Test: `src/Brisk.Tests/FlyoutViewModelTests.cs`

**Interfaces:**
- Consumes: `IEngineHost`, `CleanService`, `Loc`, `Fmt.Bytes`, `FakeEngineHost`/`TestData` (Task 10).
- Produces:
  - `sealed class AppState : ViewModelBase` — `AppState(IEngineHost host)`; `ScanSnapshot? Snapshot`, `bool IsScanning`, `string ProgressText`; `event Action? Changed` (fires after every completed scan); `Task ScanAsync()` (re-entry guarded).
  - `sealed class DelegateProgress : IProgress<string>` (in AppState.cs) — reports synchronously, used app-wide.
  - `sealed class FlyoutViewModel : ViewModelBase` — `FlyoutViewModel(AppState state, IEngineHost host, CleanService cleanService, Loc loc)`; display props `HealthText`, `FindingsLine`, `ReclaimLine`, `LastScanLine`, `HasSnapshot`; `CleanOutcome? LastCleanOutcome`; commands `ScanCommand`, `FixAllCommand`, `CleanSafeCommand`, `OpenDetailsCommand`; `event Action? OpenDetailsRequested`; async workers (tests call these directly): `Task ScanNowAsync()`, `Task FixAllAsync()`, `Task CleanSafeAsync()`.

- [ ] **Step 1: Write the failing tests**

`src/Brisk.Tests/FlyoutViewModelTests.cs`:

```csharp
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

public class FlyoutViewModelTests
{
    private static Loc EnglishLoc()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        return loc;
    }

    private static FakeEngineHost HostWithSnapshot()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(
            new[]
            {
                TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
                TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
            },
            TestData.Target("user-temp", CleanupLevel.Safe, 2048),
            TestData.Target("chrome-cache", CleanupLevel.Safe, 0, skipped: "chrome is running"),
            TestData.Target("old-installers", CleanupLevel.Deep, 4096, pick: true),
            TestData.Target("npm-cache", CleanupLevel.Developer, 1024));
        return host;
    }

    private static FlyoutViewModel Vm(FakeEngineHost host)
    {
        var state = new AppState(host);
        return new FlyoutViewModel(state, host,
            new CleanService(host, new Settings()), EnglishLoc());
    }

    [Fact]
    public async Task Scan_PopulatesSummaryLines()
    {
        var host = HostWithSnapshot();
        var vm = Vm(host);
        await vm.ScanNowAsync();

        Assert.True(vm.HasSnapshot);
        Assert.Equal("72", vm.HealthText);
        Assert.Equal("2 findings · 1 one-click fixable", vm.FindingsLine);
        Assert.Contains("7 KB", vm.ReclaimLine);   // 2048+4096+1024 = 7168
        Assert.Contains("Last scan:", vm.LastScanLine);
    }

    [Fact]
    public async Task FixAll_FixesOnlyAutoFixables_ThenRescans()
    {
        var host = HostWithSnapshot();
        var vm = Vm(host);
        await vm.ScanNowAsync();
        await vm.FixAllAsync();

        Assert.Equal(new[] { "power-plan" }, host.Fixed);
        Assert.Equal(2, host.ScanCalls);
    }

    [Fact]
    public async Task CleanSafe_CleansOnlyEligibleSafeTargets_ThenRescans()
    {
        var host = HostWithSnapshot();
        var vm = Vm(host);
        await vm.ScanNowAsync();
        await vm.CleanSafeAsync();

        Assert.Equal("user-temp", Assert.Single(host.Cleans).TargetId);
        Assert.Equal(2048, vm.LastCleanOutcome!.RecycledBytes);
        Assert.Equal(2, host.ScanCalls);
    }

    [Fact]
    public async Task ScanState_GuardsReentry()
    {
        var host = HostWithSnapshot();
        var state = new AppState(host);
        await Task.WhenAll(state.ScanAsync(), state.ScanAsync());
        Assert.Equal(1, host.ScanCalls);
    }
}
```

Note on the re-entry test: `FakeEngineHost.ScanAsync` completes synchronously, so the
two `ScanAsync()` calls run sequentially on one thread — the second must hit the
`IsScanning` guard only if the guard flag flips before the first `await`. Implement
`AppState.ScanAsync` so `IsScanning` is set BEFORE any await (see Step 3); the test
locks that in: if it flakes, the guard is in the wrong place. If the sequential
completion makes the guard unreachable (both calls see `IsScanning == false`), change
the test to assert `Equal(2, ...)` is NOT acceptable — instead start the first call,
then the second, via `var t1 = state.ScanAsync(); var t2 = state.ScanAsync(); await
Task.WhenAll(t1, t2);` which interleaves before the first completion.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Brisk.Tests --filter FlyoutViewModelTests`
Expected: build FAILS — `AppState` / `FlyoutViewModel` missing.

- [ ] **Step 3: Implement**

`src/Brisk/ViewModels/AppState.cs`:

```csharp
using System;
using System.Threading.Tasks;
using Brisk.Services;

namespace Brisk.ViewModels;

/// Reports synchronously on the calling thread. Progress<T> would post to a
/// captured SynchronizationContext, which unit tests do not have.
public sealed class DelegateProgress : IProgress<string>
{
    private readonly Action<string> _handler;
    public DelegateProgress(Action<string> handler) { _handler = handler; }
    public void Report(string value) => _handler(value);
}

/// The one shared scan state. Every page and the flyout render from here.
public sealed class AppState : ViewModelBase
{
    private readonly IEngineHost _host;
    private ScanSnapshot? _snapshot;
    private bool _isScanning;
    private string _progressText = "";

    public AppState(IEngineHost host) { _host = host; }

    public ScanSnapshot? Snapshot { get => _snapshot; private set => Set(ref _snapshot, value); }
    public bool IsScanning { get => _isScanning; private set => Set(ref _isScanning, value); }
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }

    public event Action? Changed;

    public async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;                    // set before the first await — re-entry guard
        try
        {
            Snapshot = await _host.ScanAsync(new DelegateProgress(m => ProgressText = m));
        }
        finally
        {
            IsScanning = false;
        }
        Changed?.Invoke();
    }
}
```

`src/Brisk/ViewModels/FlyoutViewModel.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using BriskEngine;
using BriskEngine.Models;

namespace Brisk.ViewModels;

public sealed class FlyoutViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly IEngineHost _host;
    private readonly CleanService _cleanService;
    private readonly Loc _loc;

    private string _healthText = "—";
    private string _findingsLine = "";
    private string _reclaimLine = "";
    private string _lastScanLine = "";
    private CleanOutcome? _lastCleanOutcome;

    public FlyoutViewModel(AppState state, IEngineHost host,
        CleanService cleanService, Loc loc)
    {
        _state = state;
        _host = host;
        _cleanService = cleanService;
        _loc = loc;
        _state.Changed += Refresh;
        ScanCommand = new RelayCommand(() => _ = ScanNowAsync());
        FixAllCommand = new RelayCommand(() => _ = FixAllAsync(), () => HasSnapshot);
        CleanSafeCommand = new RelayCommand(() => _ = CleanSafeAsync(), () => HasSnapshot);
        OpenDetailsCommand = new RelayCommand(() => OpenDetailsRequested?.Invoke());
    }

    public string HealthText { get => _healthText; private set => Set(ref _healthText, value); }
    public string FindingsLine { get => _findingsLine; private set => Set(ref _findingsLine, value); }
    public string ReclaimLine { get => _reclaimLine; private set => Set(ref _reclaimLine, value); }
    public string LastScanLine { get => _lastScanLine; private set => Set(ref _lastScanLine, value); }
    public bool HasSnapshot => _state.Snapshot is not null;
    public CleanOutcome? LastCleanOutcome
    {
        get => _lastCleanOutcome;
        private set => Set(ref _lastCleanOutcome, value);
    }
    public AppState State => _state;

    public RelayCommand ScanCommand { get; }
    public RelayCommand FixAllCommand { get; }
    public RelayCommand CleanSafeCommand { get; }
    public RelayCommand OpenDetailsCommand { get; }

    public event Action? OpenDetailsRequested;

    public Task ScanNowAsync() => _state.ScanAsync();

    public async Task FixAllAsync()
    {
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        foreach (var finding in snapshot.Findings
                     .Where(f => f.Category == RuleCategory.Auto && f.CanFix))
            _host.Fix(finding.RuleId);
        await _state.ScanAsync();
    }

    public async Task CleanSafeAsync()
    {
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        var eligible = snapshot.Cleaner.Targets.Where(t =>
            t.Target.Level == CleanupLevel.Safe
            && t.SkippedReason is null
            && !t.Target.RequiresIndividualSelection
            && !t.Target.RequiresExplicitOptIn
            && t.Items.Count > 0);
        LastCleanOutcome = _cleanService.CleanTargets(eligible);
        await _state.ScanAsync();
    }

    private void Refresh()
    {
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        HealthText = snapshot.Health.ToString();
        var fixable = snapshot.Findings.Count(f =>
            f.Category == RuleCategory.Auto && f.CanFix);
        FindingsLine = _loc.F("flyout.findings", snapshot.Findings.Count, fixable);
        ReclaimLine = _loc.F("flyout.reclaimable", Fmt.Bytes(snapshot.Cleaner.TotalBytes));
        LastScanLine = _loc.F("flyout.lastscan",
            snapshot.CompletedUtc.ToLocalTime().ToString("HH:mm"));
        Raise(nameof(HasSnapshot));
        FixAllCommand.RaiseCanExecuteChanged();
        CleanSafeCommand.RaiseCanExecuteChanged();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: app state and flyout view model"
```

---

### Task 12: HealthViewModel

**Files:**
- Create: `src/Brisk/ViewModels/HealthViewModel.cs`
- Test: `src/Brisk.Tests/HealthViewModelTests.cs`

**Interfaces:**
- Consumes: `AppState`, `IEngineHost`, `Loc` (`Title`, indexer), `UndoableFix`.
- Produces:
  - `sealed class FindingRow : ViewModelBase` — `RuleId`, `Title` (localized via `Loc.Title`), `Evidence`, `ImpactText` (`●●●○○` style), `SeverityKey` (`"SeverityInfo"|"SeverityWarning"|"SeverityCritical"` — matches Task 8 brush keys), `CategoryText`, `bool CanFix`, `bool CanUndo`, `bool IsAdvise`, `bool IsExpanded` (settable), `FixCommand`, `UndoCommand`.
  - `sealed class HealthViewModel : ViewModelBase` — ctor `(AppState state, IEngineHost host, Loc loc)`; `ObservableCollection<FindingRow> Rows`; `string ScoreText`; `bool CreateRestorePointFirst`; `string Message`; `ScanCommand`, `FixAllCommand`; workers `Task FixAllAsync()`, `Task FixAsync(FindingRow row)`, `Task UndoAsync(FindingRow row)`.
  - Fix-all semantics: if `CreateRestorePointFirst` and `host.CreateRestorePoint()` returns false → abort with a message, fix nothing (safety first).

- [ ] **Step 1: Write the failing tests**

`src/Brisk.Tests/HealthViewModelTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

public class HealthViewModelTests
{
    private static Loc EnglishLoc()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        return loc;
    }

    private static (HealthViewModel Vm, FakeEngineHost Host, AppState State) Build()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", Severity.Warning, RuleCategory.Auto,
                stars: 4, canFix: true),
            TestData.Finding("custom-x", Severity.Info, RuleCategory.Advise,
                stars: 2, canFix: false),
        });
        host.Undoable.Add(new UndoableFix("visual-effects", DateTime.UtcNow));
        var state = new AppState(host);
        return (new HealthViewModel(state, host, EnglishLoc()), host, state);
    }

    [Fact]
    public async Task Rows_MapFindings_TitlesLocalized_WithEngineFallback()
    {
        var (vm, _, state) = Build();
        await state.ScanAsync();

        Assert.Equal(2, vm.Rows.Count);
        var power = vm.Rows.Single(r => r.RuleId == "power-plan");
        // resx has rule.power-plan.title -> localized, not the engine string
        Assert.Equal("Power plan is limiting speed", power.Title);
        Assert.Equal("SeverityWarning", power.SeverityKey);
        Assert.Equal("●●●●○", power.ImpactText);
        Assert.True(power.CanFix);

        var custom = vm.Rows.Single(r => r.RuleId == "custom-x");
        // rule.custom-x.title is not in the resx -> engine English fallback
        Assert.Equal("Title custom-x", custom.Title);
        Assert.True(custom.IsAdvise);
        Assert.False(custom.CanFix);
    }

    [Fact]
    public async Task CanUndo_ComesFromJournal()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
            { TestData.Finding("visual-effects", cat: RuleCategory.Confirm) });
        await state.ScanAsync();
        Assert.True(vm.Rows.Single().CanUndo);
    }

    [Fact]
    public async Task FixRow_CallsHost_ThenRescans()
    {
        var (vm, host, state) = Build();
        await state.ScanAsync();
        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "power-plan"));

        Assert.Equal(new[] { "power-plan" }, host.Fixed);
        Assert.Equal(2, host.ScanCalls);
    }

    [Fact]
    public async Task FixAll_WithRestorePointRefused_AbortsWithMessage()
    {
        var (vm, host, state) = Build();
        await state.ScanAsync();
        vm.CreateRestorePointFirst = true;
        host.RestorePointResult = false;

        await vm.FixAllAsync();

        Assert.Equal(1, host.RestorePointCalls);
        Assert.Empty(host.Fixed);
        Assert.NotEqual("", vm.Message);
    }

    [Fact]
    public async Task FixAll_WithRestorePointOk_FixesAutoRules()
    {
        var (vm, host, state) = Build();
        await state.ScanAsync();
        vm.CreateRestorePointFirst = true;

        await vm.FixAllAsync();

        Assert.Equal(1, host.RestorePointCalls);
        Assert.Equal(new[] { "power-plan" }, host.Fixed);
    }

    [Fact]
    public async Task Score_Renders()
    {
        var (vm, _, state) = Build();
        await state.ScanAsync();
        Assert.Equal("72", vm.ScoreText);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Brisk.Tests --filter HealthViewModelTests`
Expected: build FAILS — `HealthViewModel` missing.

- [ ] **Step 3: Implement**

`src/Brisk/ViewModels/HealthViewModel.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using BriskEngine.Models;

namespace Brisk.ViewModels;

public sealed class FindingRow : ViewModelBase
{
    private bool _isExpanded;

    public FindingRow(DiagnosticFinding finding, Loc loc, bool canUndo,
        Action<FindingRow> onFix, Action<FindingRow> onUndo)
    {
        RuleId = finding.RuleId;
        Title = loc.Title(finding.TitleKey, finding.Title);
        Evidence = finding.Evidence;
        ImpactText = new string('●', finding.ImpactStars)
                   + new string('○', 5 - finding.ImpactStars);
        SeverityKey = finding.Severity switch
        {
            Severity.Critical => "SeverityCritical",
            Severity.Warning => "SeverityWarning",
            _ => "SeverityInfo",
        };
        IsAdvise = finding.Category == RuleCategory.Advise;
        CategoryText = IsAdvise ? loc["health.advise"] : "";
        CanFix = finding.CanFix && !IsAdvise;
        CanUndo = canUndo;
        FixCommand = new RelayCommand(() => onFix(this), () => CanFix);
        UndoCommand = new RelayCommand(() => onUndo(this), () => CanUndo);
    }

    public string RuleId { get; }
    public string Title { get; }
    public string Evidence { get; }
    public string ImpactText { get; }
    public string SeverityKey { get; }
    public string CategoryText { get; }
    public bool IsAdvise { get; }
    public bool CanFix { get; }
    public bool CanUndo { get; }
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public RelayCommand FixCommand { get; }
    public RelayCommand UndoCommand { get; }
}

public sealed class HealthViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly IEngineHost _host;
    private readonly Loc _loc;
    private string _scoreText = "—";
    private string _message = "";
    private bool _createRestorePointFirst;

    public HealthViewModel(AppState state, IEngineHost host, Loc loc)
    {
        _state = state;
        _host = host;
        _loc = loc;
        _state.Changed += Refresh;
        ScanCommand = new RelayCommand(() => _ = _state.ScanAsync());
        FixAllCommand = new RelayCommand(() => _ = FixAllAsync(),
            () => Rows.Any(r => r.CanFix));
    }

    public ObservableCollection<FindingRow> Rows { get; } = new();
    public string ScoreText { get => _scoreText; private set => Set(ref _scoreText, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool CreateRestorePointFirst
    {
        get => _createRestorePointFirst;
        set => Set(ref _createRestorePointFirst, value);
    }
    public RelayCommand ScanCommand { get; }
    public RelayCommand FixAllCommand { get; }

    public async Task FixAllAsync()
    {
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        if (CreateRestorePointFirst && !_host.CreateRestorePoint())
        {
            Message = "restore point was not created — nothing was changed";
            return;
        }
        foreach (var finding in snapshot.Findings
                     .Where(f => f.Category == RuleCategory.Auto && f.CanFix))
            Message = _host.Fix(finding.RuleId).Message;
        await _state.ScanAsync();
    }

    public async Task FixAsync(FindingRow row)
    {
        Message = _host.Fix(row.RuleId).Message;
        await _state.ScanAsync();
    }

    public async Task UndoAsync(FindingRow row)
    {
        Message = _host.Undo(row.RuleId).Message;
        await _state.ScanAsync();
    }

    private void Refresh()
    {
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        var undoable = _host.ListUndoable().Select(u => u.RuleId).ToHashSet();
        Rows.Clear();
        foreach (var finding in snapshot.Findings
                     .OrderByDescending(f => f.Severity)
                     .ThenByDescending(f => f.ImpactStars))
            Rows.Add(new FindingRow(finding, _loc, undoable.Contains(finding.RuleId),
                row => _ = FixAsync(row), row => _ = UndoAsync(row)));
        ScoreText = snapshot.Health.ToString();
        FixAllCommand.RaiseCanExecuteChanged();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: health view model with per-row fix and undo"
```

---

### Task 13: CleanViewModel + IRecycleBinSession

**Files:**
- Create: `src/Brisk/Services/IRecycleBinSession.cs`, `src/Brisk/Services/ShellRecycleBinSession.cs`, `src/Brisk/ViewModels/CleanViewModel.cs`
- Test: `src/Brisk.Tests/CleanViewModelTests.cs`

**Interfaces:**
- Consumes: `AppState`, `IEngineHost`, `CleanService`, `Loc`.
- Produces:
  - `interface IRecycleBinSession { bool Restore(IReadOnlyList<string> originalPaths); bool Purge(IReadOnlyList<string> originalPaths); void OpenRecycleBinUi(); }`
  - `sealed class ShellRecycleBinSession : IRecycleBinSession` — late-bound `Shell.Application` COM; matches recycled items by `System.Recycle.DeducedOriginalPath`; `Restore` invokes the item's restore verb, `Purge` deletes the physical `$Recycle.Bin` entry (`item.Path`), `OpenRecycleBinUi` opens `shell:RecycleBinFolder`. Any COM surprise → return `false`, never throw. Verified live in Task 18 (COM is untestable with fakes by design — the interface is the seam).
  - `sealed class ItemRow : ViewModelBase` — `ResolvedItem Item`, `string PathText`, `string SizeText`, `bool IsSelected` (default false).
  - `sealed class TargetRow : ViewModelBase` — `TargetScanResult Scan`, `string Id`, `string DisplayName`, `string SizeText`, `string? SkippedReason`, `bool NeedsElevation`, `bool IsPerItem`, `bool IsSelectable`, `bool IsSelected` (default: selectable && not per-item && not opt-in && bytes > 0), `ObservableCollection<ItemRow> Items` (filled only for per-item targets).
  - `sealed class LevelSection` — `CleanupLevel Level`, `string TitleKey` (`"clean.level.safe"` etc.), `ObservableCollection<TargetRow> Targets`, `string TotalText`, `RelayCommand CleanCommand`.
  - `sealed class CleanViewModel : ViewModelBase` — ctor `(AppState state, IEngineHost host, CleanService cleanService, IRecycleBinSession bin, Loc loc)`; `ObservableCollection<LevelSection> Levels`; `string LifetimeText` (from `host.LifetimeReclaimedBytes()`, refreshed on every scan — the spec's lifetime total); banner props `bool HasBanner`, `string BannerText`, `string ProblemsText`, `bool RestoreFailed`; commands `UndoCommand`, `ReclaimCommand`, `DismissCommand`, `OpenBinCommand`; worker `Task CleanLevelAsync(LevelSection section)`.
  - Clean semantics per selected row: elevation-needing target while unelevated → `host.RunElevated($"clean --target {id} --yes")`; per-item target → clean a `TargetScanResult` containing ONLY the checked items; everything else → via `CleanService`. Afterwards: build banner from outcome (skip banner on dry-run, show problems text), rescan.

- [ ] **Step 1: Write the failing tests**

`src/Brisk.Tests/CleanViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

file sealed class FakeBin : IRecycleBinSession
{
    public List<IReadOnlyList<string>> Restored { get; } = new();
    public List<IReadOnlyList<string>> Purged { get; } = new();
    public bool RestoreResult { get; set; } = true;
    public bool Restore(IReadOnlyList<string> originalPaths)
    { Restored.Add(originalPaths); return RestoreResult; }
    public bool Purge(IReadOnlyList<string> originalPaths)
    { Purged.Add(originalPaths); return true; }
    public void OpenRecycleBinUi() { }
}

public class CleanViewModelTests
{
    private static Loc EnglishLoc()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        return loc;
    }

    private static FakeEngineHost Host()
    {
        var host = new FakeEngineHost();
        host.NextSnapshot = TestData.Snapshot(null,
            TestData.Target("user-temp", CleanupLevel.Safe, 2048),
            TestData.Target("chrome-cache", CleanupLevel.Safe, 0, skipped: "chrome is running"),
            TestData.Target("docker-prune", CleanupLevel.Developer, 0, optIn: true),
            TestData.Target("old-installers", CleanupLevel.Deep, 4096, pick: true),
            TestData.Target("windows-temp", CleanupLevel.Deep, 1024, admin: true));
        return host;
    }

    private static (CleanViewModel Vm, FakeEngineHost Host, FakeBin Bin, AppState State)
        Build(FakeEngineHost host)
    {
        var state = new AppState(host);
        var bin = new FakeBin();
        var vm = new CleanViewModel(state, host,
            new CleanService(host, new Settings()), bin, EnglishLoc());
        return (vm, host, bin, state);
    }

    [Fact]
    public async Task Levels_BuildWithDefaultSelection()
    {
        var host0 = Host();
        host0.Lifetime = 5L << 30;
        var (vm, _, _, state) = Build(host0);
        await state.ScanAsync();

        Assert.Equal(3, vm.Levels.Count);
        Assert.Contains("5.0 GB", vm.LifetimeText);
        var safe = vm.Levels.Single(l => l.Level == CleanupLevel.Safe);
        Assert.True(safe.Targets.Single(t => t.Id == "user-temp").IsSelected);
        var skippedRow = safe.Targets.Single(t => t.Id == "chrome-cache");
        Assert.False(skippedRow.IsSelectable);
        Assert.False(skippedRow.IsSelected);

        var dev = vm.Levels.Single(l => l.Level == CleanupLevel.Developer);
        Assert.False(dev.Targets.Single(t => t.Id == "docker-prune").IsSelected);

        var deep = vm.Levels.Single(l => l.Level == CleanupLevel.Deep);
        var pick = deep.Targets.Single(t => t.Id == "old-installers");
        Assert.True(pick.IsPerItem);
        Assert.False(pick.IsSelected);
        Assert.Single(pick.Items);
        Assert.False(pick.Items[0].IsSelected);
        Assert.True(deep.Targets.Single(t => t.Id == "windows-temp").NeedsElevation);
    }

    [Fact]
    public async Task CleanLevel_CleansSelected_ShowsBanner_Rescans()
    {
        var (vm, host, _, state) = Build(Host());
        await state.ScanAsync();
        var safe = vm.Levels.Single(l => l.Level == CleanupLevel.Safe);

        await vm.CleanLevelAsync(safe);

        Assert.Equal("user-temp", Assert.Single(host.Cleans).TargetId);
        Assert.True(vm.HasBanner);
        Assert.Contains("2 KB", vm.BannerText);
        Assert.Equal(2, host.ScanCalls);
    }

    [Fact]
    public async Task CleanLevel_PerItemTarget_CleansOnlyCheckedItems()
    {
        var (vm, host, _, state) = Build(Host());
        await state.ScanAsync();
        var deep = vm.Levels.Single(l => l.Level == CleanupLevel.Deep);
        var pick = deep.Targets.Single(t => t.Id == "old-installers");
        pick.IsSelected = true;
        pick.Items[0].IsSelected = true;
        deep.Targets.Single(t => t.Id == "windows-temp").IsSelected = false;

        TargetScanResult? seen = null;
        host.OnClean = (scan, _) =>
        {
            if (scan.Target.Id == "old-installers") seen = scan;
            return new BriskEngine.Cleaning.CleanReport(scan.Items
                .Select(i => new BriskEngine.Cleaning.CleanEntry(
                    scan.Target.Id, i.Path, i.Bytes, "recycled")).ToList());
        };
        await vm.CleanLevelAsync(deep);

        Assert.NotNull(seen);
        Assert.Single(seen!.Items);
    }

    [Fact]
    public async Task CleanLevel_ElevationTarget_GoesThroughRunElevated()
    {
        var (vm, host, _, state) = Build(Host());
        await state.ScanAsync();
        var deep = vm.Levels.Single(l => l.Level == CleanupLevel.Deep);
        deep.Targets.Single(t => t.Id == "windows-temp").IsSelected = true;

        await vm.CleanLevelAsync(deep);

        Assert.Equal("clean --target windows-temp --yes", Assert.Single(host.ElevatedRuns));
        Assert.DoesNotContain(host.Cleans, c => c.TargetId == "windows-temp");
    }

    [Fact]
    public async Task Undo_RestoresRecycledPaths_FailureFlagged()
    {
        var (vm, host, bin, state) = Build(Host());
        await state.ScanAsync();
        await vm.CleanLevelAsync(vm.Levels.Single(l => l.Level == CleanupLevel.Safe));

        bin.RestoreResult = false;
        vm.UndoCommand.Execute(null);
        Assert.Single(bin.Restored);
        Assert.True(vm.RestoreFailed);

        bin.RestoreResult = true;
        vm.UndoCommand.Execute(null);
        Assert.False(vm.RestoreFailed);
        Assert.False(vm.HasBanner);
    }

    [Fact]
    public async Task Reclaim_PurgesAndDismisses()
    {
        var (vm, host, bin, state) = Build(Host());
        await state.ScanAsync();
        await vm.CleanLevelAsync(vm.Levels.Single(l => l.Level == CleanupLevel.Safe));

        vm.ReclaimCommand.Execute(null);
        Assert.Single(bin.Purged);
        Assert.False(vm.HasBanner);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Brisk.Tests --filter CleanViewModelTests`
Expected: build FAILS — types missing.

- [ ] **Step 3: Implement the recycle-bin seam**

`src/Brisk/Services/IRecycleBinSession.cs`:

```csharp
using System.Collections.Generic;

namespace Brisk.Services;

/// Session-scoped undo window over the Recycle Bin: restore or purge exactly
/// the items brisk just recycled, never anything else in the bin.
public interface IRecycleBinSession
{
    bool Restore(IReadOnlyList<string> originalPaths);
    bool Purge(IReadOnlyList<string> originalPaths);
    void OpenRecycleBinUi();
}
```

`src/Brisk/Services/ShellRecycleBinSession.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Brisk.Services;

/// Late-bound Shell.Application COM — no interop assembly, no NuGet. COM is
/// deliberately untestable here; the interface is the test seam and Task 18
/// verifies this class against the real bin.
public sealed class ShellRecycleBinSession : IRecycleBinSession
{
    private const int RecycleBinFolder = 10; // ssfBITBUCKET

    public bool Restore(IReadOnlyList<string> originalPaths) =>
        ForEachMatch(originalPaths, item =>
        {
            foreach (var verbObj in item.Verbs())
            {
                dynamic verb = verbObj;
                string name = ((string)verb.Name).Replace("&", "");
                // EN "Restore", TR "Geri Yükle" — match either shell language.
                if (name.StartsWith("Restore", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Geri", StringComparison.OrdinalIgnoreCase))
                {
                    verb.DoIt();
                    return true;
                }
            }
            return false;
        });

    public bool Purge(IReadOnlyList<string> originalPaths) =>
        ForEachMatch(originalPaths, item =>
        {
            string physical = (string)item.Path;
            if (Directory.Exists(physical)) Directory.Delete(physical, recursive: true);
            else if (File.Exists(physical)) File.Delete(physical);
            return true;
        });

    public void OpenRecycleBinUi()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder")
            { UseShellExecute = true });
        }
        catch (Exception) { /* UI nicety only */ }
    }

    private static bool ForEachMatch(IReadOnlyList<string> originalPaths,
        Func<dynamic, bool> action)
    {
        try
        {
            var wanted = new HashSet<string>(originalPaths, StringComparer.OrdinalIgnoreCase);
            if (wanted.Count == 0) return true;
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return false;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic bin = shell.Namespace(RecycleBinFolder);
            var matched = 0;
            foreach (var itemObj in bin.Items())
            {
                dynamic item = itemObj;
                string? original =
                    item.ExtendedProperty("System.Recycle.DeducedOriginalPath") as string;
                if (original is null || !wanted.Contains(original)) continue;
                if (!action(item)) return false;
                matched++;
            }
            return matched == wanted.Count;
        }
        catch (Exception)
        {
            return false; // COM surprises degrade to "open the bin yourself"
        }
    }
}
```

- [ ] **Step 4: Implement the view model**

`src/Brisk/ViewModels/CleanViewModel.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using BriskEngine;
using BriskEngine.Models;

namespace Brisk.ViewModels;

public sealed class ItemRow : ViewModelBase
{
    private bool _isSelected;

    public ItemRow(ResolvedItem item)
    {
        Item = item;
        PathText = item.Path;
        SizeText = Fmt.Bytes(item.Bytes);
    }

    public ResolvedItem Item { get; }
    public string PathText { get; }
    public string SizeText { get; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed class TargetRow : ViewModelBase
{
    private bool _isSelected;

    public TargetRow(TargetScanResult scan)
    {
        Scan = scan;
        SizeText = Fmt.Bytes(scan.TotalBytes);
        IsPerItem = scan.Target.RequiresIndividualSelection;
        NeedsElevation = scan.Target.RequiresElevation;
        SkippedReason = scan.SkippedReason;
        IsSelectable = scan.SkippedReason is null
            && (scan.Items.Count > 0 || scan.Target.PathTemplates.Count == 0);
        _isSelected = IsSelectable && !IsPerItem
            && !scan.Target.RequiresExplicitOptIn && scan.TotalBytes > 0;
        if (IsPerItem)
            foreach (var item in scan.Items)
                Items.Add(new ItemRow(item));
    }

    public TargetScanResult Scan { get; }
    public string Id => Scan.Target.Id;
    public string DisplayName => Scan.Target.DisplayName;
    public string SizeText { get; }
    public string? SkippedReason { get; }
    public bool NeedsElevation { get; }
    public bool IsPerItem { get; }
    public bool IsSelectable { get; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public ObservableCollection<ItemRow> Items { get; } = new();
}

public sealed class LevelSection
{
    public LevelSection(CleanupLevel level, string titleKey,
        IEnumerable<TargetRow> targets, System.Func<LevelSection, Task> clean)
    {
        Level = level;
        TitleKey = titleKey;
        Targets = new ObservableCollection<TargetRow>(targets);
        TotalText = Fmt.Bytes(Targets.Sum(t => t.Scan.TotalBytes));
        CleanCommand = new RelayCommand(() => _ = clean(this));
    }

    public CleanupLevel Level { get; }
    public string TitleKey { get; }
    public ObservableCollection<TargetRow> Targets { get; }
    public string TotalText { get; }
    public RelayCommand CleanCommand { get; }
}

public sealed class CleanViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly IEngineHost _host;
    private readonly CleanService _cleanService;
    private readonly IRecycleBinSession _bin;
    private readonly Loc _loc;

    private IReadOnlyList<string> _lastRecycled = new List<string>();
    private bool _hasBanner;
    private string _bannerText = "";
    private string _problemsText = "";
    private string _lifetimeText = "";
    private bool _restoreFailed;

    public CleanViewModel(AppState state, IEngineHost host, CleanService cleanService,
        IRecycleBinSession bin, Loc loc)
    {
        _state = state;
        _host = host;
        _cleanService = cleanService;
        _bin = bin;
        _loc = loc;
        _state.Changed += Refresh;
        UndoCommand = new RelayCommand(Undo, () => HasBanner);
        ReclaimCommand = new RelayCommand(Reclaim, () => HasBanner);
        DismissCommand = new RelayCommand(Dismiss, () => HasBanner);
        OpenBinCommand = new RelayCommand(_bin.OpenRecycleBinUi);
    }

    public ObservableCollection<LevelSection> Levels { get; } = new();
    public bool HasBanner { get => _hasBanner; private set { Set(ref _hasBanner, value); RaiseBannerCommands(); } }
    public string BannerText { get => _bannerText; private set => Set(ref _bannerText, value); }
    public string ProblemsText { get => _problemsText; private set => Set(ref _problemsText, value); }
    public string LifetimeText { get => _lifetimeText; private set => Set(ref _lifetimeText, value); }
    public bool RestoreFailed { get => _restoreFailed; private set => Set(ref _restoreFailed, value); }
    public RelayCommand UndoCommand { get; }
    public RelayCommand ReclaimCommand { get; }
    public RelayCommand DismissCommand { get; }
    public RelayCommand OpenBinCommand { get; }

    public async Task CleanLevelAsync(LevelSection section)
    {
        var selected = section.Targets.Where(t => t.IsSelected).ToList();
        var problems = new List<string>();

        var scans = new List<TargetScanResult>();
        foreach (var row in selected)
        {
            if (row.NeedsElevation && !_host.IsElevated())
            {
                if (!_host.RunElevated($"clean --target {row.Id} --yes"))
                    problems.Add($"{row.Id} — {_loc["clean.elevation"]}");
                continue;
            }
            scans.Add(row.IsPerItem
                ? row.Scan with
                {
                    Items = row.Items.Where(i => i.IsSelected)
                        .Select(i => i.Item).ToList(),
                }
                : row.Scan);
        }

        var outcome = _cleanService.CleanTargets(scans);
        problems.AddRange(outcome.Problems);
        _lastRecycled = outcome.RecycledPaths;
        RestoreFailed = false;
        ProblemsText = string.Join("\n", problems);
        if (!outcome.WasDryRun && outcome.RecycledPaths.Count > 0)
        {
            BannerText = _loc.F("clean.recycled",
                outcome.RecycledPaths.Count, Fmt.Bytes(outcome.RecycledBytes));
            HasBanner = true;
        }
        await _state.ScanAsync();
    }

    private void Undo()
    {
        if (_bin.Restore(_lastRecycled)) Dismiss();
        else RestoreFailed = true;
    }

    private void Reclaim()
    {
        _bin.Purge(_lastRecycled);
        Dismiss();
    }

    private void Dismiss()
    {
        HasBanner = false;
        RestoreFailed = false;
    }

    private void RaiseBannerCommands()
    {
        UndoCommand.RaiseCanExecuteChanged();
        ReclaimCommand.RaiseCanExecuteChanged();
        DismissCommand.RaiseCanExecuteChanged();
    }

    private void Refresh()
    {
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        Levels.Clear();
        Add(CleanupLevel.Safe, "clean.level.safe", snapshot);
        Add(CleanupLevel.Developer, "clean.level.developer", snapshot);
        Add(CleanupLevel.Deep, "clean.level.deep", snapshot);
        LifetimeText = _loc.F("clean.lifetime", Fmt.Bytes(_host.LifetimeReclaimedBytes()));
    }

    private void Add(CleanupLevel level, string titleKey, ScanSnapshot snapshot) =>
        Levels.Add(new LevelSection(level, titleKey,
            snapshot.Cleaner.Targets
                .Where(t => t.Target.Level == level)
                .Select(t => new TargetRow(t)),
            CleanLevelAsync));
}
```

Note: `row.Scan with { Items = ... }` uses the record `with` expression on
`TargetScanResult` — `Items` is a positional record property, so this compiles.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: clean view model with undo banner and per-item picks"
```

---

### Task 14: StartupViewModel + LogViewModel + SettingsViewModel

**Files:**
- Create: `src/Brisk/ViewModels/StartupViewModel.cs`, `src/Brisk/ViewModels/LogViewModel.cs`, `src/Brisk/ViewModels/SettingsViewModel.cs`
- Test: `src/Brisk.Tests/SecondaryViewModelTests.cs`

**Interfaces:**
- Consumes: `IEngineHost`, `AppState`, `Settings`, `StartupLauncher`, `Loc`, `StartupEntry`, `UndoableFix`, `ActionLogEntry`.
- Produces:
  - `sealed class StartupItemRow : ViewModelBase` — `Hive`, `Name`, `bool IsHeavy`, `bool IsEnabled` (two-way; setting it calls the toggle callback; on failure the value reverts and `ToggleFailed` goes true).
  - `sealed class StartupViewModel : ViewModelBase` — ctor `(AppState state, IEngineHost host)`; `ObservableCollection<StartupItemRow> Items` (rebuilt on `state.Changed`, heavy items first); `bool ToggleFailed`.
  - `sealed class UndoableRow` — `string RuleId`, `string WhenText`, `RelayCommand UndoCommand`.
  - `sealed class LogViewModel : ViewModelBase` — ctor `(AppState state, IEngineHost host)`; `ObservableCollection<UndoableRow> Undoables`; `ObservableCollection<ActionLogEntry> Entries`; worker `Task UndoAsync(UndoableRow row)`.
  - `sealed record ChoiceOption(string Value, string LabelKey);`
  - `sealed class SettingsViewModel : ViewModelBase` — ctor `(Settings settings, string settingsPath, StartupLauncher launcher, Action<string> applyTheme, Action<string> applyLanguage)`; `IReadOnlyList<ChoiceOption> LanguageOptions` (`system|en|tr` → `settings.value.*` keys), `ThemeOptions` (`system|light|dark`); two-way props `Language`, `Theme`, `DryRun`, `StartWithWindows` — every setter persists to `settingsPath` immediately; `Theme`/`Language` invoke their apply callback; `StartWithWindows` calls `launcher.Apply`.

- [ ] **Step 1: Write the failing tests**

`src/Brisk.Tests/SecondaryViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using Xunit;

namespace Brisk.Tests;

file sealed class RegFake : IRegistryProbe
{
    public Dictionary<string, Dictionary<string, object>> Keys { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, object> Key(string k) =>
        Keys.TryGetValue(k, out var d) ? d : Keys[k] = new(StringComparer.OrdinalIgnoreCase);
    public string? GetString(string k, string v) => Key(k).TryGetValue(v, out var o) ? o as string : null;
    public void SetString(string k, string v, string value) => Key(k)[v] = value;
    public void DeleteValue(string k, string v) => Key(k).Remove(v);
    public byte[]? GetBytes(string k, string v) => Key(k).TryGetValue(v, out var o) ? o as byte[] : null;
    public void SetBytes(string k, string v, byte[] value) => Key(k)[v] = value;
    public int? GetInt(string k, string v) => null;
    public void SetInt(string k, string v, int value) { }
    public IReadOnlyList<string> GetValueNames(string k) => Key(k).Keys.ToList();
    public IReadOnlyList<string> GetSubKeyNames(string k) => Array.Empty<string>();
}

public sealed class SecondaryViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-vm2-").FullName;

    [Fact]
    public async Task Startup_ListsHeavyFirst_TogglesThroughHost()
    {
        var host = new FakeEngineHost();
        host.Startup.Add(new StartupEntry("HKCU", "MyTool", true, false));
        host.Startup.Add(new StartupEntry("HKCU", "Discord", true, true));
        var state = new AppState(host);
        var vm = new StartupViewModel(state, host);
        await state.ScanAsync();

        Assert.Equal(new[] { "Discord", "MyTool" },
            vm.Items.Select(i => i.Name).ToArray());

        vm.Items[0].IsEnabled = false;
        Assert.Equal(("HKCU", "Discord", false), Assert.Single(host.StartupToggles));
        Assert.False(vm.ToggleFailed);
    }

    [Fact]
    public async Task Startup_FailedToggle_RevertsAndFlags()
    {
        var host = new FakeEngineHost { StartupToggleResult = false };
        host.Startup.Add(new StartupEntry("HKLM", "Svc", true, false));
        var state = new AppState(host);
        var vm = new StartupViewModel(state, host);
        await state.ScanAsync();

        vm.Items[0].IsEnabled = false;
        Assert.True(vm.Items[0].IsEnabled);   // reverted
        Assert.True(vm.ToggleFailed);
    }

    [Fact]
    public async Task Log_PopulatesAndUndoes()
    {
        var host = new FakeEngineHost();
        host.Undoable.Add(new UndoableFix("power-plan",
            new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc)));
        host.LogEntries.Add(new ActionLogEntry(DateTime.UtcNow, "fix", "fix: power-plan", "{}"));
        var state = new AppState(host);
        var vm = new LogViewModel(state, host);
        await state.ScanAsync();

        Assert.Single(vm.Entries);
        var row = Assert.Single(vm.Undoables);
        Assert.Equal("power-plan", row.RuleId);

        await vm.UndoAsync(row);
        Assert.Equal(new[] { "power-plan" }, host.Undone);
        Assert.Equal(2, host.ScanCalls);
    }

    [Fact]
    public void Settings_SettersPersistAndApply()
    {
        var path = Path.Combine(_root, "settings.json");
        var settings = new Settings();
        var reg = new RegFake();
        var applied = new List<string>();
        var vm = new SettingsViewModel(settings, path,
            new StartupLauncher(reg, @"C:\x\brisk-app.exe"),
            theme => applied.Add("theme:" + theme),
            lang => applied.Add("lang:" + lang));

        vm.Theme = "dark";
        vm.Language = "tr";
        vm.DryRun = true;
        vm.StartWithWindows = true;

        Assert.Equal(new[] { "theme:dark", "lang:tr" }, applied);
        var reloaded = Settings.Load(path);
        Assert.Equal("dark", reloaded.Theme);
        Assert.Equal("tr", reloaded.Language);
        Assert.True(reloaded.DryRun);
        Assert.True(reloaded.StartWithWindows);
        Assert.NotNull(reg.GetString(
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "brisk"));

        vm.StartWithWindows = false;
        Assert.Null(reg.GetString(
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "brisk"));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Brisk.Tests --filter SecondaryViewModelTests`
Expected: build FAILS — types missing.

- [ ] **Step 3: Implement**

`src/Brisk/ViewModels/StartupViewModel.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Brisk.Services;
using BriskEngine.Diagnostics;

namespace Brisk.ViewModels;

public sealed class StartupItemRow : ViewModelBase
{
    private readonly Func<StartupItemRow, bool, bool> _toggle;
    private bool _isEnabled;

    public StartupItemRow(StartupEntry entry, Func<StartupItemRow, bool, bool> toggle)
    {
        _toggle = toggle;
        Hive = entry.Hive;
        Name = entry.Name;
        IsHeavy = entry.KnownHeavy;
        _isEnabled = entry.Enabled;
    }

    public string Hive { get; }
    public string Name { get; }
    public bool IsHeavy { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            if (_toggle(this, value)) { _isEnabled = value; Raise(nameof(IsEnabled)); }
            else Raise(nameof(IsEnabled));   // revert the checkbox visual
        }
    }
}

public sealed class StartupViewModel : ViewModelBase
{
    private readonly IEngineHost _host;
    private bool _toggleFailed;

    public StartupViewModel(AppState state, IEngineHost host)
    {
        _host = host;
        state.Changed += Refresh;
    }

    public ObservableCollection<StartupItemRow> Items { get; } = new();
    public bool ToggleFailed { get => _toggleFailed; private set => Set(ref _toggleFailed, value); }

    private void Refresh()
    {
        Items.Clear();
        foreach (var entry in _host.ListStartup()
                     .OrderByDescending(e => e.KnownHeavy).ThenBy(e => e.Name,
                         StringComparer.OrdinalIgnoreCase))
            Items.Add(new StartupItemRow(entry, (row, enabled) =>
            {
                var ok = _host.SetStartupEnabled(row.Hive, row.Name, enabled);
                ToggleFailed = !ok;
                return ok;
            }));
    }
}
```

`src/Brisk/ViewModels/LogViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Brisk.Services;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;

namespace Brisk.ViewModels;

public sealed class UndoableRow
{
    public UndoableRow(UndoableFix fix, System.Func<UndoableRow, Task> undo)
    {
        RuleId = fix.RuleId;
        WhenText = fix.FixedAtUtc.ToLocalTime().ToString("dd.MM HH:mm");
        UndoCommand = new RelayCommand(() => _ = undo(this));
    }

    public string RuleId { get; }
    public string WhenText { get; }
    public RelayCommand UndoCommand { get; }
}

public sealed class LogViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly IEngineHost _host;

    public LogViewModel(AppState state, IEngineHost host)
    {
        _state = state;
        _host = host;
        state.Changed += Refresh;
    }

    public ObservableCollection<UndoableRow> Undoables { get; } = new();
    public ObservableCollection<ActionLogEntry> Entries { get; } = new();

    public async Task UndoAsync(UndoableRow row)
    {
        _host.Undo(row.RuleId);
        await _state.ScanAsync();   // Changed handler refreshes both lists
    }

    private void Refresh()
    {
        Undoables.Clear();
        foreach (var fix in _host.ListUndoable())
            Undoables.Add(new UndoableRow(fix, UndoAsync));
        Entries.Clear();
        foreach (var entry in _host.ReadLog())
            Entries.Add(entry);
    }
}
```

`src/Brisk/ViewModels/SettingsViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using Brisk.Services;

namespace Brisk.ViewModels;

public sealed record ChoiceOption(string Value, string LabelKey);

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly Settings _settings;
    private readonly string _settingsPath;
    private readonly StartupLauncher _launcher;
    private readonly Action<string> _applyTheme;
    private readonly Action<string> _applyLanguage;

    public SettingsViewModel(Settings settings, string settingsPath,
        StartupLauncher launcher, Action<string> applyTheme, Action<string> applyLanguage)
    {
        _settings = settings;
        _settingsPath = settingsPath;
        _launcher = launcher;
        _applyTheme = applyTheme;
        _applyLanguage = applyLanguage;
    }

    public IReadOnlyList<ChoiceOption> LanguageOptions { get; } = new[]
    {
        new ChoiceOption("system", "settings.value.system"),
        new ChoiceOption("en", "settings.value.en"),
        new ChoiceOption("tr", "settings.value.tr"),
    };

    public IReadOnlyList<ChoiceOption> ThemeOptions { get; } = new[]
    {
        new ChoiceOption("system", "settings.value.system"),
        new ChoiceOption("light", "settings.value.light"),
        new ChoiceOption("dark", "settings.value.dark"),
    };

    public string Language
    {
        get => _settings.Language;
        set
        {
            if (_settings.Language == value) return;
            _settings.Language = value;
            Persist(nameof(Language));
            _applyLanguage(value);
        }
    }

    public string Theme
    {
        get => _settings.Theme;
        set
        {
            if (_settings.Theme == value) return;
            _settings.Theme = value;
            Persist(nameof(Theme));
            _applyTheme(value);
        }
    }

    public bool DryRun
    {
        get => _settings.DryRun;
        set
        {
            if (_settings.DryRun == value) return;
            _settings.DryRun = value;
            Persist(nameof(DryRun));
        }
    }

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set
        {
            if (_settings.StartWithWindows == value) return;
            _settings.StartWithWindows = value;
            _launcher.Apply(value);
            Persist(nameof(StartWithWindows));
        }
    }

    private void Persist(string property)
    {
        _settings.Save(_settingsPath);
        Raise(property);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: startup, log and settings view models"
```

---

### Task 15: Shared styles + FlyoutWindow (XAML)

**Files:**
- Create: `src/Brisk/Theming/Shared.xaml`, `src/Brisk/Windows/Dwm.cs`, `src/Brisk/Windows/FlyoutWindow.xaml`, `src/Brisk/Windows/FlyoutWindow.xaml.cs`
- Modify: `src/Brisk/Theming/ThemeManager.cs` (also merge `Shared.xaml` after the theme dictionary)

**Interfaces:**
- Consumes: brush keys from Task 8, `FlyoutViewModel` (Task 11), `Loc`.
- Produces:
  - Styles in `Shared.xaml` referenced by BOTH windows: `Body`, `Muted`, `Faint`, `SectionLabel` (TextBlock); `PrimaryButton`, `GhostButton`, `LinkButton` (Button); `NavRadio` (RadioButton, used in Task 16).
  - `static class Dwm` — `void RoundCorners(Window window)` (DWMWA_WINDOW_CORNER_PREFERENCE=33 → DWMWCP_ROUND=2), `void DarkTitleBar(Window window, bool dark)` (DWMWA_USE_IMMERSIVE_DARK_MODE=20). Both no-op silently on pre-Win11.
  - `FlyoutWindow` — borderless 330-wide panel; `void ShowAt()` positions bottom-right of the primary work area and activates; hides on `Deactivated` and on Esc.

- [ ] **Step 1: Shared styles**

`src/Brisk/Theming/Shared.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Style x:Key="Body" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="Segoe UI Variable Text, Segoe UI" />
        <Setter Property="FontSize" Value="12.5" />
        <Setter Property="Foreground" Value="{DynamicResource Text}" />
        <Setter Property="TextTrimming" Value="CharacterEllipsis" />
    </Style>
    <Style x:Key="Muted" TargetType="TextBlock" BasedOn="{StaticResource Body}">
        <Setter Property="FontSize" Value="11.5" />
        <Setter Property="Foreground" Value="{DynamicResource TextMuted}" />
    </Style>
    <Style x:Key="Faint" TargetType="TextBlock" BasedOn="{StaticResource Body}">
        <Setter Property="FontSize" Value="11" />
        <Setter Property="Foreground" Value="{DynamicResource TextFaint}" />
    </Style>
    <Style x:Key="SectionLabel" TargetType="TextBlock" BasedOn="{StaticResource Body}">
        <Setter Property="FontSize" Value="10.5" />
        <Setter Property="Foreground" Value="{DynamicResource TextFaint}" />
        <Setter Property="Typography.Capitals" Value="AllSmallCaps" />
        <Setter Property="Margin" Value="14,8,14,2" />
    </Style>

    <Style x:Key="PrimaryButton" TargetType="Button">
        <Setter Property="Background" Value="{DynamicResource AccentBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource AccentTextBrush}" />
        <Setter Property="FontFamily" Value="Segoe UI Variable Text, Segoe UI" />
        <Setter Property="FontSize" Value="11.5" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Padding" Value="14,5" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="Chrome" Background="{TemplateBinding Background}"
                            CornerRadius="4" Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center"
                                          VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Chrome" Property="Opacity" Value="0.88" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter TargetName="Chrome" Property="Opacity" Value="0.45" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="GhostButton" TargetType="Button" BasedOn="{StaticResource PrimaryButton}">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="Foreground" Value="{DynamicResource Text}" />
        <Setter Property="FontWeight" Value="Normal" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="Chrome" Background="{TemplateBinding Background}"
                            BorderBrush="{DynamicResource BorderBrushKey}"
                            BorderThickness="1" CornerRadius="4"
                            Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center"
                                          VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Chrome" Property="Background"
                                    Value="{DynamicResource BgHover}" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter TargetName="Chrome" Property="Opacity" Value="0.45" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="LinkButton" TargetType="Button">
        <Setter Property="Foreground" Value="{DynamicResource AccentBrush}" />
        <Setter Property="FontFamily" Value="Segoe UI Variable Text, Segoe UI" />
        <Setter Property="FontSize" Value="11.5" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <TextBlock Text="{TemplateBinding Content}"
                               Foreground="{TemplateBinding Foreground}"
                               FontSize="{TemplateBinding FontSize}" />
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="NavRadio" TargetType="RadioButton">
        <Setter Property="Foreground" Value="{DynamicResource TextMuted}" />
        <Setter Property="FontFamily" Value="Segoe UI Variable Text, Segoe UI" />
        <Setter Property="FontSize" Value="12.5" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="RadioButton">
                    <Border x:Name="Chrome" CornerRadius="5" Padding="10,6" Margin="6,1"
                            Background="Transparent">
                        <ContentPresenter VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Chrome" Property="Background"
                                    Value="{DynamicResource BgHover}" />
                        </Trigger>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Chrome" Property="Background"
                                    Value="{DynamicResource BgHover}" />
                            <Setter Property="Foreground" Value="{DynamicResource Text}" />
                            <Setter Property="FontWeight" Value="SemiBold" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>
```

In `ThemeManager.Apply`, after adding the theme dictionary, also add Shared:

```csharp
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Theming/Shared.xaml"),
        });
```

(Shared references brushes via `DynamicResource`, so theme swaps keep working.)

- [ ] **Step 2: DWM helper**

`src/Brisk/Windows/Dwm.cs`:

```csharp
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Brisk.Windows;

public static class Dwm
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr,
        ref int value, int size);

    public static void RoundCorners(Window window) =>
        Set(window, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);

    public static void DarkTitleBar(Window window, bool dark) =>
        Set(window, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);

    private static void Set(Window window, int attr, int value)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        try { _ = DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int)); }
        catch (DllNotFoundException) { }   // pre-Win11 / odd environments: no-op
        catch (EntryPointNotFoundException) { }
    }
}
```

- [ ] **Step 3: FlyoutWindow**

`src/Brisk/Windows/FlyoutWindow.xaml` — the approved mockup, panel B:

```xml
<Window x:Class="Brisk.Windows.FlyoutWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:loc="clr-namespace:Brisk.Localization"
        Width="300" SizeToContent="Height"
        WindowStyle="None" ResizeMode="NoResize" ShowInTaskbar="False"
        ShowActivated="True" Topmost="True"
        Background="{DynamicResource Bg}">
    <Border BorderBrush="{DynamicResource BorderBrushKey}" BorderThickness="1">
        <StackPanel Margin="0,4">
            <DockPanel Margin="14,6,14,8">
                <TextBlock Style="{StaticResource Body}" FontWeight="SemiBold"
                           Text="{Binding [app.name], Source={x:Static loc:Loc.Instance}}" />
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal"
                            HorizontalAlignment="Right">
                    <TextBlock Style="{StaticResource Muted}"
                               Text="{Binding [flyout.health], Source={x:Static loc:Loc.Instance}}" />
                    <TextBlock Style="{StaticResource Body}" FontWeight="SemiBold"
                               Margin="8,0,0,0"
                               Foreground="{DynamicResource SeverityWarning}"
                               Text="{Binding HealthText}" />
                </StackPanel>
            </DockPanel>
            <Separator Background="{DynamicResource Divider}" Margin="0" />

            <StackPanel Orientation="Horizontal" Margin="16,8,16,0">
                <Ellipse Width="7" Height="7" Fill="{DynamicResource SeverityWarning}"
                         VerticalAlignment="Center" />
                <TextBlock Style="{StaticResource Body}" Margin="8,0,0,0"
                           Text="{Binding FindingsLine}" />
            </StackPanel>
            <StackPanel Orientation="Horizontal" Margin="16,7,16,0">
                <Ellipse Width="7" Height="7" Fill="{DynamicResource Good}"
                         VerticalAlignment="Center" />
                <TextBlock Style="{StaticResource Body}" Margin="8,0,0,0"
                           Text="{Binding ReclaimLine}" />
            </StackPanel>

            <UniformGrid Columns="2" Margin="14,10,14,4">
                <Button Style="{StaticResource PrimaryButton}" Margin="0,0,3,0"
                        Command="{Binding FixAllCommand}"
                        Content="{Binding [flyout.fixall], Source={x:Static loc:Loc.Instance}}" />
                <Button Style="{StaticResource GhostButton}" Margin="3,0,0,0"
                        Command="{Binding CleanSafeCommand}"
                        Content="{Binding [flyout.clean], Source={x:Static loc:Loc.Instance}}" />
            </UniformGrid>

            <Separator Background="{DynamicResource Divider}" Margin="0,6,0,0" />
            <DockPanel Margin="14,8,14,6">
                <TextBlock Style="{StaticResource Faint}" Text="{Binding LastScanLine}" />
                <Button DockPanel.Dock="Right" HorizontalAlignment="Right"
                        Style="{StaticResource LinkButton}"
                        Command="{Binding OpenDetailsCommand}"
                        Content="{Binding [flyout.details], Source={x:Static loc:Loc.Instance}}" />
            </DockPanel>
        </StackPanel>
    </Border>
</Window>
```

`src/Brisk/Windows/FlyoutWindow.xaml.cs`:

```csharp
using System;
using System.Windows;
using System.Windows.Input;
using Brisk.ViewModels;

namespace Brisk.Windows;

public partial class FlyoutWindow : Window
{
    public FlyoutWindow(FlyoutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Deactivated += (_, _) => Hide();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
        SourceInitialized += (_, _) => Dwm.RoundCorners(this);
        SizeChanged += (_, _) => Position();
    }

    /// Anchors the panel to the bottom-right work-area corner, like the
    /// volume flyout. WorkArea already excludes the taskbar.
    public void ShowAt()
    {
        Show();
        Position();
        Activate();
    }

    private void Position()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 12;
        Top = area.Bottom - ActualHeight - 12;
    }
}
```

- [ ] **Step 4: Build + quick visual smoke**

Run: `dotnet build`
Expected: zero warnings. (The window is wired to the tray in Task 17; a standalone visual check happens in Task 18.)

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: shared styles and tray flyout window"
```

---

### Task 16: MainWindow + pages (Health, Clean, Log, Settings)

**Files:**
- Create: `src/Brisk/Views/LocKeyConverter.cs`, `src/Brisk/Views/HealthPage.xaml(.cs)`, `src/Brisk/Views/CleanPage.xaml(.cs)`, `src/Brisk/Views/LogPage.xaml(.cs)`, `src/Brisk/Views/SettingsPage.xaml(.cs)`, `src/Brisk/Windows/MainWindow.xaml(.cs)`
- Test: `src/Brisk.Tests/LocKeyConverterTests.cs`

**Interfaces:**
- Consumes: all view models (Tasks 11–14), styles + `Dwm` (Task 15), `Loc`.
- Produces:
  - `sealed class LocKeyConverter : IValueConverter` — converts a key string (e.g. `LevelSection.TitleKey`, `ChoiceOption.LabelKey`) to `Loc.Instance[key]`.
  - `MainWindow(HealthViewModel health, StartupViewModel startup, CleanViewModel clean, LogViewModel log, SettingsViewModel settings, ThemeManager theme)` — 900×600, left nav (NavRadio ×4), page visibility switched in code-behind; closing the window HIDES it (the app lives in the tray; tray → Exit quits, per the approved design); dark title bar follows the theme.
  - `HealthPage.Bind(HealthViewModel, StartupViewModel)` — health rows plus the startup-programs section on one page.

- [ ] **Step 1: LocKeyConverter + its test**

`src/Brisk.Tests/LocKeyConverterTests.cs`:

```csharp
using System.Globalization;
using Brisk.Localization;
using Brisk.Views;
using Xunit;

namespace Brisk.Tests;

public class LocKeyConverterTests
{
    [Fact]
    public void ConvertsKeyThroughLoc()
    {
        Loc.Instance.SetLanguage("en");
        var converter = new LocKeyConverter();
        Assert.Equal("Safe", converter.Convert("clean.level.safe", typeof(string),
            null, CultureInfo.InvariantCulture));
        Assert.Equal("x.missing", converter.Convert("x.missing", typeof(string),
            null, CultureInfo.InvariantCulture));
    }
}
```

Run: `dotnet test src/Brisk.Tests --filter LocKeyConverterTests` — build FAILS. Then:

`src/Brisk/Views/LocKeyConverter.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows.Data;
using Brisk.Localization;

namespace Brisk.Views;

public sealed class LocKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter,
        CultureInfo culture) => value is string key ? Loc.Instance[key] : "";

    public object ConvertBack(object? value, Type targetType, object? parameter,
        CultureInfo culture) => throw new NotSupportedException();
}
```

Run: `dotnet test` — PASS.

- [ ] **Step 2: HealthPage**

`src/Brisk/Views/HealthPage.xaml`:

```xml
<UserControl x:Class="Brisk.Views.HealthPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:loc="clr-namespace:Brisk.Localization"
             xmlns:Brisk="clr-namespace:Brisk.Views">
    <DockPanel Margin="12,10">
        <DockPanel DockPanel.Dock="Top" Margin="2,0,2,8">
            <StackPanel Orientation="Horizontal">
                <TextBlock Style="{StaticResource Muted}" VerticalAlignment="Center"
                           Text="{Binding [health.title], Source={x:Static loc:Loc.Instance}}" />
                <TextBlock Style="{StaticResource Body}" FontSize="16" FontWeight="SemiBold"
                           Margin="8,0,0,0" VerticalAlignment="Center"
                           Foreground="{DynamicResource SeverityWarning}"
                           Text="{Binding ScoreText}" />
            </StackPanel>
            <StackPanel DockPanel.Dock="Right" Orientation="Horizontal"
                        HorizontalAlignment="Right">
                <CheckBox VerticalAlignment="Center" Margin="0,0,10,0"
                          Foreground="{DynamicResource TextMuted}"
                          IsChecked="{Binding CreateRestorePointFirst}"
                          Content="{Binding [health.restorepoint], Source={x:Static loc:Loc.Instance}}" />
                <Button Style="{StaticResource GhostButton}" Margin="0,0,6,0"
                        Command="{Binding ScanCommand}"
                        Content="{Binding [flyout.scan], Source={x:Static loc:Loc.Instance}}" />
                <Button Style="{StaticResource PrimaryButton}"
                        Command="{Binding FixAllCommand}"
                        Content="{Binding [health.fixall], Source={x:Static loc:Loc.Instance}}" />
            </StackPanel>
        </DockPanel>
        <TextBlock DockPanel.Dock="Top" Style="{StaticResource Muted}" Margin="2,0,2,6"
                   Text="{Binding Message}" />
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <StackPanel>
                <ItemsControl ItemsSource="{Binding Rows}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Background="{DynamicResource BgElevated}" CornerRadius="6"
                                    Margin="2,3" Padding="10,8">
                                <StackPanel>
                                    <DockPanel>
                                        <Ellipse Width="7" Height="7" VerticalAlignment="Center">
                                            <Ellipse.Style>
                                                <Style TargetType="Ellipse">
                                                    <Setter Property="Fill"
                                                            Value="{DynamicResource SeverityInfo}" />
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding SeverityKey}"
                                                                     Value="SeverityWarning">
                                                            <Setter Property="Fill"
                                                                    Value="{DynamicResource SeverityWarning}" />
                                                        </DataTrigger>
                                                        <DataTrigger Binding="{Binding SeverityKey}"
                                                                     Value="SeverityCritical">
                                                            <Setter Property="Fill"
                                                                    Value="{DynamicResource SeverityCritical}" />
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </Ellipse.Style>
                                        </Ellipse>
                                        <TextBlock Style="{StaticResource Body}" Margin="8,0,0,0"
                                                   Text="{Binding Title}" />
                                        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal"
                                                    HorizontalAlignment="Right">
                                            <TextBlock Style="{StaticResource Faint}"
                                                       VerticalAlignment="Center" Margin="0,0,10,0"
                                                       Text="{Binding CategoryText}" />
                                            <TextBlock Foreground="{DynamicResource SeverityWarning}"
                                                       FontSize="10" VerticalAlignment="Center"
                                                       Margin="0,0,10,0"
                                                       Text="{Binding ImpactText}" />
                                            <ToggleButton IsChecked="{Binding IsExpanded}"
                                                          Background="Transparent"
                                                          BorderThickness="0" Cursor="Hand"
                                                          Foreground="{DynamicResource TextFaint}"
                                                          Content="⌄" />
                                        </StackPanel>
                                    </DockPanel>
                                    <StackPanel Margin="15,6,0,0"
                                                Visibility="{Binding IsExpanded,
                                                    Converter={x:Static Brisk:BoolToVis.Instance}}">
                                        <TextBlock Style="{StaticResource Muted}" TextWrapping="Wrap"
                                                   Text="{Binding Evidence}" />
                                        <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                                            <Button Style="{StaticResource PrimaryButton}"
                                                    Command="{Binding FixCommand}"
                                                    Visibility="{Binding CanFix,
                                                        Converter={x:Static Brisk:BoolToVis.Instance}}"
                                                    Content="{Binding [health.fix],
                                                        Source={x:Static loc:Loc.Instance}}" />
                                            <Button Style="{StaticResource GhostButton}" Margin="6,0,0,0"
                                                    Command="{Binding UndoCommand}"
                                                    Visibility="{Binding CanUndo,
                                                        Converter={x:Static Brisk:BoolToVis.Instance}}"
                                                    Content="{Binding [health.undo],
                                                        Source={x:Static loc:Loc.Instance}}" />
                                        </StackPanel>
                                    </StackPanel>
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <StackPanel x:Name="StartupSection" Margin="0,10,0,0">
                    <TextBlock Style="{StaticResource SectionLabel}" Margin="2,0,2,4"
                               Text="{Binding [startup.title], Source={x:Static loc:Loc.Instance}}" />
                    <ItemsControl ItemsSource="{Binding Items}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <DockPanel Margin="4,3">
                                    <CheckBox IsChecked="{Binding IsEnabled}"
                                              VerticalAlignment="Center" />
                                    <TextBlock Style="{StaticResource Body}" Margin="8,0,0,0"
                                               Text="{Binding Name}" />
                                    <StackPanel DockPanel.Dock="Right" Orientation="Horizontal"
                                                HorizontalAlignment="Right">
                                        <Ellipse Width="6" Height="6" VerticalAlignment="Center"
                                                 Margin="0,0,8,0"
                                                 Fill="{DynamicResource SeverityWarning}"
                                                 Visibility="{Binding IsHeavy,
                                                     Converter={x:Static Brisk:BoolToVis.Instance}}" />
                                        <TextBlock Style="{StaticResource Faint}"
                                                   VerticalAlignment="Center"
                                                   Text="{Binding Hive}" />
                                    </StackPanel>
                                </DockPanel>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </StackPanel>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

XAML namespace note: declare `xmlns:Brisk="clr-namespace:Brisk.Views"` in every page
that uses `BoolToVis`. Add the tiny converter next to LocKeyConverter, in
`src/Brisk/Views/LocKeyConverter.cs`:

```csharp
public sealed class BoolToVis : IValueConverter
{
    public static readonly BoolToVis Instance = new();
    public object Convert(object? value, Type targetType, object? parameter,
        CultureInfo culture) =>
        value is true ? System.Windows.Visibility.Visible
                      : System.Windows.Visibility.Collapsed;
    public object ConvertBack(object? value, Type targetType, object? parameter,
        CultureInfo culture) => throw new NotSupportedException();
}
```

`src/Brisk/Views/HealthPage.xaml.cs`:

```csharp
using System.Windows.Controls;
using Brisk.ViewModels;

namespace Brisk.Views;

public partial class HealthPage : UserControl
{
    public HealthPage() { InitializeComponent(); }

    public void Bind(HealthViewModel health, StartupViewModel startup)
    {
        DataContext = health;
        StartupSection.DataContext = startup;
    }
}
```

- [ ] **Step 3: CleanPage**

`src/Brisk/Views/CleanPage.xaml`:

```xml
<UserControl x:Class="Brisk.Views.CleanPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:loc="clr-namespace:Brisk.Localization"
             xmlns:Brisk="clr-namespace:Brisk.Views">
    <UserControl.Resources>
        <Brisk:LocKeyConverter x:Key="LocKey" />
    </UserControl.Resources>
    <DockPanel Margin="12,10">
        <Border DockPanel.Dock="Top" Background="{DynamicResource BgElevated}"
                CornerRadius="6" Padding="10,8" Margin="2,0,2,8"
                Visibility="{Binding HasBanner,
                    Converter={x:Static Brisk:BoolToVis.Instance}}">
            <DockPanel>
                <TextBlock Style="{StaticResource Body}" VerticalAlignment="Center"
                           Text="{Binding BannerText}" />
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal"
                            HorizontalAlignment="Right">
                    <Button Style="{StaticResource GhostButton}" Margin="0,0,6,0"
                            Command="{Binding UndoCommand}"
                            Content="{Binding [clean.undo], Source={x:Static loc:Loc.Instance}}" />
                    <Button Style="{StaticResource GhostButton}" Margin="0,0,6,0"
                            Command="{Binding ReclaimCommand}"
                            Content="{Binding [clean.reclaim], Source={x:Static loc:Loc.Instance}}" />
                    <Button Style="{StaticResource LinkButton}" VerticalAlignment="Center"
                            Margin="0,0,10,0" Command="{Binding OpenBinCommand}"
                            Visibility="{Binding RestoreFailed,
                                Converter={x:Static Brisk:BoolToVis.Instance}}"
                            Content="↗" />
                    <Button Style="{StaticResource LinkButton}" VerticalAlignment="Center"
                            Command="{Binding DismissCommand}"
                            Content="{Binding [clean.dismiss], Source={x:Static loc:Loc.Instance}}" />
                </StackPanel>
            </DockPanel>
        </Border>
        <TextBlock DockPanel.Dock="Top" Style="{StaticResource Muted}" Margin="4,0,4,6"
                   TextWrapping="Wrap" Text="{Binding ProblemsText}" />
        <TextBlock DockPanel.Dock="Bottom" Style="{StaticResource Faint}" Margin="4,6,4,0"
                   Text="{Binding LifetimeText}" />
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <ItemsControl ItemsSource="{Binding Levels}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Background="{DynamicResource BgElevated}" CornerRadius="6"
                                Margin="2,3" Padding="10,8">
                            <StackPanel>
                                <DockPanel>
                                    <TextBlock Style="{StaticResource Body}" FontWeight="SemiBold"
                                               Text="{Binding TitleKey,
                                                   Converter={StaticResource LocKey}}" />
                                    <StackPanel DockPanel.Dock="Right" Orientation="Horizontal"
                                                HorizontalAlignment="Right">
                                        <TextBlock Style="{StaticResource Muted}"
                                                   VerticalAlignment="Center" Margin="0,0,10,0"
                                                   Text="{Binding TotalText}" />
                                        <Button Style="{StaticResource PrimaryButton}"
                                                Command="{Binding CleanCommand}"
                                                Content="{Binding [clean.clean],
                                                    Source={x:Static loc:Loc.Instance}}" />
                                    </StackPanel>
                                </DockPanel>
                                <ItemsControl ItemsSource="{Binding Targets}" Margin="0,6,0,0">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <StackPanel Margin="2,2">
                                                <DockPanel>
                                                    <CheckBox IsChecked="{Binding IsSelected}"
                                                              IsEnabled="{Binding IsSelectable}"
                                                              VerticalAlignment="Center" />
                                                    <TextBlock Style="{StaticResource Body}"
                                                               Margin="8,0,0,0"
                                                               Text="{Binding DisplayName}" />
                                                    <StackPanel DockPanel.Dock="Right"
                                                                Orientation="Horizontal"
                                                                HorizontalAlignment="Right">
                                                        <TextBlock Style="{StaticResource Faint}"
                                                                   VerticalAlignment="Center"
                                                                   Margin="0,0,8,0"
                                                                   Text="{Binding SkippedReason}" />
                                                        <TextBlock Style="{StaticResource Faint}"
                                                                   VerticalAlignment="Center"
                                                                   Margin="0,0,8,0"
                                                                   Visibility="{Binding NeedsElevation,
                                                                       Converter={x:Static Brisk:BoolToVis.Instance}}"
                                                                   Text="{Binding [clean.elevation],
                                                                       Source={x:Static loc:Loc.Instance}}" />
                                                        <TextBlock Style="{StaticResource Muted}"
                                                                   VerticalAlignment="Center"
                                                                   Text="{Binding SizeText}" />
                                                    </StackPanel>
                                                </DockPanel>
                                                <ItemsControl ItemsSource="{Binding Items}"
                                                              Margin="24,2,0,0">
                                                    <ItemsControl.ItemTemplate>
                                                        <DataTemplate>
                                                            <DockPanel Margin="0,1">
                                                                <CheckBox IsChecked="{Binding IsSelected}"
                                                                          VerticalAlignment="Center" />
                                                                <TextBlock Style="{StaticResource Faint}"
                                                                           Margin="8,0,0,0"
                                                                           Text="{Binding PathText}" />
                                                                <TextBlock DockPanel.Dock="Right"
                                                                           HorizontalAlignment="Right"
                                                                           Style="{StaticResource Faint}"
                                                                           Text="{Binding SizeText}" />
                                                            </DockPanel>
                                                        </DataTemplate>
                                                    </ItemsControl.ItemTemplate>
                                                </ItemsControl>
                                            </StackPanel>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

`src/Brisk/Views/CleanPage.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace Brisk.Views;

public partial class CleanPage : UserControl
{
    public CleanPage() { InitializeComponent(); }
}
```

- [ ] **Step 4: LogPage + SettingsPage**

`src/Brisk/Views/LogPage.xaml`:

```xml
<UserControl x:Class="Brisk.Views.LogPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:loc="clr-namespace:Brisk.Localization">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="12,10">
            <TextBlock Style="{StaticResource SectionLabel}" Margin="2,0,2,4"
                       Text="{Binding [log.undoable], Source={x:Static loc:Loc.Instance}}" />
            <ItemsControl ItemsSource="{Binding Undoables}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <DockPanel Margin="4,3">
                            <TextBlock Style="{StaticResource Body}" Text="{Binding RuleId}" />
                            <StackPanel DockPanel.Dock="Right" Orientation="Horizontal"
                                        HorizontalAlignment="Right">
                                <TextBlock Style="{StaticResource Faint}"
                                           VerticalAlignment="Center" Margin="0,0,10,0"
                                           Text="{Binding WhenText}" />
                                <Button Style="{StaticResource GhostButton}"
                                        Command="{Binding UndoCommand}"
                                        Content="{Binding [health.undo],
                                            Source={x:Static loc:Loc.Instance}}" />
                            </StackPanel>
                        </DockPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <TextBlock Style="{StaticResource SectionLabel}" Margin="2,12,2,4"
                       Text="{Binding [log.actions], Source={x:Static loc:Loc.Instance}}" />
            <TextBlock Style="{StaticResource Faint}" Margin="4,0"
                       Text="{Binding [log.empty], Source={x:Static loc:Loc.Instance}}">
                <TextBlock.Style>
                    <Style TargetType="TextBlock" BasedOn="{StaticResource Faint}">
                        <Setter Property="Visibility" Value="Collapsed" />
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding Entries.Count}" Value="0">
                                <Setter Property="Visibility" Value="Visible" />
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </TextBlock.Style>
            </TextBlock>
            <ItemsControl ItemsSource="{Binding Entries}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <DockPanel Margin="4,2">
                            <TextBlock Style="{StaticResource Faint}" Margin="0,0,10,0"
                                       Text="{Binding TsUtc, StringFormat=dd.MM HH:mm}" />
                            <TextBlock Style="{StaticResource Muted}"
                                       Text="{Binding Summary}" />
                        </DockPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

`src/Brisk/Views/LogPage.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace Brisk.Views;

public partial class LogPage : UserControl
{
    public LogPage() { InitializeComponent(); }
}
```

`src/Brisk/Views/SettingsPage.xaml`:

```xml
<UserControl x:Class="Brisk.Views.SettingsPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:loc="clr-namespace:Brisk.Localization"
             xmlns:Brisk="clr-namespace:Brisk.Views">
    <UserControl.Resources>
        <Brisk:LocKeyConverter x:Key="LocKey" />
        <DataTemplate x:Key="ChoiceTemplate">
            <TextBlock Text="{Binding LabelKey, Converter={StaticResource LocKey}}" />
        </DataTemplate>
    </UserControl.Resources>
    <StackPanel Margin="16,14" MaxWidth="420" HorizontalAlignment="Left">
        <TextBlock Style="{StaticResource Muted}"
                   Text="{Binding [settings.language], Source={x:Static loc:Loc.Instance}}" />
        <ComboBox Margin="0,4,0,12" ItemsSource="{Binding LanguageOptions}"
                  SelectedValue="{Binding Language}" SelectedValuePath="Value"
                  ItemTemplate="{StaticResource ChoiceTemplate}" />
        <TextBlock Style="{StaticResource Muted}"
                   Text="{Binding [settings.theme], Source={x:Static loc:Loc.Instance}}" />
        <ComboBox Margin="0,4,0,12" ItemsSource="{Binding ThemeOptions}"
                  SelectedValue="{Binding Theme}" SelectedValuePath="Value"
                  ItemTemplate="{StaticResource ChoiceTemplate}" />
        <CheckBox Margin="0,4,0,10" Foreground="{DynamicResource Text}"
                  IsChecked="{Binding DryRun}"
                  Content="{Binding [settings.dryrun], Source={x:Static loc:Loc.Instance}}" />
        <CheckBox Margin="0,0,0,2" Foreground="{DynamicResource Text}"
                  IsChecked="{Binding StartWithWindows}"
                  Content="{Binding [settings.startwithwindows], Source={x:Static loc:Loc.Instance}}" />
        <TextBlock Style="{StaticResource Faint}" TextWrapping="Wrap" Margin="22,0,0,0"
                   Text="{Binding [settings.startwithwindows.hint], Source={x:Static loc:Loc.Instance}}" />
    </StackPanel>
</UserControl>
```

`src/Brisk/Views/SettingsPage.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace Brisk.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage() { InitializeComponent(); }
}
```

- [ ] **Step 5: MainWindow**

`src/Brisk/Windows/MainWindow.xaml`:

```xml
<Window x:Class="Brisk.Windows.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:loc="clr-namespace:Brisk.Localization"
        xmlns:views="clr-namespace:Brisk.Views"
        Title="brisk" Width="900" Height="600"
        Background="{DynamicResource Bg}">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="120" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <Border Grid.Column="0" BorderBrush="{DynamicResource Divider}"
                BorderThickness="0,0,1,0">
            <StackPanel Margin="0,10">
                <RadioButton x:Name="NavHealth" Style="{StaticResource NavRadio}"
                             GroupName="nav" IsChecked="True" Checked="Nav_Checked"
                             Content="{Binding [nav.health], Source={x:Static loc:Loc.Instance}}" />
                <RadioButton x:Name="NavClean" Style="{StaticResource NavRadio}"
                             GroupName="nav" Checked="Nav_Checked"
                             Content="{Binding [nav.clean], Source={x:Static loc:Loc.Instance}}" />
                <RadioButton x:Name="NavLog" Style="{StaticResource NavRadio}"
                             GroupName="nav" Checked="Nav_Checked"
                             Content="{Binding [nav.log], Source={x:Static loc:Loc.Instance}}" />
                <RadioButton x:Name="NavSettings" Style="{StaticResource NavRadio}"
                             GroupName="nav" Checked="Nav_Checked"
                             Content="{Binding [nav.settings], Source={x:Static loc:Loc.Instance}}" />
            </StackPanel>
        </Border>
        <Grid Grid.Column="1">
            <views:HealthPage x:Name="HealthView" />
            <views:CleanPage x:Name="CleanView" Visibility="Collapsed" />
            <views:LogPage x:Name="LogView" Visibility="Collapsed" />
            <views:SettingsPage x:Name="SettingsView" Visibility="Collapsed" />
        </Grid>
    </Grid>
</Window>
```

`src/Brisk/Windows/MainWindow.xaml.cs`:

```csharp
using System.ComponentModel;
using System.Windows;
using Brisk.Theming;
using Brisk.ViewModels;

namespace Brisk.Windows;

public partial class MainWindow : Window
{
    private readonly ThemeManager _theme;

    public MainWindow(HealthViewModel health, StartupViewModel startup,
        CleanViewModel clean, LogViewModel log, SettingsViewModel settings,
        ThemeManager theme)
    {
        _theme = theme;
        InitializeComponent();
        HealthView.Bind(health, startup);
        CleanView.DataContext = clean;
        LogView.DataContext = log;
        SettingsView.DataContext = settings;
        SourceInitialized += (_, _) => ApplyTitleBar();
    }

    public void ApplyTitleBar() => Dwm.DarkTitleBar(this, _theme.Current == "dark");

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (HealthView is null) return;   // fires during InitializeComponent
        HealthView.Visibility = sender == NavHealth ? Visibility.Visible : Visibility.Collapsed;
        CleanView.Visibility = sender == NavClean ? Visibility.Visible : Visibility.Collapsed;
        LogView.Visibility = sender == NavLog ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = sender == NavSettings ? Visibility.Visible : Visibility.Collapsed;
    }

    /// The app lives in the tray; the window close button only hides it.
    /// Quitting is the tray menu's Exit (per the approved design).
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
```

- [ ] **Step 6: Build + run the whole suite**

Run: `dotnet build`, then `dotnet test`
Expected: zero warnings, all tests green.

- [ ] **Step 7: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: main window with health, clean, log, settings pages"
```

---

### Task 17: Tray icon + App startup wiring

**Files:**
- Create: `src/Brisk/Tray/TrayIcon.cs`
- Modify: `src/Brisk/App.xaml.cs` (full startup), `src/Brisk/Localization/Strings.resx` + `Strings.tr.resx` (3 tray keys)

**Interfaces:**
- Consumes: everything built so far.
- Produces:
  - `sealed class TrayIcon : IDisposable` — ctor `(System.Drawing.Color accent, Loc loc)`; events `LeftClick`, `OpenRequested`, `ScanRequested`, `ExitRequested`; `void UpdateTooltip(string text)` (trimmed to 63 chars — NotifyIcon limit). Icon is drawn at runtime (accent rounded square + white `b`), no .ico asset needed.
  - App behavior: single instance (second launch pings the first to show its window); `--tray` arg starts hidden; launch triggers a scan (a launch IS a user action); tooltip shows free space + health after every scan; tray Exit is the only full quit.

- [ ] **Step 1: Add tray strings to BOTH resx files**

`Strings.resx`:

```xml
  <data name="tray.open" xml:space="preserve"><value>Open brisk</value></data>
  <data name="tray.scan" xml:space="preserve"><value>Scan now</value></data>
  <data name="tray.exit" xml:space="preserve"><value>Exit</value></data>
```

`Strings.tr.resx`:

```xml
  <data name="tray.open" xml:space="preserve"><value>brisk'i aç</value></data>
  <data name="tray.scan" xml:space="preserve"><value>Şimdi tara</value></data>
  <data name="tray.exit" xml:space="preserve"><value>Çıkış</value></data>
```

- [ ] **Step 2: TrayIcon**

`src/Brisk/Tray/TrayIcon.cs`:

```csharp
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Brisk.Localization;

namespace Brisk.Tray;

public sealed class TrayIcon : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private readonly NotifyIcon _notify;
    private readonly Icon _icon;
    private readonly IntPtr _iconHandle;

    public event Action? LeftClick;
    public event Action? OpenRequested;
    public event Action? ScanRequested;
    public event Action? ExitRequested;

    public TrayIcon(Color accent, Loc loc)
    {
        (_icon, _iconHandle) = DrawIcon(accent);
        var menu = new ContextMenuStrip();
        menu.Items.Add(loc["tray.open"], null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(loc["tray.scan"], null, (_, _) => ScanRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(loc["tray.exit"], null, (_, _) => ExitRequested?.Invoke());
        _notify = new NotifyIcon
        {
            Icon = _icon,
            Text = "brisk",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notify.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) LeftClick?.Invoke();
        };
    }

    public void UpdateTooltip(string text) =>
        _notify.Text = text.Length <= 63 ? text : text[..63];

    private static (Icon Icon, IntPtr Handle) DrawIcon(Color accent)
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath();
        path.AddRoundedRectangle(new Rectangle(0, 0, 15, 15), 4);
        using var fill = new SolidBrush(accent);
        g.FillPath(fill, path);
        using var font = new Font("Segoe UI", 9, System.Drawing.FontStyle.Bold,
            GraphicsUnit.Pixel);
        var size = g.MeasureString("b", font);
        g.DrawString("b", font, Brushes.White,
            (16 - size.Width) / 2f, (16 - size.Height) / 2f);
        var handle = bmp.GetHicon();
        return (Icon.FromHandle(handle), handle);
    }

    public void Dispose()
    {
        _notify.Visible = false;
        _notify.Dispose();
        _icon.Dispose();
        DestroyIcon(_iconHandle);
    }
}
```

Note: `AddRoundedRectangle` exists on `GraphicsPath` from .NET 7+ (System.Drawing).
If the compiler disagrees, build the rounded rect from four `AddArc` calls
(radius 4 corners) — same visual result.

- [ ] **Step 3: App wiring**

Replace `src/Brisk/App.xaml.cs` with:

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Windows;
using Brisk.Localization;
using Brisk.Services;
using Brisk.Theming;
using Brisk.Tray;
using Brisk.ViewModels;
using Brisk.Windows;
using BriskEngine;
using Microsoft.Win32;

namespace Brisk;

public partial class App : Application
{
    private Mutex? _single;
    private EventWaitHandle? _showSignal;
    private TrayIcon? _tray;
    private MainWindow? _main;
    private FlyoutWindow? _flyout;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _single = new Mutex(true, "brisk-app-single", out var isFirst);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "brisk-app-show");
        if (!isFirst)
        {
            _showSignal.Set();   // ask the running instance to show itself
            Shutdown();
            return;
        }

        var composition = AppServices.Build();
        Loc.Instance.SetLanguage(composition.Settings.Language);
        var theme = new ThemeManager();
        theme.Apply(composition.Settings.Theme);

        var state = new AppState(composition.Host);
        var cleanService = new CleanService(composition.Host, composition.Settings);
        var flyoutVm = new FlyoutViewModel(state, composition.Host, cleanService,
            Loc.Instance);
        var healthVm = new HealthViewModel(state, composition.Host, Loc.Instance);
        var startupVm = new StartupViewModel(state, composition.Host);
        var cleanVm = new CleanViewModel(state, composition.Host, cleanService,
            new ShellRecycleBinSession(), Loc.Instance);
        var logVm = new LogViewModel(state, composition.Host);
        var settingsVm = new SettingsViewModel(composition.Settings,
            composition.SettingsPath, composition.Launcher,
            themeSetting => { theme.Apply(themeSetting); _main?.ApplyTitleBar(); },
            Loc.Instance.SetLanguage);

        _flyout = new FlyoutWindow(flyoutVm);
        _main = new MainWindow(healthVm, startupVm, cleanVm, logVm, settingsVm, theme);
        flyoutVm.OpenDetailsRequested += () => { _flyout.Hide(); ShowMain(); };

        var accent = ThemeResolver.AccentFrom(
            Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM")
                ?.GetValue("ColorizationColor") as int?);
        _tray = new TrayIcon(System.Drawing.Color.FromArgb(accent.R, accent.G, accent.B),
            Loc.Instance);
        _tray.LeftClick += () => _flyout.ShowAt();
        _tray.OpenRequested += ShowMain;
        _tray.ScanRequested += () => _ = state.ScanAsync();
        _tray.ExitRequested += () =>
        {
            _tray?.Dispose();
            _tray = null;
            Shutdown();
        };
        state.Changed += () => Dispatcher.Invoke(() => _tray?.UpdateTooltip(
            $"brisk — {Fmt.Bytes(composition.Host.FreeDiskBytes())} free" +
            $" · {state.Snapshot?.Health}"));

        var showWaiter = new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Dispatcher.Invoke(ShowMain);
        })
        { IsBackground = true };
        showWaiter.Start();

        if (!e.Args.Contains("--tray")) ShowMain();
        _ = state.ScanAsync();   // launching brisk is a user action — scan once
    }

    private void ShowMain()
    {
        if (_main is null) return;
        _main.Show();
        _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 4: Build + run the suite**

Run: `dotnet build`, then `dotnet test`
Expected: zero warnings, all green.

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: tray icon, single instance, app wiring"
```

---

### Task 18: End-to-end verification on the real machine

**Files:**
- Modify: `README.md` (status line), anything the live run flushes out.

This mirrors Plan A's live-smoke task: run the real app on Mert's machine and walk
every surface. Mutating steps use trivially reversible actions only.

- [ ] **Step 1: Launch**

```powershell
dotnet run --project src/Brisk
```

Checklist: window opens on Health; tray shows the `b` icon; dark/light follows the
OS; title bar matches the theme.

- [ ] **Step 2: Flyout**

Left-click the tray icon: panel opens bottom-right above the taskbar, matches the
approved mockup (header `brisk … sağlık/health NN`, two summary rows, two buttons,
footer). Click elsewhere → it hides. Esc → hides. `Ayrıntıları aç` → detail window.

- [ ] **Step 3: Health + startup**

Scan completes with real findings; a row expands to evidence + Fix. Toggle one
LIGHT startup item off, verify in Task Manager → Startup, toggle it back on.

- [ ] **Step 4: Clean + undo window**

Settings → dry run ON → Clean safe level → nothing recycled, plan text only.
Dry run OFF → Clean safe → banner appears; **Undo** → spot-check a restored file;
Clean again → **Reclaim now** → items gone from the Recycle Bin (only brisk's).

- [ ] **Step 5: Elevation path**

Deep level → select `windows-temp` → Clean → UAC prompt names Brisk.Cli → accept →
target cleaned; cancel-path also checked (refusal message, nothing deleted).

- [ ] **Step 6: Settings + persistence**

Language → Türkçe: UI switches instantly. Theme light/dark. Start with Windows ON →
`HKCU\...\Run\brisk` exists with `--tray`; OFF → gone. Restart app → settings held.

- [ ] **Step 7: Lifecycle**

Second `dotnet run` while running → first instance's window comes forward. Window ✕
→ hides to tray. Tray → Exit → process fully gone.

- [ ] **Step 8: README + wrap-up**

Update `README.md` Status: "Engine + CLI and the tray GUI (flyout + window) working;
packaging and docs are coming." Adjust the Recycle-Bin sentence: the GUI now offers
Undo / Reclaim-now after each clean. Fix anything the walk found, run `dotnet test`,
then:

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "docs: readme status after gui e2e walk"
```

---

## Execution note

Plan C (distribution: packaging, winget/Scoop, README GIF + comparison table,
growth assets) is a separate plan, written after this one ships.

