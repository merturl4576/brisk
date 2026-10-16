# Action Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One click that does what brisk finds — free space for real, speed the machine up, switch telemetry off — with a before/after receipt and one-step undo, usable from the CLI before the new GUI exists.

**Architecture:** A thin `BriskEngine.Actions` layer over what exists. Rules keep `Detect`/`Fix`/`Undo`; `FixJournal` keeps prior states; `ReadBack` keeps verifying writes; `CleanRunner` and `SafetyValidator` keep deleting safely. New: data-defined switches, a plan builder that turns a scan into three groups of ticked rows, a runner that applies them under one run id behind a write-ahead registry, a receipt, three live readers, a deferred clean queue, and CLI verbs.

**Tech Stack:** C# / .NET 8 (`net8.0-windows`, x64), xUnit 2.5, Win32 P/Invoke, WMI (`System.Management`, already referenced), PowerShell 5.1 as a bridge for one WinRT call.

**Spec:** `docs/superpowers/specs/2026-10-16-action-engine-design.md`

## Global Constraints

- Branch `feat/action-engine` (cut from `feat/honest-gauge` @ c8d6154). Never push, merge or tag — the maintainer's call.
- Projects stay `net8.0-windows`, x64, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. **No target framework change and no new NuGet package.** The 30-day usage read goes through PowerShell (decided by the 2026-10-16 spike below).
- brisk itself makes **no network connection at runtime**. Only `tools/build-asn-table.ps1` downloads, once, at development time.
- Copy rules (spec): row title in everyday words, about six words at most; cost line at most five or six words, empty when there is no cost; Windows' own name shown only when a row is expanded.
- Regenerating caches are deleted for real in the action flow (not recycled). The existing `brisk clean` and GUI flows keep their behaviour.
- Never touched: Desktop, Documents, the large-files list. Downloads files only when listed at plan time **and** unchanged in size and last-write time at run time.
- Defaults from the spec tables: shader caches off; hibernation off on laptops (`IPowercfgProbe.HasBattery()` true — an unknown answer counts as a laptop); memory integrity off; virtual machine platform off; location off; Docker off; everything else on.
- A switch writes only values that currently read as "on"; a value that is absent and declared `AbsentIsOff` is never created.
- Test commands: one class `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~<Class>"`; whole suite `dotnet test brisk.sln -c Release`. The suite is 1687 green at the start and must stay green after every task.
- Commit subjects are plain conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`, `refactor:`); every commit message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

**Spike already done (2026-10-16, maintainer's PC, unelevated PowerShell 5.1):** `ConnectionProfile.GetAttributedNetworkUsageAsync` returns per-app bytes for desktop apps without elevation. `AttributionId` is an NT device path (`\device\harddiskvolume3\...\discord.exe`) or a package family name (`5319275A.WhatsAppDesktop_cv1g1gvanyjgm`); an empty id is system traffic; `AttributionName` comes back empty, so brisk names apps itself. Calling it from .NET directly would need a `net8.0-windows10.0.x` target and the ~25 MB Windows SDK projection, so it goes through PowerShell. Also measured: the DNS client cache (`MSFT_DNSClientCache`) returns 0 rows unelevated and 41 rows elevated on build 26200 (`Entry` = queried name, `Data` = address), so the connection reader treats it as admin-only and falls back to an offline IP-owner table. `HwSchMode`, `ActiveOverlayAcPowerScheme`, `HistoricalCaptureEnabled` and `DirectXUserGlobalSettings` were all absent on that machine; `MinAnimate` = 1; memory integrity on; `VirtualMachinePlatform` InstallState 1; the task `Microsoft Compatibility Appraiser` does not exist on build 26200.

## Review Focus

1. **The CLI runs unelevated.** Every row that writes HKLM or runs `schtasks /Change`, DISM, `powercfg /h`, `netsh` or `dism` must end as *skipped — needs administrator*, never as a thrown exception or a silent "done". Pinned in Task 15 (`Run_RowNeedingAdmin_IsSkippedWhenNotElevated`).
2. **A Downloads file changes between plan and run.** It must be skipped, not deleted. Pinned in Task 15 (`Run_DownloadsFileChangedSincePlan_IsSkipped`).
3. **An app is open at plan or run time.** Its cache is queued for later, never deleted under it. Pinned in Task 15 (`Run_TargetSkippedForRunningApp_IsDeferredNotCleaned`).
4. **A machine without a feature** (no Recall, no `HwSchMode`, no Docker, a Windows 10 build, no hibernation file). Rows hide; nothing throws. Pinned in Task 14 (`Build_MachineWithoutOptionalFeatures_HidesThoseRowsAndDoesNotThrow`).
5. **The process dies halfway through a multi-value write.** The next run restores what was half-written. Pinned in Task 1 (`RecoverPending_RestoresWhatACrashedRunHalfWrote`) and Task 15 (`Run_RecoversAPendingWriteAheadLogFirst`).

---

## File map

Engine (`src/BriskEngine/`):

| File | Responsibility |
|---|---|
| `Actions/WriteAheadRegistry.cs` | Registry decorator: records each value before its first write; roll back, commit, recover after a crash |
| `Diagnostics/IReadBackable.cs` | The read-back contract (`IsOn`, `EffectOfTheWrite`) shared by every switch |
| `Actions/Switches/SwitchSpec.cs` | `SwitchValue`, `SwitchSpec`, `SwitchRule` (registry switch from data) |
| `Actions/Switches/SwitchCatalog.cs` | The data for every new registry switch |
| `Actions/Switches/TelemetryServicesRule.cs` | DiagTrack/dmwappushservice switch that also stops the services |
| `Actions/Switches/TaskSwitchRule.cs` | Scheduled-task switch + `IScheduledTaskProbe` + real probe |
| `Actions/Rules/WindowAnimationsRule.cs` | Animations switch + `IUiEffectsProbe` + real probe |
| `Actions/Rules/WindowedGamesRule.cs` | DirectX windowed-games string setting |
| `Actions/Rules/PowerModeRule.cs` | Plugged-in power mode + `IPowerOverlayProbe` + real probe |
| `Actions/Rules/VirtualMachinePlatformRule.cs` | VMP switch + `IOptionalFeatureProbe` + real probe |
| `Actions/Rules/FirewallOnRule.cs`, `Actions/Rules/DefenderRealtimeRule.cs` | Security rows |
| `Actions/Rules/HibernationRule.cs`, `Actions/Rules/StartupEntryRule.cs` | Hibernation file; one startup entry as a rule |
| `Actions/StartupProtection.cs` | Which startup entries stay unticked, and why |
| `Cleaning/IDeleter.cs` | Permanent deletion |
| `Actions/SpaceProbe.cs` | Docker reclaimable bytes, Recycle Bin size |
| `Actions/DeferredCleanQueue.cs` | Caches skipped for a running app, cleaned when it closes |
| `Actions/ActionModels.cs` | Items, groups, plan, selection, results, run record |
| `Actions/ActionCatalog.cs` | Every fixed row: id, copy, defaults, links |
| `Actions/ActionRules.cs` | Finds a rule by id (existing, new, `startup:` ids) |
| `Actions/ActionPlanBuilder.cs` | Scan → `ActionPlan` |
| `Actions/ActionRunner.cs`, `Actions/RunStore.cs` | Applies a selection; undo |
| `Live/ProcessSnapshot.cs`, `Live/AppNamer.cs`, `Live/LoadSampler.cs`, `Live/ProcessCloser.cs` | Current load |
| `Actions/Receipt.cs` | Receipt, store, recorder, calculator |
| `Live/TcpTable.cs`, `Live/DnsCache.cs`, `Live/OwnerTable.cs`, `Live/ConnectionReader.cs`, `Live/asn-owners.tsv` | Current connections |
| `Live/NetworkUsageReader.cs` | 30-day per-app usage |
| `Actions/ActionEngine.cs` | Real wiring, shared by the CLI and the future GUI |

Also modified: `Diagnostics/StartupManager.cs` (Startup folder entries, command on entries), `Diagnostics/Rules/Privacy/TelemetrySwitchRule.cs`, `Diagnostics/ReadBack.cs`, `Cleaning/CleanRunner.cs`, `Cleaning/CleanupTargetRegistry.cs`, `Diagnostics/RealProbes/RealPowercfgProbe.cs`, `BriskEngine.csproj` (embedded resource), `src/Brisk.Cli/CliParser.cs`, `src/Brisk.Cli/Program.cs`, `src/Brisk/Localization/Strings.resx` and `Strings.tr.resx`. New script: `tools/build-asn-table.ps1`.

Tests go in `src/BriskEngine.Tests/` (engine and CLI) and `src/Brisk.Tests/` (resx parity). Existing fakes reused: `FakeRegistry`, `FakeFiles`, `FakeRunningApps`, `FakePowercfg`, `FakeEventLog`, `FakeDisk`, `FakeDefender`, `TestContext.Empty()` (all in `TestContext.cs`), and `FakeRunner` / `ScriptedRunner` (in `CleanRunnerTests.cs`, internal to the test assembly).

---

### Task 1: Write-ahead registry

The spec's "journal before write". Today a multi-value switch that throws halfway leaves its first writes unrecorded (`TelemetrySwitchRule.cs` documents it). Instead of changing every rule, the runner hands rules a registry that records each value before its first write.

**Files:**
- Create: `src/BriskEngine/Actions/WriteAheadRegistry.cs`
- Test: `src/BriskEngine.Tests/WriteAheadRegistryTests.cs`

**Interfaces:**
- Consumes: `IRegistryProbe` (`src/BriskEngine/Diagnostics/Probes.cs`).
- Produces: `public sealed class WriteAheadRegistry : IRegistryProbe` — `WriteAheadRegistry(IRegistryProbe inner, string walPath)`, `int RollBack()` (returns how many values could not be restored), `void Commit()`, `static int RecoverPending(IRegistryProbe registry, string walPath)`, `IReadOnlyList<WriteAheadRegistry.Prior> Priors`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.IO;
using BriskEngine.Actions;
using Xunit;

namespace BriskEngine.Tests;

public sealed class WriteAheadRegistryTests : IDisposable
{
    private const string K = @"HKCU\Software\BriskTest";
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-wal-").FullName;
    private string Wal => Path.Combine(_root, "wal.jsonl");

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    [Fact]
    public void RollBack_RestoresEveryTouchedValue()
    {
        var reg = new FakeRegistry();
        reg.SetInt(K, "A", 1);
        reg.SetString(K, "C", "x");
        var wal = new WriteAheadRegistry(reg, Wal);

        wal.SetInt(K, "A", 0);
        wal.SetInt(K, "B", 5);
        wal.SetString(K, "C", "y");
        Assert.Equal(0, wal.RollBack());

        Assert.Equal(1, reg.GetInt(K, "A"));
        Assert.Null(reg.GetInt(K, "B"));
        Assert.Equal("x", reg.GetString(K, "C"));
        Assert.False(File.Exists(Wal));
    }

    [Fact]
    public void OnlyTheFirstWriteOfAValueIsRecorded()
    {
        var reg = new FakeRegistry();
        reg.SetInt(K, "A", 1);
        var wal = new WriteAheadRegistry(reg, Wal);

        wal.SetInt(K, "A", 0);
        wal.SetInt(K, "A", 7);
        Assert.Single(wal.Priors);
        wal.RollBack();

        Assert.Equal(1, reg.GetInt(K, "A"));
    }

    [Fact]
    public void AWriteThatThrows_ChangesNothing_AndRollBackDoesNotThrow()
    {
        var reg = new FakeRegistry();
        reg.DenyWriteKeys.Add(K);
        var wal = new WriteAheadRegistry(reg, Wal);

        Assert.Throws<UnauthorizedAccessException>(() => wal.SetInt(K, "A", 1));
        Assert.Equal(0, wal.RollBack());
        Assert.Null(reg.GetInt(K, "A"));
    }

    [Fact]
    public void RecoverPending_RestoresWhatACrashedRunHalfWrote()
    {
        var reg = new FakeRegistry();
        reg.SetInt(K, "A", 1);
        var crashed = new WriteAheadRegistry(reg, Wal);
        crashed.SetInt(K, "A", 0);
        crashed.SetInt(K, "B", 9);
        // no Commit, no RollBack: the process died here

        Assert.Equal(2, WriteAheadRegistry.RecoverPending(reg, Wal));
        Assert.Equal(1, reg.GetInt(K, "A"));
        Assert.Null(reg.GetInt(K, "B"));
        Assert.False(File.Exists(Wal));
        Assert.Equal(0, WriteAheadRegistry.RecoverPending(reg, Wal));
    }

    [Fact]
    public void Commit_KeepsTheWrites_AndDropsTheRecord()
    {
        var reg = new FakeRegistry();
        var wal = new WriteAheadRegistry(reg, Wal);
        wal.SetInt(K, "A", 3);
        wal.Commit();

        Assert.Equal(3, reg.GetInt(K, "A"));
        Assert.Empty(wal.Priors);
        Assert.False(File.Exists(Wal));
    }

    [Fact]
    public void ABinaryValueRoundTrips()
    {
        var reg = new FakeRegistry();
        reg.SetBytes(K, "Bin", new byte[] { 2, 0, 0 });
        var wal = new WriteAheadRegistry(reg, Wal);
        wal.SetBytes(K, "Bin", new byte[] { 3, 0, 0 });
        wal.RollBack();
        Assert.Equal(new byte[] { 2, 0, 0 }, reg.GetBytes(K, "Bin"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~WriteAheadRegistryTests"`
Expected: build FAILS — `WriteAheadRegistry` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BriskEngine.Diagnostics;

namespace BriskEngine.Actions;

/// Records each registry value BEFORE its first write. A rule that throws
/// halfway is rolled back from memory; a process that dies halfway is rolled
/// back from the file at the next start. Rules need no change: the runner
/// hands them this instead of the real registry.
public sealed class WriteAheadRegistry : IRegistryProbe
{
    public sealed record Prior(string KeyPath, string ValueName, string Kind,
        string? Text, int? Number, byte[]? Bytes);

    private readonly IRegistryProbe _inner;
    private readonly string _walPath;
    private readonly List<Prior> _priors = new();
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public WriteAheadRegistry(IRegistryProbe inner, string walPath)
    {
        _inner = inner;
        _walPath = walPath;
        Directory.CreateDirectory(Path.GetDirectoryName(walPath)!);
    }

    public IReadOnlyList<Prior> Priors => _priors;

    private void Remember(string k, string v)
    {
        if (!_seen.Add($"{k}::{v}")) return;
        var prior = Read(_inner, k, v);
        _priors.Add(prior);
        File.AppendAllText(_walPath, JsonSerializer.Serialize(prior) + "\n");
    }

    internal static Prior Read(IRegistryProbe reg, string k, string v)
    {
        var n = reg.GetInt(k, v);
        if (n is not null) return new Prior(k, v, "dword", null, n, null);
        var s = reg.GetString(k, v);
        if (s is not null) return new Prior(k, v, "string", s, null, null);
        var b = reg.GetBytes(k, v);
        if (b is not null) return new Prior(k, v, "binary", null, null, b);
        return new Prior(k, v, "absent", null, null, null);
    }

    private static bool Same(Prior a, Prior b) =>
        a.Kind == b.Kind && a.Number == b.Number && a.Text == b.Text
        && (a.Bytes ?? Array.Empty<byte>()).SequenceEqual(b.Bytes ?? Array.Empty<byte>());

    /// Puts one value back unless it already reads as the prior. Returns
    /// false when the restore itself was refused.
    internal static bool Restore(IRegistryProbe reg, Prior p)
    {
        try
        {
            if (Same(Read(reg, p.KeyPath, p.ValueName), p)) return true;
            switch (p.Kind)
            {
                case "dword": reg.SetInt(p.KeyPath, p.ValueName, p.Number!.Value); break;
                case "string": reg.SetString(p.KeyPath, p.ValueName, p.Text!); break;
                case "binary": reg.SetBytes(p.KeyPath, p.ValueName, p.Bytes!); break;
                default: reg.DeleteValue(p.KeyPath, p.ValueName); break;
            }
            return true;
        }
        catch (Exception) { return false; }
    }

    /// Restores every recorded value, newest first. Returns how many could
    /// not be restored; the record is dropped either way.
    public int RollBack()
    {
        var failed = Enumerable.Reverse(_priors).Count(p => !Restore(_inner, p));
        Commit();
        return failed;
    }

    /// The writes stand, so the record is no longer needed.
    public void Commit()
    {
        _priors.Clear();
        _seen.Clear();
        File.Delete(_walPath);
    }

    /// Restores what a run that died left recorded. Returns how many values.
    public static int RecoverPending(IRegistryProbe registry, string walPath)
    {
        if (!File.Exists(walPath)) return 0;
        var priors = new List<Prior>();
        foreach (var line in File.ReadAllLines(walPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                if (JsonSerializer.Deserialize<Prior>(line) is { } p) priors.Add(p);
            }
            catch (JsonException) { }
        }
        foreach (var p in Enumerable.Reverse(priors)) Restore(registry, p);
        File.Delete(walPath);
        return priors.Count;
    }

    public string? GetString(string k, string v) => _inner.GetString(k, v);
    public byte[]? GetBytes(string k, string v) => _inner.GetBytes(k, v);
    public int? GetInt(string k, string v) => _inner.GetInt(k, v);
    public long? GetLong(string k, string v) => _inner.GetLong(k, v);
    public IReadOnlyList<string> GetValueNames(string k) => _inner.GetValueNames(k);
    public IReadOnlyList<string> GetSubKeyNames(string k) => _inner.GetSubKeyNames(k);

    public void SetString(string k, string v, string value) { Remember(k, v); _inner.SetString(k, v, value); }
    public void SetBytes(string k, string v, byte[] value) { Remember(k, v); _inner.SetBytes(k, v, value); }
    public void SetInt(string k, string v, int value) { Remember(k, v); _inner.SetInt(k, v, value); }
    public void DeleteValue(string k, string v) { Remember(k, v); _inner.DeleteValue(k, v); }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~WriteAheadRegistryTests"`
Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Actions/WriteAheadRegistry.cs src/BriskEngine.Tests/WriteAheadRegistryTests.cs
git commit -m "feat(actions): write-ahead registry that records before it writes" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: One read-back contract for every switch

`ReadBack.For` only knows `TelemetrySwitchRule`. New switches (scheduled tasks, animations, power mode) are not registry-value rules, so the contract moves to an interface the old family already satisfies.

**Files:**
- Create: `src/BriskEngine/Diagnostics/IReadBackable.cs`
- Modify: `src/BriskEngine/Diagnostics/Rules/Privacy/TelemetrySwitchRule.cs` (declaration `public abstract class TelemetrySwitchRule : IDiagnosticRule`)
- Modify: `src/BriskEngine/Diagnostics/ReadBack.cs` (`For` and `StateOf`)
- Test: `src/BriskEngine.Tests/ReadBackContractTests.cs`

**Interfaces:**
- Produces: `public interface IReadBackable : IDiagnosticRule { bool IsOn(DiagnosticContext ctx); WriteEffect EffectOfTheWrite(DiagnosticContext ctx); }` and `public static ReadBackState ReadBack.StateOf(DiagnosticContext ctx, IReadBackable rule)`.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

file sealed class NonRegistrySwitch : IReadBackable
{
    public bool On;
    public WriteEffect Effect = WriteEffect.NotAPolicy;
    public string Id => "non-registry";
    public RuleCategory Category => RuleCategory.Auto;
    public bool IsOn(DiagnosticContext ctx) => On;
    public WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => Effect;
    public DiagnosticFinding? Detect(DiagnosticContext ctx) => null;
    public string Fix(DiagnosticContext ctx) { On = false; return "{}"; }
    public void Undo(DiagnosticContext ctx, string prior) => On = true;
}

public sealed class ReadBackContractTests
{
    [Fact]
    public void For_ReadsBackAnySwitch_NotOnlyTheRegistryFamily()
    {
        var rule = new NonRegistrySwitch { On = false };
        var rows = ReadBack.For(TestContext.Empty(),
            new[] { new UndoableFix("non-registry", DateTime.UtcNow) },
            new IDiagnosticRule[] { rule });
        Assert.Single(rows);
        Assert.Equal(ReadBackState.Held, rows[0].State);
    }

    [Theory]
    [InlineData(true, WriteEffect.NotAPolicy, ReadBackState.Reverted)]
    [InlineData(false, WriteEffect.Ignored, ReadBackState.WrittenButIgnored)]
    [InlineData(false, WriteEffect.Unread, ReadBackState.WrittenButUnverified)]
    [InlineData(false, WriteEffect.ActedOn, ReadBackState.Held)]
    public void StateOf_MapsEveryAnswer(bool on, WriteEffect effect, ReadBackState expected)
    {
        var rule = new NonRegistrySwitch { On = on, Effect = effect };
        Assert.Equal(expected, ReadBack.StateOf(TestContext.Empty(), rule));
    }

    [Fact]
    public void TheRegistryFamilyIsReadBackable()
    {
        Assert.True(typeof(IReadBackable).IsAssignableFrom(typeof(TelemetrySwitchRule)));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~ReadBackContractTests"`
Expected: build FAILS — `IReadBackable` does not exist.

- [ ] **Step 3: Implement**

Create `src/BriskEngine/Diagnostics/IReadBackable.cs`:

```csharp
using BriskEngine.Diagnostics.Rules.Privacy;

namespace BriskEngine.Diagnostics;

/// A switch brisk can re-read after writing it: whether it still reads as on,
/// and what brisk knows about whether this machine acts on the write.
public interface IReadBackable : IDiagnosticRule
{
    bool IsOn(DiagnosticContext ctx);
    WriteEffect EffectOfTheWrite(DiagnosticContext ctx);
}
```

In `TelemetrySwitchRule.cs` change the declaration (its `IsOn` and `EffectOfTheWrite` are already `public virtual` with these signatures):

```csharp
public abstract class TelemetrySwitchRule : IReadBackable
```

In `ReadBack.cs`, inside `For`, replace

```csharp
            var rule = rules.OfType<TelemetrySwitchRule>().FirstOrDefault(
```

with

```csharp
            var rule = rules.OfType<IReadBackable>().FirstOrDefault(
```

and replace the `StateOf` signature

```csharp
    private static ReadBackState StateOf(DiagnosticContext ctx, TelemetrySwitchRule rule)
```

with

```csharp
    public static ReadBackState StateOf(DiagnosticContext ctx, IReadBackable rule)
```

The body is unchanged: `rule.IsOn(ctx)`, `rule.EffectOfTheWrite(ctx)` and `rule.Id` all exist on the interface.

- [ ] **Step 4: Run the new tests and the existing read-back tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~ReadBack"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Diagnostics/IReadBackable.cs src/BriskEngine/Diagnostics/Rules/Privacy/TelemetrySwitchRule.cs src/BriskEngine/Diagnostics/ReadBack.cs src/BriskEngine.Tests/ReadBackContractTests.cs
git commit -m "refactor(readback): one contract for every switch brisk can re-read" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 3: Data-defined registry switches

**Files:**
- Create: `src/BriskEngine/Actions/Switches/SwitchSpec.cs`
- Create: `src/BriskEngine/Actions/Switches/SwitchCatalog.cs`
- Create: `src/BriskEngine/Actions/Switches/TelemetryServicesRule.cs`
- Test: `src/BriskEngine.Tests/SwitchRuleTests.cs`

**Interfaces:**
- Consumes: `TelemetrySwitchRule` (abstract `Id`, `Values`, `Title`, `Evidence`, `FixDescription`; virtual `Fix`, `Undo`, `EffectOfTheWrite`, `ReadsAsOn(int? actual, RegistryValue value)`), `WriteEffect`, `IProcessRunner`, `ReadBack.StateOf` (Task 2).
- Produces:
  - `public sealed record SwitchValue(string KeyPath, string ValueName, int OffValue, bool AbsentIsOff = false, int[]? AlsoOff = null)`
  - `public sealed record SwitchSpec(string Id, string Title, string Evidence, IReadOnlyList<SwitchValue> Values, WriteEffect Effect = WriteEffect.NotAPolicy)`
  - `public class SwitchRule : TelemetrySwitchRule` — `SwitchRule(SwitchSpec spec)`, `protected SwitchSpec Spec`
  - `public sealed class TelemetryServicesRule : SwitchRule` — `TelemetryServicesRule(IProcessRunner runner)`
  - `public static class SwitchCatalog` — `TelemetryServices`, `ErrorReporting`, `Feedback`, `Copilot`, `Recall`, `DeliveryPeers`, `GameRecording`, `GpuScheduling`, `MemoryIntegrity` (all `SwitchSpec`), `IReadOnlyList<SwitchSpec> Plain` (all but `TelemetryServices`)
  - Rule ids: `telemetry-services`, `error-reporting-off`, `feedback-off`, `copilot-off`, `recall-off`, `delivery-peers-off`, `game-recording`, `gpu-scheduling`, `memory-integrity`.

A switch's `IsOn` means "brisk would still act here" — the meaning `ReadBack` already gives it: still on after a fix reads as *Reverted*.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using BriskEngine.Actions.Switches;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

public sealed class SwitchRuleTests
{
    private static (SwitchRule Rule, FakeRegistry Reg, DiagnosticContext Ctx) Make(SwitchSpec spec)
    {
        var ctx = TestContext.Empty();
        return (new SwitchRule(spec), (FakeRegistry)ctx.Registry, ctx);
    }

    [Fact]
    public void AbsentValueDeclaredOff_IsNotOn_AndIsNeverCreated()
    {
        var (rule, reg, ctx) = Make(SwitchCatalog.GpuScheduling);
        Assert.False(rule.IsOn(ctx));
        Assert.Null(rule.Detect(ctx));
        rule.Fix(ctx);
        Assert.Null(reg.GetInt(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"));
    }

    [Fact]
    public void GpuScheduling_RecordedOff_IsOn_FixTurnsItOn_UndoRestores()
    {
        var (rule, reg, ctx) = Make(SwitchCatalog.GpuScheduling);
        const string key = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
        reg.SetInt(key, "HwSchMode", 1);
        Assert.NotNull(rule.Detect(ctx));

        var prior = rule.Fix(ctx);
        Assert.Equal(2, reg.GetInt(key, "HwSchMode"));
        Assert.False(rule.IsOn(ctx));

        rule.Undo(ctx, prior);
        Assert.Equal(1, reg.GetInt(key, "HwSchMode"));
    }

    [Fact]
    public void Fix_WritesOnlyTheValuesThatReadAsOn()
    {
        var (rule, reg, ctx) = Make(SwitchCatalog.TelemetryServices);
        const string dmw = @"HKLM\SYSTEM\CurrentControlSet\Services\dmwappushservice";
        reg.SetInt(dmw, "Start", 3);   // DiagTrack absent: no such service here

        var prior = rule.Fix(ctx);

        Assert.Equal(4, reg.GetInt(dmw, "Start"));
        Assert.Null(reg.GetInt(@"HKLM\SYSTEM\CurrentControlSet\Services\DiagTrack", "Start"));
        rule.Undo(ctx, prior);
        Assert.Equal(3, reg.GetInt(dmw, "Start"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(3, true)]
    [InlineData(1, false)]
    [InlineData(0, false)]
    [InlineData(99, false)]
    [InlineData(100, false)]
    public void DeliveryPeers_ModesThatKeepTrafficLocalReadAsOff(int? mode, bool on)
    {
        var (rule, reg, ctx) = Make(SwitchCatalog.DeliveryPeers);
        if (mode is { } m)
            reg.SetInt(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", m);
        Assert.Equal(on, rule.IsOn(ctx));
    }

    [Fact]
    public void APolicySwitch_ReadsBackAsWrittenButUnverified()
    {
        var (rule, _, ctx) = Make(SwitchCatalog.Copilot);
        Assert.True(rule.IsOn(ctx));
        rule.Fix(ctx);
        Assert.Equal(ReadBackState.WrittenButUnverified, ReadBack.StateOf(ctx, rule));
    }

    [Fact]
    public void ErrorReporting_AbsentMeansOn()
    {
        var (rule, reg, ctx) = Make(SwitchCatalog.ErrorReporting);
        Assert.True(rule.IsOn(ctx));
        rule.Fix(ctx);
        Assert.Equal(1, reg.GetInt(@"HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled"));
    }

    [Fact]
    public void CatalogIdsAreUnique_AndPlainExcludesTheServiceSwitch()
    {
        var ids = SwitchCatalog.Plain.Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.DoesNotContain("telemetry-services", ids);
        Assert.Contains("memory-integrity", ids);
    }

    [Fact]
    public void TelemetryServices_StopsTheServicesItDisabled_AndUndoStartsDiagTrack()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetInt(@"HKLM\SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 2);
        var runner = new FakeRunner();
        var rule = new TelemetryServicesRule(runner);

        var prior = rule.Fix(ctx);

        Assert.Contains("sc.exe stop DiagTrack", runner.Commands);
        rule.Undo(ctx, prior);
        Assert.Equal(2, reg.GetInt(@"HKLM\SYSTEM\CurrentControlSet\Services\DiagTrack", "Start"));
        Assert.Contains("sc.exe start DiagTrack", runner.Commands);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~SwitchRuleTests"`
Expected: build FAILS — namespace `BriskEngine.Actions.Switches` does not exist.

- [ ] **Step 3: Implement `SwitchSpec.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;

namespace BriskEngine.Actions.Switches;

/// One registry value of a switch. OffValue is what the fix writes and what
/// reads as done. AbsentIsOff: a missing value already means "done", and the
/// fix never creates it. AlsoOff: other numbers that also mean "done".
public sealed record SwitchValue(string KeyPath, string ValueName, int OffValue,
    bool AbsentIsOff = false, int[]? AlsoOff = null);

public sealed record SwitchSpec(string Id, string Title, string Evidence,
    IReadOnlyList<SwitchValue> Values, WriteEffect Effect = WriteEffect.NotAPolicy);

/// A TelemetrySwitchRule built from data, so a new switch is a few lines in
/// SwitchCatalog and rides FixRunner, FixJournal and ReadBack unchanged.
public class SwitchRule : TelemetrySwitchRule
{
    private sealed record PriorValue(string KeyPath, string ValueName, int? Value);

    protected SwitchSpec Spec { get; }

    public SwitchRule(SwitchSpec spec) { Spec = spec; }

    public override string Id => Spec.Id;

    public override IReadOnlyList<RegistryValue> Values => Spec.Values
        .Select(v => new RegistryValue(v.KeyPath, v.ValueName,
            OnValue: v.OffValue == 0 ? 1 : 0, OffValue: v.OffValue))
        .ToList();

    protected override string Title => Spec.Title;
    protected override string Evidence => Spec.Evidence;
    protected override string FixDescription => Spec.Title;

    public override WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => Spec.Effect;

    internal static bool ReadsAsOn(int? actual, SwitchValue v)
    {
        if (actual is null) return !v.AbsentIsOff;
        if (actual == v.OffValue) return false;
        return v.AlsoOff is null || !v.AlsoOff.Contains(actual.Value);
    }

    protected override bool ReadsAsOn(int? actual, RegistryValue value) =>
        ReadsAsOn(actual, Spec.Values.First(s =>
            s.KeyPath == value.KeyPath && s.ValueName == value.ValueName));

    /// Writes only the values that read as on: a service that does not exist
    /// on this machine must not get a Start value created for it.
    public override string Fix(DiagnosticContext ctx)
    {
        var touched = Spec.Values
            .Where(v => ReadsAsOn(ctx.Registry.GetInt(v.KeyPath, v.ValueName), v))
            .ToList();
        var prior = touched
            .Select(v => new PriorValue(v.KeyPath, v.ValueName, ctx.Registry.GetInt(v.KeyPath, v.ValueName)))
            .ToList();
        foreach (var v in touched) ctx.Registry.SetInt(v.KeyPath, v.ValueName, v.OffValue);
        return JsonSerializer.Serialize(prior);
    }

    public override void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<List<PriorValue>>(priorStateJson) ?? new();
        foreach (var p in prior)
        {
            if (p.Value is null) ctx.Registry.DeleteValue(p.KeyPath, p.ValueName);
            else ctx.Registry.SetInt(p.KeyPath, p.ValueName, p.Value.Value);
        }
    }
}
```

- [ ] **Step 4: Implement `SwitchCatalog.cs`**

```csharp
using System.Collections.Generic;
using BriskEngine.Diagnostics.Rules.Privacy;

namespace BriskEngine.Actions.Switches;

public static class SwitchCatalog
{
    public const string Services = @"HKLM\SYSTEM\CurrentControlSet\Services";

    public static readonly SwitchSpec TelemetryServices = new("telemetry-services",
        "Connected User Experiences and Telemetry (DiagTrack, dmwappushservice)",
        "The services that send usage data to Microsoft start with Windows.",
        new[]
        {
            new SwitchValue($@"{Services}\DiagTrack", "Start", 4, AbsentIsOff: true),
            new SwitchValue($@"{Services}\dmwappushservice", "Start", 4, AbsentIsOff: true),
        });

    public static readonly SwitchSpec ErrorReporting = new("error-reporting-off",
        "Windows Error Reporting",
        "Windows sends crash and error reports to Microsoft.",
        new[] { new SwitchValue(@"HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1) });

    public static readonly SwitchSpec Feedback = new("feedback-off",
        "Feedback frequency",
        "Windows asks how you like it and sends the answers to Microsoft.",
        new[]
        {
            new SwitchValue(@"HKCU\Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0),
            new SwitchValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1),
        },
        WriteEffect.Unread);

    public static readonly SwitchSpec Copilot = new("copilot-off",
        "Windows Copilot",
        "Copilot is allowed and its button can sit on the taskbar.",
        new[]
        {
            new SwitchValue(@"HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1),
            new SwitchValue(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 0, AbsentIsOff: true),
        },
        WriteEffect.Unread);

    public static readonly SwitchSpec Recall = new("recall-off",
        "Recall",
        "Recall is allowed to save snapshots of the screen.",
        new[] { new SwitchValue(@"HKCU\Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1) },
        WriteEffect.Unread);

    public static readonly SwitchSpec DeliveryPeers = new("delivery-peers-off",
        "Delivery Optimization",
        "Windows can upload updates to other PCs on the internet.",
        new[]
        {
            new SwitchValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 1,
                AlsoOff: new[] { 0, 99, 100 }),
        },
        WriteEffect.Unread);

    public static readonly SwitchSpec GameRecording = new("game-recording",
        "Game DVR background recording",
        "Windows records gameplay in the background.",
        new[] { new SwitchValue(@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", 0, AbsentIsOff: true) });

    public static readonly SwitchSpec GpuScheduling = new("gpu-scheduling",
        "Hardware-accelerated GPU scheduling",
        "Windows recorded GPU scheduling as off on this machine.",
        new[] { new SwitchValue(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, AbsentIsOff: true) });

    public static readonly SwitchSpec MemoryIntegrity = new("memory-integrity",
        "Memory integrity",
        "Memory integrity is on.",
        new[] { new SwitchValue(@"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0, AbsentIsOff: true) });

    /// Every spec that is a plain SwitchRule. TelemetryServices is not: its
    /// rule also stops the services (TelemetryServicesRule).
    public static IReadOnlyList<SwitchSpec> Plain { get; } = new[]
    {
        ErrorReporting, Feedback, Copilot, Recall, DeliveryPeers,
        GameRecording, GpuScheduling, MemoryIntegrity,
    };
}
```

- [ ] **Step 5: Implement `TelemetryServicesRule.cs`**

```csharp
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;

namespace BriskEngine.Actions.Switches;

/// The DiagTrack switch, which also stops the services now: the start type
/// alone waits for a reboot, and the receipt counts telemetry connections two
/// minutes after the run.
public sealed class TelemetryServicesRule : SwitchRule
{
    private readonly IProcessRunner _runner;

    public TelemetryServicesRule(IProcessRunner runner) : base(SwitchCatalog.TelemetryServices)
    {
        _runner = runner;
    }

    public override string Fix(DiagnosticContext ctx)
    {
        var prior = base.Fix(ctx);
        _runner.Run("sc.exe", "stop DiagTrack");
        _runner.Run("sc.exe", "stop dmwappushservice");
        return prior;
    }

    public override void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        base.Undo(ctx, priorStateJson);
        if (ctx.Registry.GetInt($@"{SwitchCatalog.Services}\DiagTrack", "Start") == 2)
            _runner.Run("sc.exe", "start DiagTrack");
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~SwitchRuleTests"`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add src/BriskEngine/Actions/Switches src/BriskEngine.Tests/SwitchRuleTests.cs
git commit -m "feat(actions): data-defined registry switches for privacy and games" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Scheduled-task switch

**Files:**
- Create: `src/BriskEngine/Actions/Switches/TaskSwitchRule.cs`
- Test: `src/BriskEngine.Tests/TaskSwitchRuleTests.cs`

**Interfaces:**
- Consumes: `IReadBackable` (Task 2), `IProcessRunner`.
- Produces:
  - `public interface IScheduledTaskProbe { bool? IsEnabled(string taskPath); void SetEnabled(string taskPath, bool enabled); }` (`null` = no such task)
  - `public sealed class RealScheduledTaskProbe : IScheduledTaskProbe` — `RealScheduledTaskProbe(IProcessRunner runner)`, `internal static bool? ParseEnabled(string xml)`
  - `public sealed class TaskSwitchRule : IReadBackable` — `TaskSwitchRule(IScheduledTaskProbe probe)`, `static IReadOnlyList<string> TelemetryTasks`, id `telemetry-tasks`
  - Test fake `FakeTasks : IScheduledTaskProbe` (internal, in the test file) — reused by Tasks 13–15.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using BriskEngine.Actions.Switches;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

sealed class FakeTasks : IScheduledTaskProbe
{
    public Dictionary<string, bool> Tasks = new(System.StringComparer.OrdinalIgnoreCase);
    public bool? IsEnabled(string taskPath) => Tasks.TryGetValue(taskPath, out var on) ? on : null;
    public void SetEnabled(string taskPath, bool enabled) => Tasks[taskPath] = enabled;
}

public sealed class TaskSwitchRuleTests
{
    private const string Consolidator =
        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator";
    private const string UsbCeip =
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip";

    [Fact]
    public void ParseEnabled_ReadsTheTaskSetting_NotATriggerSetting()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers><TimeTrigger><Enabled>false</Enabled></TimeTrigger></Triggers>
              <Settings><Enabled>true</Enabled></Settings>
            </Task>
            """;
        Assert.True(RealScheduledTaskProbe.ParseEnabled(xml));
    }

    [Fact]
    public void ParseEnabled_DisabledTask()
    {
        const string xml = """
            <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Settings><Enabled>false</Enabled></Settings>
            </Task>
            """;
        Assert.False(RealScheduledTaskProbe.ParseEnabled(xml));
    }

    [Fact]
    public void ParseEnabled_NoEnabledElement_MeansEnabled()
    {
        const string xml = """
            <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task"><Settings /></Task>
            """;
        Assert.True(RealScheduledTaskProbe.ParseEnabled(xml));
    }

    [Fact]
    public void ParseEnabled_Garbage_IsNull()
    {
        Assert.Null(RealScheduledTaskProbe.ParseEnabled("ERROR: The system cannot find the file specified."));
    }

    [Fact]
    public void IsOn_WhenAnyListedTaskExistsAndIsEnabled()
    {
        var tasks = new FakeTasks();
        var rule = new TaskSwitchRule(tasks);
        var ctx = TestContext.Empty();
        Assert.False(rule.IsOn(ctx));   // none exist on this machine
        tasks.Tasks[UsbCeip] = false;
        Assert.False(rule.IsOn(ctx));
        tasks.Tasks[Consolidator] = true;
        Assert.True(rule.IsOn(ctx));
        Assert.NotNull(rule.Detect(ctx));
    }

    [Fact]
    public void Fix_DisablesOnlyEnabledTasks_UndoReenablesExactlyThose()
    {
        var tasks = new FakeTasks();
        tasks.Tasks[Consolidator] = true;
        tasks.Tasks[UsbCeip] = false;
        var rule = new TaskSwitchRule(tasks);
        var ctx = TestContext.Empty();

        var prior = rule.Fix(ctx);
        Assert.False(tasks.Tasks[Consolidator]);
        Assert.Equal(ReadBackState.Held, ReadBack.StateOf(ctx, rule));

        rule.Undo(ctx, prior);
        Assert.True(tasks.Tasks[Consolidator]);
        Assert.False(tasks.Tasks[UsbCeip]);
    }

    [Fact]
    public void RealProbe_SetEnabledThrows_AndIsEnabledIsNull_WhenSchtasksFails()
    {
        var probe = new RealScheduledTaskProbe(new ScriptedRunner((_, _) => (1, "")));
        Assert.Throws<System.InvalidOperationException>(() => probe.SetEnabled(Consolidator, false));
        Assert.Null(probe.IsEnabled(Consolidator));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~TaskSwitchRuleTests"`
Expected: build FAILS — `TaskSwitchRule` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;

namespace BriskEngine.Actions.Switches;

public interface IScheduledTaskProbe
{
    /// null: no such task on this machine.
    bool? IsEnabled(string taskPath);
    void SetEnabled(string taskPath, bool enabled);
}

/// schtasks /XML is read instead of the table output because the table's
/// status words are localized; the XML element names are not.
public sealed class RealScheduledTaskProbe : IScheduledTaskProbe
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private readonly IProcessRunner _runner;

    public RealScheduledTaskProbe(IProcessRunner runner) { _runner = runner; }

    public bool? IsEnabled(string taskPath)
    {
        var (exit, xml) = _runner.Run("schtasks.exe", $"/Query /TN \"{taskPath}\" /XML");
        return exit == 0 ? ParseEnabled(xml) : null;
    }

    public void SetEnabled(string taskPath, bool enabled)
    {
        var (exit, _) = _runner.Run("schtasks.exe",
            $"/Change /TN \"{taskPath}\" /{(enabled ? "Enable" : "Disable")}");
        if (exit != 0)
            throw new InvalidOperationException($"schtasks exited {exit} for '{taskPath}'");
    }

    internal static bool? ParseEnabled(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml.Trim());
            var settings = doc.Root?.Element(Ns + "Settings");
            if (settings is null) return null;
            var enabled = settings.Element(Ns + "Enabled");
            return enabled is null || !string.Equals(enabled.Value.Trim(), "false",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (XmlException) { return null; }
    }
}

/// The scheduled tasks that collect telemetry. A task this build does not
/// have (Compatibility Appraiser is gone on 26200) is skipped silently.
public sealed class TaskSwitchRule : IReadBackable
{
    public static IReadOnlyList<string> TelemetryTasks { get; } = new[]
    {
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser Exp",
        @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
        @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
        @"\Microsoft\Windows\Feedback\Siuf\DmClient",
        @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload",
    };

    private readonly IScheduledTaskProbe _probe;

    public TaskSwitchRule(IScheduledTaskProbe probe) { _probe = probe; }

    public string Id => "telemetry-tasks";
    public RuleCategory Category => RuleCategory.Auto;

    private List<string> Enabled() =>
        TelemetryTasks.Where(t => _probe.IsEnabled(t) == true).ToList();

    public bool IsOn(DiagnosticContext ctx) => Enabled().Count > 0;

    public WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => WriteEffect.NotAPolicy;

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var enabled = Enabled();
        if (enabled.Count == 0) return null;
        return new DiagnosticFinding(Id, $"rule.{Id}.title",
            "Telemetry scheduled tasks are enabled",
            $"{enabled.Count} scheduled tasks that collect usage data are enabled.",
            Severity.Info, Category, ImpactStars: 1, CanFix: true,
            FixDescription: "Disable the telemetry scheduled tasks (undoable)",
            Kind: FindingKind.Notice);
    }

    public string Fix(DiagnosticContext ctx)
    {
        var enabled = Enabled();
        foreach (var t in enabled) _probe.SetEnabled(t, false);
        return JsonSerializer.Serialize(enabled);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        foreach (var t in JsonSerializer.Deserialize<List<string>>(priorStateJson) ?? new())
            _probe.SetEnabled(t, true);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~TaskSwitchRuleTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Actions/Switches/TaskSwitchRule.cs src/BriskEngine.Tests/TaskSwitchRuleTests.cs
git commit -m "feat(actions): switch for the telemetry scheduled tasks" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Window animations and windowed games

**Files:**
- Create: `src/BriskEngine/Actions/Rules/WindowAnimationsRule.cs`
- Create: `src/BriskEngine/Actions/Rules/WindowedGamesRule.cs`
- Test: `src/BriskEngine.Tests/SpeedSwitchTests.cs`

**Interfaces:**
- Consumes: `IReadBackable`, `IRegistryProbe`.
- Produces:
  - `public interface IUiEffectsProbe { bool MinimizeAnimation(); bool ClientAreaAnimation(); void Set(bool minimize, bool clientArea); }`
  - `public sealed class RealUiEffectsProbe : IUiEffectsProbe`
  - `public sealed class WindowAnimationsRule : IReadBackable` — `WindowAnimationsRule(IUiEffectsProbe ui)`, id `window-animations`
  - `public sealed class WindowedGamesRule : IReadBackable` — parameterless, id `windowed-games`, `internal static string? ReadSetting(string? all, string key)`, `internal static string WithSetting(string? all, string key, string value)`
  - Test fake `FakeUi : IUiEffectsProbe` (internal) — reused by Task 13.

`SystemParametersInfo` changes take effect at once, which is what the cost line "Pencereler anında açılır" promises; the existing `VisualEffectsRule` only writes `VisualFXSetting`, which Windows does not apply until the preset is re-chosen, so it is not reused here.

- [ ] **Step 1: Write the failing tests**

```csharp
using BriskEngine.Actions.Rules;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

sealed class FakeUi : IUiEffectsProbe
{
    public bool Minimize = true, ClientArea = true;
    public bool MinimizeAnimation() => Minimize;
    public bool ClientAreaAnimation() => ClientArea;
    public void Set(bool minimize, bool clientArea) { Minimize = minimize; ClientArea = clientArea; }
}

public sealed class SpeedSwitchTests
{
    [Fact]
    public void Animations_FixTurnsBothOff_UndoRestoresEach()
    {
        var ui = new FakeUi { Minimize = true, ClientArea = false };
        var rule = new WindowAnimationsRule(ui);
        var ctx = TestContext.Empty();
        Assert.True(rule.IsOn(ctx));

        var prior = rule.Fix(ctx);
        Assert.False(ui.Minimize);
        Assert.False(ui.ClientArea);
        Assert.Equal(ReadBackState.Held, ReadBack.StateOf(ctx, rule));

        rule.Undo(ctx, prior);
        Assert.True(ui.Minimize);
        Assert.False(ui.ClientArea);
    }

    [Fact]
    public void Animations_AlreadyOff_NoFinding()
    {
        var rule = new WindowAnimationsRule(new FakeUi { Minimize = false, ClientArea = false });
        Assert.Null(rule.Detect(TestContext.Empty()));
    }

    [Theory]
    [InlineData(null, "SwapEffectUpgradeEnable", null)]
    [InlineData("VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;", "SwapEffectUpgradeEnable", "1")]
    [InlineData("SwapEffectUpgradeEnable=0;", "SwapEffectUpgradeEnable", "0")]
    public void ReadSetting(string? all, string key, string? expected) =>
        Assert.Equal(expected, WindowedGamesRule.ReadSetting(all, key));

    [Theory]
    [InlineData(null, "SwapEffectUpgradeEnable=1;")]
    [InlineData("VRROptimizeEnable=0;", "VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;")]
    [InlineData("SwapEffectUpgradeEnable=0;VRROptimizeEnable=0;", "SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;")]
    public void WithSetting_KeepsTheOtherSettings(string? all, string expected) =>
        Assert.Equal(expected, WindowedGamesRule.WithSetting(all, "SwapEffectUpgradeEnable", "1"));

    [Fact]
    public void WindowedGames_OnlyOnWindows11()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        var rule = new WindowedGamesRule();

        reg.SetString(WindowedGamesRule.BuildKey, "CurrentBuildNumber", "19045");
        Assert.False(rule.IsOn(ctx));

        reg.SetString(WindowedGamesRule.BuildKey, "CurrentBuildNumber", "26200");
        Assert.True(rule.IsOn(ctx));
    }

    [Fact]
    public void WindowedGames_FixAndUndo()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetString(WindowedGamesRule.BuildKey, "CurrentBuildNumber", "26200");
        reg.SetString(WindowedGamesRule.Key, WindowedGamesRule.Value, "VRROptimizeEnable=0;");
        var rule = new WindowedGamesRule();

        var prior = rule.Fix(ctx);
        Assert.Equal("VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;", reg.GetString(WindowedGamesRule.Key, WindowedGamesRule.Value));
        Assert.False(rule.IsOn(ctx));

        rule.Undo(ctx, prior);
        Assert.Equal("VRROptimizeEnable=0;", reg.GetString(WindowedGamesRule.Key, WindowedGamesRule.Value));
    }

    [Fact]
    public void WindowedGames_UndoDeletesAValueThatWasAbsent()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetString(WindowedGamesRule.BuildKey, "CurrentBuildNumber", "22631");
        var rule = new WindowedGamesRule();
        var prior = rule.Fix(ctx);
        rule.Undo(ctx, prior);
        Assert.Null(reg.GetString(WindowedGamesRule.Key, WindowedGamesRule.Value));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~SpeedSwitchTests"`
Expected: build FAILS — `BriskEngine.Actions.Rules` does not exist.

- [ ] **Step 3: Implement `WindowAnimationsRule.cs`**

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;

namespace BriskEngine.Actions.Rules;

public interface IUiEffectsProbe
{
    bool MinimizeAnimation();
    bool ClientAreaAnimation();
    void Set(bool minimize, bool clientArea);
}

public sealed class RealUiEffectsProbe : IUiEffectsProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct AnimationInfo { public uint cbSize; public int iMinAnimate; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, ref AnimationInfo pvParam, uint winIni);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, ref bool pvParam, uint winIni);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, System.IntPtr pvParam, uint winIni);

    private const uint SPI_GETANIMATION = 0x0048, SPI_SETANIMATION = 0x0049;
    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042, SPI_SETCLIENTAREAANIMATION = 0x1043;
    private const uint SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

    public bool MinimizeAnimation()
    {
        var info = new AnimationInfo { cbSize = (uint)Marshal.SizeOf<AnimationInfo>() };
        return SystemParametersInfo(SPI_GETANIMATION, info.cbSize, ref info, 0) && info.iMinAnimate != 0;
    }

    public bool ClientAreaAnimation()
    {
        var on = false;
        return SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref on, 0) && on;
    }

    public void Set(bool minimize, bool clientArea)
    {
        var info = new AnimationInfo
        {
            cbSize = (uint)Marshal.SizeOf<AnimationInfo>(),
            iMinAnimate = minimize ? 1 : 0,
        };
        if (!SystemParametersInfo(SPI_SETANIMATION, info.cbSize, ref info, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, (System.IntPtr)(clientArea ? 1 : 0),
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}

/// Minimize/maximize and in-window animations, through SystemParametersInfo
/// so the change is felt at once.
public sealed class WindowAnimationsRule : IReadBackable
{
    private sealed record Prior(bool Minimize, bool ClientArea);
    private readonly IUiEffectsProbe _ui;

    public WindowAnimationsRule(IUiEffectsProbe ui) { _ui = ui; }

    public string Id => "window-animations";
    public RuleCategory Category => RuleCategory.Auto;

    public bool IsOn(DiagnosticContext ctx) => _ui.MinimizeAnimation() || _ui.ClientAreaAnimation();

    public WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => WriteEffect.NotAPolicy;

    public DiagnosticFinding? Detect(DiagnosticContext ctx) => IsOn(ctx)
        ? new DiagnosticFinding(Id, $"rule.{Id}.title", "Window animations are on",
            "Windows animates windows when they open, minimize and maximize.",
            Severity.Info, Category, ImpactStars: 1, CanFix: true,
            FixDescription: "Turn window animations off (undoable)", Kind: FindingKind.Notice)
        : null;

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Prior(_ui.MinimizeAnimation(), _ui.ClientAreaAnimation());
        _ui.Set(false, false);
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Prior>(priorStateJson)!;
        _ui.Set(prior.Minimize, prior.ClientArea);
    }
}
```

- [ ] **Step 4: Implement `WindowedGamesRule.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;

namespace BriskEngine.Actions.Rules;

/// Windows 11's "Optimizations for windowed games": one "k=v;" pair inside a
/// string value that can hold other DirectX settings, which are kept.
public sealed class WindowedGamesRule : IReadBackable
{
    public const string Key = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    public const string Value = "DirectXUserGlobalSettings";
    public const string Setting = "SwapEffectUpgradeEnable";
    public const string BuildKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    public const int FirstWindows11Build = 22000;

    private sealed record Prior(string? Value);

    public string Id => "windowed-games";
    public RuleCategory Category => RuleCategory.Auto;

    private static bool Applies(DiagnosticContext ctx) =>
        int.TryParse(ctx.Registry.GetString(BuildKey, "CurrentBuildNumber"), out var build)
        && build >= FirstWindows11Build;

    public bool IsOn(DiagnosticContext ctx) =>
        Applies(ctx) && ReadSetting(ctx.Registry.GetString(Key, Value), Setting) != "1";

    public WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => WriteEffect.NotAPolicy;

    public DiagnosticFinding? Detect(DiagnosticContext ctx) => IsOn(ctx)
        ? new DiagnosticFinding(Id, $"rule.{Id}.title", "Optimizations for windowed games are off",
            "Games in windowed and borderless mode use the slower presentation path.",
            Severity.Info, Category, ImpactStars: 1, CanFix: true,
            FixDescription: "Turn on optimizations for windowed games (undoable)", Kind: FindingKind.Notice)
        : null;

    public string Fix(DiagnosticContext ctx)
    {
        var current = ctx.Registry.GetString(Key, Value);
        ctx.Registry.SetString(Key, Value, WithSetting(current, Setting, "1"));
        return JsonSerializer.Serialize(new Prior(current));
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Prior>(priorStateJson)!;
        if (prior.Value is null) ctx.Registry.DeleteValue(Key, Value);
        else ctx.Registry.SetString(Key, Value, prior.Value);
    }

    private static List<(string Key, string Value)> Pairs(string? all) =>
        (all ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .Select(p => (p[0].Trim(), p[1].Trim()))
            .ToList();

    internal static string? ReadSetting(string? all, string key) =>
        Pairs(all).Where(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Value).FirstOrDefault();

    internal static string WithSetting(string? all, string key, string value)
    {
        var pairs = Pairs(all);
        var i = pairs.FindIndex(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) pairs[i] = (key, value);
        else pairs.Add((key, value));
        return string.Concat(pairs.Select(p => $"{p.Key}={p.Value};"));
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~SpeedSwitchTests"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/BriskEngine/Actions/Rules/WindowAnimationsRule.cs src/BriskEngine/Actions/Rules/WindowedGamesRule.cs src/BriskEngine.Tests/SpeedSwitchTests.cs
git commit -m "feat(actions): window animations and windowed-games switches" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Power mode and virtual machine platform

**Files:**
- Create: `src/BriskEngine/Actions/Rules/PowerModeRule.cs`
- Create: `src/BriskEngine/Actions/Rules/VirtualMachinePlatformRule.cs`
- Test: `src/BriskEngine.Tests/PowerAndFeatureRuleTests.cs`

**Interfaces:**
- Consumes: `IReadBackable`, `IPowercfgProbe.GetActiveScheme()`, `PowerPlanRule.Balanced`, `IProcessRunner`.
- Produces:
  - `public interface IPowerOverlayProbe { bool OnAcPower(); void ApplyOverlay(System.Guid overlay); }`, `public sealed class RealPowerOverlayProbe : IPowerOverlayProbe`
  - `public sealed class PowerModeRule : IReadBackable` — `PowerModeRule(IPowerOverlayProbe overlay)`, id `power-mode`, `static readonly Guid BestPerformance`
  - `public interface IOptionalFeatureProbe { int? InstallState(string featureName); }` (1 enabled, 2 disabled, 3 absent, `null` unread), `public sealed class RealOptionalFeatureProbe : IOptionalFeatureProbe`
  - `public sealed class VirtualMachinePlatformRule : IReadBackable` — `VirtualMachinePlatformRule(IOptionalFeatureProbe features, IProcessRunner runner)`, id `vm-platform`
  - Test fakes `FakeOverlay`, `FakeFeatures` (internal) — reused by Tasks 13–15.

Power mode applies only while the active plan is Balanced: Windows shows the power-mode slider only then. The plugged-in value is the one Settings writes (`ActiveOverlayAcPowerScheme`); when the machine is on AC power the overlay is also applied now.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using BriskEngine.Actions.Rules;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using Xunit;

namespace BriskEngine.Tests;

sealed class FakeOverlay : IPowerOverlayProbe
{
    public bool Ac = true;
    public List<Guid> Applied = new();
    public bool OnAcPower() => Ac;
    public void ApplyOverlay(Guid overlay) => Applied.Add(overlay);
}

sealed class FakeFeatures : IOptionalFeatureProbe
{
    public Dictionary<string, int> States = new(StringComparer.OrdinalIgnoreCase);
    public int? InstallState(string featureName) => States.TryGetValue(featureName, out var s) ? s : 3;
}

public sealed class PowerAndFeatureRuleTests
{
    private static DiagnosticContext Balanced()
    {
        var ctx = TestContext.Empty();
        ((FakePowercfg)ctx.Powercfg).Active = (PowerPlanRule.Balanced, "Balanced");
        return ctx;
    }

    [Fact]
    public void PowerMode_OnlyWhileTheBalancedPlanIsActive()
    {
        var ctx = TestContext.Empty();
        ((FakePowercfg)ctx.Powercfg).Active = (PowerPlanRule.HighPerformance, "High performance");
        Assert.False(new PowerModeRule(new FakeOverlay()).IsOn(ctx));
        Assert.True(new PowerModeRule(new FakeOverlay()).IsOn(Balanced()));
    }

    [Fact]
    public void PowerMode_FixWritesThePluggedInValue_AndAppliesItOnAc()
    {
        var ctx = Balanced();
        var overlay = new FakeOverlay { Ac = true };
        var rule = new PowerModeRule(overlay);

        var prior = rule.Fix(ctx);

        Assert.Equal(PowerModeRule.BestPerformance.ToString(),
            ctx.Registry.GetString(PowerModeRule.Key, PowerModeRule.AcValue));
        Assert.Equal(new[] { PowerModeRule.BestPerformance }, overlay.Applied);
        Assert.False(rule.IsOn(ctx));

        rule.Undo(ctx, prior);
        Assert.Null(ctx.Registry.GetString(PowerModeRule.Key, PowerModeRule.AcValue));
        Assert.Equal(Guid.Empty, overlay.Applied[^1]);
    }

    [Fact]
    public void PowerMode_OnBattery_WritesButDoesNotApply()
    {
        var ctx = Balanced();
        var overlay = new FakeOverlay { Ac = false };
        new PowerModeRule(overlay).Fix(ctx);
        Assert.Empty(overlay.Applied);
        Assert.Equal(PowerModeRule.BestPerformance.ToString(),
            ctx.Registry.GetString(PowerModeRule.Key, PowerModeRule.AcValue));
    }

    [Fact]
    public void Vmp_OnlyWhenEnabled()
    {
        var features = new FakeFeatures();
        var rule = new VirtualMachinePlatformRule(features, new FakeRunner());
        Assert.False(rule.IsOn(TestContext.Empty()));
        features.States[VirtualMachinePlatformRule.Feature] = 1;
        Assert.True(rule.IsOn(TestContext.Empty()));
    }

    [Fact]
    public void Vmp_FixAndUndoRunDism_AndRestartPendingCountsAsSuccess()
    {
        var features = new FakeFeatures();
        features.States[VirtualMachinePlatformRule.Feature] = 1;
        var runner = new ScriptedRunner((_, _) => (3010, ""));
        var rule = new VirtualMachinePlatformRule(features, runner);

        var prior = rule.Fix(TestContext.Empty());
        rule.Undo(TestContext.Empty(), prior);

        Assert.Contains("dism.exe /Online /Disable-Feature /FeatureName:VirtualMachinePlatform /NoRestart", runner.Commands);
        Assert.Contains("dism.exe /Online /Enable-Feature /FeatureName:VirtualMachinePlatform /All /NoRestart", runner.Commands);
    }

    [Fact]
    public void Vmp_DismFailure_Throws()
    {
        var features = new FakeFeatures();
        features.States[VirtualMachinePlatformRule.Feature] = 1;
        var rule = new VirtualMachinePlatformRule(features, new ScriptedRunner((_, _) => (740, "")));
        Assert.Throws<InvalidOperationException>(() => rule.Fix(TestContext.Empty()));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~PowerAndFeatureRuleTests"`
Expected: build FAILS — `PowerModeRule` does not exist.

- [ ] **Step 3: Implement `PowerModeRule.cs`**

```csharp
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;

namespace BriskEngine.Actions.Rules;

public interface IPowerOverlayProbe
{
    bool OnAcPower();
    void ApplyOverlay(Guid overlay);
}

public sealed class RealPowerOverlayProbe : IPowerOverlayProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

    /// 0 = on battery. 1 (AC) and 255 (unknown) both count as plugged in.
    public bool OnAcPower() => !GetSystemPowerStatus(out var s) || s.ACLineStatus != 0;

    public void ApplyOverlay(Guid overlay)
    {
        var rc = PowerSetActiveOverlayScheme(overlay);
        if (rc != 0) throw new Win32Exception((int)rc);
    }
}

/// "Best performance" power mode while plugged in. Battery mode is untouched.
public sealed class PowerModeRule : IReadBackable
{
    public static readonly Guid BestPerformance = Guid.Parse("ded574b5-45a0-4f42-8737-46345c09c238");
    public const string Key = @"HKLM\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";
    public const string AcValue = "ActiveOverlayAcPowerScheme";

    private sealed record Prior(string? Ac);
    private readonly IPowerOverlayProbe _overlay;

    public PowerModeRule(IPowerOverlayProbe overlay) { _overlay = overlay; }

    public string Id => "power-mode";
    public RuleCategory Category => RuleCategory.Auto;

    public bool IsOn(DiagnosticContext ctx)
    {
        if (ctx.Powercfg.GetActiveScheme().Id != PowerPlanRule.Balanced) return false;
        var current = ctx.Registry.GetString(Key, AcValue);
        return !Guid.TryParse(current, out var g) || g != BestPerformance;
    }

    public WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => WriteEffect.NotAPolicy;

    public DiagnosticFinding? Detect(DiagnosticContext ctx) => IsOn(ctx)
        ? new DiagnosticFinding(Id, $"rule.{Id}.title", "Power mode is not Best performance when plugged in",
            "While plugged in, Windows balances speed against power use.",
            Severity.Info, Category, ImpactStars: 2, CanFix: true,
            FixDescription: "Use Best performance while plugged in (undoable)", Kind: FindingKind.Notice)
        : null;

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Prior(ctx.Registry.GetString(Key, AcValue));
        ctx.Registry.SetString(Key, AcValue, BestPerformance.ToString());
        if (_overlay.OnAcPower()) _overlay.ApplyOverlay(BestPerformance);
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Prior>(priorStateJson)!;
        if (prior.Ac is null) ctx.Registry.DeleteValue(Key, AcValue);
        else ctx.Registry.SetString(Key, AcValue, prior.Ac);
        if (_overlay.OnAcPower())
            _overlay.ApplyOverlay(Guid.TryParse(prior.Ac, out var g) ? g : Guid.Empty);
    }
}
```

- [ ] **Step 4: Implement `VirtualMachinePlatformRule.cs`**

```csharp
using System;
using System.Management;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;

namespace BriskEngine.Actions.Rules;

public interface IOptionalFeatureProbe
{
    /// 1 enabled, 2 disabled, 3 absent; null when it could not be read.
    int? InstallState(string featureName);
}

public sealed class RealOptionalFeatureProbe : IOptionalFeatureProbe
{
    public int? InstallState(string featureName)
    {
        try
        {
            var name = featureName.Replace("'", "");
            using var searcher = new ManagementObjectSearcher(
                $"SELECT InstallState FROM Win32_OptionalFeature WHERE Name = '{name}'");
            foreach (ManagementObject o in searcher.Get())
                using (o) return Convert.ToInt32(o["InstallState"]);
            return 3;
        }
        catch (Exception) { return null; }
    }
}

/// Listed because Microsoft's gaming guidance names it; never ticked by
/// default because WSL and Docker need it.
public sealed class VirtualMachinePlatformRule : IReadBackable
{
    public const string Feature = "VirtualMachinePlatform";
    private readonly IOptionalFeatureProbe _features;
    private readonly IProcessRunner _runner;

    public VirtualMachinePlatformRule(IOptionalFeatureProbe features, IProcessRunner runner)
    {
        _features = features;
        _runner = runner;
    }

    public string Id => "vm-platform";
    public RuleCategory Category => RuleCategory.Confirm;

    public bool IsOn(DiagnosticContext ctx) => _features.InstallState(Feature) == 1;

    public WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => WriteEffect.NotAPolicy;

    public DiagnosticFinding? Detect(DiagnosticContext ctx) => IsOn(ctx)
        ? new DiagnosticFinding(Id, $"rule.{Id}.title", "Virtual Machine Platform is on",
            "The virtualization layer WSL and Docker use is enabled.",
            Severity.Info, Category, ImpactStars: 1, CanFix: true,
            FixDescription: "Turn Virtual Machine Platform off (undoable, needs a restart)", Kind: FindingKind.Notice)
        : null;

    public string Fix(DiagnosticContext ctx)
    {
        Dism($"/Online /Disable-Feature /FeatureName:{Feature} /NoRestart");
        return "\"enabled\"";
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson) =>
        Dism($"/Online /Enable-Feature /FeatureName:{Feature} /All /NoRestart");

    /// 3010: done, restart required — the row is pending restart either way.
    private void Dism(string args)
    {
        var (exit, _) = _runner.Run("dism.exe", args);
        if (exit is not (0 or 3010))
            throw new InvalidOperationException($"DISM exited {exit}");
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~PowerAndFeatureRuleTests"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/BriskEngine/Actions/Rules/PowerModeRule.cs src/BriskEngine/Actions/Rules/VirtualMachinePlatformRule.cs src/BriskEngine.Tests/PowerAndFeatureRuleTests.cs
git commit -m "feat(actions): plugged-in power mode and virtual machine platform switches" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 7: Security rows — firewall and real-time protection

**Files:**
- Create: `src/BriskEngine/Actions/Rules/FirewallOnRule.cs`
- Create: `src/BriskEngine/Actions/Rules/DefenderRealtimeRule.cs`
- Test: `src/BriskEngine.Tests/SecurityRowRuleTests.cs`

**Interfaces:**
- Consumes: `SecurityPosture.FirewallRoot`, `IDefenderProbe.Read()` / `Registered()`, `DefenderStatus`, `AntivirusProduct(Name, IsDefender, Enabled, DefinitionsOutdated)`, `IProcessRunner`.
- Produces: `public sealed class FirewallOnRule : IDiagnosticRule` (`FirewallOnRule(IProcessRunner runner)`, id `firewall-on`); `public sealed class DefenderRealtimeRule : IDiagnosticRule` (`DefenderRealtimeRule(IProcessRunner runner)`, id `defender-realtime`).

The firewall rule reads the registry itself rather than `SecurityPosture.Read`, which is memoized on the context and would read back the pre-fix state. Real-time protection is only offered when no other antivirus is registered and on: with one, Defender being off is by design.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using BriskEngine.Actions.Rules;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

public sealed class SecurityRowRuleTests
{
    private static string Profile(string p) => $@"{SecurityPosture.FirewallRoot}\{p}";

    [Fact]
    public void Firewall_NoFinding_WhenEveryProfileIsOnOrUnread()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetInt(Profile("DomainProfile"), "EnableFirewall", 1);
        Assert.Null(new FirewallOnRule(new FakeRunner()).Detect(ctx));
    }

    [Fact]
    public void Firewall_TurnsOnOnlyTheProfilesThatAreOff_AndUndoTurnsThoseOff()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetInt(Profile("DomainProfile"), "EnableFirewall", 1);
        reg.SetInt(Profile("PublicProfile"), "EnableFirewall", 0);
        var runner = new ScriptedRunner((_, args) =>
        {
            if (args == "advfirewall set publicprofile state on") reg.SetInt(Profile("PublicProfile"), "EnableFirewall", 1);
            if (args == "advfirewall set publicprofile state off") reg.SetInt(Profile("PublicProfile"), "EnableFirewall", 0);
            return (0, "");
        });
        var rule = new FirewallOnRule(runner);
        Assert.NotNull(rule.Detect(ctx));

        var prior = rule.Fix(ctx);
        Assert.Equal(new[] { "netsh.exe advfirewall set publicprofile state on" }, runner.Commands);
        Assert.Null(rule.Detect(ctx));

        rule.Undo(ctx, prior);
        Assert.Equal(0, reg.GetInt(Profile("PublicProfile"), "EnableFirewall"));
    }

    [Fact]
    public void Firewall_NetshFailure_Throws()
    {
        var ctx = TestContext.Empty();
        ((FakeRegistry)ctx.Registry).SetInt(Profile("StandardProfile"), "EnableFirewall", 0);
        var rule = new FirewallOnRule(new ScriptedRunner((_, _) => (1, "")));
        Assert.Throws<InvalidOperationException>(() => rule.Fix(ctx));
    }

    [Fact]
    public void RealTime_OfferedOnlyWhenOffAndNoOtherAntivirusIsOn()
    {
        var ctx = TestContext.Empty();
        var defender = (FakeDefender)ctx.Defender;
        var rule = new DefenderRealtimeRule(new FakeRunner());

        defender.Status = new DefenderStatus(true, true, "Normal", 0, Array.Empty<string>(), null, null, 0);
        Assert.Null(rule.Detect(ctx));

        defender.Status = new DefenderStatus(false, true, "Normal", 0, Array.Empty<string>(), null, null, 0);
        Assert.NotNull(rule.Detect(ctx));

        defender.Products.Add(new AntivirusProduct("McAfee LiveSafe", IsDefender: false, Enabled: true, DefinitionsOutdated: false));
        Assert.Null(rule.Detect(ctx));
    }

    [Fact]
    public void RealTime_FixAndUndoCallSetMpPreference()
    {
        var runner = new FakeRunner();
        var rule = new DefenderRealtimeRule(runner);
        var prior = rule.Fix(TestContext.Empty());
        rule.Undo(TestContext.Empty(), prior);
        Assert.Contains(runner.Commands, c => c.Contains("-DisableRealtimeMonitoring $false"));
        Assert.Contains(runner.Commands, c => c.Contains("-DisableRealtimeMonitoring $true"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~SecurityRowRuleTests"`
Expected: build FAILS — `FirewallOnRule` does not exist.

- [ ] **Step 3: Implement `FirewallOnRule.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Models;

namespace BriskEngine.Actions.Rules;

public sealed class FirewallOnRule : IDiagnosticRule
{
    private static readonly (string Profile, string Netsh)[] Profiles =
    {
        ("DomainProfile", "domainprofile"),
        ("StandardProfile", "privateprofile"),
        ("PublicProfile", "publicprofile"),
    };

    private readonly IProcessRunner _runner;

    public FirewallOnRule(IProcessRunner runner) { _runner = runner; }

    public string Id => "firewall-on";
    public RuleCategory Category => RuleCategory.Auto;

    private static List<string> OffProfiles(DiagnosticContext ctx) => Profiles
        .Where(p =>
        {
            try { return ctx.Registry.GetInt($@"{SecurityPosture.FirewallRoot}\{p.Profile}", "EnableFirewall") == 0; }
            catch (Exception) { return false; }
        })
        .Select(p => p.Netsh)
        .ToList();

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var off = OffProfiles(ctx);
        if (off.Count == 0) return null;
        return new DiagnosticFinding(Id, $"rule.{Id}.title", "Windows Firewall is off",
            $"Windows Firewall is off for {off.Count} network profile(s).",
            Severity.Warning, Category, ImpactStars: 3, CanFix: true,
            FixDescription: "Turn Windows Firewall back on (undoable)");
    }

    public string Fix(DiagnosticContext ctx)
    {
        var off = OffProfiles(ctx);
        foreach (var profile in off) Netsh(profile, "on");
        return JsonSerializer.Serialize(off);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        foreach (var profile in JsonSerializer.Deserialize<List<string>>(priorStateJson) ?? new())
            Netsh(profile, "off");
    }

    private void Netsh(string profile, string state)
    {
        var (exit, _) = _runner.Run("netsh.exe", $"advfirewall set {profile} state {state}");
        if (exit != 0) throw new InvalidOperationException($"netsh exited {exit}");
    }
}
```

- [ ] **Step 4: Implement `DefenderRealtimeRule.cs`**

```csharp
using System;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Models;

namespace BriskEngine.Actions.Rules;

public sealed class DefenderRealtimeRule : IDiagnosticRule
{
    private readonly IProcessRunner _runner;

    public DefenderRealtimeRule(IProcessRunner runner) { _runner = runner; }

    public string Id => "defender-realtime";
    public RuleCategory Category => RuleCategory.Auto;

    private static bool Applies(DiagnosticContext ctx) =>
        ctx.Defender.Read()?.RealTimeOn == false
        && !ctx.Defender.Registered().Any(p => !p.IsDefender && p.Enabled);

    public DiagnosticFinding? Detect(DiagnosticContext ctx) => Applies(ctx)
        ? new DiagnosticFinding(Id, $"rule.{Id}.title", "Real-time protection is off",
            "Defender's real-time protection is off and no other antivirus is on.",
            Severity.Critical, Category, ImpactStars: 4, CanFix: true,
            FixDescription: "Turn real-time protection back on (undoable)")
        : null;

    public string Fix(DiagnosticContext ctx)
    {
        Ps("Set-MpPreference -DisableRealtimeMonitoring $false");
        return "\"off\"";
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson) =>
        Ps("Set-MpPreference -DisableRealtimeMonitoring $true");

    private void Ps(string command)
    {
        var (exit, _) = _runner.Run("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"");
        if (exit != 0) throw new InvalidOperationException($"Set-MpPreference exited {exit}");
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~SecurityRowRuleTests"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/BriskEngine/Actions/Rules/FirewallOnRule.cs src/BriskEngine/Actions/Rules/DefenderRealtimeRule.cs src/BriskEngine.Tests/SecurityRowRuleTests.cs
git commit -m "feat(actions): firewall and real-time protection rows" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Hibernation, startup entries as rules, Startup folder, protected entries

**Files:**
- Create: `src/BriskEngine/Actions/Rules/HibernationRule.cs`
- Create: `src/BriskEngine/Actions/Rules/StartupEntryRule.cs`
- Create: `src/BriskEngine/Actions/StartupProtection.cs`
- Modify: `src/BriskEngine/Diagnostics/StartupManager.cs` (record `StartupEntry`, constructor, `List()`, `SetEnabled`)
- Modify: `src/BriskEngine/Diagnostics/RealProbes/RealPowercfgProbe.cs` (`HasBattery`)
- Test: `src/BriskEngine.Tests/StartupActionTests.cs`

**Interfaces:**
- Consumes: `StartupManager` (`List()`, `SetEnabled(hive, name, enabled)`, `ExecutableName`, `PathExpander.Expand`), `IFileProbe.ListFiles`, `IProcessRunner`, `IReadBackable`.
- Produces:
  - `public sealed record StartupEntry(string Hive, string Name, bool Enabled, bool KnownHeavy, string? Command = null)` (added optional `Command`)
  - `StartupManager(IRegistryProbe registry, ActionLog? log, IEventLogProbe? eventLog = null, IFileProbe? files = null)`; hives `"Folder"` and `"FolderAll"` for the user and all-users Startup folders; `internal static readonly (string Hive, string Folder, string Approved)[] Folders`
  - `public sealed class HibernationRule : IReadBackable` — `HibernationRule(IProcessRunner runner, Func<bool> fileExists)`, id `hibernation`
  - `public sealed class StartupEntryRule : IReadBackable` — `StartupEntryRule(StartupManager startup, string hive, string name)`, `static string IdFor(string hive, string name)` → `startup:{hive}:{name}`, `static bool TryParseId(string id, out string hive, out string name)`
  - `public enum StartupProtectionKind { None, Security, Hardware, Sync, Passwords, Vpn, Keyboard }` and `public static class StartupProtection { static StartupProtectionKind Classify(string name, string? command, IReadOnlyCollection<string> antivirusNames); }`
  - `public static bool RealPowercfgProbe.IsLaptop(byte batteryFlag)` (unknown 255 → laptop)

The Startup folder is disabled the way Task Manager does it: a 12-byte `StartupApproved\StartupFolder` value named after the shortcut file, first byte 3 = disabled, 2 = enabled.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using BriskEngine.Actions;
using BriskEngine.Actions.Rules;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.RealProbes;
using BriskEngine.Paths;
using Xunit;

namespace BriskEngine.Tests;

public sealed class StartupActionTests
{
    private const string Run = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";

    [Fact]
    public void List_CarriesTheCommand_AndIncludesStartupFolderShortcuts()
    {
        var reg = new FakeRegistry();
        reg.SetString(Run, "Discord", @"""C:\Users\x\AppData\Local\Discord\Update.exe"" --processStart Discord.exe");
        var files = new FakeFiles();
        var folder = PathExpander.Expand(StartupManager.Folders[0].Folder)!;
        files.FileLists[folder] = new List<string> { $@"{folder}\Wispr Flow.lnk", $@"{folder}\desktop.ini" };
        var mgr = new StartupManager(reg, null, null, files);

        var entries = mgr.List();

        Assert.Contains(entries, e => e.Name == "Discord" && e.Command!.Contains("Update.exe") && e.Enabled);
        Assert.Contains(entries, e => e.Hive == "Folder" && e.Name == "Wispr Flow.lnk" && e.Enabled);
        Assert.DoesNotContain(entries, e => e.Name == "desktop.ini");
    }

    [Fact]
    public void SetEnabled_StartupFolderShortcut_WritesTheApprovedValue()
    {
        var reg = new FakeRegistry();
        var files = new FakeFiles();
        var folder = PathExpander.Expand(StartupManager.Folders[0].Folder)!;
        files.FileLists[folder] = new List<string> { $@"{folder}\Wispr Flow.lnk" };
        var mgr = new StartupManager(reg, null, null, files);

        Assert.True(mgr.SetEnabled("Folder", "Wispr Flow.lnk", false));

        Assert.Equal(3, reg.GetBytes(StartupManager.Folders[0].Approved, "Wispr Flow.lnk")![0]);
        Assert.Contains(mgr.List(), e => e.Name == "Wispr Flow.lnk" && !e.Enabled);
    }

    [Fact]
    public void StartupEntryRule_DisablesAndReenables()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetString(Run, "Spotify", @"C:\Spotify\Spotify.exe /minimized");
        var mgr = new StartupManager(reg, null);
        var rule = new StartupEntryRule(mgr, "HKCU", "Spotify");

        Assert.Equal("startup:HKCU:Spotify", rule.Id);
        Assert.True(rule.IsOn(ctx));
        var prior = rule.Fix(ctx);
        Assert.False(rule.IsOn(ctx));
        Assert.Equal(ReadBackState.Held, ReadBack.StateOf(ctx, rule));
        rule.Undo(ctx, prior);
        Assert.True(rule.IsOn(ctx));
    }

    [Fact]
    public void StartupEntryRule_RefusedWrite_ThrowsUnauthorized()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetString(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Vendor", @"C:\v.exe");
        reg.DenyWriteKeys.Add(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
        var rule = new StartupEntryRule(new StartupManager(reg, null), "HKLM", "Vendor");
        Assert.Throws<UnauthorizedAccessException>(() => rule.Fix(ctx));
    }

    [Theory]
    [InlineData("startup:HKCU:Discord", "HKCU", "Discord")]
    [InlineData("startup:Folder:My: App.lnk", "Folder", "My: App.lnk")]
    public void TryParseId_RoundTrips(string id, string hive, string name)
    {
        Assert.True(StartupEntryRule.TryParseId(id, out var h, out var n));
        Assert.Equal(hive, h);
        Assert.Equal(name, n);
        Assert.Equal(id, StartupEntryRule.IdFor(h, n));
    }

    [Fact]
    public void TryParseId_RejectsOtherIds() =>
        Assert.False(StartupEntryRule.TryParseId("power-mode", out _, out _));

    [Theory]
    [InlineData("Discord", null, StartupProtectionKind.None)]
    [InlineData("RtkAudUService", null, StartupProtectionKind.Hardware)]
    [InlineData("OneDrive", null, StartupProtectionKind.Sync)]
    [InlineData("Bitwarden", null, StartupProtectionKind.Passwords)]
    [InlineData("NordVPN", null, StartupProtectionKind.Vpn)]
    [InlineData("PowerToys", null, StartupProtectionKind.Keyboard)]
    [InlineData("Time Tracker", null, StartupProtectionKind.None)]
    [InlineData("SecurityHealth", null, StartupProtectionKind.Security)]
    [InlineData("Tray", @"C:\Program Files\McAfee\mcuicnt.exe", StartupProtectionKind.Security)]
    public void Classify(string name, string? command, StartupProtectionKind expected) =>
        Assert.Equal(expected, StartupProtection.Classify(name, command, Array.Empty<string>()));

    [Fact]
    public void Classify_ARegisteredAntivirusNameIsSecurity() =>
        Assert.Equal(StartupProtectionKind.Security,
            StartupProtection.Classify("Acme Shield Tray", null, new[] { "Acme Shield Total" }));

    [Fact]
    public void Hibernation_OffAndOn()
    {
        var exists = true;
        var runner = new ScriptedRunner((_, args) => { exists = args.EndsWith("on"); return (0, ""); });
        var rule = new HibernationRule(runner, () => exists);
        Assert.True(rule.IsOn(TestContext.Empty()));
        var prior = rule.Fix(TestContext.Empty());
        Assert.False(rule.IsOn(TestContext.Empty()));
        rule.Undo(TestContext.Empty(), prior);
        Assert.True(rule.IsOn(TestContext.Empty()));
        Assert.Equal(new[] { "powercfg.exe /hibernate off", "powercfg.exe /hibernate on" }, runner.Commands);
    }

    [Theory]
    [InlineData((byte)128, false)]
    [InlineData((byte)8, true)]
    [InlineData((byte)1, true)]
    [InlineData((byte)255, true)]
    public void IsLaptop_UnknownCountsAsLaptop(byte flag, bool laptop) =>
        Assert.Equal(laptop, RealPowercfgProbe.IsLaptop(flag));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~StartupActionTests"`
Expected: build FAILS — `StartupManager.Folders`, `StartupEntryRule`, `StartupProtection` do not exist.

- [ ] **Step 3: Modify `StartupManager.cs`**

Change the record:

```csharp
public sealed record StartupEntry(string Hive, string Name, bool Enabled, bool KnownHeavy,
    string? Command = null);
```

Add, next to `Hives`:

```csharp
    /// The two Startup folders Task Manager lists, and the StartupApproved
    /// key it disables their shortcuts under (value name = shortcut file name).
    internal static readonly (string Hive, string Folder, string Approved)[] Folders =
    {
        ("Folder", @"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup",
                   @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"),
        ("FolderAll", @"%ProgramData%\Microsoft\Windows\Start Menu\Programs\StartUp",
                      @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"),
    };
```

Add a field and extend the constructor (existing callers pass three or fewer arguments and keep compiling):

```csharp
    private readonly IFileProbe? _files;

    public StartupManager(IRegistryProbe registry, ActionLog? log,
        IEventLogProbe? eventLog = null, IFileProbe? files = null)
    {
        _registry = registry;
        _log = log;
        _eventLog = eventLog;
        _files = files;
    }
```

In `List()`, carry the command on Run entries — replace

```csharp
            items.Add(new StartupEntry(hive, name, !disabled,
                IsHeavy(name, _registry.GetString(run, name), Blamed())));
```

with

```csharp
            var command = _registry.GetString(run, name);
            items.Add(new StartupEntry(hive, name, !disabled,
                IsHeavy(name, command, Blamed()), command));
```

and, before the Store loop, add the Startup folders:

```csharp
        if (_files is not null)
        foreach (var (hive, folder, approved) in Folders)
        {
            var dir = Paths.PathExpander.Expand(folder);
            if (dir is null) continue;
            foreach (var file in _files.ListFiles(dir))
            {
                var name = System.IO.Path.GetFileName(file);
                if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                var bytes = _registry.GetBytes(approved, name);
                var disabled = bytes is { Length: > 0 } && (bytes[0] & 1) == 1;
                items.Add(new StartupEntry(hive, name, !disabled, IsHeavy(name), file));
            }
        }
```

In `SetEnabled`, replace

```csharp
        var approved = Hives.FirstOrDefault(h => string.Equals(h.Hive, hive, StringComparison.OrdinalIgnoreCase)).Approved;
```

with

```csharp
        var approved = Hives.FirstOrDefault(h => string.Equals(h.Hive, hive, StringComparison.OrdinalIgnoreCase)).Approved
            ?? Folders.FirstOrDefault(f => string.Equals(f.Hive, hive, StringComparison.OrdinalIgnoreCase)).Approved;
```

- [ ] **Step 4: Modify `RealPowercfgProbe.HasBattery`**

Replace

```csharp
        if (!GetSystemPowerStatus(out var status)) return true;
        const byte NoSystemBattery = 128;
        return (status.BatteryFlag & NoSystemBattery) == 0;
```

with

```csharp
        if (!GetSystemPowerStatus(out var status)) return true;
        return IsLaptop(status.BatteryFlag);
```

and add:

```csharp
    /// 128 = no system battery. 255 = status unknown, which the spec counts
    /// as a laptop: the cautious answer keeps hibernation unticked.
    public static bool IsLaptop(byte batteryFlag)
    {
        const byte NoSystemBattery = 128, Unknown = 255;
        if (batteryFlag == Unknown) return true;
        return (batteryFlag & NoSystemBattery) == 0;
    }
```

- [ ] **Step 5: Create `StartupEntryRule.cs` and `HibernationRule.cs`**

```csharp
using System;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;

namespace BriskEngine.Actions.Rules;

/// One startup entry as a rule, so disabling it is journaled and undone like
/// any switch. Disabled the way Task Manager does it, so it can be seen and
/// turned back on there.
public sealed class StartupEntryRule : IReadBackable
{
    private const string Prefix = "startup:";
    private readonly StartupManager _startup;
    private readonly string _hive;
    private readonly string _name;

    public StartupEntryRule(StartupManager startup, string hive, string name)
    {
        _startup = startup;
        _hive = hive;
        _name = name;
    }

    public static string IdFor(string hive, string name) => $"{Prefix}{hive}:{name}";

    public static bool TryParseId(string id, out string hive, out string name)
    {
        hive = name = "";
        if (!id.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var rest = id[Prefix.Length..];
        var colon = rest.IndexOf(':');
        if (colon <= 0 || colon == rest.Length - 1) return false;
        hive = rest[..colon];
        name = rest[(colon + 1)..];
        return true;
    }

    public string Id => IdFor(_hive, _name);
    public RuleCategory Category => RuleCategory.Auto;

    public bool IsOn(DiagnosticContext ctx) => _startup.List().Any(e =>
        string.Equals(e.Hive, _hive, StringComparison.OrdinalIgnoreCase)
        && string.Equals(e.Name, _name, StringComparison.OrdinalIgnoreCase)
        && e.Enabled);

    public WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => WriteEffect.NotAPolicy;

    public DiagnosticFinding? Detect(DiagnosticContext ctx) => IsOn(ctx)
        ? new DiagnosticFinding(Id, "rule.startup-entry.title", $"{_name} starts with Windows",
            $"{_name} starts every time you sign in.", Severity.Info, Category,
            ImpactStars: 1, CanFix: true, FixDescription: $"Stop {_name} starting with Windows (undoable)",
            Kind: FindingKind.Notice)
        : null;

    public string Fix(DiagnosticContext ctx)
    {
        if (!_startup.SetEnabled(_hive, _name, false))
            throw new UnauthorizedAccessException($"startup entry '{_name}' could not be changed");
        return "\"enabled\"";
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        if (!_startup.SetEnabled(_hive, _name, true))
            throw new UnauthorizedAccessException($"startup entry '{_name}' could not be changed");
    }
}
```

```csharp
using System;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;

namespace BriskEngine.Actions.Rules;

/// powercfg frees hiberfil.sys and turns hibernation and Fast Startup off;
/// "powercfg /hibernate on" reverses all of it.
public sealed class HibernationRule : IReadBackable
{
    private readonly IProcessRunner _runner;
    private readonly Func<bool> _fileExists;

    public HibernationRule(IProcessRunner runner, Func<bool> fileExists)
    {
        _runner = runner;
        _fileExists = fileExists;
    }

    public string Id => "hibernation";
    public RuleCategory Category => RuleCategory.Confirm;

    public bool IsOn(DiagnosticContext ctx) => _fileExists();

    public WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => WriteEffect.NotAPolicy;

    public DiagnosticFinding? Detect(DiagnosticContext ctx) => IsOn(ctx)
        ? new DiagnosticFinding(Id, $"rule.{Id}.title", "Hibernation file is present",
            "hiberfil.sys reserves space on the system drive.", Severity.Info, Category,
            ImpactStars: 1, CanFix: true, FixDescription: "Turn hibernation off (undoable)",
            Kind: FindingKind.Notice)
        : null;

    public string Fix(DiagnosticContext ctx)
    {
        Powercfg("off");
        return "\"on\"";
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson) => Powercfg("on");

    private void Powercfg(string state)
    {
        var (exit, _) = _runner.Run("powercfg.exe", $"/hibernate {state}");
        if (exit != 0) throw new InvalidOperationException($"powercfg exited {exit}");
    }
}
```

- [ ] **Step 6: Create `StartupProtection.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BriskEngine.Actions;

public enum StartupProtectionKind { None, Security, Hardware, Sync, Passwords, Vpn, Keyboard }

/// Startup entries that break something the user would notice when they stop
/// starting. Those stay unticked, each with its cost line. Words of four
/// letters or fewer match as whole words, so "IME" never matches "Time".
public static class StartupProtection
{
    private static readonly (StartupProtectionKind Kind, string[] Words)[] Table =
    {
        (StartupProtectionKind.Security, new[] { "SecurityHealth", "Windows Security", "McAfee", "Norton",
            "Avast", "AVG", "Bitdefender", "Kaspersky", "ESET", "Malwarebytes", "Sophos", "Trend Micro",
            "Avira", "Webroot", "F-Secure" }),
        (StartupProtectionKind.Hardware, new[] { "Realtek", "RtkAud", "Synaptics", "SynTP", "ELAN",
            "ETDCtrl", "igfx", "AMD", "Radeon", "NVIDIA", "Nahimic", "Dolby", "Waves", "MaxxAudio",
            "Conexant", "Bluetooth", "Logitech", "Razer", "Corsair", "SteelSeries", "Armoury", "ASUS",
            "Lenovo Vantage", "Dell", "HP" }),
        (StartupProtectionKind.Sync, new[] { "OneDrive", "Google Drive", "GoogleDrive", "Dropbox", "iCloud",
            "MEGAsync", "pCloud", "Box Drive", "Nextcloud", "Syncthing" }),
        (StartupProtectionKind.Passwords, new[] { "1Password", "Bitwarden", "KeePass", "LastPass",
            "Dashlane", "Enpass", "Proton Pass" }),
        (StartupProtectionKind.Vpn, new[] { "NordVPN", "ProtonVPN", "Proton VPN", "ExpressVPN", "Mullvad",
            "WireGuard", "OpenVPN", "Surfshark", "AnyConnect", "GlobalProtect", "Windscribe", "Tailscale",
            "ZeroTier" }),
        (StartupProtectionKind.Keyboard, new[] { "PowerToys", "AutoHotkey", "AHK", "ctfmon", "IME",
            "WinCompose", "Keyboard" }),
    };

    private static bool Matches(string haystack, string word) => word.Length <= 4
        ? Regex.IsMatch(haystack, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase)
        : haystack.Contains(word, StringComparison.OrdinalIgnoreCase);

    public static StartupProtectionKind Classify(string name, string? command,
        IReadOnlyCollection<string> antivirusNames)
    {
        var haystack = $"{name} {command}";
        foreach (var av in antivirusNames)
        {
            var first = av.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (first is { Length: > 2 } && Matches(haystack, first)) return StartupProtectionKind.Security;
        }
        foreach (var (kind, words) in Table)
            if (words.Any(w => Matches(haystack, w))) return kind;
        return StartupProtectionKind.None;
    }
}
```

- [ ] **Step 7: Run the new tests and the existing startup tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~Startup"`
Expected: all pass (`StartupActionTests`, `StartupManagerTests`, `StartupBloatRuleTests` if present).

- [ ] **Step 8: Commit**

```bash
git add src/BriskEngine/Actions/Rules/HibernationRule.cs src/BriskEngine/Actions/Rules/StartupEntryRule.cs src/BriskEngine/Actions/StartupProtection.cs src/BriskEngine/Diagnostics/StartupManager.cs src/BriskEngine/Diagnostics/RealProbes/RealPowercfgProbe.cs src/BriskEngine.Tests/StartupActionTests.cs
git commit -m "feat(actions): startup entries and hibernation as undoable rules" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Permanent deletion in CleanRunner

Today every regenerating cache goes to the Recycle Bin, so the free space Explorer shows does not change until the bin is emptied — one reason testers felt nothing.

**Files:**
- Create: `src/BriskEngine/Cleaning/IDeleter.cs`
- Modify: `src/BriskEngine/Cleaning/CleanRunner.cs` (constructor, `Clean`, `CleanReport`)
- Test: `src/BriskEngine.Tests/PermanentCleanTests.cs`

**Interfaces:**
- Consumes: `SafetyValidator`, `ILockProbe`, `SizeCalculator.SizeOf`, `DeleteLockProbe.ExtendedLength`.
- Produces:
  - `public interface IDeleter { void Delete(string path); }`, `public sealed class WindowsDeleter : IDeleter`
  - `CleanRunner(..., Func<long>? systemDriveFreeBytes = null, IDeleter? deleter = null)` (new optional last parameter)
  - `CleanReport Clean(TargetScanResult scan, bool dryRun, Action<CleanEntry>? onEntry = null, bool permanent = false)`
  - Entry action `"deleted"`; `CleanReport.DeletedBytes`; `CleanReport.RemovedBytes` (sum of `"removed"`).

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Logging;
using BriskEngine.Models;
using BriskEngine.Safety;
using Xunit;

namespace BriskEngine.Tests;

public sealed class PermanentCleanTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-perm-").FullName;
    private readonly FakeRecycler _recycler = new();

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private CleanRunner Runner() => new(new SafetyValidator(), _recycler,
        new ActionLog(Path.Combine(_root, "log.jsonl")), new FakeRunner(), () => false,
        lockProbe: null, systemDriveFreeBytes: () => 0, deleter: new WindowsDeleter());

    private TargetScanResult ScanOver(string dir, params (string Name, int Bytes)[] entries)
    {
        Directory.CreateDirectory(dir);
        var items = new List<ResolvedItem>();
        foreach (var (name, bytes) in entries)
        {
            var p = Path.Combine(dir, name);
            if (name.EndsWith('\\'))
            {
                Directory.CreateDirectory(p);
                File.WriteAllBytes(Path.Combine(p, "inner.bin"), new byte[bytes]);
                items.Add(new ResolvedItem("t", p.TrimEnd('\\'), bytes, DateTime.UtcNow));
            }
            else
            {
                File.WriteAllBytes(p, new byte[bytes]);
                items.Add(new ResolvedItem("t", p, bytes, DateTime.UtcNow));
            }
        }
        var target = new CleanupTarget("t", "T", CleanupLevel.Safe, new List<string> { dir }, "Test",
            DeletesContentsNotDirectory: true, Regenerates: true);
        return new TargetScanResult(target, items, null);
    }

    [Fact]
    public void Permanent_DeletesFilesAndFolders_WithoutTheBin()
    {
        var dir = Path.Combine(_root, "cache");
        var scan = ScanOver(dir, ("a.tmp", 10), ("sub\\", 30));

        var report = Runner().Clean(scan, dryRun: false, permanent: true);

        Assert.Empty(_recycler.Recycled);
        Assert.Equal(40, report.DeletedBytes);
        Assert.All(report.Entries, e => Assert.Equal("deleted", e.Action));
        Assert.Empty(Directory.EnumerateFileSystemEntries(dir));
    }

    [Fact]
    public void Permanent_DeletesAReadOnlyFile()
    {
        var dir = Path.Combine(_root, "ro");
        var scan = ScanOver(dir, ("ro.bin", 10));
        File.SetAttributes(scan.Items[0].Path, FileAttributes.ReadOnly);

        var report = Runner().Clean(scan, dryRun: false, permanent: true);

        Assert.Equal(10, report.DeletedBytes);
        Assert.False(File.Exists(scan.Items[0].Path));
    }

    [Fact]
    public void Permanent_ALockedFileInsideAFolder_IsLeft_AndOnlyWhatWentIsCounted()
    {
        var dir = Path.Combine(_root, "locked");
        var scan = ScanOver(dir, ("sub\\", 30));
        var keep = Path.Combine(scan.Items[0].Path, "held.bin");
        File.WriteAllBytes(keep, new byte[20]);
        var withHeld = scan with { Items = new[] { scan.Items[0] with { Bytes = 50 } } };

        using (new FileStream(keep, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var report = Runner().Clean(withHeld, dryRun: false, permanent: true);
            var entry = Assert.Single(report.Entries);
            Assert.Equal("deleted", entry.Action);
            Assert.Equal(30, entry.Bytes);
            Assert.NotNull(entry.Reason);
        }
        Assert.True(File.Exists(keep));
    }

    [Fact]
    public void Permanent_AnItemOutsideTheTarget_IsRefused()
    {
        var dir = Path.Combine(_root, "allowed");
        var scan = ScanOver(dir, ("a.tmp", 10));
        var outside = Path.Combine(_root, "outside.tmp");
        File.WriteAllBytes(outside, new byte[10]);
        var rogue = scan with { Items = new[] { new ResolvedItem("t", outside, 10, DateTime.UtcNow) } };

        var report = Runner().Clean(rogue, dryRun: false, permanent: true);

        Assert.Equal("refused", Assert.Single(report.Entries).Action);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void NotPermanent_StillRecycles()
    {
        var scan = ScanOver(Path.Combine(_root, "bin"), ("a.tmp", 10));
        var report = Runner().Clean(scan, dryRun: false);
        Assert.Single(_recycler.Recycled);
        Assert.Equal(10, report.RecycledBytes);
        Assert.Equal(0, report.DeletedBytes);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~PermanentCleanTests"`
Expected: build FAILS — `WindowsDeleter` does not exist; `Clean` has no `permanent` parameter.

- [ ] **Step 3: Create `IDeleter.cs`**

```csharp
using System;
using System.IO;

namespace BriskEngine.Cleaning;

public interface IDeleter
{
    /// Deletes what it can. A file in use is left behind without throwing;
    /// the caller observes what remains.
    void Delete(string path);
}

public sealed class WindowsDeleter : IDeleter
{
    public void Delete(string path)
    {
        var full = DeleteLockProbe.ExtendedLength(path);
        if (File.Exists(full)) { DeleteFile(full); return; }
        if (Directory.Exists(full)) DeleteTree(full);
    }

    private static void DeleteFile(string file)
    {
        try
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void DeleteTree(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir)) DeleteFile(file);
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            // A junction or symlink is removed as a link; what it points to
            // is never walked.
            if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
            {
                try { Directory.Delete(sub, recursive: false); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                continue;
            }
            DeleteTree(sub);
        }
        try { Directory.Delete(dir, recursive: false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
```

- [ ] **Step 4: Modify `CleanRunner.cs`**

Add to `CleanReport`:

```csharp
    public long DeletedBytes =>
        Entries.Where(e => e.Action == "deleted").Sum(e => e.Bytes);

    public long RemovedBytes =>
        Entries.Where(e => e.Action == "removed").Sum(e => e.Bytes);
```

Add a field and the constructor parameter:

```csharp
    private readonly IDeleter _deleter;

    public CleanRunner(SafetyValidator validator, IRecycler recycler, ActionLog log,
        IProcessRunner processRunner, Func<bool> isElevated,
        ILockProbe? lockProbe = null, Func<long>? systemDriveFreeBytes = null,
        IDeleter? deleter = null)
    {
        _validator = validator;
        _recycler = recycler;
        _log = log;
        _processRunner = processRunner;
        _isElevated = isElevated;
        _lockProbe = lockProbe;
        _systemDriveFreeBytes = systemDriveFreeBytes
            ?? (() => new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!).AvailableFreeSpace);
        _deleter = deleter ?? new WindowsDeleter();
    }
```

Change the `Clean` signature:

```csharp
    public CleanReport Clean(TargetScanResult scan, bool dryRun,
        Action<CleanEntry>? onEntry = null, bool permanent = false)
```

Inside `Clean`, just before `var blockedByElevation = ...`, add the permanent path:

```csharp
        // The action flow deletes for real: a regenerating cache sent to the
        // bin frees nothing Explorer can see until the bin is emptied.
        void DeleteNow(ResolvedItem item)
        {
            if (!Exists(item.Path))
            {
                Record(item.Path, 0, "error", "no longer exists (nothing to delete)");
                return;
            }
            try { _deleter.Delete(item.Path); }
            catch (Exception ex) { Record(item.Path, 0, "error", ex.Message); return; }
            if (!Exists(item.Path)) { Record(item.Path, item.Bytes, "deleted"); return; }
            var freed = Math.Max(0, item.Bytes - SizeCalculator.SizeOf(item.Path));
            if (freed > 0) Record(item.Path, freed, "deleted", "partly deleted — files in use were left");
            else Record(item.Path, 0, "error", "in use by a running app");
        }
```

and in the item loop replace

```csharp
            batch.Add(item);
            if (batch.Count >= BatchSize) Flush();
```

with

```csharp
            if (permanent) { DeleteNow(item); continue; }
            batch.Add(item);
            if (batch.Count >= BatchSize) Flush();
```

The external cases above the loop (`docker-prune`, `empty-recycle-bin`, the heavy trio, `delivery-optimization`, `component-store`) never reach the loop and keep their behaviour.

- [ ] **Step 5: Run the new tests and the existing clean tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~Clean"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/BriskEngine/Cleaning/IDeleter.cs src/BriskEngine/Cleaning/CleanRunner.cs src/BriskEngine.Tests/PermanentCleanTests.cs
git commit -m "feat(clean): permanent deletion for regenerating caches" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: New cleanup targets

**Files:**
- Modify: `src/BriskEngine/Cleaning/CleanupTargetRegistry.cs`
- Test: `src/BriskEngine.Tests/CleanupTargetRegistryTests.cs` (add tests)

**Interfaces:**
- Produces target ids `nvidia-shader-cache`, `amd-shader-cache`, `d3d-shader-cache` (Deep, opt-in, regenerating, no elevation, category `Game`), `wer-system` (Deep, admin, regenerating); Chrome, Edge and Brave templates cover every profile (`User Data\Default` and `User Data\Profile *`).

Shader caches are Deep and opt-in so the existing `brisk clean` default (Safe) never sweeps them — the spec keeps them unticked.

- [ ] **Step 1: Add the failing tests to `CleanupTargetRegistryTests`**

```csharp
    [Theory]
    [InlineData("nvidia-shader-cache")]
    [InlineData("amd-shader-cache")]
    [InlineData("d3d-shader-cache")]
    public void ShaderCaches_AreDeepOptIn_AndNeedNoElevation(string id)
    {
        var t = CleanupTargetRegistry.All.Single(x => x.Id == id);
        Assert.Equal(CleanupLevel.Deep, t.Level);
        Assert.True(t.RequiresExplicitOptIn);
        Assert.False(t.RequiresElevation);
        Assert.True(t.Regenerates);
        Assert.True(t.DeletesContentsNotDirectory);
    }

    [Fact]
    public void WerSystem_IsDeepAndAdmin()
    {
        var t = CleanupTargetRegistry.All.Single(x => x.Id == "wer-system");
        Assert.Equal(CleanupLevel.Deep, t.Level);
        Assert.True(t.RequiresElevation);
    }

    [Theory]
    [InlineData("chrome-cache")]
    [InlineData("edge-cache")]
    [InlineData("brave-cache")]
    public void BrowserCaches_CoverEveryProfile(string id)
    {
        var t = CleanupTargetRegistry.All.Single(x => x.Id == id);
        Assert.Contains(t.PathTemplates, p => p.Contains(@"\Default\Cache"));
        Assert.Contains(t.PathTemplates, p => p.Contains(@"\Profile *\Cache"));
    }
```

(The file already has `using System.Linq;` and `using BriskEngine.Models;` — check and add them if missing.)

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~CleanupTargetRegistryTests"`
Expected: the three new tests FAIL (`Sequence contains no matching element` / missing templates).

- [ ] **Step 3: Change the registry**

Add a helper above `All`:

```csharp
    /// Every Chromium profile, not only "Default": a second profile's cache
    /// was invisible to brisk before.
    private static string[] Chromium(string userData) => new[]
    {
        $@"{userData}\Default\Cache", $@"{userData}\Default\Code Cache", $@"{userData}\Default\GPUCache",
        $@"{userData}\Profile *\Cache", $@"{userData}\Profile *\Code Cache", $@"{userData}\Profile *\GPUCache",
    };
```

Replace the three Chromium entries with:

```csharp
        T("chrome-cache", "Chrome cache", CleanupLevel.Safe, "Browser",
            Chromium(@"%LOCALAPPDATA%\Google\Chrome\User Data"),
            contents: true, regen: true, app: "chrome"),
        T("edge-cache", "Edge cache", CleanupLevel.Safe, "Browser",
            Chromium(@"%LOCALAPPDATA%\Microsoft\Edge\User Data"),
            contents: true, regen: true, app: "msedge"),
```

and

```csharp
        T("brave-cache", "Brave cache", CleanupLevel.Safe, "Browser",
            Chromium(@"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data"),
            contents: true, regen: true, app: "brave"),
```

`wer-system` needs elevation, and a Safe target may not (`SafeLevel_NeverRequiresElevation`), so add these at the end of the Deep section, before the closing `};`:

```csharp
        // ---- Action-flow additions (2026-10-16)
        T("wer-system", "Windows Error Reporting queues (all users)", CleanupLevel.Deep, "System",
            new[] { @"%ProgramData%\Microsoft\Windows\WER\ReportQueue",
                    @"%ProgramData%\Microsoft\Windows\WER\ReportArchive" },
            contents: true, regen: true, admin: true),
        // Games rebuild these and stutter while they do, so they are opt-in.
        T("nvidia-shader-cache", "NVIDIA shader cache", CleanupLevel.Deep, "Game",
            new[] { @"%LOCALAPPDATA%\NVIDIA\DXCache", @"%LOCALAPPDATA%\NVIDIA\GLCache",
                    @"%USERPROFILE%\AppData\LocalLow\NVIDIA\PerDriverVersion\DXCache" },
            contents: true, regen: true, optIn: true),
        T("amd-shader-cache", "AMD shader cache", CleanupLevel.Deep, "Game",
            new[] { @"%LOCALAPPDATA%\AMD\DxCache", @"%LOCALAPPDATA%\AMD\GLCache",
                    @"%LOCALAPPDATA%\AMD\DxcCache", @"%LOCALAPPDATA%\AMD\VkCache" },
            contents: true, regen: true, optIn: true),
        T("d3d-shader-cache", "DirectX shader cache", CleanupLevel.Deep, "Game",
            new[] { @"%LOCALAPPDATA%\D3DSCache" },
            contents: true, regen: true, optIn: true),
```

- [ ] **Step 4: Run the registry and scanner tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~CleanupTargetRegistryTests|FullyQualifiedName~ScannerTests|FullyQualifiedName~CleanSelectionTests"`
Expected: all pass. If a test pins the total number of targets, update its expected count by +4 and say so in the commit body.

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Cleaning/CleanupTargetRegistry.cs src/BriskEngine.Tests/CleanupTargetRegistryTests.cs
git commit -m "feat(clean): every browser profile, shader caches, system error reports" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 11: Docker and Recycle Bin sizes

**Files:**
- Create: `src/BriskEngine/Actions/SpaceProbe.cs`
- Test: `src/BriskEngine.Tests/SpaceProbeTests.cs`

**Interfaces:**
- Consumes: `IProcessRunner`.
- Produces: `public interface ISpaceProbe { long? DockerReclaimableBytes(); long RecycleBinBytes(); }` (`null` = Docker not installed or not running), `public sealed class RealSpaceProbe : ISpaceProbe` (`RealSpaceProbe(IProcessRunner runner)`, `internal static long? ParseDockerReclaimable(string stdout)`), test fake `FakeSpace : ISpaceProbe` (internal) — reused by Tasks 14–15.

`docker system df` prints sizes in decimal units (go-units: kB = 1000).

- [ ] **Step 1: Write the failing tests**

```csharp
using BriskEngine.Actions;
using Xunit;

namespace BriskEngine.Tests;

sealed class FakeSpace : ISpaceProbe
{
    public long? Docker;
    public long Bin;
    public long? DockerReclaimableBytes() => Docker;
    public long RecycleBinBytes() => Bin;
}

public sealed class SpaceProbeTests
{
    [Fact]
    public void ParseDockerReclaimable_SumsEveryLine()
    {
        const string output = "2.3GB (45%)\n0B (0%)\n1.2kB\n523.4MB (100%)\n";
        Assert.Equal(2_300_000_000L + 1_200L + 523_400_000L,
            RealSpaceProbe.ParseDockerReclaimable(output));
    }

    [Fact]
    public void ParseDockerReclaimable_EmptyOutputIsZero() =>
        Assert.Equal(0L, RealSpaceProbe.ParseDockerReclaimable(""));

    [Fact]
    public void Docker_FailingCommand_IsNull()
    {
        var probe = new RealSpaceProbe(new ScriptedRunner((_, _) => (1, "")));
        Assert.Null(probe.DockerReclaimableBytes());
    }

    [Fact]
    public void Docker_MissingExecutable_IsNull()
    {
        var probe = new RealSpaceProbe(new ScriptedRunner((_, _) =>
            throw new System.ComponentModel.Win32Exception(2)));
        Assert.Null(probe.DockerReclaimableBytes());
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~SpaceProbeTests"`
Expected: build FAILS — `RealSpaceProbe` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BriskEngine.Cleaning;

namespace BriskEngine.Actions;

public interface ISpaceProbe
{
    /// null: Docker is not installed, or its engine is not running.
    long? DockerReclaimableBytes();
    long RecycleBinBytes();
}

public sealed class RealSpaceProbe : ISpaceProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfo { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinW(string? rootPath, ref ShQueryRbInfo info);

    private static readonly Regex Size = new(@"^\s*([\d.]+)\s*([kKMGT]?B)", RegexOptions.Multiline);
    private readonly IProcessRunner _runner;

    public RealSpaceProbe(IProcessRunner runner) { _runner = runner; }

    public long? DockerReclaimableBytes()
    {
        try
        {
            var (exit, output) = _runner.Run("docker", "system df --format \"{{.Reclaimable}}\"");
            return exit == 0 ? ParseDockerReclaimable(output) : null;
        }
        catch (Exception) { return null; }
    }

    internal static long? ParseDockerReclaimable(string stdout)
    {
        long total = 0;
        foreach (Match m in Size.Matches(stdout))
        {
            var value = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var unit = m.Groups[2].Value.ToUpperInvariant() switch
            {
                "KB" => 1e3, "MB" => 1e6, "GB" => 1e9, "TB" => 1e12, _ => 1d,
            };
            total += (long)Math.Round(value * unit);
        }
        return total;
    }

    public long RecycleBinBytes()
    {
        var info = new ShQueryRbInfo { cbSize = Marshal.SizeOf<ShQueryRbInfo>() };
        return SHQueryRecycleBinW(null, ref info) == 0 ? info.i64Size : 0;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~SpaceProbeTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Actions/SpaceProbe.cs src/BriskEngine.Tests/SpaceProbeTests.cs
git commit -m "feat(actions): read Docker reclaimable data and Recycle Bin size" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Deferred clean queue

**Files:**
- Create: `src/BriskEngine/Actions/DeferredCleanQueue.cs`
- Test: `src/BriskEngine.Tests/DeferredCleanQueueTests.cs`

**Interfaces:**
- Consumes: `CleanupTargetRegistry.All`, `CleanupTarget.AppProcessCandidates`, `IProcessLister`, `TargetScanResult`, `CleanReport`.
- Produces: `public sealed class DeferredCleanQueue` — `DeferredCleanQueue(string path)`, `IReadOnlyList<DeferredCleanQueue.Entry> Pending()`, `void Add(string targetId, DateTime nowUtc)`, `IReadOnlyList<CleanReport> ProcessDue(DateTime nowUtc, IProcessLister apps, Func<CleanupTarget, TargetScanResult> scanOne, Func<TargetScanResult, CleanReport> clean)`, `static readonly TimeSpan Expiry` (7 days); `public sealed record Entry(string TargetId, DateTime QueuedUtc)`.

Who calls `ProcessDue` and how often (every 30 s while brisk runs) is the GUI's job in sub-project 2; the CLI calls it at the start of `brisk run`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using BriskEngine.Actions;
using BriskEngine.Cleaning;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

public sealed class DeferredCleanQueueTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-defer-").FullName;
    private string QueuePath => Path.Combine(_root, "deferred.json");
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static TargetScanResult Scan(CleanupTarget t, string? skipped = null) =>
        new(t, new List<ResolvedItem>(), skipped);

    [Fact]
    public void Add_IsPersisted_AndDeduplicated()
    {
        var q = new DeferredCleanQueue(QueuePath);
        q.Add("chrome-cache", Now);
        q.Add("chrome-cache", Now.AddMinutes(1));
        q.Add("whatsapp-cache", Now);

        var again = new DeferredCleanQueue(QueuePath);
        Assert.Equal(2, again.Pending().Count);
    }

    [Fact]
    public void ProcessDue_CleansWhenTheAppIsClosed_AndKeepsItWhileOpen()
    {
        var q = new DeferredCleanQueue(QueuePath);
        q.Add("chrome-cache", Now);
        q.Add("whatsapp-cache", Now);
        var apps = new FakeRunningApps();
        apps.Running.Add("WhatsApp.Root");
        var cleaned = new List<string>();

        var reports = q.ProcessDue(Now.AddMinutes(5), apps, t => Scan(t),
            s => { cleaned.Add(s.Target.Id); return new CleanReport(new List<CleanEntry>()); });

        Assert.Single(reports);
        Assert.Equal(new[] { "chrome-cache" }, cleaned);
        var left = Assert.Single(q.Pending());
        Assert.Equal("whatsapp-cache", left.TargetId);
    }

    [Fact]
    public void ProcessDue_DropsWorkOlderThanAWeek()
    {
        var q = new DeferredCleanQueue(QueuePath);
        q.Add("chrome-cache", Now);
        var reports = q.ProcessDue(Now.AddDays(8), new FakeRunningApps(), t => Scan(t),
            _ => throw new InvalidOperationException("must not clean expired work"));
        Assert.Empty(reports);
        Assert.Empty(q.Pending());
    }

    [Fact]
    public void ProcessDue_KeepsATargetTheFreshScanStillSkips()
    {
        var q = new DeferredCleanQueue(QueuePath);
        q.Add("discord-cache", Now);
        q.ProcessDue(Now, new FakeRunningApps(), t => Scan(t, "Discord is running"),
            _ => throw new InvalidOperationException("must not clean a skipped scan"));
        Assert.Single(q.Pending());
    }

    [Fact]
    public void Pending_ACorruptFileReadsAsEmpty()
    {
        File.WriteAllText(QueuePath, "{not json");
        Assert.Empty(new DeferredCleanQueue(QueuePath).Pending());
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~DeferredCleanQueueTests"`
Expected: build FAILS — `DeferredCleanQueue` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BriskEngine.Cleaning;
using BriskEngine.Models;

namespace BriskEngine.Actions;

/// Cache targets that were skipped because their app was open. They are
/// cleaned when the app is closed; work older than a week drops out.
public sealed class DeferredCleanQueue
{
    public sealed record Entry(string TargetId, DateTime QueuedUtc);

    public static readonly TimeSpan Expiry = TimeSpan.FromDays(7);
    private readonly string _path;

    public DeferredCleanQueue(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public IReadOnlyList<Entry> Pending()
    {
        if (!File.Exists(_path)) return Array.Empty<Entry>();
        try { return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path)) ?? new List<Entry>(); }
        catch (JsonException) { return Array.Empty<Entry>(); }
    }

    public void Add(string targetId, DateTime nowUtc)
    {
        var list = Pending().Where(e => e.TargetId != targetId).ToList();
        list.Add(new Entry(targetId, nowUtc));
        Save(list);
    }

    public IReadOnlyList<CleanReport> ProcessDue(DateTime nowUtc, IProcessLister apps,
        Func<CleanupTarget, TargetScanResult> scanOne, Func<TargetScanResult, CleanReport> clean)
    {
        var reports = new List<CleanReport>();
        var keep = new List<Entry>();
        foreach (var entry in Pending())
        {
            if (nowUtc - entry.QueuedUtc > Expiry) continue;
            var target = CleanupTargetRegistry.All.FirstOrDefault(t => t.Id == entry.TargetId);
            if (target is null) continue;
            if (target.AppProcessCandidates.Any(apps.IsRunning)) { keep.Add(entry); continue; }
            var scan = scanOne(target);
            if (scan.SkippedReason is not null) { keep.Add(entry); continue; }
            reports.Add(clean(scan));
        }
        Save(keep);
        return reports;
    }

    private void Save(List<Entry> list) =>
        File.WriteAllText(_path, JsonSerializer.Serialize(list));
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~DeferredCleanQueueTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Actions/DeferredCleanQueue.cs src/BriskEngine.Tests/DeferredCleanQueueTests.cs
git commit -m "feat(actions): queue caches of open apps and clean them when the app closes" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 13: Action models, the row catalog, and rule lookup

**Files:**
- Create: `src/BriskEngine/Actions/ActionModels.cs`
- Create: `src/BriskEngine/Actions/ActionCatalog.cs`
- Create: `src/BriskEngine/Actions/ActionRules.cs`
- Test: `src/BriskEngine.Tests/ActionCatalogTests.cs`

**Interfaces:**
- Consumes: every rule from Tasks 3–8, `DiagnosticRuleRegistry.All`, `CleanupTargetRegistry.All`, `StartupProtectionKind`, `ReadBackState`, `ScanResult`.
- Produces (exact names later tasks use):
  - `enum ActionGroupKind { Space, Speed, Privacy }`, `enum ActionLinkKind { Rules, Cleanup }`
  - `record ActionFile(string Path, long Bytes, DateTime? LastWriteUtc)`
  - `record ActionItem(string Id, ActionGroupKind Group, string? TitleKey, string Title, string? CostKey, string? Cost, string? WindowsNameKey, string? WindowsName, long? Bytes, bool DefaultOn, bool NeedsRestart, bool NeedsAdmin, bool Reversible, ActionLinkKind LinkKind, IReadOnlyList<string> LinkIds, IReadOnlyList<ActionFile> Files)`
  - `record ActionGroup(ActionGroupKind Kind, IReadOnlyList<ActionItem> Items)`
  - `record ActionPlan(DateTime CreatedUtc, IReadOnlyList<ActionGroup> Groups, ScanResult Scan)` with `IEnumerable<ActionItem> Items` and `ActionGroup? Group(ActionGroupKind kind)`
  - `record ActionSelection(IReadOnlySet<string> RowIds, IReadOnlySet<string> Files)` with `static ActionSelection Default(ActionPlan plan)` and `ActionSelection Only(ActionGroupKind group, ActionPlan plan)`
  - `enum RowOutcome { Done, PendingRestart, Skipped, Failed }`
  - `record RowResult(string RowId, RowOutcome Outcome, string? Reason, ReadBackState? ReadBack, long BytesFreed, long BytesDeferred)`
  - `record RunRecord(string RunId, DateTime StartedUtc, DateTime FinishedUtc, IReadOnlyList<RowResult> Rows, IReadOnlyList<string> UndoableRuleIds)`
  - `record RowDef(...)` with `TitleKey`, `CostKey`, `NameKey`; `static class ActionCatalog` with `Rows`, `Find(id)`, `StartupCost(kind)`, `HibernationLaptopCost(Key)`, `StartupName(Key)`, `AllKeys()`
  - `static class ActionStrings` with `IReadOnlyDictionary<string, string> Ui` (key → English) and `string En(string key)`
  - `record ActionProbes(IProcessRunner Runner, IUiEffectsProbe Ui, IPowerOverlayProbe Overlay, IOptionalFeatureProbe Features, IScheduledTaskProbe Tasks, StartupManager Startup, Func<bool> HiberfilExists)`; `sealed class ActionRules` with `ActionRules(ActionProbes probes)` and `IDiagnosticRule? Find(string id)`

Row ids are `space.*`, `speed.*`, `privacy.*`; a startup entry row's id is its rule id (`startup:{hive}:{name}`). Keys are `action.{row id}.title|cost|name`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Linq;
using BriskEngine.Actions;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

public sealed class ActionCatalogTests
{
    internal static ActionRules Rules(DiagnosticContext ctx) => new(new ActionProbes(
        new FakeRunner(), new FakeUi(), new FakeOverlay(), new FakeFeatures(), new FakeTasks(),
        new StartupManager(ctx.Registry, null), () => false));

    [Fact]
    public void RowIdsAndKeysAreUnique()
    {
        var ids = ActionCatalog.Rows.Select(r => r.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        var keys = ActionCatalog.AllKeys().ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void EveryRuleLinkResolves()
    {
        var rules = Rules(TestContext.Empty());
        foreach (var row in ActionCatalog.Rows.Where(r => r.LinkKind == ActionLinkKind.Rules))
        foreach (var id in row.LinkIds)
            Assert.True(rules.Find(id) is not null, $"{row.Id} links to unknown rule '{id}'");
    }

    [Fact]
    public void EveryCleanupLinkIsARegisteredTarget()
    {
        var targets = CleanupTargetRegistry.All.Select(t => t.Id).ToHashSet();
        foreach (var row in ActionCatalog.Rows.Where(r => r.LinkKind == ActionLinkKind.Cleanup))
        foreach (var id in row.LinkIds)
            Assert.Contains(id, targets);
    }

    [Fact]
    public void CostLinesAreSixWordsAtMost()
    {
        var costs = ActionCatalog.Rows.Select(r => r.Cost).Where(c => c is not null)
            .Append(ActionCatalog.HibernationLaptopCost)
            .Concat(Enum.GetValues<StartupProtectionKind>()
                .Where(k => k != StartupProtectionKind.None)
                .Select(k => ActionCatalog.StartupCost(k).English));
        foreach (var cost in costs)
            Assert.True(cost!.Split(' ').Length <= 6, $"too long: {cost}");
    }

    [Fact]
    public void SpecDefaults()
    {
        Assert.False(ActionCatalog.Find("space.shaders").DefaultOn);
        Assert.False(ActionCatalog.Find("space.docker").DefaultOn);
        Assert.False(ActionCatalog.Find("speed.memory-integrity").DefaultOn);
        Assert.False(ActionCatalog.Find("speed.vm-platform").DefaultOn);
        Assert.False(ActionCatalog.Find("privacy.location").DefaultOn);
        Assert.True(ActionCatalog.Find("space.caches").DefaultOn);
        Assert.Equal("component-store", ActionCatalog.Find("space.caches").LinkIds[^1]);
    }

    [Fact]
    public void Find_BuildsAStartupRuleFromItsId()
    {
        var rules = Rules(TestContext.Empty());
        Assert.Equal("startup:HKCU:Discord", rules.Find("startup:HKCU:Discord")!.Id);
        Assert.Null(rules.Find("no-such-rule"));
    }

    [Fact]
    public void Selection_DefaultTicksDefaultOnRowsAndEveryListedFile()
    {
        var file = new ActionFile(@"C:\Users\x\Downloads\setup.exe", 10, null);
        ActionItem Item(string id, bool on, params ActionFile[] files) => new(id, ActionGroupKind.Space,
            "k", "t", null, null, null, null, 1, on, false, false, false, ActionLinkKind.Cleanup,
            new[] { "user-temp" }, files);
        var plan = new ActionPlan(DateTime.UtcNow, new[]
        {
            new ActionGroup(ActionGroupKind.Space, new[] { Item("space.caches", true), Item("space.shaders", false), Item("space.downloads", true, file) }),
        }, new BriskEngine.Models.ScanResult(Array.Empty<BriskEngine.Models.TargetScanResult>()));

        var selection = ActionSelection.Default(plan);

        Assert.Contains("space.caches", selection.RowIds);
        Assert.DoesNotContain("space.shaders", selection.RowIds);
        Assert.Contains(file.Path, selection.Files);
        Assert.Empty(selection.Only(ActionGroupKind.Privacy, plan).RowIds);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~ActionCatalogTests"`
Expected: build FAILS — `ActionCatalog` does not exist.

- [ ] **Step 3: Create `ActionModels.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Models;

namespace BriskEngine.Actions;

public enum ActionGroupKind { Space, Speed, Privacy }

public enum ActionLinkKind { Rules, Cleanup }

public sealed record ActionFile(string Path, long Bytes, DateTime? LastWriteUtc);

/// One row a user sees. Title/Cost/WindowsName are English; the keys find the
/// translation (a startup entry has no TitleKey: its title is the app name).
/// Bytes is set on space rows. Files is set on the Downloads row.
public sealed record ActionItem(
    string Id, ActionGroupKind Group,
    string? TitleKey, string Title,
    string? CostKey, string? Cost,
    string? WindowsNameKey, string? WindowsName,
    long? Bytes, bool DefaultOn, bool NeedsRestart, bool NeedsAdmin, bool Reversible,
    ActionLinkKind LinkKind, IReadOnlyList<string> LinkIds,
    IReadOnlyList<ActionFile> Files);

public sealed record ActionGroup(ActionGroupKind Kind, IReadOnlyList<ActionItem> Items);

public sealed record ActionPlan(DateTime CreatedUtc, IReadOnlyList<ActionGroup> Groups, ScanResult Scan)
{
    public IEnumerable<ActionItem> Items => Groups.SelectMany(g => g.Items);
    public ActionGroup? Group(ActionGroupKind kind) => Groups.FirstOrDefault(g => g.Kind == kind);
}

public sealed record ActionSelection(IReadOnlySet<string> RowIds, IReadOnlySet<string> Files)
{
    public static ActionSelection Default(ActionPlan plan) => new(
        plan.Items.Where(i => i.DefaultOn).Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase),
        plan.Items.SelectMany(i => i.Files).Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase));

    public ActionSelection Only(ActionGroupKind group, ActionPlan plan) => this with
    {
        RowIds = RowIds.Where(id => plan.Items.Any(i => i.Group == group
                && string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase),
    };
}

public enum RowOutcome { Done, PendingRestart, Skipped, Failed }

public sealed record RowResult(string RowId, RowOutcome Outcome, string? Reason,
    ReadBackState? ReadBack, long BytesFreed, long BytesDeferred);

public sealed record RunRecord(string RunId, DateTime StartedUtc, DateTime FinishedUtc,
    IReadOnlyList<RowResult> Rows, IReadOnlyList<string> UndoableRuleIds);
```

- [ ] **Step 4: Create `ActionCatalog.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace BriskEngine.Actions;

public sealed record RowDef(string Id, ActionGroupKind Group, string Title, string? Cost,
    string? WindowsName, bool DefaultOn, bool NeedsRestart, bool NeedsAdmin, bool Reversible,
    ActionLinkKind LinkKind, IReadOnlyList<string> LinkIds)
{
    public string TitleKey => $"action.{Id}.title";
    public string? CostKey => Cost is null ? null : $"action.{Id}.cost";
    public string? NameKey => WindowsName is null ? null : $"action.{Id}.name";
}

/// Every fixed row. Copy rules (spec): title in everyday words, cost five or
/// six words at most, Windows' own name only when the row is expanded.
public static class ActionCatalog
{
    private static RowDef R(string id, ActionGroupKind g, string title, string? cost, string? name,
        bool on = true, bool restart = false, bool admin = false, bool reversible = true,
        ActionLinkKind kind = ActionLinkKind.Rules, string[]? links = null) =>
        new(id, g, title, cost, name, on, restart, admin, reversible, kind, links ?? System.Array.Empty<string>());

    private const ActionGroupKind Space = ActionGroupKind.Space;
    private const ActionGroupKind Speed = ActionGroupKind.Speed;
    private const ActionGroupKind Privacy = ActionGroupKind.Privacy;
    private const ActionLinkKind Clean = ActionLinkKind.Cleanup;

    public static IReadOnlyList<RowDef> Rows { get; } = new[]
    {
        // ---- Space: buckets. component-store stays last: DISM takes minutes.
        R("space.caches", Space, "Caches and temporary files", null, null, reversible: false, kind: Clean,
            links: new[] { "user-temp", "chrome-cache", "edge-cache", "firefox-cache", "brave-cache",
                "opera-cache", "thumbnail-cache", "discord-cache", "spotify-storage", "teams-cache",
                "slack-cache", "vscode-cache", "whatsapp-cache", "telegram-media-cache", "crash-dumps",
                "wer-reports", "wer-system", "windows-temp", "windows-update-cache",
                "delivery-optimization", "component-store" }),
        R("space.developer", Space, "Developer caches", "Downloads again later.", null,
            reversible: false, kind: Clean,
            links: new[] { "npm-cache", "pip-cache", "yarn-cache", "pnpm-store", "nuget-http-cache",
                "cargo-registry-cache", "gradle-caches" }),
        R("space.shaders", Space, "Game shader caches", "Games may stutter on first launch.", null,
            on: false, reversible: false, kind: Clean,
            links: new[] { "nvidia-shader-cache", "amd-shader-cache", "d3d-shader-cache" }),
        R("space.hibernation", Space, "Hibernation file", "Fast startup turns off.", "hiberfil.sys",
            admin: true, links: new[] { "hibernation" }),
        R("space.windows-old", Space, "Old Windows copy", "No going back to old Windows.", "Windows.old",
            admin: true, reversible: false, kind: Clean, links: new[] { "windows-old" }),
        R("space.downloads", Space, "Installers in Downloads", "Can't be undone.", null,
            reversible: false, kind: Clean, links: new[] { "old-installers" }),
        R("space.docker", Space, "Docker's unused data", "Stopped containers are deleted.", "docker system prune",
            on: false, reversible: false, kind: Clean, links: new[] { "docker-prune" }),
        R("space.recycle-bin", Space, "Recycle Bin", null, null, reversible: false, kind: Clean,
            links: new[] { "empty-recycle-bin" }),

        // ---- Speed: fixed switches (startup entries are added per machine).
        R("speed.power-mode", Speed, "Full power when plugged in", "The fan may spin more.",
            "Power mode: Best performance", admin: true, links: new[] { "power-mode" }),
        R("speed.game-recording", Speed, "Background video recording in games", null,
            "Game DVR background recording", links: new[] { "game-recording" }),
        R("speed.gpu-scheduling", Speed, "Lower lag in games", null,
            "Hardware-accelerated GPU scheduling", restart: true, admin: true, links: new[] { "gpu-scheduling" }),
        R("speed.windowed-games", Speed, "Speed up windowed games", null,
            "Optimizations for windowed games", links: new[] { "windowed-games" }),
        R("speed.animations", Speed, "Window opening animations", "Windows open instantly.",
            "Animate windows when minimizing and maximizing", links: new[] { "window-animations" }),
        R("speed.memory-integrity", Speed, "Extra kernel protection", "Security drops one level.",
            "Memory integrity", on: false, restart: true, admin: true, links: new[] { "memory-integrity" }),
        R("speed.vm-platform", Speed, "Virtual machine support", "WSL and Docker stop working.",
            "Virtual Machine Platform", on: false, restart: true, admin: true, links: new[] { "vm-platform" }),

        // ---- Privacy: a ticked box means "stop this".
        R("privacy.telemetry-services", Privacy, "Sending usage data to Microsoft", null,
            "Connected User Experiences and Telemetry", admin: true, links: new[] { "telemetry-services" }),
        R("privacy.telemetry-tasks", Privacy, "Collecting data in the background", null,
            "Telemetry scheduled tasks", admin: true, links: new[] { "telemetry-tasks" }),
        R("privacy.error-reports", Privacy, "Sending error and usage reports", null,
            "Diagnostic data, Windows Error Reporting", admin: true,
            links: new[] { "diagnostic-level", "error-reporting-off" }),
        R("privacy.ads", Privacy, "Showing ads and suggestions for you", null,
            "Advertising ID, tailored experiences", links: new[] { "advertising-id", "tailored-experiences" }),
        R("privacy.activity", Privacy, "Recording the files and sites you open", null,
            "Activity history", admin: true, links: new[] { "activity-history" }),
        R("privacy.start-ads", Privacy, "Ads in the Start menu", null, "Start suggestions",
            links: new[] { "windows-suggestions" }),
        R("privacy.start-web", Privacy, "Going online when you search Start", "No web search in Start.",
            "Bing in Start search", links: new[] { "search-web-results" }),
        R("privacy.speech", Privacy, "Sending your voice and typing", "Windows voice typing stops working.",
            "Online speech recognition, inking and typing", links: new[] { "speech-typing" }),
        R("privacy.feedback", Privacy, "\"How do you like Windows?\" questions", null,
            "Feedback frequency", admin: true, links: new[] { "feedback-off" }),
        R("privacy.recall", Privacy, "Saving pictures of your screen", null, "Recall", links: new[] { "recall-off" }),
        R("privacy.copilot", Privacy, "Copilot", "Copilot turns off.", "Windows Copilot", links: new[] { "copilot-off" }),
        R("privacy.delivery-peers", Privacy, "Sharing updates with strangers", null,
            "Delivery Optimization", admin: true, links: new[] { "delivery-peers-off" }),
        R("privacy.location", Privacy, "Sharing your location with apps", "Find my device stops working.",
            "Location", on: false, links: new[] { "location" }),
        R("privacy.uac", Privacy, "Programs becoming admin without asking", "Programs ask for permission again.",
            "User Account Control", restart: true, admin: true, links: new[] { "uac-prompts" }),
        R("privacy.firewall", Privacy, "The firewall being off", null, "Windows Firewall",
            admin: true, links: new[] { "firewall-on" }),
        R("privacy.defender-realtime", Privacy, "Virus protection being off", null, "Real-time protection",
            admin: true, links: new[] { "defender-realtime" }),
    };

    public static RowDef Find(string id) => Rows.First(r => r.Id == id);

    public const string HibernationLaptopCost = "Open work lost if battery dies.";
    public const string HibernationLaptopCostKey = "action.space.hibernation.cost-laptop";
    public const string StartupName = "Startup apps (Task Manager)";
    public const string StartupNameKey = "action.speed.startup.name";

    public static (string Key, string English) StartupCost(StartupProtectionKind kind) => kind switch
    {
        StartupProtectionKind.Security => ("action.speed.startup.cost.security", "Protection won't start at boot."),
        StartupProtectionKind.Hardware => ("action.speed.startup.cost.hardware", "Hardware settings may not work."),
        StartupProtectionKind.Sync => ("action.speed.startup.cost.sync", "File sync stops."),
        StartupProtectionKind.Passwords => ("action.speed.startup.cost.passwords", "Password manager won't open."),
        StartupProtectionKind.Vpn => ("action.speed.startup.cost.vpn", "VPN won't connect automatically."),
        StartupProtectionKind.Keyboard => ("action.speed.startup.cost.keyboard", "Your shortcuts won't work."),
        _ => ("", ""),
    };

    /// Every localization key the engine hands out — the resx test in
    /// Brisk.Tests checks both languages carry each one.
    public static IEnumerable<string> AllKeys()
    {
        foreach (var r in Rows)
        {
            yield return r.TitleKey;
            if (r.CostKey is not null) yield return r.CostKey;
            if (r.NameKey is not null) yield return r.NameKey;
        }
        yield return HibernationLaptopCostKey;
        yield return StartupNameKey;
        foreach (var kind in System.Enum.GetValues<StartupProtectionKind>())
            if (kind != StartupProtectionKind.None) yield return StartupCost(kind).Key;
        foreach (var key in ActionStrings.Ui.Keys) yield return key;
    }
}

/// Fixed interface words the engine's callers print: group names, results,
/// footers, receipt lines, load notes. English here; Turkish in the resx.
public static class ActionStrings
{
    public static IReadOnlyDictionary<string, string> Ui { get; } = new Dictionary<string, string>
    {
        ["action.group.space"] = "Free up space",
        ["action.group.speed"] = "Speed up",
        ["action.group.privacy"] = "Privacy",
        ["action.group.load"] = "Slowing you down now",
        ["action.result.done"] = "Done ✓",
        ["action.result.off"] = "Off ✓",
        ["action.result.ignored"] = "Windows ignored it",
        ["action.result.reverted"] = "Turned back on",
        ["action.result.written"] = "Written",
        ["action.result.pending"] = "After restart",
        ["action.result.skipped"] = "Skipped",
        ["action.result.failed"] = "Didn't work",
        ["action.result.needs-admin"] = "Needs administrator",
        ["action.result.deferred"] = "Cleans when {0} closes",
        ["action.result.cannot-undo"] = "Can't be undone",
        ["action.connections.footer"] = "A connection doesn't mean data was sent.",
        ["action.connections.local"] = "Local network",
        ["action.connections.unknown"] = "Unknown",
        ["action.connections.telemetry"] = "Telemetry",
        ["action.usage.footer"] = "Amounts only, no contents.",
        ["action.usage.windows"] = "Windows (system)",
        ["action.unread"] = "Couldn't read.",
        ["action.load.close"] = "Close",
        ["action.load.force"] = "Force close",
        ["action.load.remove-startup"] = "Remove from startup",
        ["action.load.note.defender"] = "Defender is scanning",
        ["action.load.note.update"] = "Windows is installing updates",
        ["action.load.note.search"] = "Windows is indexing for search",
        ["action.load.note.memory"] = "Windows is compressing memory",
        ["action.load.note.compat"] = "Windows is collecting compatibility data",
        ["action.load.note.dwm"] = "Windows is drawing the screen",
        ["action.load.note.system"] = "Windows itself",
        ["action.load.note.services"] = "Windows services",
        ["action.receipt.freed"] = "{0} freed",
        ["action.receipt.deferred"] = "{0} more when {1} closes",
        ["action.receipt.boot"] = "Startup {0} s → {1} s",
        ["action.receipt.startup"] = "Startup apps {0} → {1}",
        ["action.receipt.ram"] = "{0} RAM freed",
        ["action.receipt.privacy"] = "{0} settings off · Windows ignored {1}",
        ["action.receipt.telemetry"] = "Telemetry connections {0} → {1}",
        ["action.restart"] = "Restart for full effect",
        ["action.undo"] = "Undo",
    };

    public static string En(string key) => Ui.TryGetValue(key, out var s) ? s : key;
}
```

- [ ] **Step 5: Create `ActionRules.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Actions.Rules;
using BriskEngine.Actions.Switches;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;

namespace BriskEngine.Actions;

public sealed record ActionProbes(
    IProcessRunner Runner,
    IUiEffectsProbe Ui,
    IPowerOverlayProbe Overlay,
    IOptionalFeatureProbe Features,
    IScheduledTaskProbe Tasks,
    StartupManager Startup,
    Func<bool> HiberfilExists);

/// Finds the rule behind a row by id: the existing registry, the new rules,
/// and "startup:{hive}:{name}" ids built on demand.
public sealed class ActionRules
{
    private readonly Dictionary<string, IDiagnosticRule> _byId;
    private readonly StartupManager _startup;

    public ActionRules(ActionProbes probes)
    {
        _startup = probes.Startup;
        var rules = new List<IDiagnosticRule>(DiagnosticRuleRegistry.All);
        rules.AddRange(SwitchCatalog.Plain.Select(s => new SwitchRule(s)));
        rules.Add(new TelemetryServicesRule(probes.Runner));
        rules.Add(new TaskSwitchRule(probes.Tasks));
        rules.Add(new WindowAnimationsRule(probes.Ui));
        rules.Add(new WindowedGamesRule());
        rules.Add(new PowerModeRule(probes.Overlay));
        rules.Add(new VirtualMachinePlatformRule(probes.Features, probes.Runner));
        rules.Add(new FirewallOnRule(probes.Runner));
        rules.Add(new DefenderRealtimeRule(probes.Runner));
        rules.Add(new HibernationRule(probes.Runner, probes.HiberfilExists));
        _byId = rules.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IDiagnosticRule? Find(string id)
    {
        if (_byId.TryGetValue(id, out var rule)) return rule;
        return StartupEntryRule.TryParseId(id, out var hive, out var name)
            ? new StartupEntryRule(_startup, hive, name)
            : null;
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~ActionCatalogTests"`
Expected: all pass. If `EveryRuleLinkResolves` names an id, compare it with the rule's `Id` (the existing ids are `windows-suggestions` and `uac-prompts`, not the class names).

- [ ] **Step 7: Commit**

```bash
git add src/BriskEngine/Actions/ActionModels.cs src/BriskEngine/Actions/ActionCatalog.cs src/BriskEngine/Actions/ActionRules.cs src/BriskEngine.Tests/ActionCatalogTests.cs
git commit -m "feat(actions): the one-click rows, their copy, and rule lookup" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 14: Plan builder

**Files:**
- Create: `src/BriskEngine/Actions/ActionPlanBuilder.cs`
- Test: `src/BriskEngine.Tests/ActionPlanBuilderTests.cs`

**Interfaces:**
- Consumes: `ActionCatalog`, `ActionRules.Find`, `StartupManager.List()`, `StartupProtection.Classify`, `StartupEntryRule.IdFor`, `ISpaceProbe`, `IOptionalFeatureProbe`, `IPowercfgProbe.HasBattery()`, `IDefenderProbe.Registered()`, `ScanResult`.
- Produces: `public sealed class ActionPlanBuilder` — `ActionPlanBuilder(DiagnosticContext ctx, ActionRules rules, StartupManager startup, ISpaceProbe space, IOptionalFeatureProbe features)`, `ActionPlan Build(ScanResult scan, DateTime nowUtc)`, `public static ActionItem ItemFrom(RowDef row, long? bytes = null, bool? defaultOn = null, string? cost = null, string? costKey = null, IReadOnlyList<ActionFile>? files = null)`.

Rules for the plan (spec): a rule row appears when at least one linked rule's `Detect` returns a finding; a space bucket appears when it has bytes; a startup row appears for every enabled entry except brisk's own; Recall appears only where the Recall feature exists; groups are ordered Space, Speed, Privacy for display. A bucket's size counts a target skipped for a running app at its full size (it is cleaned later).

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Actions;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

public sealed class ActionPlanBuilderTests
{
    private const string Run = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Kit(ActionPlanBuilder Builder, DiagnosticContext Ctx, FakeRegistry Reg,
        FakeFeatures Features, FakeSpace Space, FakeUi Ui);

    private static Kit Make()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        var features = new FakeFeatures();
        var space = new FakeSpace();
        var ui = new FakeUi();
        var startup = new StartupManager(reg, null);
        var rules = new ActionRules(new ActionProbes(new FakeRunner(), ui, new FakeOverlay(), features,
            new FakeTasks(), startup, () => false));
        return new Kit(new ActionPlanBuilder(ctx, rules, startup, space, features), ctx, reg, features, space, ui);
    }

    private static TargetScanResult T(string id, long bytes, string? skipped = null) => new(
        CleanupTargetRegistry.All.Single(t => t.Id == id),
        new[] { new ResolvedItem(id, $@"C:\fake\{id}", bytes, Now.AddDays(-40)) }, skipped);

    private static ScanResult Scan(params TargetScanResult[] targets) => new(targets);

    private static ActionItem? Row(ActionPlan plan, string id) => plan.Items.FirstOrDefault(i => i.Id == id);

    [Fact]
    public void Build_MachineWithoutOptionalFeatures_HidesThoseRowsAndDoesNotThrow()
    {
        var kit = Make();
        var plan = kit.Builder.Build(Scan(), Now);

        foreach (var id in new[] { "privacy.recall", "speed.gpu-scheduling", "speed.windowed-games",
                     "speed.power-mode", "speed.vm-platform", "space.docker", "space.hibernation",
                     "space.recycle-bin", "space.downloads", "space.caches", "privacy.firewall",
                     "privacy.defender-realtime" })
            Assert.Null(Row(plan, id));
        Assert.NotNull(Row(plan, "speed.animations"));   // the fake UI reports animations on
        Assert.NotNull(Row(plan, "privacy.copilot"));    // policy absent = Copilot allowed
    }

    [Fact]
    public void Build_GroupsAreSpaceSpeedPrivacy_AndEmptyGroupsAreLeftOut()
    {
        var kit = Make();
        var plan = kit.Builder.Build(Scan(T("user-temp", 100)), Now);
        Assert.Equal(new[] { ActionGroupKind.Space, ActionGroupKind.Speed, ActionGroupKind.Privacy },
            plan.Groups.Select(g => g.Kind));
    }

    [Fact]
    public void Build_CacheBucketCountsARunningAppsCacheAtFullSize()
    {
        var kit = Make();
        var plan = kit.Builder.Build(Scan(T("user-temp", 100), T("chrome-cache", 50, "chrome is running")), Now);
        Assert.Equal(150, Row(plan, "space.caches")!.Bytes);
    }

    [Fact]
    public void Build_ShaderCachesAreListedUnticked()
    {
        var kit = Make();
        var plan = kit.Builder.Build(Scan(T("nvidia-shader-cache", 2_300)), Now);
        var row = Row(plan, "space.shaders")!;
        Assert.False(row.DefaultOn);
        Assert.Equal("Games may stutter on first launch.", row.Cost);
    }

    [Fact]
    public void Build_Hibernation_TickedOnADesktop_UntickedOnALaptop()
    {
        var desktop = Make();
        ((FakePowercfg)desktop.Ctx.Powercfg).Battery = false;
        var row = Row(desktop.Builder.Build(Scan(T("hibernation-file", 13_000)), Now), "space.hibernation")!;
        Assert.True(row.DefaultOn);
        Assert.Equal("Fast startup turns off.", row.Cost);

        var laptop = Make();
        ((FakePowercfg)laptop.Ctx.Powercfg).Battery = true;
        row = Row(laptop.Builder.Build(Scan(T("hibernation-file", 13_000)), Now), "space.hibernation")!;
        Assert.False(row.DefaultOn);
        Assert.Equal(ActionCatalog.HibernationLaptopCost, row.Cost);
    }

    [Fact]
    public void Build_DownloadsRowListsEachFile()
    {
        var kit = Make();
        var target = CleanupTargetRegistry.All.Single(t => t.Id == "old-installers");
        var scan = Scan(new TargetScanResult(target, new[]
        {
            new ResolvedItem("old-installers", @"C:\Users\x\Downloads\a.exe", 100, Now.AddDays(-60)),
            new ResolvedItem("old-installers", @"C:\Users\x\Downloads\b.iso", 900, Now.AddDays(-90)),
        }, null));

        var row = Row(kit.Builder.Build(scan, Now), "space.downloads")!;

        Assert.Equal(1000, row.Bytes);
        Assert.Equal(@"C:\Users\x\Downloads\b.iso", row.Files[0].Path);
        Assert.Equal(2, row.Files.Count);
    }

    [Fact]
    public void Build_DockerAndRecycleBinComeFromTheSpaceProbe()
    {
        var kit = Make();
        kit.Space.Docker = 5_000;
        kit.Space.Bin = 700;
        var plan = kit.Builder.Build(Scan(), Now);
        Assert.Equal(5_000, Row(plan, "space.docker")!.Bytes);
        Assert.False(Row(plan, "space.docker")!.DefaultOn);
        Assert.Equal(700, Row(plan, "space.recycle-bin")!.Bytes);
    }

    [Fact]
    public void Build_StartupRows_TickedUnlessProtected_AndNeverBrisk()
    {
        var kit = Make();
        kit.Reg.SetString(Run, "Discord", @"C:\Discord\Update.exe --processStart Discord.exe");
        kit.Reg.SetString(Run, "OneDrive", @"C:\OneDrive\OneDrive.exe /background");
        kit.Reg.SetString(Run, "brisk", @"C:\brisk\brisk-app.exe --tray");
        kit.Reg.SetString(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Vendor", @"C:\v\v.exe");

        var plan = kit.Builder.Build(Scan(), Now);

        var discord = Row(plan, "startup:HKCU:Discord")!;
        Assert.True(discord.DefaultOn);
        Assert.Null(discord.TitleKey);
        Assert.Equal("Discord", discord.Title);
        var onedrive = Row(plan, "startup:HKCU:OneDrive")!;
        Assert.False(onedrive.DefaultOn);
        Assert.Equal("File sync stops.", onedrive.Cost);
        Assert.Null(Row(plan, "startup:HKCU:brisk"));
        Assert.True(Row(plan, "startup:HKLM:Vendor")!.NeedsAdmin);
    }

    [Fact]
    public void Build_RecallRowNeedsTheFeature()
    {
        var kit = Make();
        Assert.Null(Row(kit.Builder.Build(Scan(), Now), "privacy.recall"));
        kit.Features.States["Recall"] = 2;
        Assert.NotNull(Row(kit.Builder.Build(Scan(), Now), "privacy.recall"));
    }

    [Fact]
    public void Build_ARuleThatIsAlreadyDone_HidesItsRow()
    {
        var kit = Make();
        kit.Ui.Minimize = false;
        kit.Ui.ClientArea = false;
        Assert.Null(Row(kit.Builder.Build(Scan(), Now), "speed.animations"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~ActionPlanBuilderTests"`
Expected: build FAILS — `ActionPlanBuilder` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Actions.Rules;
using BriskEngine.Diagnostics;
using BriskEngine.Models;

namespace BriskEngine.Actions;

/// Turns one scan into the three groups of ticked rows. Every decision that
/// depends on the machine is made here: laptop or desktop, which startup
/// entries are protected, which features exist.
public sealed class ActionPlanBuilder
{
    private readonly DiagnosticContext _ctx;
    private readonly ActionRules _rules;
    private readonly StartupManager _startup;
    private readonly ISpaceProbe _space;
    private readonly IOptionalFeatureProbe _features;

    public ActionPlanBuilder(DiagnosticContext ctx, ActionRules rules, StartupManager startup,
        ISpaceProbe space, IOptionalFeatureProbe features)
    {
        _ctx = ctx;
        _rules = rules;
        _startup = startup;
        _space = space;
        _features = features;
    }

    public static ActionItem ItemFrom(RowDef row, long? bytes = null, bool? defaultOn = null,
        string? cost = null, string? costKey = null, IReadOnlyList<ActionFile>? files = null) =>
        new(row.Id, row.Group, row.TitleKey, row.Title, costKey ?? row.CostKey, cost ?? row.Cost,
            row.NameKey, row.WindowsName, bytes, defaultOn ?? row.DefaultOn, row.NeedsRestart,
            row.NeedsAdmin, row.Reversible, row.LinkKind, row.LinkIds,
            files ?? Array.Empty<ActionFile>());

    public ActionPlan Build(ScanResult scan, DateTime nowUtc)
    {
        var groups = new[]
        {
            new ActionGroup(ActionGroupKind.Space, SpaceRows(scan).ToList()),
            new ActionGroup(ActionGroupKind.Speed, SpeedRows().ToList()),
            new ActionGroup(ActionGroupKind.Privacy, PrivacyRows().ToList()),
        };
        return new ActionPlan(nowUtc, groups.Where(g => g.Items.Count > 0).ToList(), scan);
    }

    private bool Wants(string ruleId)
    {
        var rule = _rules.Find(ruleId);
        if (rule is null) return false;
        try { return rule.Detect(_ctx) is not null; }
        catch (Exception) { return false; }
    }

    private IEnumerable<ActionItem> PrivacyRows()
    {
        foreach (var row in ActionCatalog.Rows.Where(r => r.Group == ActionGroupKind.Privacy))
        {
            if (row.Id == "privacy.recall" && _features.InstallState("Recall") is not (1 or 2)) continue;
            if (row.LinkIds.Any(Wants)) yield return ItemFrom(row);
        }
    }

    private IReadOnlyCollection<string> AntivirusNames()
    {
        try { return _ctx.Defender.Registered().Where(p => !p.IsDefender).Select(p => p.Name).ToList(); }
        catch (Exception) { return Array.Empty<string>(); }
    }

    private IEnumerable<ActionItem> SpeedRows()
    {
        var antivirus = AntivirusNames();
        foreach (var e in _startup.List())
        {
            if (!e.Enabled || e.Name.Contains("brisk", StringComparison.OrdinalIgnoreCase)) continue;
            var kind = StartupProtection.Classify(e.Name, e.Command, antivirus);
            var protectedEntry = kind != StartupProtectionKind.None;
            var (costKey, cost) = ActionCatalog.StartupCost(kind);
            var id = StartupEntryRule.IdFor(e.Hive, e.Name);
            var title = e.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? e.Name[..^4] : e.Name;
            yield return new ActionItem(id, ActionGroupKind.Speed,
                null, title,
                protectedEntry ? costKey : null, protectedEntry ? cost : null,
                ActionCatalog.StartupNameKey, ActionCatalog.StartupName,
                null, !protectedEntry, false, e.Hive is "HKLM" or "FolderAll", true,
                ActionLinkKind.Rules, new[] { id }, Array.Empty<ActionFile>());
        }
        foreach (var row in ActionCatalog.Rows.Where(r => r.Group == ActionGroupKind.Speed))
            if (row.LinkIds.Any(Wants)) yield return ItemFrom(row);
    }

    private static long? Safe(Func<long?> read)
    {
        try { return read(); }
        catch (Exception) { return null; }
    }

    private IEnumerable<ActionItem> SpaceRows(ScanResult scan)
    {
        TargetScanResult? Target(string id) => scan.Targets.FirstOrDefault(t => t.Target.Id == id);

        foreach (var row in ActionCatalog.Rows.Where(r => r.Group == ActionGroupKind.Space))
        {
            switch (row.Id)
            {
                case "space.hibernation":
                {
                    var bytes = Target("hibernation-file")?.TotalBytes ?? 0;
                    if (bytes == 0) break;
                    yield return _ctx.Powercfg.HasBattery()
                        ? ItemFrom(row, bytes, defaultOn: false,
                            cost: ActionCatalog.HibernationLaptopCost, costKey: ActionCatalog.HibernationLaptopCostKey)
                        : ItemFrom(row, bytes);
                    break;
                }
                case "space.downloads":
                {
                    var t = Target("old-installers");
                    if (t is null || t.Items.Count == 0) break;
                    var files = t.Items.OrderByDescending(i => i.Bytes)
                        .Select(i => new ActionFile(i.Path, i.Bytes, i.LastWriteUtc)).ToList();
                    yield return ItemFrom(row, files.Sum(f => f.Bytes), files: files);
                    break;
                }
                case "space.docker":
                {
                    var bytes = Safe(_space.DockerReclaimableBytes);
                    if (bytes is null or 0) break;
                    yield return ItemFrom(row, bytes);
                    break;
                }
                case "space.recycle-bin":
                {
                    var bytes = Safe(() => _space.RecycleBinBytes()) ?? 0;
                    if (bytes == 0) break;
                    yield return ItemFrom(row, bytes);
                    break;
                }
                default:
                {
                    var bytes = row.LinkIds.Select(Target).OfType<TargetScanResult>()
                        .Sum(t => t.SkippedReason is null ? t.ReclaimableBytes : t.TotalBytes);
                    if (bytes > 0) yield return ItemFrom(row, bytes);
                    break;
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~ActionPlanBuilderTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Actions/ActionPlanBuilder.cs src/BriskEngine.Tests/ActionPlanBuilderTests.cs
git commit -m "feat(actions): build the three groups of ticked rows from a scan" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 15: Runner, run store, undo

**Files:**
- Create: `src/BriskEngine/Actions/ActionRunner.cs`
- Create: `src/BriskEngine/Actions/RunStore.cs`
- Test: `src/BriskEngine.Tests/ActionRunnerTests.cs`

**Interfaces:**
- Consumes: `WriteAheadRegistry` (Task 1), `ReadBack.StateOf` (Task 2), `ActionRules`, `FixJournal.RecordFix/RecordUndo/LastUndoablePriorState`, `CleanRunner.Clean(..., permanent: true)` (Task 9), `DeferredCleanQueue.Add` (Task 12), `ActionPlan`, `ActionSelection`, `DiagnosticContext.RunningApps`.
- Produces:
  - `public interface IReceiptRecorder { void Before(string runId, ActionPlan plan, ActionSelection selection); void After(RunRecord run); }`
  - `public sealed class ActionRunner` — `ActionRunner(DiagnosticContext ctx, ActionRules rules, FixJournal journal, CleanRunner cleaner, DeferredCleanQueue deferred, RunStore runs, Func<bool> isElevated, Func<string, (long Bytes, DateTime? LastWriteUtc)?>? stat = null, Func<DateTime>? now = null, IReceiptRecorder? receipts = null)`, `RunRecord Run(ActionPlan plan, ActionSelection selection, Action<RowResult>? onRow = null)`, `IReadOnlyList<RowResult> Undo(string runId)`, `const string NeedsAdmin = "needs administrator"`, `static (long, DateTime?)? StatFile(string path)`
  - `public sealed class RunStore` — `RunStore(string directory)`, `void Save(RunRecord run)`, `RunRecord? Load(string runId)`, `RunRecord? Latest()`

Order of a run: Privacy, then Speed, then Space (Space last: DISM takes minutes). A row's outcome is the worst of its rules (Failed > Skipped > PendingRestart > Done); its read-back is the worst state (Reverted > WrittenButIgnored > WrittenButUnverified > Held). A cache target whose app is running — at plan time or at run time — is queued, never cleaned.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Actions;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Logging;
using BriskEngine.Models;
using BriskEngine.Safety;
using Xunit;

namespace BriskEngine.Tests;

public sealed class ActionRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-run-").FullName;
    private readonly DiagnosticContext _ctx;
    private readonly FakeRegistry _reg;
    private readonly FakeRecycler _recycler = new();
    private readonly FixJournal _journal;
    private readonly DeferredCleanQueue _deferred;
    private readonly RunStore _runs;
    private readonly FakeUi _ui = new();
    private bool _elevated = true;
    private Func<string, (long, DateTime?)?> _stat = ActionRunner.StatFile;

    public ActionRunnerTests()
    {
        _ctx = TestContext.Empty(Path.Combine(_root, "data"));
        _reg = (FakeRegistry)_ctx.Registry;
        _journal = new FixJournal(Path.Combine(_root, "journal.jsonl"));
        _deferred = new DeferredCleanQueue(Path.Combine(_root, "deferred.json"));
        _runs = new RunStore(Path.Combine(_root, "runs"));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private ActionRunner Runner()
    {
        var rules = new ActionRules(new ActionProbes(new FakeRunner(), _ui, new FakeOverlay(), new FakeFeatures(),
            new FakeTasks(), new StartupManager(_reg, null), () => false));
        var cleaner = new CleanRunner(new SafetyValidator(), _recycler, new ActionLog(Path.Combine(_root, "log.jsonl")),
            new FakeRunner(), () => _elevated, null, () => 0, new WindowsDeleter());
        return new ActionRunner(_ctx, rules, _journal, cleaner, _deferred, _runs, () => _elevated, s => _stat(s));
    }

    private static ActionItem Row(string id, long? bytes = null, IReadOnlyList<ActionFile>? files = null) =>
        ActionPlanBuilder.ItemFrom(ActionCatalog.Find(id), bytes, files: files);

    private static ActionPlan Plan(ScanResult scan, params ActionItem[] items) => new(DateTime.UtcNow,
        items.GroupBy(i => i.Group).Select(g => new ActionGroup(g.Key, g.ToList())).ToList(), scan);

    private static ActionSelection Select(params string[] ids) => new(
        ids.ToHashSet(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static readonly ScanResult NoScan = new(Array.Empty<TargetScanResult>());

    [Fact]
    public void Run_AppliesTickedRows_AndLeavesUntickedOnesAlone()
    {
        var plan = Plan(NoScan, Row("privacy.ads"), Row("privacy.copilot"));
        var run = Runner().Run(plan, Select("privacy.ads"));

        var row = Assert.Single(run.Rows);
        Assert.Equal(RowOutcome.Done, row.Outcome);
        Assert.Equal(ReadBackState.Held, row.ReadBack);
        Assert.Equal(0, _reg.GetInt(@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled"));
        Assert.Null(_reg.GetInt(@"HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot"));
        Assert.Contains("advertising-id", run.UndoableRuleIds);
    }

    [Fact]
    public void Run_RowNeedingAdmin_IsSkippedWhenNotElevated()
    {
        _elevated = false;
        var plan = Plan(NoScan, Row("privacy.error-reports"));
        var row = Assert.Single(Runner().Run(plan, Select("privacy.error-reports")).Rows);

        Assert.Equal(RowOutcome.Skipped, row.Outcome);
        Assert.Equal(ActionRunner.NeedsAdmin, row.Reason);
        Assert.Null(_reg.GetInt(@"HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled"));
    }

    [Fact]
    public void Run_ARuleRefusedHalfway_IsRolledBack()
    {
        // search-web-results writes a policy value, then a second key; the
        // second key refuses, so the first write must not survive.
        _reg.DenyWriteKeys.Add(SearchWebResultsRule.LegacyKey);
        var plan = Plan(NoScan, Row("privacy.start-web"));

        var row = Assert.Single(Runner().Run(plan, Select("privacy.start-web")).Rows);

        Assert.Equal(RowOutcome.Skipped, row.Outcome);
        Assert.Null(_reg.GetInt(SearchWebResultsRule.PolicyKey, SearchWebResultsRule.PolicyValue));
        Assert.Null(_journal.LastUndoablePriorState("search-web-results"));
    }

    [Fact]
    public void Run_RecoversAPendingWriteAheadLogFirst()
    {
        const string key = @"HKCU\Software\BriskTest";
        _reg.SetInt(key, "A", 1);
        var crashed = new WriteAheadRegistry(_reg, Path.Combine(_ctx.DataDirectory, "registry-wal.jsonl"));
        crashed.SetInt(key, "A", 0);   // a previous run died here

        Runner().Run(Plan(NoScan), Select());

        Assert.Equal(1, _reg.GetInt(key, "A"));
    }

    [Fact]
    public void Run_RestartRows_ArePendingRestart()
    {
        _reg.SetInt(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 1);
        var row = Assert.Single(Runner().Run(Plan(NoScan, Row("speed.gpu-scheduling")), Select("speed.gpu-scheduling")).Rows);
        Assert.Equal(RowOutcome.PendingRestart, row.Outcome);
        Assert.Equal(2, _reg.GetInt(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"));
    }

    [Fact]
    public void Run_DeletesACacheForReal()
    {
        var dir = Directory.CreateTempSubdirectory("brisk-run-cache-").FullName;   // under %TEMP%
        File.WriteAllBytes(Path.Combine(dir, "x.bin"), new byte[64]);
        var target = CleanupTargetRegistry.All.Single(t => t.Id == "user-temp");
        var scan = new ScanResult(new[] { new TargetScanResult(target,
            new[] { new ResolvedItem("user-temp", dir, 64, DateTime.UtcNow) }, null) });

        var row = Assert.Single(Runner().Run(Plan(scan, Row("space.caches", 64)), Select("space.caches")).Rows);

        Assert.Equal(RowOutcome.Done, row.Outcome);
        Assert.Equal(64, row.BytesFreed);
        Assert.False(Directory.Exists(dir));
        Assert.Empty(_recycler.Recycled);
    }

    [Fact]
    public void Run_TargetSkippedForRunningApp_IsDeferredNotCleaned()
    {
        var target = CleanupTargetRegistry.All.Single(t => t.Id == "chrome-cache");
        var scan = new ScanResult(new[] { new TargetScanResult(target,
            new[] { new ResolvedItem("chrome-cache", @"C:\fake\chrome\cache", 500, DateTime.UtcNow) },
            "chrome is running — close it to include this target") });

        var row = Assert.Single(Runner().Run(Plan(scan, Row("space.caches", 500)), Select("space.caches")).Rows);

        Assert.Equal(RowOutcome.Done, row.Outcome);
        Assert.Equal(500, row.BytesDeferred);
        Assert.Equal("chrome-cache", Assert.Single(_deferred.Pending()).TargetId);
    }

    [Fact]
    public void Run_AnAppThatOpenedAfterThePlan_IsDeferredToo()
    {
        ((FakeRunningApps)_ctx.RunningApps).Running.Add("chrome");
        var target = CleanupTargetRegistry.All.Single(t => t.Id == "chrome-cache");
        var scan = new ScanResult(new[] { new TargetScanResult(target,
            new[] { new ResolvedItem("chrome-cache", @"C:\fake\chrome\cache", 500, DateTime.UtcNow) }, null) });

        var row = Assert.Single(Runner().Run(Plan(scan, Row("space.caches", 500)), Select("space.caches")).Rows);

        Assert.Equal(500, row.BytesDeferred);
        Assert.Single(_deferred.Pending());
    }

    [Fact]
    public void Run_DownloadsFileChangedSincePlan_IsSkipped()
    {
        const string path = @"C:\Users\x\Downloads\setup.exe";
        var written = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var target = CleanupTargetRegistry.All.Single(t => t.Id == "old-installers");
        var scan = new ScanResult(new[] { new TargetScanResult(target,
            new[] { new ResolvedItem("old-installers", path, 100, written) }, null) });
        _stat = _ => (200, written);   // grew since the plan
        var plan = Plan(scan, Row("space.downloads", 100, new[] { new ActionFile(path, 100, written) }));
        var selection = new ActionSelection(new HashSet<string> { "space.downloads" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path });

        var row = Assert.Single(Runner().Run(plan, selection).Rows);

        Assert.Equal(RowOutcome.Skipped, row.Outcome);
        Assert.Contains("changed", row.Reason);
        Assert.Equal(0, row.BytesFreed);
    }

    [Fact]
    public void Run_IsSaved_AndUndoReversesItInReverse()
    {
        var runner = Runner();
        var run = runner.Run(Plan(NoScan, Row("privacy.ads"), Row("speed.animations")),
            Select("privacy.ads", "speed.animations"));
        Assert.False(_ui.Minimize);
        Assert.NotNull(_runs.Load(run.RunId));
        Assert.Equal(run.RunId, _runs.Latest()!.RunId);

        var undone = runner.Undo(run.RunId);

        Assert.All(undone, r => Assert.Equal(RowOutcome.Done, r.Outcome));
        Assert.Equal("window-animations", undone[0].RowId);   // last applied, first undone
        Assert.True(_ui.Minimize);
        Assert.Null(_reg.GetInt(@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled"));
    }

    [Fact]
    public void Undo_UnknownRun_ReturnsNothing() =>
        Assert.Empty(Runner().Undo("no-such-run"));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~ActionRunnerTests"`
Expected: build FAILS — `ActionRunner` does not exist.

- [ ] **Step 3: Create `RunStore.cs`**

```csharp
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BriskEngine.Actions;

public sealed class RunStore
{
    private readonly string _dir;

    public RunStore(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(directory);
    }

    public void Save(RunRecord run) =>
        File.WriteAllText(Path.Combine(_dir, $"{run.RunId}.json"), JsonSerializer.Serialize(run));

    public RunRecord? Load(string runId)
    {
        var path = Path.Combine(_dir, $"{runId}.json");
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(path)); }
        catch (JsonException) { return null; }
    }

    /// Run ids start with a UTC timestamp, so the newest sorts last.
    public RunRecord? Latest() => Directory.EnumerateFiles(_dir, "*.json")
        .OrderByDescending(f => f, StringComparer.Ordinal)
        .Select(f => Load(Path.GetFileNameWithoutExtension(f)))
        .FirstOrDefault(r => r is not null);
}
```

- [ ] **Step 4: Create `ActionRunner.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Models;

namespace BriskEngine.Actions;

public interface IReceiptRecorder
{
    void Before(string runId, ActionPlan plan, ActionSelection selection);
    void After(RunRecord run);
}

/// Applies a selection under one run id. Every rule writes through a
/// write-ahead registry: a rule that throws is rolled back, and a run that
/// died is rolled back at the start of the next one.
public sealed class ActionRunner
{
    public const string NeedsAdmin = "needs administrator";

    private static readonly ActionGroupKind[] Order =
        { ActionGroupKind.Privacy, ActionGroupKind.Speed, ActionGroupKind.Space };

    private readonly DiagnosticContext _ctx;
    private readonly ActionRules _rules;
    private readonly FixJournal _journal;
    private readonly CleanRunner _cleaner;
    private readonly DeferredCleanQueue _deferred;
    private readonly RunStore _runs;
    private readonly Func<bool> _isElevated;
    private readonly Func<string, (long Bytes, DateTime? LastWriteUtc)?> _stat;
    private readonly Func<DateTime> _now;
    private readonly IReceiptRecorder? _receipts;

    public ActionRunner(DiagnosticContext ctx, ActionRules rules, FixJournal journal,
        CleanRunner cleaner, DeferredCleanQueue deferred, RunStore runs, Func<bool> isElevated,
        Func<string, (long Bytes, DateTime? LastWriteUtc)?>? stat = null,
        Func<DateTime>? now = null, IReceiptRecorder? receipts = null)
    {
        _ctx = ctx;
        _rules = rules;
        _journal = journal;
        _cleaner = cleaner;
        _deferred = deferred;
        _runs = runs;
        _isElevated = isElevated;
        _stat = stat ?? StatFile;
        _now = now ?? (() => DateTime.UtcNow);
        _receipts = receipts;
    }

    private string WalPath => Path.Combine(_ctx.DataDirectory, "registry-wal.jsonl");

    public static (long Bytes, DateTime? LastWriteUtc)? StatFile(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? (info.Length, info.LastWriteTimeUtc) : null;
    }

    public RunRecord Run(ActionPlan plan, ActionSelection selection, Action<RowResult>? onRow = null)
    {
        WriteAheadRegistry.RecoverPending(_ctx.Registry, WalPath);
        var started = _now();
        var runId = $"{started:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..22];
        _receipts?.Before(runId, plan, selection);

        var rows = new List<RowResult>();
        var undoable = new List<string>();
        foreach (var kind in Order)
        foreach (var item in plan.Group(kind)?.Items ?? Array.Empty<ActionItem>())
        {
            if (!selection.RowIds.Contains(item.Id)) continue;
            var result = item.LinkKind == ActionLinkKind.Rules
                ? RunRules(item, undoable)
                : RunCleanup(item, plan.Scan, selection);
            rows.Add(result);
            onRow?.Invoke(result);
        }

        var record = new RunRecord(runId, started, _now(), rows, undoable);
        _runs.Save(record);
        _receipts?.After(record);
        return record;
    }

    private static int Weight(RowOutcome o) => o switch
    {
        RowOutcome.Failed => 3, RowOutcome.Skipped => 2, RowOutcome.PendingRestart => 1, _ => 0,
    };

    private static int Weight(ReadBackState s) => s switch
    {
        ReadBackState.Reverted => 3, ReadBackState.WrittenButIgnored => 2,
        ReadBackState.WrittenButUnverified => 1, _ => 0,
    };

    private ReadBackState? ReadBackOf(IDiagnosticRule rule)
    {
        var fresh = _ctx.ForScan();
        try
        {
            return rule is IReadBackable rb
                ? ReadBack.StateOf(fresh, rb)
                : rule.Detect(fresh) is null ? ReadBackState.Held : ReadBackState.Reverted;
        }
        catch (Exception) { return null; }
    }

    private RowResult RunRules(ActionItem item, List<string> undoable)
    {
        if (item.NeedsAdmin && !_isElevated())
            return new RowResult(item.Id, RowOutcome.Skipped, NeedsAdmin, null, 0, 0);

        var outcomes = new List<(RowOutcome Outcome, string? Reason, ReadBackState? ReadBack)>();
        foreach (var id in item.LinkIds)
        {
            var rule = _rules.Find(id);
            if (rule is null) { outcomes.Add((RowOutcome.Failed, $"unknown rule '{id}'", null)); continue; }

            var wal = new WriteAheadRegistry(_ctx.Registry, WalPath);
            try
            {
                var prior = rule.Fix(_ctx with { Registry = wal });
                _journal.RecordFix(rule.Id, prior);
                wal.Commit();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                wal.RollBack();
                outcomes.Add((RowOutcome.Skipped, NeedsAdmin, null));
                continue;
            }
            catch (Exception ex)
            {
                wal.RollBack();
                outcomes.Add((RowOutcome.Failed, ex.Message, null));
                continue;
            }

            if (item.Reversible) undoable.Add(rule.Id);
            outcomes.Add(item.NeedsRestart
                ? (RowOutcome.PendingRestart, null, null)
                : (RowOutcome.Done, null, ReadBackOf(rule)));
        }

        var worst = outcomes.OrderByDescending(o => Weight(o.Outcome)).First();
        var readBack = outcomes.Where(o => o.ReadBack is not null)
            .Select(o => o.ReadBack!.Value)
            .OrderByDescending(s => Weight(s))
            .Select(s => (ReadBackState?)s)
            .FirstOrDefault();
        return new RowResult(item.Id, worst.Outcome, worst.Reason, readBack, 0, 0);
    }

    private static bool IsPathless(string id) =>
        id is "docker-prune" or "empty-recycle-bin" or "component-store";

    private TargetScanResult SelectedDownloads(TargetScanResult t, ActionSelection selection, List<string> reasons)
    {
        var keep = new List<ResolvedItem>();
        foreach (var i in t.Items)
        {
            if (!selection.Files.Contains(i.Path)) continue;
            var now = _stat(i.Path);
            if (now is null || now.Value.Bytes != i.Bytes || now.Value.LastWriteUtc != i.LastWriteUtc)
            {
                reasons.Add($"{Path.GetFileName(i.Path)} changed since the plan");
                continue;
            }
            keep.Add(i);
        }
        return t with { Items = keep };
    }

    private RowResult RunCleanup(ActionItem item, ScanResult scan, ActionSelection selection)
    {
        if (item.NeedsAdmin && !_isElevated())
            return new RowResult(item.Id, RowOutcome.Skipped, NeedsAdmin, null, 0, 0);

        long freed = 0, deferred = 0;
        var did = false;
        var adminSkips = 0;
        var reasons = new List<string>();
        foreach (var id in item.LinkIds)
        {
            var t = scan.Targets.FirstOrDefault(x => x.Target.Id == id);
            if (t is null) continue;

            var appOpen = t.SkippedReason is not null
                || t.Target.AppProcessCandidates.Any(_ctx.RunningApps.IsRunning);
            if (appOpen)
            {
                if (t.TotalBytes > 0)
                {
                    _deferred.Add(id, _now());
                    deferred += t.TotalBytes;
                }
                continue;
            }
            if (t.Target.RequiresElevation && !_isElevated()) { adminSkips++; continue; }

            var toClean = id == "old-installers" ? SelectedDownloads(t, selection, reasons) : t;
            if (toClean.Items.Count == 0 && !IsPathless(id)) continue;

            var report = _cleaner.Clean(toClean, dryRun: false, permanent: true);
            freed += report.DeletedBytes + report.RemovedBytes;
            if ((id is "docker-prune" or "empty-recycle-bin") && report.Entries.Any(e => e.Action == "external"))
                freed += item.Bytes ?? 0;
            did |= report.Entries.Any(e => e.Action is "deleted" or "removed" or "external");
            var error = report.Entries.FirstOrDefault(e => e.Action is "error" or "refused")?.Reason;
            if (error is not null) reasons.Add(error);
        }

        if (did || deferred > 0)
            return new RowResult(item.Id, RowOutcome.Done, reasons.FirstOrDefault(), null, freed, deferred);
        if (adminSkips > 0)
            return new RowResult(item.Id, RowOutcome.Skipped, NeedsAdmin, null, 0, 0);
        return new RowResult(item.Id, RowOutcome.Skipped, reasons.FirstOrDefault() ?? "nothing to clean", null, 0, 0);
    }

    /// Reverses a run's reversible rules, newest first. Deleted files are not
    /// in the list: they cannot come back, and undo never says they did.
    public IReadOnlyList<RowResult> Undo(string runId)
    {
        var record = _runs.Load(runId);
        if (record is null) return Array.Empty<RowResult>();
        var results = new List<RowResult>();
        foreach (var id in Enumerable.Reverse(record.UndoableRuleIds))
        {
            var rule = _rules.Find(id);
            var prior = _journal.LastUndoablePriorState(id);
            if (rule is null || prior is null)
            {
                results.Add(new RowResult(id, RowOutcome.Skipped, "nothing to undo", null, 0, 0));
                continue;
            }
            var wal = new WriteAheadRegistry(_ctx.Registry, WalPath);
            try
            {
                rule.Undo(_ctx with { Registry = wal }, prior);
                _journal.RecordUndo(id);
                wal.Commit();
                results.Add(new RowResult(id, RowOutcome.Done, null, null, 0, 0));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                wal.RollBack();
                results.Add(new RowResult(id, RowOutcome.Skipped, NeedsAdmin, null, 0, 0));
            }
            catch (Exception ex)
            {
                wal.RollBack();
                results.Add(new RowResult(id, RowOutcome.Failed, ex.Message, null, 0, 0));
            }
        }
        return results;
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~ActionRunnerTests"`
Expected: all pass. `Run_DeletesACacheForReal` deletes a folder it created under `%TEMP%`; if `SafetyValidator` refuses it on a machine where `%TEMP%` is redirected into a protected root, the failure names the reason — report it rather than loosening the validator.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test brisk.sln -c Release`
Expected: all green (1687 + the tests added so far).

- [ ] **Step 7: Commit**

```bash
git add src/BriskEngine/Actions/ActionRunner.cs src/BriskEngine/Actions/RunStore.cs src/BriskEngine.Tests/ActionRunnerTests.cs
git commit -m "feat(actions): apply a selection under one run id, with undo" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 16: Current load — process snapshot, app names, sampler, closer

**Files:**
- Create: `src/BriskEngine/Live/ProcessSnapshot.cs`
- Create: `src/BriskEngine/Live/AppNamer.cs`
- Create: `src/BriskEngine/Live/LoadSampler.cs`
- Create: `src/BriskEngine/Live/ProcessCloser.cs`
- Test: `src/BriskEngine.Tests/LoadSamplerTests.cs`

**Interfaces:**
- Consumes: `StartupEntry`, `StartupManager.ExecutableName` (internal, same assembly), `StartupEntryRule.IdFor`.
- Produces:
  - `public sealed record ProcSample(int Pid, string Name, int SessionId, TimeSpan? Cpu, long PrivateBytes, bool HasWindow, string? ExePath)`
  - `public interface IProcessSnapshotProbe { int ProcessorCount { get; } IReadOnlyList<ProcSample> Snapshot(); }`, `public sealed class RealProcessSnapshotProbe`
  - `public interface IAppNamer { string Name(string processOrExeName, string? fullPath); }`, `public sealed class RealAppNamer` with `internal static string Stem(string)`, `internal static string Pretty(string)`; `public static class DevicePath { static string? ToDos(string path); }`
  - `public sealed record AppLoad(string Key, string DisplayName, double CpuPercent, long PrivateBytes, bool IsSystem, string? NoteKey, string? Note, IReadOnlyList<int> Pids, bool HasWindow)`
  - `public sealed class LoadSampler` — `LoadSampler(IProcessSnapshotProbe probe, IAppNamer namer, Action<TimeSpan>? wait = null)`, `IReadOnlyList<AppLoad> Sample()`, `static bool CanClose(AppLoad load)`, `static string? StartupIdFor(AppLoad load, IReadOnlyList<StartupEntry> entries)`, constants `Window` (10 s), `CpuThresholdPercent` (10), `RamThresholdBytes` (500 MB), `MaxRows` (5)
  - `public static class StartupProcessMatch { static bool Matches(StartupEntry entry, string processName); }`
  - `public interface IProcessCloser { int RequestClose(IReadOnlyList<int> pids); void Kill(IReadOnlyList<int> pids); }`, `public sealed class RealProcessCloser`
  - Test fakes `FakeProcs : IProcessSnapshotProbe` and `FakeNamer : IAppNamer` (internal) — reused by Tasks 17–19.

CPU % is of the whole machine (all cores). Close asks the app to close its window (it can prompt to save); `Kill` is the second, explicit click. Closing is never part of a run.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Live;
using Xunit;

namespace BriskEngine.Tests;

sealed class FakeProcs : IProcessSnapshotProbe
{
    public Queue<List<ProcSample>> Sequence = new();
    public List<ProcSample> Now = new();
    public int ProcessorCount { get; set; } = 4;
    public IReadOnlyList<ProcSample> Snapshot() => Sequence.Count > 0 ? Sequence.Dequeue() : Now;
}

sealed class FakeNamer : IAppNamer
{
    public string Name(string processOrExeName, string? fullPath) =>
        RealAppNamer.Pretty(RealAppNamer.Stem(processOrExeName));
}

public sealed class LoadSamplerTests
{
    private static ProcSample P(int pid, string name, double cpuSeconds, long mb, int session = 1, bool window = false) =>
        new(pid, name, session, TimeSpan.FromSeconds(cpuSeconds), mb << 20, window, null);

    private static IReadOnlyList<AppLoad> Sample(List<ProcSample> before, List<ProcSample> after, int cores = 4)
    {
        var procs = new FakeProcs { ProcessorCount = cores };
        procs.Sequence.Enqueue(before);
        procs.Sequence.Enqueue(after);
        return new LoadSampler(procs, new FakeNamer(), _ => { }).Sample();
    }

    [Fact]
    public void GroupsAnAppsProcesses_AndMeasuresCpuOverTheWindow()
    {
        // 4 cores x 10 s = 40 s of capacity; Chrome used 8 s across two processes = 20 %
        var rows = Sample(
            new() { P(1, "chrome", 10, 300), P(2, "chrome", 5, 300) },
            new() { P(1, "chrome", 15, 400, window: true), P(2, "chrome", 8, 400) });

        var chrome = Assert.Single(rows);
        Assert.Equal("Chrome", chrome.DisplayName);
        Assert.Equal(20, chrome.CpuPercent);
        Assert.Equal(800L << 20, chrome.PrivateBytes);
        Assert.Equal(new[] { 1, 2 }, chrome.Pids);
        Assert.True(LoadSampler.CanClose(chrome));
    }

    [Fact]
    public void KeepsOnlyRowsAboveAThreshold_AndAtMostFive()
    {
        var before = Enumerable.Range(1, 8).Select(i => P(i, $"app{i}", 0, 600)).ToList();
        before.Add(P(50, "quiet", 0, 10));
        var after = Enumerable.Range(1, 8).Select(i => P(i, $"app{i}", i, 600)).ToList();
        after.Add(P(50, "quiet", 0, 10));

        var rows = Sample(before, after);

        Assert.Equal(LoadSampler.MaxRows, rows.Count);
        Assert.DoesNotContain(rows, r => r.Key == "quiet");
        Assert.Equal("app8", rows[0].Key);
    }

    [Fact]
    public void WindowsOwnProcessesGetANote_AndNoClose()
    {
        var rows = Sample(
            new() { P(10, "MsMpEng", 0, 200, session: 0) },
            new() { P(10, "MsMpEng", 8, 200, session: 0) });
        var defender = Assert.Single(rows);
        Assert.True(defender.IsSystem);
        Assert.Equal("action.load.note.defender", defender.NoteKey);
        Assert.False(LoadSampler.CanClose(defender));
    }

    [Fact]
    public void IgnoresIdleAndBrisk()
    {
        var rows = Sample(
            new() { P(0, "Idle", 0, 0, session: 0), P(7, "brisk-app", 0, 900) },
            new() { P(0, "Idle", 30, 0, session: 0), P(7, "brisk-app", 9, 900) });
        Assert.Empty(rows);
    }

    [Fact]
    public void AProcessThatStartedDuringTheWindow_CountsOnlyItsMemory()
    {
        var rows = Sample(new(), new() { P(3, "game", 50, 2000) });
        var game = Assert.Single(rows);
        Assert.Equal(0, game.CpuPercent);
    }

    [Theory]
    [InlineData("Discord", @"C:\Users\x\AppData\Local\Discord\Update.exe --processStart Discord.exe", "Discord", true)]
    [InlineData("Spotify", @"C:\Spotify\Spotify.exe /minimized", "Spotify", true)]
    [InlineData("Wispr Flow.lnk", @"C:\Users\x\Startup\Wispr Flow.lnk", "Wispr Flow", true)]
    [InlineData("Steam", @"C:\Steam\steam.exe -silent", "chrome", false)]
    public void StartupProcessMatch_FindsTheProcessAnEntryStarts(string name, string command, string process, bool match) =>
        Assert.Equal(match, StartupProcessMatch.Matches(new StartupEntry("HKCU", name, true, false, command), process));

    [Fact]
    public void StartupIdFor_PointsAtTheEntryThatStartsTheApp()
    {
        var load = new AppLoad("Discord", "Discord", 12, 1L << 30, false, null, null, new[] { 1 }, true);
        var entries = new[]
        {
            new StartupEntry("HKCU", "Discord", true, false, @"C:\Discord\Update.exe --processStart Discord.exe"),
        };
        Assert.Equal("startup:HKCU:Discord", LoadSampler.StartupIdFor(load, entries));
        Assert.Null(LoadSampler.StartupIdFor(load with { Key = "chrome" }, entries));
    }

    [Fact]
    public void Closer_AMissingProcessIsNotAnError()
    {
        var closer = new RealProcessCloser();
        Assert.Equal(0, closer.RequestClose(new[] { int.MaxValue }));
        closer.Kill(new[] { int.MaxValue });
    }

    [Theory]
    [InlineData("discord.exe", "Discord")]
    [InlineData("Code", "Code")]
    [InlineData("", "")]
    public void NamerFallback(string name, string expected) =>
        Assert.Equal(expected, RealAppNamer.Pretty(RealAppNamer.Stem(name)));

    [Fact]
    public void DevicePath_LeavesADosPathAlone() =>
        Assert.Equal(@"C:\x\y.exe", DevicePath.ToDos(@"C:\x\y.exe"));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~LoadSamplerTests"`
Expected: build FAILS — namespace `BriskEngine.Live` does not exist.

- [ ] **Step 3: Create `ProcessSnapshot.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BriskEngine.Live;

public sealed record ProcSample(int Pid, string Name, int SessionId, TimeSpan? Cpu,
    long PrivateBytes, bool HasWindow, string? ExePath);

public interface IProcessSnapshotProbe
{
    int ProcessorCount { get; }
    IReadOnlyList<ProcSample> Snapshot();
}

public sealed class RealProcessSnapshotProbe : IProcessSnapshotProbe
{
    public int ProcessorCount => Environment.ProcessorCount;

    public IReadOnlyList<ProcSample> Snapshot()
    {
        var list = new List<ProcSample>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    TimeSpan? cpu = null;
                    try { cpu = p.TotalProcessorTime; } catch (Exception) { }
                    long privateBytes = 0;
                    try { privateBytes = p.PrivateMemorySize64; } catch (Exception) { }
                    var window = false;
                    try { window = p.MainWindowHandle != IntPtr.Zero; } catch (Exception) { }
                    string? exe = null;
                    try { exe = p.MainModule?.FileName; } catch (Exception) { }
                    list.Add(new ProcSample(p.Id, p.ProcessName, p.SessionId, cpu, privateBytes, window, exe));
                }
                catch (Exception) { }   // the process exited while it was being read
            }
        }
        return list;
    }
}
```

- [ ] **Step 4: Create `AppNamer.cs`**

```csharp
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BriskEngine.Live;

public interface IAppNamer
{
    string Name(string processOrExeName, string? fullPath);
}

/// The file's own description ("Google Chrome") when the file can be read,
/// else the process name with a capital first letter.
public sealed class RealAppNamer : IAppNamer
{
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string Name(string processOrExeName, string? fullPath)
    {
        var fallback = Pretty(Stem(processOrExeName));
        if (fullPath is null) return fallback;
        return _cache.GetOrAdd(fullPath, path =>
        {
            try
            {
                var dos = DevicePath.ToDos(path) ?? path;
                var description = FileVersionInfo.GetVersionInfo(dos).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
            }
            catch (Exception) { }
            return fallback;
        });
    }

    internal static string Stem(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    internal static string Pretty(string stem) =>
        stem.Length == 0 ? stem : char.ToUpperInvariant(stem[0]) + stem[1..];
}

/// "\device\harddiskvolume3\x" → "C:\x" (the form Windows' usage record uses).
public static class DevicePath
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string deviceName, StringBuilder target, int max);

    public static string? ToDos(string path)
    {
        if (!path.StartsWith(@"\device\", StringComparison.OrdinalIgnoreCase)) return path;
        foreach (var drive in Environment.GetLogicalDrives())
        {
            var letter = drive.TrimEnd('\\');
            var target = new StringBuilder(260);
            if (QueryDosDevice(letter, target, target.Capacity) == 0) continue;
            var device = target.ToString();
            if (path.StartsWith(device + @"\", StringComparison.OrdinalIgnoreCase))
                return letter + path[device.Length..];
        }
        return null;
    }
}
```

- [ ] **Step 5: Create `LoadSampler.cs`**

```csharp
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
```

- [ ] **Step 6: Create `ProcessCloser.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BriskEngine.Live;

public interface IProcessCloser
{
    /// Asks each window to close, so the app can prompt to save. Returns how
    /// many were asked.
    int RequestClose(IReadOnlyList<int> pids);
    void Kill(IReadOnlyList<int> pids);
}

public sealed class RealProcessCloser : IProcessCloser
{
    public int RequestClose(IReadOnlyList<int> pids)
    {
        var asked = 0;
        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow()) asked++;
            }
            catch (Exception) { }
        }
        return asked;
    }

    public void Kill(IReadOnlyList<int> pids)
    {
        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(entireProcessTree: true);
            }
            catch (Exception) { }
        }
    }
}
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~LoadSamplerTests"`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add src/BriskEngine/Live/ProcessSnapshot.cs src/BriskEngine/Live/AppNamer.cs src/BriskEngine/Live/LoadSampler.cs src/BriskEngine/Live/ProcessCloser.cs src/BriskEngine.Tests/LoadSamplerTests.cs
git commit -m "feat(live): what is slowing the machine down right now" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 17: Receipt

**Files:**
- Create: `src/BriskEngine/Actions/Receipt.cs`
- Test: `src/BriskEngine.Tests/ReceiptTests.cs`

**Interfaces:**
- Consumes: `IReceiptRecorder` (Task 15), `BootRecord` (`When` is UTC), `IProcessSnapshotProbe`, `ProcSample`, `StartupProcessMatch`, `StartupEntryRule.TryParseId`, `IDiskInfoProbe.FreeBytes`, `ReadBackState`.
- Produces:
  - `public sealed record StartupRam(string RuleId, string Name, IReadOnlyList<string> Processes, long PrivateBytes)`
  - `public sealed record Receipt(string RunId, DateTime StartedUtc, DateTime? FinishedUtc, long FreeBytesBefore, long? FreeBytesAfter, long DeferredBytes, int StartupEnabledBefore, int? StartupEnabledAfter, IReadOnlyList<StartupRam> StartupRam, IReadOnlyList<string> StillStarted, bool StartupRamConfirmed, int? BootMsBefore, int? BootMsAfter, int PrivacyOff, int PrivacyIgnored, int? TelemetryBefore, int? TelemetryAfter)` with `long? FreedBytes` and `long? RamFreedBytes`
  - `public sealed class ReceiptStore` — `ReceiptStore(string directory)`, `Save`, `Load(runId)`, `Latest()`
  - `public static class ReceiptCalculator` — `int? MedianBootMs(IReadOnlyList<BootRecord> boots, DateTime beforeUtc)`, `Receipt Complete(Receipt r, IReadOnlyList<BootRecord> boots, IReadOnlyList<ProcSample> processes, int? telemetryNow, DateTime nowUtc)`, constants `BootsBefore` (5), `SettleAfterBoot` (3 min), `TelemetrySettle` (2 min)
  - `public sealed class ReceiptRecorder : IReceiptRecorder` — `ReceiptRecorder(DiagnosticContext ctx, StartupManager startup, IProcessSnapshotProbe processes, ReceiptStore store, Func<int?> telemetryCount, Func<DateTime>? now = null)`, `Receipt? Refresh(string runId)`

The three comparable numbers (spec): free space on the system drive before/after; boot time — median of up to five boots before the run against the first boot after it; RAM — the startup programs being disabled, measured while they run, claimed only after the next boot shows they did not start (one that did is named and left out).

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Actions;
using BriskEngine.Diagnostics;
using BriskEngine.Live;
using Xunit;

namespace BriskEngine.Tests;

public sealed class ReceiptTests : IDisposable
{
    private static readonly DateTime Run = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-receipt-").FullName;

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static BootRecord Boot(DateTime when, int seconds) =>
        new(when, seconds * 1000, null, Array.Empty<BootOffender>());

    private static Receipt Finished(params StartupRam[] ram) => new("r1", Run, Run.AddMinutes(1),
        100, 300, 0, 14, 3, ram, Array.Empty<string>(), false, 48_000, null, 9, 1, 3, null);

    [Fact]
    public void MedianBootMs_UsesUpToFiveBootsBeforeTheRun()
    {
        var boots = new[]
        {
            Boot(Run.AddDays(-1), 40), Boot(Run.AddDays(-2), 60), Boot(Run.AddDays(-3), 50),
            Boot(Run.AddHours(2), 20),   // after the run: ignored
        };
        Assert.Equal(50_000, ReceiptCalculator.MedianBootMs(boots, Run));
        Assert.Null(ReceiptCalculator.MedianBootMs(Array.Empty<BootRecord>(), Run));
    }

    [Fact]
    public void Complete_TakesTheFirstBootAfterTheRun()
    {
        var boots = new[] { Boot(Run.AddHours(5), 33), Boot(Run.AddHours(2), 31) };
        var r = ReceiptCalculator.Complete(Finished(), boots, Array.Empty<ProcSample>(), null, Run.AddHours(1));
        Assert.Null(r.BootMsAfter);   // "now" is before either boot
        r = ReceiptCalculator.Complete(Finished(), boots, Array.Empty<ProcSample>(), null, Run.AddHours(6));
        Assert.Equal(31_000, r.BootMsAfter);
    }

    [Fact]
    public void Complete_ConfirmsRamOnlyAfterTheBootHasSettled_AndNamesWhatStillStarted()
    {
        var ram = new[]
        {
            new StartupRam("startup:HKCU:Discord", "Discord", new[] { "Discord" }, 900),
            new StartupRam("startup:HKCU:Spotify", "Spotify", new[] { "Spotify" }, 300),
        };
        var boots = new[] { Boot(Run.AddHours(2), 31) };
        var running = new[] { new ProcSample(5, "Spotify", 1, null, 300, false, null) };

        var early = ReceiptCalculator.Complete(Finished(ram), boots, running, null, Run.AddHours(2).AddMinutes(1));
        Assert.False(early.StartupRamConfirmed);
        Assert.Null(early.RamFreedBytes);

        var settled = ReceiptCalculator.Complete(Finished(ram), boots, running, null, Run.AddHours(2).AddMinutes(4));
        Assert.True(settled.StartupRamConfirmed);
        Assert.Equal(new[] { "Spotify" }, settled.StillStarted);
        Assert.Equal(900, settled.RamFreedBytes);
    }

    [Fact]
    public void Complete_TelemetryAfterWaitsTwoMinutes()
    {
        var r = ReceiptCalculator.Complete(Finished(), Array.Empty<BootRecord>(), Array.Empty<ProcSample>(), 0, Run.AddMinutes(2));
        Assert.Null(r.TelemetryAfter);
        r = ReceiptCalculator.Complete(Finished(), Array.Empty<BootRecord>(), Array.Empty<ProcSample>(), 0, Run.AddMinutes(4));
        Assert.Equal(0, r.TelemetryAfter);
    }

    [Fact]
    public void FreedBytes_IsTheRealChangeInFreeSpace()
    {
        Assert.Equal(200, Finished().FreedBytes);
        Assert.Equal(0, (Finished() with { FreeBytesAfter = 50 }).FreedBytes);
    }

    [Fact]
    public void Recorder_BeforeAndAfter()
    {
        var ctx = TestContext.Empty();
        var disk = (FakeDisk)ctx.Disk;
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetString(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "Discord", @"C:\D\Update.exe --processStart Discord.exe");
        ((FakeEventLog)ctx.EventLog).Boots.Add(Boot(Run.AddDays(-1), 48));
        var startup = new StartupManager(reg, null);
        var procs = new FakeProcs { Now = new() { new ProcSample(9, "Discord", 1, null, 700, true, null) } };
        var store = new ReceiptStore(Path.Combine(_root, "receipts"));
        var recorder = new ReceiptRecorder(ctx, startup, procs, store, () => 3, () => Run);

        var discord = ActionPlanBuilderRow("startup:HKCU:Discord", "Discord");
        var plan = new ActionPlan(Run, new[] { new ActionGroup(ActionGroupKind.Speed, new[] { discord }) },
            new BriskEngine.Models.ScanResult(Array.Empty<BriskEngine.Models.TargetScanResult>()));
        disk.Free = 1_000;
        recorder.Before("r1", plan, ActionSelection.Default(plan));

        startup.SetEnabled("HKCU", "Discord", false);
        disk.Free = 21_000;
        recorder.After(new RunRecord("r1", Run, Run.AddMinutes(1), new[]
        {
            new RowResult("startup:HKCU:Discord", RowOutcome.Done, null, ReadBackState.Held, 0, 0),
            new RowResult("privacy.ads", RowOutcome.Done, null, ReadBackState.Held, 0, 0),
            new RowResult("privacy.error-reports", RowOutcome.Done, null, ReadBackState.WrittenButIgnored, 0, 0),
        }, Array.Empty<string>()));

        var r = store.Load("r1")!;
        Assert.Equal(20_000, r.FreedBytes);
        Assert.Equal(1, r.StartupEnabledBefore);
        Assert.Equal(0, r.StartupEnabledAfter);
        Assert.Equal(700, Assert.Single(r.StartupRam).PrivateBytes);
        Assert.Equal(48_000, r.BootMsBefore);
        Assert.Equal(2, r.PrivacyOff);
        Assert.Equal(1, r.PrivacyIgnored);
        Assert.Equal(3, r.TelemetryBefore);
        Assert.Equal("r1", store.Latest()!.RunId);
    }

    private static ActionItem ActionPlanBuilderRow(string id, string title) => new(id, ActionGroupKind.Speed,
        null, title, null, null, null, null, null, true, false, false, true, ActionLinkKind.Rules,
        new[] { id }, Array.Empty<ActionFile>());
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~ReceiptTests"`
Expected: build FAILS — `Receipt` does not exist.

- [ ] **Step 3: Implement `Receipt.cs`**

```csharp
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

    public ReceiptStore(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(directory);
    }

    public void Save(Receipt receipt) =>
        File.WriteAllText(Path.Combine(_dir, $"{receipt.RunId}.json"), JsonSerializer.Serialize(receipt));

    public Receipt? Load(string runId)
    {
        var path = Path.Combine(_dir, $"{runId}.json");
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path)); }
        catch (JsonException) { return null; }
    }

    public Receipt? Latest() => Directory.EnumerateFiles(_dir, "*.json")
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
            PrivacyOff = privacy.Count(r => r.Outcome is RowOutcome.Done or RowOutcome.PendingRestart),
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
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~ReceiptTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Actions/Receipt.cs src/BriskEngine.Tests/ReceiptTests.cs
git commit -m "feat(actions): the receipt — free space, boot time and startup RAM, before and after" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 18: Who this PC is talking to now

**Files:**
- Create: `src/BriskEngine/Live/TcpTable.cs`
- Create: `src/BriskEngine/Live/DnsCache.cs`
- Create: `src/BriskEngine/Live/OwnerTable.cs`
- Create: `src/BriskEngine/Live/ConnectionReader.cs`
- Create: `tools/build-asn-table.ps1`
- Create (generated by the script, committed): `src/BriskEngine/Live/asn-owners.tsv`
- Modify: `src/BriskEngine/BriskEngine.csproj` (embedded resource)
- Test: `src/BriskEngine.Tests/ConnectionReaderTests.cs`

**Interfaces:**
- Consumes: `IProcessSnapshotProbe`, `IAppNamer` (Task 16).
- Produces:
  - `public sealed record TcpConnection(int Pid, IPAddress Remote, int RemotePort)`, `public interface ITcpTableProbe { IReadOnlyList<TcpConnection> Established(); }`, `public sealed class RealTcpTableProbe`
  - `public interface IDnsCacheProbe { IReadOnlyList<(string Name, IPAddress Address)>? Entries(); }` (`null` = not read), `public sealed class RealDnsCacheProbe` (`RealDnsCacheProbe(Func<bool> isElevated)`)
  - `public sealed class OwnerTable` — `OwnerTable(string tsv)`, `static OwnerTable Default`, `static string? OwnerOfHost(string host)`, `string? OwnerOfAddress(IPAddress ip)`, `static bool IsLocal(IPAddress ip)`; `public static class TelemetryHosts { static bool IsTelemetry(string? host); }`
  - `public sealed record ConnectionRow(string App, string Owner, string? Host, int Count, bool IsTelemetry, bool IsLocal)`, `public sealed record ConnectionSnapshot(IReadOnlyList<ConnectionRow> Rows, bool DnsRead)` with `int? TelemetryCount` (`null` when the DNS cache was not read), `public sealed class ConnectionReader` — `ConnectionReader(ITcpTableProbe tcp, IDnsCacheProbe dns, OwnerTable owners, IProcessSnapshotProbe processes, IAppNamer namer)`, `ConnectionSnapshot Read()`, constants `UnknownOwner = "Unknown"`, `LocalOwner = "Local network"`
  - Test fakes `FakeTcp`, `FakeDns` (internal) — reused by Task 20.

Telemetry can only be told apart by host name, and the DNS cache needs elevation (measured), so without it the telemetry count is unknown rather than zero. brisk makes no network call: owners come from the cache and a bundled table.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using BriskEngine.Live;
using Xunit;

namespace BriskEngine.Tests;

sealed class FakeTcp : ITcpTableProbe
{
    public List<TcpConnection> Rows = new();
    public IReadOnlyList<TcpConnection> Established() => Rows;
}

sealed class FakeDns : IDnsCacheProbe
{
    public List<(string Name, IPAddress Address)>? Rows = new();
    public IReadOnlyList<(string Name, IPAddress Address)>? Entries() => Rows;
}

public sealed class ConnectionReaderTests
{
    private const string Tsv = "# test table\n1.0.0.0\t1.0.0.255\tCloudflare\n8.8.8.0\t8.8.8.255\tGoogle\n2001:4860::\t2001:4860:ffff:ffff:ffff:ffff:ffff:ffff\tGoogle\n";

    private static TcpConnection C(int pid, string ip) => new(pid, IPAddress.Parse(ip), 443);

    private static (ConnectionReader Reader, FakeTcp Tcp, FakeDns Dns) Make()
    {
        var tcp = new FakeTcp();
        var dns = new FakeDns();
        var procs = new FakeProcs
        {
            Now = new()
            {
                new ProcSample(10, "StartMenuExperienceHost", 1, null, 0, false, null),
                new ProcSample(20, "chrome", 1, null, 0, true, null),
                new ProcSample(30, "svchost", 0, null, 0, false, null),
            },
        };
        return (new ConnectionReader(tcp, dns, new OwnerTable(Tsv), procs, new FakeNamer()), tcp, dns);
    }

    [Theory]
    [InlineData("8.8.8.8", "Google")]
    [InlineData("1.0.0.1", "Cloudflare")]
    [InlineData("2001:4860:4860::8888", "Google")]
    [InlineData("9.9.9.9", null)]
    public void OwnerOfAddress(string ip, string? owner) =>
        Assert.Equal(owner, new OwnerTable(Tsv).OwnerOfAddress(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("www.bing.com", "Microsoft")]
    [InlineData("glb.cws.prod.dcat.dsp.trafficmanager.net", "Microsoft")]
    [InlineData("api.anthropic.com.", "Anthropic")]
    [InlineData("rr3---sn-4g5e6nsz.googlevideo.com", "Google")]
    [InlineData("example.org", null)]
    public void OwnerOfHost(string host, string? owner) => Assert.Equal(owner, OwnerTable.OwnerOfHost(host));

    [Theory]
    [InlineData("v10.events.data.microsoft.com", true)]
    [InlineData("v10.vortex-win.data.microsoft.com", true)]
    [InlineData("settings-win.data.microsoft.com", true)]
    [InlineData("www.microsoft.com", false)]
    [InlineData(null, false)]
    public void IsTelemetry(string? host, bool telemetry) => Assert.Equal(telemetry, TelemetryHosts.IsTelemetry(host));

    [Theory]
    [InlineData("192.168.1.10", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.20.0.1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("8.8.8.8", false)]
    public void IsLocal(string ip, bool local) => Assert.Equal(local, OwnerTable.IsLocal(IPAddress.Parse(ip)));

    [Fact]
    public void Read_NamesTheAppAndTheOwner_AndCountsConnections()
    {
        var (reader, tcp, dns) = Make();
        tcp.Rows.AddRange(new[] { C(10, "13.107.5.80"), C(10, "13.107.5.81"), C(20, "8.8.8.8") });
        dns.Rows!.Add(("www.bing.com", IPAddress.Parse("13.107.5.80")));
        dns.Rows.Add(("www.bing.com", IPAddress.Parse("13.107.5.81")));

        var snap = reader.Read();

        Assert.True(snap.DnsRead);
        var start = snap.Rows.Single(r => r.App == "StartMenuExperienceHost");
        Assert.Equal("Microsoft", start.Owner);
        Assert.Equal("www.bing.com", start.Host);
        Assert.Equal(2, start.Count);
        Assert.Equal("Google", snap.Rows.Single(r => r.App == "Chrome").Owner);
        Assert.Equal(0, snap.TelemetryCount);
    }

    [Fact]
    public void Read_TagsTelemetry_AndSortsItFirst()
    {
        var (reader, tcp, dns) = Make();
        tcp.Rows.AddRange(new[] { C(20, "8.8.8.8"), C(30, "20.42.65.92") });
        dns.Rows!.Add(("v10.events.data.microsoft.com", IPAddress.Parse("20.42.65.92")));

        var snap = reader.Read();

        Assert.True(snap.Rows[0].IsTelemetry);
        Assert.Equal(1, snap.TelemetryCount);
    }

    [Fact]
    public void Read_WithoutTheDnsCache_TelemetryIsUnknown_AndTheTableStillNamesOwners()
    {
        var (reader, tcp, dns) = Make();
        dns.Rows = null;
        tcp.Rows.Add(C(20, "8.8.8.8"));

        var snap = reader.Read();

        Assert.False(snap.DnsRead);
        Assert.Null(snap.TelemetryCount);
        Assert.Equal("Google", Assert.Single(snap.Rows).Owner);
    }

    [Fact]
    public void Read_LocalAndUnknown()
    {
        var (reader, tcp, _) = Make();
        tcp.Rows.AddRange(new[] { C(20, "192.168.1.1"), C(20, "9.9.9.9"), C(20, "127.0.0.1") });
        var rows = reader.Read().Rows;
        Assert.Contains(rows, r => r.IsLocal && r.Owner == ConnectionReader.LocalOwner);
        Assert.Contains(rows, r => r.Owner == ConnectionReader.UnknownOwner);
        Assert.Equal(2, rows.Sum(r => r.Count));   // loopback left out
    }

    [Fact]
    public void DefaultTable_IsBundled_AndKnowsTheBigNames()
    {
        Assert.Equal("Google", OwnerTable.Default.OwnerOfAddress(IPAddress.Parse("8.8.8.8")));
        Assert.Equal("Microsoft", OwnerTable.Default.OwnerOfAddress(IPAddress.Parse("13.107.42.14")));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~ConnectionReaderTests"`
Expected: build FAILS — `ConnectionReader` does not exist.

- [ ] **Step 3: Create `TcpTable.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;

namespace BriskEngine.Live;

public sealed record TcpConnection(int Pid, IPAddress Remote, int RemotePort);

public interface ITcpTableProbe
{
    IReadOnlyList<TcpConnection> Established();
}

public sealed class RealTcpTableProbe : ITcpTableProbe
{
    private const int AfInet = 2, AfInet6 = 23, TableOwnerPidAll = 5;
    private const uint StateEstablished = 5, InsufficientBuffer = 122;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order,
        int af, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct Row4 { public uint State, LocalAddr, LocalPort, RemoteAddr, RemotePort, OwningPid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Row6
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddr;
        public uint LocalScopeId, LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddr;
        public uint RemoteScopeId, RemotePort, State, OwningPid;
    }

    private static int Port(uint networkOrder) =>
        (int)(((networkOrder & 0xFF) << 8) | ((networkOrder >> 8) & 0xFF));

    public IReadOnlyList<TcpConnection> Established()
    {
        var list = new List<TcpConnection>();
        Read<Row4>(AfInet, list, r => r.State == StateEstablished
            ? new TcpConnection((int)r.OwningPid, new IPAddress(r.RemoteAddr), Port(r.RemotePort)) : null);
        Read<Row6>(AfInet6, list, r => r.State == StateEstablished
            ? new TcpConnection((int)r.OwningPid, new IPAddress(r.RemoteAddr, r.RemoteScopeId), Port(r.RemotePort)) : null);
        return list;
    }

    /// The table can grow between the size query and the read, so it retries.
    private static void Read<T>(int af, List<TcpConnection> into, Func<T, TcpConnection?> map) where T : struct
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, TableOwnerPidAll, 0);
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var rc = GetExtendedTcpTable(buffer, ref size, false, af, TableOwnerPidAll, 0);
                if (rc == InsufficientBuffer) continue;
                if (rc != 0) return;
                var count = Marshal.ReadInt32(buffer);
                var row = buffer + 4;
                var rowSize = Marshal.SizeOf<T>();
                for (var i = 0; i < count; i++, row += rowSize)
                    if (map(Marshal.PtrToStructure<T>(row)) is { } c) into.Add(c);
                return;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
}
```

- [ ] **Step 4: Create `DnsCache.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Management;
using System.Net;

namespace BriskEngine.Live;

public interface IDnsCacheProbe
{
    /// null: the cache was not read.
    IReadOnlyList<(string Name, IPAddress Address)>? Entries();
}

/// Without elevation the cache reads as EMPTY, not as refused (build 26200,
/// measured 2026-10-16) — so an unelevated read is reported as not read.
public sealed class RealDnsCacheProbe : IDnsCacheProbe
{
    private readonly Func<bool> _isElevated;

    public RealDnsCacheProbe(Func<bool> isElevated) { _isElevated = isElevated; }

    public IReadOnlyList<(string Name, IPAddress Address)>? Entries()
    {
        if (!_isElevated()) return null;
        try
        {
            var list = new List<(string Name, IPAddress Address)>();
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"\\.\root\StandardCimv2"),
                new ObjectQuery("SELECT Entry, Data, Type FROM MSFT_DNSClientCache"));
            foreach (ManagementObject o in searcher.Get())
            {
                using (o)
                {
                    var type = Convert.ToInt32(o["Type"]);
                    if (type is not (1 or 28)) continue;   // A, AAAA
                    if (o["Entry"] is string name && o["Data"] is string data
                        && IPAddress.TryParse(data, out var ip))
                        list.Add((name, ip));
                }
            }
            return list;
        }
        catch (Exception) { return null; }
    }
}
```

- [ ] **Step 5: Create `OwnerTable.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace BriskEngine.Live;

/// Who owns an address: a curated list of domain suffixes for host names, and
/// a bundled table of IP ranges (iptoasn.com, public domain) for addresses
/// with no name. No lookup ever leaves the machine.
public sealed class OwnerTable
{
    private static readonly (string Suffix, string Owner)[] Domains =
    {
        ("microsoft.com", "Microsoft"), ("msft.net", "Microsoft"), ("live.com", "Microsoft"),
        ("live.net", "Microsoft"), ("windows.com", "Microsoft"), ("windows.net", "Microsoft"),
        ("windowsupdate.com", "Microsoft"), ("msn.com", "Microsoft"), ("bing.com", "Microsoft"),
        ("bing.net", "Microsoft"), ("office.com", "Microsoft"), ("office.net", "Microsoft"),
        ("office365.com", "Microsoft"), ("outlook.com", "Microsoft"), ("skype.com", "Microsoft"),
        ("xbox.com", "Microsoft"), ("xboxlive.com", "Microsoft"), ("azure.com", "Microsoft"),
        ("azureedge.net", "Microsoft"), ("trafficmanager.net", "Microsoft"), ("msedge.net", "Microsoft"),
        ("microsoftonline.com", "Microsoft"), ("onedrive.com", "Microsoft"), ("sharepoint.com", "Microsoft"),
        ("github.com", "GitHub"), ("githubusercontent.com", "GitHub"),
        ("google.com", "Google"), ("googleapis.com", "Google"), ("gstatic.com", "Google"),
        ("googleusercontent.com", "Google"), ("googlevideo.com", "Google"), ("youtube.com", "Google"),
        ("ytimg.com", "Google"), ("doubleclick.net", "Google"), ("google-analytics.com", "Google"),
        ("gvt1.com", "Google"), ("gvt2.com", "Google"),
        ("amazonaws.com", "Amazon"), ("amazon.com", "Amazon"), ("cloudfront.net", "Amazon"),
        ("cloudflare.com", "Cloudflare"), ("cloudflare.net", "Cloudflare"), ("cloudflare-dns.com", "Cloudflare"),
        ("akamai.net", "Akamai"), ("akamaiedge.net", "Akamai"), ("akamaihd.net", "Akamai"),
        ("akamaized.net", "Akamai"), ("edgekey.net", "Akamai"), ("edgesuite.net", "Akamai"),
        ("facebook.com", "Meta"), ("facebook.net", "Meta"), ("fbcdn.net", "Meta"),
        ("instagram.com", "Meta"), ("whatsapp.net", "Meta"), ("whatsapp.com", "Meta"),
        ("apple.com", "Apple"), ("icloud.com", "Apple"), ("mzstatic.com", "Apple"),
        ("discord.com", "Discord"), ("discord.gg", "Discord"), ("discordapp.com", "Discord"),
        ("discordapp.net", "Discord"), ("discord.media", "Discord"),
        ("spotify.com", "Spotify"), ("scdn.co", "Spotify"), ("spotifycdn.com", "Spotify"),
        ("riotgames.com", "Riot Games"), ("riotcdn.net", "Riot Games"), ("leagueoflegends.com", "Riot Games"),
        ("steampowered.com", "Valve"), ("steamcommunity.com", "Valve"), ("steamserver.net", "Valve"),
        ("steamcontent.com", "Valve"), ("valvesoftware.com", "Valve"),
        ("anthropic.com", "Anthropic"), ("claude.ai", "Anthropic"),
        ("openai.com", "OpenAI"), ("chatgpt.com", "OpenAI"), ("oaiusercontent.com", "OpenAI"),
        ("epicgames.com", "Epic Games"), ("unrealengine.com", "Epic Games"),
        ("nvidia.com", "NVIDIA"), ("geforce.com", "NVIDIA"),
        ("adobe.com", "Adobe"), ("adobe.io", "Adobe"),
        ("dropbox.com", "Dropbox"), ("dropboxapi.com", "Dropbox"),
        ("zoom.us", "Zoom"), ("slack.com", "Slack"), ("slack-edge.com", "Slack"),
        ("telegram.org", "Telegram"), ("twitch.tv", "Twitch"),
        ("x.com", "X"), ("twitter.com", "X"), ("twimg.com", "X"),
        ("tiktok.com", "ByteDance"), ("tiktokcdn.com", "ByteDance"), ("byteoversea.com", "ByteDance"),
        ("docker.com", "Docker"), ("docker.io", "Docker"), ("npmjs.org", "npm"), ("npmjs.com", "npm"),
    };

    private readonly UInt128[] _starts4, _ends4, _starts6, _ends6;
    private readonly string[] _owners4, _owners6;

    public OwnerTable(string tsv)
    {
        var v4 = new List<(UInt128 Start, UInt128 End, string Owner)>();
        var v6 = new List<(UInt128 Start, UInt128 End, string Owner)>();
        foreach (var raw in tsv.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split('\t');
            if (f.Length < 3 || !IPAddress.TryParse(f[0], out var a) || !IPAddress.TryParse(f[1], out var b)) continue;
            var start = ToNumber(a, out var isV6);
            var end = ToNumber(b, out _);
            (isV6 ? v6 : v4).Add((start, end, f[2]));
        }
        v4.Sort((x, y) => x.Start.CompareTo(y.Start));
        v6.Sort((x, y) => x.Start.CompareTo(y.Start));
        _starts4 = v4.Select(x => x.Start).ToArray(); _ends4 = v4.Select(x => x.End).ToArray(); _owners4 = v4.Select(x => x.Owner).ToArray();
        _starts6 = v6.Select(x => x.Start).ToArray(); _ends6 = v6.Select(x => x.End).ToArray(); _owners6 = v6.Select(x => x.Owner).ToArray();
    }

    private static readonly Lazy<OwnerTable> DefaultTable = new(() => new OwnerTable(LoadResource()));
    public static OwnerTable Default => DefaultTable.Value;

    private static string LoadResource()
    {
        using var stream = typeof(OwnerTable).Assembly.GetManifestResourceStream("BriskEngine.Live.asn-owners.tsv");
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string? OwnerOfHost(string host)
    {
        var h = host.TrimEnd('.').ToLowerInvariant();
        foreach (var (suffix, owner) in Domains)
            if (h == suffix || h.EndsWith("." + suffix, StringComparison.Ordinal)) return owner;
        return null;
    }

    public string? OwnerOfAddress(IPAddress ip)
    {
        var key = ToNumber(ip, out var isV6);
        return isV6 ? Find(_starts6, _ends6, _owners6, key) : Find(_starts4, _ends4, _owners4, key);
    }

    private static string? Find(UInt128[] starts, UInt128[] ends, string[] owners, UInt128 key)
    {
        var i = Array.BinarySearch(starts, key);
        if (i < 0) i = ~i - 1;
        return i >= 0 && key <= ends[i] ? owners[i] : null;
    }

    internal static UInt128 ToNumber(IPAddress ip, out bool isV6)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var bytes = ip.GetAddressBytes();
        isV6 = bytes.Length == 16;
        UInt128 n = 0;
        foreach (var b in bytes) n = (n << 8) | b;
        return n;
    }

    public static bool IsLocal(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }
        return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal;
    }
}

public static class TelemetryHosts
{
    private static readonly string[] Suffixes =
    {
        "events.data.microsoft.com", "telemetry.microsoft.com", "vortex.data.microsoft.com",
        "vortex-win.data.microsoft.com", "settings-win.data.microsoft.com", "watson.microsoft.com",
        "events.data.msn.com",
    };

    public static bool IsTelemetry(string? host)
    {
        if (host is null) return false;
        var h = host.TrimEnd('.').ToLowerInvariant();
        return Suffixes.Any(s => h == s || h.EndsWith("." + s, StringComparison.Ordinal));
    }
}
```

- [ ] **Step 6: Create `ConnectionReader.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace BriskEngine.Live;

public sealed record ConnectionRow(string App, string Owner, string? Host, int Count,
    bool IsTelemetry, bool IsLocal);

public sealed record ConnectionSnapshot(IReadOnlyList<ConnectionRow> Rows, bool DnsRead)
{
    /// null when the DNS cache was not read: without host names telemetry
    /// cannot be told apart, so the count is unknown, not zero.
    public int? TelemetryCount => DnsRead ? Rows.Where(r => r.IsTelemetry).Sum(r => r.Count) : null;
}

public sealed class ConnectionReader
{
    public const string UnknownOwner = "Unknown";
    public const string LocalOwner = "Local network";

    private readonly ITcpTableProbe _tcp;
    private readonly IDnsCacheProbe _dns;
    private readonly OwnerTable _owners;
    private readonly IProcessSnapshotProbe _processes;
    private readonly IAppNamer _namer;

    public ConnectionReader(ITcpTableProbe tcp, IDnsCacheProbe dns, OwnerTable owners,
        IProcessSnapshotProbe processes, IAppNamer namer)
    {
        _tcp = tcp;
        _dns = dns;
        _owners = owners;
        _processes = processes;
        _namer = namer;
    }

    private static IPAddress Normal(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    public ConnectionSnapshot Read()
    {
        var dns = _dns.Entries();
        var hosts = new Dictionary<IPAddress, string>();
        if (dns is not null)
            foreach (var (name, ip) in dns) hosts.TryAdd(Normal(ip), name);
        var processes = _processes.Snapshot().GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First());

        var rows = _tcp.Established()
            .Where(c => !IPAddress.IsLoopback(Normal(c.Remote)))
            .Select(c =>
            {
                var remote = Normal(c.Remote);
                var app = processes.TryGetValue(c.Pid, out var p) ? _namer.Name(p.Name, p.ExePath) : $"PID {c.Pid}";
                var local = OwnerTable.IsLocal(remote);
                hosts.TryGetValue(remote, out var host);
                var owner = local ? LocalOwner
                    : (host is null ? null : OwnerTable.OwnerOfHost(host))
                      ?? _owners.OwnerOfAddress(remote) ?? UnknownOwner;
                return (App: app, Owner: owner, Host: host, Telemetry: TelemetryHosts.IsTelemetry(host), Local: local);
            })
            .GroupBy(x => (x.App, x.Owner, x.Telemetry, x.Local))
            .Select(g => new ConnectionRow(g.Key.App, g.Key.Owner,
                g.Select(x => x.Host).FirstOrDefault(h => h is not null),
                g.Count(), g.Key.Telemetry, g.Key.Local))
            .OrderByDescending(r => r.IsTelemetry)
            .ThenByDescending(r => r.Count)
            .ToList();

        return new ConnectionSnapshot(rows, dns is not null);
    }
}
```

- [ ] **Step 7: Create `tools/build-asn-table.ps1`**

```powershell
<#
.SYNOPSIS
  Rebuilds src/BriskEngine/Live/asn-owners.tsv from iptoasn.com.

.DESCRIPTION
  Run by a developer, never by brisk: brisk makes no network connection. The
  source is iptoasn.com's ip2asn tables (PDDL 1.0, public domain). Only the
  networks of companies people recognise are kept, so the file stays small.
  Re-run now and then and commit the result.
#>
[CmdletBinding()]
param([string] $Out = '')

$ErrorActionPreference = 'Stop'
if (-not $Out) { $Out = Join-Path $PSScriptRoot '..\src\BriskEngine\Live\asn-owners.tsv' }

# AS description prefix -> the name brisk shows.
$owners = [ordered]@{
    'MICROSOFT' = 'Microsoft'; 'GOOGLE' = 'Google'; 'AMAZON' = 'Amazon'; 'CLOUDFLARENET' = 'Cloudflare'
    'AKAMAI' = 'Akamai'; 'FACEBOOK' = 'Meta'; 'APPLE' = 'Apple'; 'FASTLY' = 'Fastly'
    'RIOT' = 'Riot Games'; 'VALVE' = 'Valve'; 'SPOTIFY' = 'Spotify'; 'NETFLIX' = 'Netflix'
    'TWITTER' = 'X'; 'ZOOM' = 'Zoom'; 'ORACLE' = 'Oracle'; 'GITHUB' = 'GitHub'
    'ANTHROPIC' = 'Anthropic'; 'OPENAI' = 'OpenAI'; 'DISCORD' = 'Discord'; 'NVIDIA' = 'NVIDIA'
    'ADOBE' = 'Adobe'; 'DROPBOX' = 'Dropbox'; 'BYTEDANCE' = 'ByteDance'; 'TENCENT' = 'Tencent'
    'ALIBABA' = 'Alibaba'
}
$prefix = [regex]('^(' + (($owners.Keys | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')')

$tmp = Join-Path ([IO.Path]::GetTempPath()) 'brisk-asn'
New-Item -ItemType Directory -Force $tmp | Out-Null
$rows = New-Object System.Collections.Generic.List[string]

foreach ($name in 'ip2asn-v4.tsv.gz', 'ip2asn-v6.tsv.gz') {
    $gz = Join-Path $tmp $name
    Invoke-WebRequest "https://iptoasn.com/data/$name" -OutFile $gz -UseBasicParsing
    $file = [IO.File]::OpenRead($gz)
    $reader = [IO.StreamReader]::new([IO.Compression.GZipStream]::new($file, [IO.Compression.CompressionMode]::Decompress))
    try {
        while ($null -ne ($line = $reader.ReadLine())) {
            $f = $line.Split("`t")
            if ($f.Count -lt 5 -or $f[2] -eq '0') { continue }
            $m = $prefix.Match($f[4].ToUpperInvariant())
            if (-not $m.Success) { continue }
            $rows.Add("$($f[0])`t$($f[1])`t$($owners[$m.Groups[1].Value])")
        }
    } finally { $reader.Dispose() }
}

$header = "# IP ranges of well-known companies. Source: iptoasn.com (PDDL 1.0, public domain). " +
          "Built by tools/build-asn-table.ps1 on $(Get-Date -Format yyyy-MM-dd)."
[IO.File]::WriteAllLines([IO.Path]::GetFullPath($Out), [string[]](@($header) + $rows))
'{0} ranges written to {1}' -f $rows.Count, $Out
```

- [ ] **Step 8: Generate the table and embed it**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-asn-table.ps1`
Expected: a line like `NNNNN ranges written to ...asn-owners.tsv`; the file starts with the `# IP ranges ...` header. Check its size with `(Get-Item src/BriskEngine/Live/asn-owners.tsv).Length`; above 3 MB, report it before committing.

Add to `src/BriskEngine/BriskEngine.csproj`, before `</Project>`:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Live\asn-owners.tsv" LogicalName="BriskEngine.Live.asn-owners.tsv" />
  </ItemGroup>
```

- [ ] **Step 9: Run the tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~ConnectionReaderTests"`
Expected: all pass, including `DefaultTable_IsBundled_AndKnowsTheBigNames`.

- [ ] **Step 10: Commit**

```bash
git add src/BriskEngine/Live/TcpTable.cs src/BriskEngine/Live/DnsCache.cs src/BriskEngine/Live/OwnerTable.cs src/BriskEngine/Live/ConnectionReader.cs src/BriskEngine/Live/asn-owners.tsv src/BriskEngine/BriskEngine.csproj tools/build-asn-table.ps1 src/BriskEngine.Tests/ConnectionReaderTests.cs
git commit -m "feat(live): who this PC is talking to now, with offline owner lookup" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 19: Who sent what in the last 30 days

**Files:**
- Create: `src/BriskEngine/Live/NetworkUsageReader.cs`
- Test: `src/BriskEngine.Tests/NetworkUsageReaderTests.cs`

**Interfaces:**
- Consumes: `IProcessRunner`, `IAppNamer`, `StartupManager.FriendlyPackageName` (internal, same assembly).
- Produces: `public sealed record UsageRow(string App, long BytesSent, long BytesReceived)`; `public interface INetworkUsageProbe { IReadOnlyList<(string Id, long Sent, long Received)>? Usage(int days); }` (`null` = not read); `public sealed class RealNetworkUsageProbe` (`RealNetworkUsageProbe(IProcessRunner runner)`, `internal const string Script`, `internal static IReadOnlyList<(string Id, long Sent, long Received)>? Parse(string json)`); `public sealed class NetworkUsageReader` (`NetworkUsageReader(INetworkUsageProbe probe, IAppNamer namer)`, `IReadOnlyList<UsageRow>? Top(int days = 30, int take = 10)`, `const string WindowsSystem = "Windows (system)"`); test fake `FakeUsage` (internal).

The script sums every connection profile per attribution id and always prints a JSON array (`ConvertTo-Json -InputObject @(...)`). It avoids `$profile`, which is a PowerShell automatic variable.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BriskEngine.Live;
using Xunit;

namespace BriskEngine.Tests;

sealed class FakeUsage : INetworkUsageProbe
{
    public List<(string Id, long Sent, long Received)>? Rows = new();
    public IReadOnlyList<(string Id, long Sent, long Received)>? Usage(int days) => Rows;
}

public sealed class NetworkUsageReaderTests
{
    [Fact]
    public void Parse_AnArray()
    {
        var rows = RealNetworkUsageProbe.Parse("""[{"id":"a","sent":10,"recv":20},{"id":"","sent":1.0E10,"recv":0}]""")!;
        Assert.Equal(2, rows.Count);
        Assert.Equal(("a", 10L, 20L), rows[0]);
        Assert.Equal(10_000_000_000L, rows[1].Sent);
    }

    [Fact]
    public void Parse_ASingleObject_AnEmptyOutput_AndGarbage()
    {
        Assert.Single(RealNetworkUsageProbe.Parse("""{"id":null,"sent":5,"recv":6}""")!);
        Assert.Empty(RealNetworkUsageProbe.Parse("  ")!);
        Assert.Null(RealNetworkUsageProbe.Parse("Add-Type : something failed"));
    }

    [Fact]
    public void RealProbe_SendsAnEncodedScriptForTheRequestedDays()
    {
        string? args = null;
        var probe = new RealNetworkUsageProbe(new ScriptedRunner((_, a) => { args = a; return (0, "[]"); }));

        Assert.Empty(probe.Usage(30)!);

        var encoded = args!.Split(' ').Last();
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
        Assert.Contains("AddDays(-30)", script);
        Assert.DoesNotContain("$profile", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-EncodedCommand", args);
    }

    [Fact]
    public void RealProbe_AFailingRunIsNotRead()
    {
        var probe = new RealNetworkUsageProbe(new ScriptedRunner((_, _) => (1, "")));
        Assert.Null(probe.Usage(30));
    }

    [Fact]
    public void Top_NamesAppsMergesVersionsAndSortsBySent()
    {
        var usage = new FakeUsage
        {
            Rows = new()
            {
                (@"\device\harddiskvolume3\users\x\appdata\local\discord\app-1.0.9260\discord.exe", 100, 1),
                (@"\device\harddiskvolume3\users\x\appdata\local\discord\app-1.0.9261\discord.exe", 50, 1),
                ("5319275A.WhatsAppDesktop_cv1g1gvanyjgm", 500, 9),
                ("", 1_000, 2),
            },
        };

        var rows = new NetworkUsageReader(usage, new FakeNamer()).Top()!;

        Assert.Equal(new[] { NetworkUsageReader.WindowsSystem, "WhatsAppDesktop", "Discord" }, rows.Select(r => r.App));
        Assert.Equal(150, rows.Single(r => r.App == "Discord").BytesSent);
    }

    [Fact]
    public void Top_TakesTheLimit_AndPassesUnreadThrough()
    {
        var usage = new FakeUsage { Rows = Enumerable.Range(1, 15).Select(i => ($"app{i}", (long)i, 0L)).ToList() };
        Assert.Equal(10, new NetworkUsageReader(usage, new FakeNamer()).Top()!.Count);
        Assert.Null(new NetworkUsageReader(new FakeUsage { Rows = null }, new FakeNamer()).Top());
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~NetworkUsageReaderTests"`
Expected: build FAILS — `NetworkUsageReader` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;

namespace BriskEngine.Live;

public sealed record UsageRow(string App, long BytesSent, long BytesReceived);

public interface INetworkUsageProbe
{
    /// null: the record could not be read.
    IReadOnlyList<(string Id, long Sent, long Received)>? Usage(int days);
}

/// The record behind Settings > Network > Data usage, read through
/// PowerShell: calling the WinRT API from .NET directly would need a
/// Windows-versioned target framework and ~25 MB of projection assembly.
public sealed class RealNetworkUsageProbe : INetworkUsageProbe
{
    internal const string Script = """
        $ErrorActionPreference = 'Stop'
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
        [void][Windows.Networking.Connectivity.NetworkInformation, Windows.Networking.Connectivity, ContentType = WindowsRuntime]
        $listType = [System.Collections.Generic.IReadOnlyList[Windows.Networking.Connectivity.AttributedNetworkUsage]]
        $end = [DateTimeOffset]::Now
        $start = $end.AddDays(-__DAYS__)
        $states = New-Object Windows.Networking.Connectivity.NetworkUsageStates
        $sum = @{}
        foreach ($p in [Windows.Networking.Connectivity.NetworkInformation]::GetConnectionProfiles()) {
          try {
            $task = $asTask.MakeGenericMethod($listType).Invoke($null, @($p.GetAttributedNetworkUsageAsync($start, $end, $states)))
            if (-not $task.Wait(20000)) { continue }
            foreach ($u in $task.Result) {
              $k = [string]$u.AttributionId
              if (-not $sum.ContainsKey($k)) { $sum[$k] = @([double]0, [double]0) }
              $sum[$k][0] += [double]$u.BytesSent
              $sum[$k][1] += [double]$u.BytesReceived
            }
          } catch { }
        }
        $rows = @($sum.GetEnumerator() | ForEach-Object { [pscustomobject]@{ id = $_.Key; sent = $_.Value[0]; recv = $_.Value[1] } })
        ConvertTo-Json -InputObject $rows -Compress
        """;

    private readonly IProcessRunner _runner;

    public RealNetworkUsageProbe(IProcessRunner runner) { _runner = runner; }

    public IReadOnlyList<(string Id, long Sent, long Received)>? Usage(int days)
    {
        var script = Script.Replace("__DAYS__", days.ToString(CultureInfo.InvariantCulture));
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        try
        {
            var (exit, output) = _runner.Run("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}");
            return exit == 0 ? Parse(output) : null;
        }
        catch (Exception) { return null; }
    }

    internal static IReadOnlyList<(string Id, long Sent, long Received)>? Parse(string json)
    {
        json = json.Trim();
        if (json.Length == 0) return Array.Empty<(string, long, long)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().ToList()
                : new List<JsonElement> { doc.RootElement };
            return items.Select(e => (
                    Id: e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : "",
                    Sent: (long)e.GetProperty("sent").GetDouble(),
                    Received: (long)e.GetProperty("recv").GetDouble()))
                .ToList();
        }
        catch (Exception) { return null; }
    }
}

public sealed class NetworkUsageReader
{
    public const string WindowsSystem = "Windows (system)";

    private readonly INetworkUsageProbe _probe;
    private readonly IAppNamer _namer;

    public NetworkUsageReader(INetworkUsageProbe probe, IAppNamer namer)
    {
        _probe = probe;
        _namer = namer;
    }

    public IReadOnlyList<UsageRow>? Top(int days = 30, int take = 10)
    {
        var raw = _probe.Usage(days);
        if (raw is null) return null;
        return raw.Select(r => (App: NameOf(r.Id), r.Sent, r.Received))
            .GroupBy(r => r.App, StringComparer.OrdinalIgnoreCase)
            .Select(g => new UsageRow(g.Key, g.Sum(x => x.Sent), g.Sum(x => x.Received)))
            .OrderByDescending(r => r.BytesSent)
            .Take(take)
            .ToList();
    }

    /// An exe's device path, a Store package family name, or empty for
    /// traffic Windows attributes to itself.
    private string NameOf(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return WindowsSystem;
        if (id.Contains('\\')) return _namer.Name(Path.GetFileName(id), id);
        return StartupManager.FriendlyPackageName(id);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~NetworkUsageReaderTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Live/NetworkUsageReader.cs src/BriskEngine.Tests/NetworkUsageReaderTests.cs
git commit -m "feat(live): who sent what in the last 30 days, from Windows' own record" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 20: Real wiring and CLI verbs

**Files:**
- Create: `src/BriskEngine/Actions/ActionEngine.cs`
- Modify: `src/Brisk.Cli/CliParser.cs` (`CliCommand`, `Verbs`, `--group`)
- Modify: `src/Brisk.Cli/Program.cs` (routing, `Scan`, help, new verb methods)
- Test: `src/BriskEngine.Tests/CliActionTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - `public sealed class ActionEngine` — `static ActionEngine CreateReal(DiagnosticContext ctx, IProcessRunner runner, Func<bool> isElevated)`, properties `Rules`, `Startup`, `Builder`, `Runner`, `Receipts`, `Runs`, `Deferred`, `Cleaner`, `Load`, `Connections`, `Usage`, `Closer`; `ActionPlan BuildPlan(ScanResult scan)`; `IReadOnlyList<CleanReport> ProcessDeferred()`
  - `CliCommand(..., string? Error = null, string? Group = null)`; verbs `run`, `undo`, `receipt`, `live`; option `--group space|speed|privacy`
  - `Program.PrintPlan(ActionPlan plan, TextWriter o)`, `Program.ResultWord(ActionItem? item, RowResult r)`, `Program.PrintReceiptLines(Receipt? r, IReadOnlyList<string> deferredApps, TextWriter o)`
  - CLI: `brisk scan` ends with the three groups; `brisk run [--group g] [--all]` prints the plan and changes nothing without `--yes`; `brisk undo` reverses the latest run; `brisk receipt` prints the latest receipt; `brisk live` prints current load, connections and 30-day usage.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.IO;
using BriskEngine.Actions;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Brisk.Cli;
using Xunit;

namespace BriskEngine.Tests;

public sealed class CliActionTests
{
    [Fact]
    public void Parser_RunWithGroupAndYes()
    {
        var cmd = CliParser.Parse(new[] { "run", "--group", "speed", "--yes" });
        Assert.Equal("run", cmd.Verb);
        Assert.Equal("speed", cmd.Group);
        Assert.True(cmd.Yes);
    }

    [Theory]
    [InlineData("undo")]
    [InlineData("receipt")]
    [InlineData("live")]
    public void Parser_NewVerbs(string verb) => Assert.Equal(verb, CliParser.Parse(new[] { verb }).Verb);

    [Fact]
    public void Parser_UnknownGroupIsAnError() =>
        Assert.Equal("error", CliParser.Parse(new[] { "run", "--group", "games" }).Verb);

    private static ActionItem Item(string id, long? bytes = null, params ActionFile[] files) =>
        ActionPlanBuilder.ItemFrom(ActionCatalog.Find(id), bytes, files: files);

    [Fact]
    public void PrintPlan_ShowsTicksSizesCostsAndFiles()
    {
        var plan = new ActionPlan(DateTime.UtcNow, new[]
        {
            new ActionGroup(ActionGroupKind.Space, new[]
            {
                Item("space.caches", 6L << 30),
                Item("space.shaders", 2L << 30),
                Item("space.downloads", 100L << 20, new ActionFile(@"C:\Users\x\Downloads\setup.exe", 100L << 20, null)),
            }),
        }, new ScanResult(Array.Empty<TargetScanResult>()));
        var o = new StringWriter();

        Program.PrintPlan(plan, o);

        var text = o.ToString();
        Assert.Contains("Free up space", text);
        Assert.Contains("[x] Caches and temporary files  6.0 GB", text);
        Assert.Contains("[ ] Game shader caches  2.0 GB  — Games may stutter on first launch.", text);
        Assert.Contains("setup.exe  100 MB", text);
    }

    [Theory]
    [InlineData("privacy.ads", RowOutcome.Done, ReadBackState.Held, "Off ✓")]
    [InlineData("speed.animations", RowOutcome.Done, ReadBackState.Held, "Done ✓")]
    [InlineData("privacy.error-reports", RowOutcome.Done, ReadBackState.WrittenButIgnored, "Windows ignored it")]
    [InlineData("privacy.copilot", RowOutcome.Done, ReadBackState.WrittenButUnverified, "Written")]
    [InlineData("speed.animations", RowOutcome.Done, ReadBackState.Reverted, "Turned back on")]
    [InlineData("speed.gpu-scheduling", RowOutcome.PendingRestart, null, "After restart")]
    public void ResultWord(string id, RowOutcome outcome, ReadBackState? readBack, string expected)
    {
        var item = Item(id);
        Assert.StartsWith(expected, Program.ResultWord(item, new RowResult(id, outcome, null, readBack, 0, 0)));
    }

    [Fact]
    public void ResultWord_SkippedAndFailedCarryTheReason()
    {
        Assert.Equal("Needs administrator", Program.ResultWord(null,
            new RowResult("x", RowOutcome.Skipped, ActionRunner.NeedsAdmin, null, 0, 0)));
        Assert.Equal("Didn't work: boom", Program.ResultWord(null,
            new RowResult("x", RowOutcome.Failed, "boom", null, 0, 0)));
    }

    [Fact]
    public void PrintReceiptLines_PrintsOnlyWhatIsKnown()
    {
        var r = new Receipt("r", DateTime.UtcNow, DateTime.UtcNow, 0, 20L << 30, 2L << 30, 14, 3,
            Array.Empty<StartupRam>(), Array.Empty<string>(), false, 48_000, null, 9, 1, null, null);
        var o = new StringWriter();

        Program.PrintReceiptLines(r, new[] { "Chrome" }, o);

        var text = o.ToString();
        Assert.Contains("20.0 GB freed", text);
        Assert.Contains("2.0 GB more when Chrome closes", text);
        Assert.Contains("Startup apps 14 → 3", text);
        Assert.Contains("9 settings off · Windows ignored 1", text);
        Assert.DoesNotContain("Startup 48", text);   // no boot after the run yet
        Assert.DoesNotContain("RAM freed", text);
    }

    [Fact]
    public void ActionEngine_CreateReal_Wires()
    {
        var engine = ActionEngine.CreateReal(TestContext.Empty(), new FakeRunner(), () => false);
        Assert.NotNull(engine.Runner);
        Assert.NotNull(engine.Builder);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~CliActionTests"`
Expected: build FAILS — `ActionEngine` does not exist; `CliCommand.Group` does not exist.

- [ ] **Step 3: Create `ActionEngine.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using BriskEngine.Actions.Rules;
using BriskEngine.Actions.Switches;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Live;
using BriskEngine.Logging;
using BriskEngine.Models;
using BriskEngine.Safety;

namespace BriskEngine.Actions;

/// The real wiring, in one place, for the CLI now and the GUI later.
public sealed class ActionEngine
{
    public DiagnosticContext Context { get; private init; } = null!;
    public ActionRules Rules { get; private init; } = null!;
    public StartupManager Startup { get; private init; } = null!;
    public ActionPlanBuilder Builder { get; private init; } = null!;
    public ActionRunner Runner { get; private init; } = null!;
    public ReceiptRecorder Receipts { get; private init; } = null!;
    public RunStore Runs { get; private init; } = null!;
    public DeferredCleanQueue Deferred { get; private init; } = null!;
    public CleanRunner Cleaner { get; private init; } = null!;
    public LoadSampler Load { get; private init; } = null!;
    public ConnectionReader Connections { get; private init; } = null!;
    public NetworkUsageReader Usage { get; private init; } = null!;
    public IProcessCloser Closer { get; private init; } = null!;

    private ActionEngine() { }

    public static ActionEngine CreateReal(DiagnosticContext ctx, IProcessRunner runner, Func<bool> isElevated)
    {
        var data = ctx.DataDirectory;
        var log = new ActionLog(Path.Combine(data, "action-log.jsonl"));
        var startup = new StartupManager(ctx.Registry, log, ctx.EventLog, ctx.Files);
        var features = new RealOptionalFeatureProbe();
        var hiberfil = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "hiberfil.sys");
        var rules = new ActionRules(new ActionProbes(runner, new RealUiEffectsProbe(),
            new RealPowerOverlayProbe(), features, new RealScheduledTaskProbe(runner), startup,
            () => File.Exists(hiberfil)));
        var processes = new RealProcessSnapshotProbe();
        var namer = new RealAppNamer();
        var connections = new ConnectionReader(new RealTcpTableProbe(), new RealDnsCacheProbe(isElevated),
            OwnerTable.Default, processes, namer);
        var cleaner = new CleanRunner(new SafetyValidator(), new WindowsRecycler(), log, runner,
            isElevated, new DeleteLockProbe());
        var deferred = new DeferredCleanQueue(Path.Combine(data, "deferred-clean.json"));
        var runs = new RunStore(Path.Combine(data, "runs"));
        var receipts = new ReceiptRecorder(ctx, startup, processes,
            new ReceiptStore(Path.Combine(data, "receipts")), () => connections.Read().TelemetryCount);
        var journal = new FixJournal(Path.Combine(data, "fix-journal.jsonl"));

        return new ActionEngine
        {
            Context = ctx,
            Rules = rules,
            Startup = startup,
            Builder = new ActionPlanBuilder(ctx, rules, startup, new RealSpaceProbe(runner), features),
            Runner = new ActionRunner(ctx, rules, journal, cleaner, deferred, runs, isElevated, receipts: receipts),
            Receipts = receipts,
            Runs = runs,
            Deferred = deferred,
            Cleaner = cleaner,
            Load = new LoadSampler(processes, namer),
            Connections = connections,
            Usage = new NetworkUsageReader(new RealNetworkUsageProbe(runner), namer),
            Closer = new RealProcessCloser(),
        };
    }

    public ActionPlan BuildPlan(ScanResult scan) => Builder.Build(scan, DateTime.UtcNow);

    /// Cleans the queued caches whose apps have closed since.
    public IReadOnlyList<CleanReport> ProcessDeferred() => Deferred.ProcessDue(DateTime.UtcNow,
        Context.RunningApps,
        t => new Scanner(new[] { t }, Context.RunningApps, new DeleteLockProbe()).Scan().Targets[0],
        s => Cleaner.Clean(s, dryRun: false, permanent: true));
}
```

(`SwitchCatalog` is not referenced here; if the compiler flags the `using BriskEngine.Actions.Switches;` line as unused under `TreatWarningsAsErrors`, delete it — `RealScheduledTaskProbe` lives in that namespace, so it is normally used.)

- [ ] **Step 4: Change `CliParser.cs`**

```csharp
public sealed record CliCommand(string Verb, string? RuleId = null, string? Level = null,
    bool Json = false, bool Yes = false, bool All = false, bool Undo = false,
    bool Keep = false, string? Target = null, string? Error = null, string? Group = null);
```

```csharp
    private static readonly HashSet<string> Verbs =
        new() { "scan", "fix", "clean", "targets", "rules", "version", "report",
                "run", "undo", "receipt", "live" };
    private static readonly HashSet<string> Groups = new() { "space", "speed", "privacy" };
```

and in the switch, next to `--target`:

```csharp
                case "--group" when i + 1 < args.Length && Groups.Contains(args[i + 1]):
                    cmd = cmd with { Group = args[++i] }; break;
```

- [ ] **Step 5: Change `Program.cs`**

Add `using System.Globalization;`, `using BriskEngine.Actions;`, `using BriskEngine.Live;` at the top.

In `Run`, after `cleanRunner` is created, add:

```csharp
        ActionEngine Engine() => ActionEngine.CreateReal(ctx, runner, IsElevated);
```

and extend the verb switch:

```csharp
                "scan" => Scan(cmd, ctx, scanner, IsElevated(), Engine),
                "run" => RunActions(cmd, scanner, Engine(), Console.Out),
                "undo" => UndoLatest(Engine(), Console.Out),
                "receipt" => PrintReceipt(Engine(), Console.Out),
                "live" => PrintLive(Engine(), Console.Out),
```

Change the `Scan` signature to `private static int Scan(CliCommand cmd, DiagnosticContext ctx, Scanner scanner, bool elevated, Func<ActionEngine> engine)` and, at the end of its text output (after the posture lines, before its final `return`), add:

```csharp
        PrintPlan(engine().BuildPlan(scan), Console.Out);
```

Add these methods to `Program`:

```csharp
    private static string Groupname(ActionGroupKind kind) =>
        ActionStrings.En($"action.group.{kind.ToString().ToLowerInvariant()}");

    private static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, ActionStrings.En(key), args);

    public static void PrintPlan(ActionPlan plan, TextWriter o)
    {
        o.WriteLine();
        o.WriteLine("One click (brisk run --yes):");
        foreach (var group in plan.Groups)
        {
            o.WriteLine($"  {Groupname(group.Kind)}");
            foreach (var item in group.Items)
            {
                var box = item.DefaultOn ? "[x]" : "[ ]";
                var size = item.Bytes is { } b ? $"  {Fmt.Bytes(b)}" : "";
                var cost = item.Cost is null ? "" : $"  — {item.Cost}";
                o.WriteLine($"    {box} {item.Title}{size}{cost}");
                foreach (var f in item.Files.Take(10))
                    o.WriteLine($"          {Path.GetFileName(f.Path)}  {Fmt.Bytes(f.Bytes)}");
            }
        }
    }

    public static string ResultWord(ActionItem? item, RowResult r) => r.Outcome switch
    {
        RowOutcome.Skipped when r.Reason == ActionRunner.NeedsAdmin => ActionStrings.En("action.result.needs-admin"),
        RowOutcome.Skipped => $"{ActionStrings.En("action.result.skipped")}: {r.Reason}",
        RowOutcome.Failed => $"{ActionStrings.En("action.result.failed")}: {r.Reason}",
        RowOutcome.PendingRestart => ActionStrings.En("action.result.pending"),
        _ => r.ReadBack switch
        {
            ReadBackState.Reverted => ActionStrings.En("action.result.reverted"),
            ReadBackState.WrittenButIgnored => ActionStrings.En("action.result.ignored"),
            ReadBackState.WrittenButUnverified => ActionStrings.En("action.result.written"),
            _ => ActionStrings.En(item?.Group == ActionGroupKind.Privacy ? "action.result.off" : "action.result.done"),
        },
    };

    public static void PrintReceiptLines(Receipt? r, IReadOnlyList<string> deferredApps, TextWriter o)
    {
        if (r is null) return;
        if (r.FreedBytes is { } freed) o.WriteLine("  " + Format("action.receipt.freed", Fmt.Bytes(freed)));
        if (r.DeferredBytes > 0 && deferredApps.Count > 0)
            o.WriteLine("  " + Format("action.receipt.deferred", Fmt.Bytes(r.DeferredBytes), string.Join(", ", deferredApps)));
        if (r.StartupEnabledAfter is { } after)
            o.WriteLine("  " + Format("action.receipt.startup", r.StartupEnabledBefore, after));
        if (r.BootMsBefore is { } before && r.BootMsAfter is { } boot)
            o.WriteLine("  " + Format("action.receipt.boot", before / 1000, boot / 1000));
        if (r.RamFreedBytes is { } ram) o.WriteLine("  " + Format("action.receipt.ram", Fmt.Bytes(ram)));
        if (r.PrivacyOff > 0) o.WriteLine("  " + Format("action.receipt.privacy", r.PrivacyOff, r.PrivacyIgnored));
        if (r.TelemetryBefore is { } tb && r.TelemetryAfter is { } ta)
            o.WriteLine("  " + Format("action.receipt.telemetry", tb, ta));
    }

    private static IReadOnlyList<string> DeferredApps(ActionEngine engine) => engine.Deferred.Pending()
        .Select(e => CleanupTargetRegistry.All.FirstOrDefault(t => t.Id == e.TargetId)?.AppDisplayName)
        .OfType<string>().Distinct().ToList();

    private static int RunActions(CliCommand cmd, Scanner scanner, ActionEngine engine, TextWriter o)
    {
        engine.ProcessDeferred();
        var plan = engine.BuildPlan(scanner.Scan());
        var selection = ActionSelection.Default(plan);
        if (cmd.Group is { } g)
            selection = selection.Only(Enum.Parse<ActionGroupKind>(g, ignoreCase: true), plan);

        if (!cmd.Yes)
        {
            o.WriteLine("PLAN (nothing changed) — add --yes to apply the ticked rows");
            PrintPlan(plan, o);
            return 0;
        }

        var items = plan.Items.ToDictionary(i => i.Id);
        var run = engine.Runner.Run(plan, selection, r =>
        {
            items.TryGetValue(r.RowId, out var item);
            var freed = r.BytesFreed > 0 ? $" · {Fmt.Bytes(r.BytesFreed)}" : "";
            var later = r.BytesDeferred > 0 ? $" · +{Fmt.Bytes(r.BytesDeferred)} later" : "";
            o.WriteLine($"  {item?.Title ?? r.RowId}: {ResultWord(item, r)}{freed}{later}");
        });

        o.WriteLine();
        PrintReceiptLines(engine.Receipts.Refresh(run.RunId), DeferredApps(engine), o);
        if (run.Rows.Any(r => r.Outcome == RowOutcome.PendingRestart))
            o.WriteLine("  " + ActionStrings.En("action.restart"));
        o.WriteLine($"  run {run.RunId} — undo with: brisk undo");
        return run.Rows.Any(r => r.Outcome == RowOutcome.Failed) ? 1 : 0;
    }

    private static int UndoLatest(ActionEngine engine, TextWriter o)
    {
        var latest = engine.Runs.Latest();
        if (latest is null) { o.WriteLine("nothing to undo"); return 0; }
        var results = engine.Runner.Undo(latest.RunId);
        foreach (var r in results) o.WriteLine($"  {r.RowId}: {ResultWord(null, r)}");
        o.WriteLine($"  {ActionStrings.En("action.result.cannot-undo")}: deleted caches and files.");
        return results.Any(r => r.Outcome == RowOutcome.Failed) ? 1 : 0;
    }

    private static int PrintReceipt(ActionEngine engine, TextWriter o)
    {
        var latest = engine.Runs.Latest();
        if (latest is null) { o.WriteLine("no run yet"); return 0; }
        o.WriteLine($"run {latest.RunId}");
        PrintReceiptLines(engine.Receipts.Refresh(latest.RunId), DeferredApps(engine), o);
        return 0;
    }

    private static int PrintLive(ActionEngine engine, TextWriter o)
    {
        o.WriteLine($"{ActionStrings.En("action.group.load")} (measuring for 10 seconds):");
        foreach (var l in engine.Load.Sample())
            o.WriteLine($"  {l.DisplayName,-30} {l.CpuPercent,5:0.#}% CPU  {Fmt.Bytes(l.PrivateBytes),9}" +
                        (l.Note is null ? "" : $"  — {l.Note}"));

        o.WriteLine();
        o.WriteLine("Talking to now:");
        var snap = engine.Connections.Read();
        foreach (var c in snap.Rows.Take(15))
            o.WriteLine($"  {c.App} → {c.Owner}{(c.Host is null ? "" : $" ({c.Host})")} · {c.Count}" +
                        (c.IsTelemetry ? $"  [{ActionStrings.En("action.connections.telemetry")}]" : ""));
        if (!snap.DnsRead) o.WriteLine("  (host names need administrator, so telemetry is not told apart)");
        o.WriteLine("  " + ActionStrings.En("action.connections.footer"));

        o.WriteLine();
        o.WriteLine("Sent in the last 30 days:");
        var usage = engine.Usage.Top();
        if (usage is null) o.WriteLine("  " + ActionStrings.En("action.unread"));
        else
            foreach (var u in usage)
                o.WriteLine($"  {u.App,-30} sent {Fmt.Bytes(u.BytesSent),9}  received {Fmt.Bytes(u.BytesReceived),9}");
        o.WriteLine("  " + ActionStrings.En("action.usage.footer"));
        return 0;
    }
```

In `PrintHelp`, after the `clean` lines, add:

```csharp
        Console.WriteLine("  run                        one click: free space, speed up, privacy");
        Console.WriteLine("    --group <space|speed|privacy>  only that group");
        Console.WriteLine("    --yes                    apply (without it: show the plan, change nothing)");
        Console.WriteLine("  undo                       undo the latest run");
        Console.WriteLine("  receipt                    before/after numbers of the latest run");
        Console.WriteLine("  live                       what is slowing you down, who you talk to, who sent what");
```

- [ ] **Step 6: Run the tests, then the whole suite**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --filter "FullyQualifiedName~CliActionTests|FullyQualifiedName~CliParserTests|FullyQualifiedName~CliHelpSwitchTests"`
Expected: all pass. Then `dotnet test brisk.sln -c Release` — all green.

- [ ] **Step 7: Commit**

```bash
git add src/BriskEngine/Actions/ActionEngine.cs src/Brisk.Cli/CliParser.cs src/Brisk.Cli/Program.cs src/BriskEngine.Tests/CliActionTests.cs
git commit -m "feat(cli): brisk run, undo, receipt and live" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 21: Both languages carry every action string

**Files:**
- Modify: `src/Brisk/Localization/Strings.resx` (append before `</root>`)
- Modify: `src/Brisk/Localization/Strings.tr.resx` (append before `</root>`)
- Test: `src/Brisk.Tests/ActionStringsTests.cs`

**Interfaces:**
- Consumes: `ActionCatalog.AllKeys()` (Task 13).
- Produces: 122 `action.*` keys in both files. Sub-project 2's GUI binds to these keys.

Use the Edit tool (not a shell) for the two resx files so the Turkish characters stay UTF-8.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BriskEngine.Actions;
using Xunit;

namespace Brisk.Tests;

public class ActionStringsTests
{
    private static string LocalizationDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return Path.Combine(dir.FullName, "src", "Brisk", "Localization");
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }

    private static System.Collections.Generic.Dictionary<string, string> Load(string file) =>
        XDocument.Load(Path.Combine(LocalizationDir(), file)).Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Element("value")!);

    [Theory]
    [InlineData("Strings.resx")]
    [InlineData("Strings.tr.resx")]
    public void EveryEngineActionKey_IsTranslated(string file)
    {
        var strings = Load(file);
        foreach (var key in ActionCatalog.AllKeys())
            Assert.True(strings.ContainsKey(key), $"{file} has no '{key}'");
    }

    [Fact]
    public void TurkishTitlesAndCostLines_FollowTheCopyRules()
    {
        var tr = Load("Strings.tr.resx");
        foreach (var key in ActionCatalog.AllKeys().Where(k => k.EndsWith(".title") || k.Contains(".cost")))
            Assert.True(tr[key].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 6,
                $"'{key}' is longer than six words: {tr[key]}");
    }

    [Fact]
    public void EnglishResxMatchesTheEngineEnglish()
    {
        var en = Load("Strings.resx");
        foreach (var row in ActionCatalog.Rows)
            Assert.Equal(row.Title, en[row.TitleKey]);
        foreach (var (key, english) in ActionStrings.Ui)
            Assert.Equal(english, en[key]);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Brisk.Tests/Brisk.Tests.csproj -c Release --filter "FullyQualifiedName~ActionStringsTests"`
Expected: FAIL — `Strings.resx has no 'action.space.caches.title'`.

- [ ] **Step 3: Append to `Strings.resx`**, immediately before the closing `</root>`:

```xml
  <data name="action.space.caches.title" xml:space="preserve"><value>Caches and temporary files</value></data>
  <data name="action.space.developer.title" xml:space="preserve"><value>Developer caches</value></data>
  <data name="action.space.developer.cost" xml:space="preserve"><value>Downloads again later.</value></data>
  <data name="action.space.shaders.title" xml:space="preserve"><value>Game shader caches</value></data>
  <data name="action.space.shaders.cost" xml:space="preserve"><value>Games may stutter on first launch.</value></data>
  <data name="action.space.hibernation.title" xml:space="preserve"><value>Hibernation file</value></data>
  <data name="action.space.hibernation.cost" xml:space="preserve"><value>Fast startup turns off.</value></data>
  <data name="action.space.hibernation.name" xml:space="preserve"><value>hiberfil.sys</value></data>
  <data name="action.space.hibernation.cost-laptop" xml:space="preserve"><value>Open work lost if battery dies.</value></data>
  <data name="action.space.windows-old.title" xml:space="preserve"><value>Old Windows copy</value></data>
  <data name="action.space.windows-old.cost" xml:space="preserve"><value>No going back to old Windows.</value></data>
  <data name="action.space.windows-old.name" xml:space="preserve"><value>Windows.old</value></data>
  <data name="action.space.downloads.title" xml:space="preserve"><value>Installers in Downloads</value></data>
  <data name="action.space.downloads.cost" xml:space="preserve"><value>Can't be undone.</value></data>
  <data name="action.space.docker.title" xml:space="preserve"><value>Docker's unused data</value></data>
  <data name="action.space.docker.cost" xml:space="preserve"><value>Stopped containers are deleted.</value></data>
  <data name="action.space.docker.name" xml:space="preserve"><value>docker system prune</value></data>
  <data name="action.space.recycle-bin.title" xml:space="preserve"><value>Recycle Bin</value></data>
  <data name="action.speed.power-mode.title" xml:space="preserve"><value>Full power when plugged in</value></data>
  <data name="action.speed.power-mode.cost" xml:space="preserve"><value>The fan may spin more.</value></data>
  <data name="action.speed.power-mode.name" xml:space="preserve"><value>Power mode: Best performance</value></data>
  <data name="action.speed.game-recording.title" xml:space="preserve"><value>Background video recording in games</value></data>
  <data name="action.speed.game-recording.name" xml:space="preserve"><value>Game DVR background recording</value></data>
  <data name="action.speed.gpu-scheduling.title" xml:space="preserve"><value>Lower lag in games</value></data>
  <data name="action.speed.gpu-scheduling.name" xml:space="preserve"><value>Hardware-accelerated GPU scheduling</value></data>
  <data name="action.speed.windowed-games.title" xml:space="preserve"><value>Speed up windowed games</value></data>
  <data name="action.speed.windowed-games.name" xml:space="preserve"><value>Optimizations for windowed games</value></data>
  <data name="action.speed.animations.title" xml:space="preserve"><value>Window opening animations</value></data>
  <data name="action.speed.animations.cost" xml:space="preserve"><value>Windows open instantly.</value></data>
  <data name="action.speed.animations.name" xml:space="preserve"><value>Animate windows when minimizing and maximizing</value></data>
  <data name="action.speed.memory-integrity.title" xml:space="preserve"><value>Extra kernel protection</value></data>
  <data name="action.speed.memory-integrity.cost" xml:space="preserve"><value>Security drops one level.</value></data>
  <data name="action.speed.memory-integrity.name" xml:space="preserve"><value>Memory integrity</value></data>
  <data name="action.speed.vm-platform.title" xml:space="preserve"><value>Virtual machine support</value></data>
  <data name="action.speed.vm-platform.cost" xml:space="preserve"><value>WSL and Docker stop working.</value></data>
  <data name="action.speed.vm-platform.name" xml:space="preserve"><value>Virtual Machine Platform</value></data>
  <data name="action.speed.startup.name" xml:space="preserve"><value>Startup apps (Task Manager)</value></data>
  <data name="action.speed.startup.cost.security" xml:space="preserve"><value>Protection won't start at boot.</value></data>
  <data name="action.speed.startup.cost.hardware" xml:space="preserve"><value>Hardware settings may not work.</value></data>
  <data name="action.speed.startup.cost.sync" xml:space="preserve"><value>File sync stops.</value></data>
  <data name="action.speed.startup.cost.passwords" xml:space="preserve"><value>Password manager won't open.</value></data>
  <data name="action.speed.startup.cost.vpn" xml:space="preserve"><value>VPN won't connect automatically.</value></data>
  <data name="action.speed.startup.cost.keyboard" xml:space="preserve"><value>Your shortcuts won't work.</value></data>
  <data name="action.privacy.telemetry-services.title" xml:space="preserve"><value>Sending usage data to Microsoft</value></data>
  <data name="action.privacy.telemetry-services.name" xml:space="preserve"><value>Connected User Experiences and Telemetry</value></data>
  <data name="action.privacy.telemetry-tasks.title" xml:space="preserve"><value>Collecting data in the background</value></data>
  <data name="action.privacy.telemetry-tasks.name" xml:space="preserve"><value>Telemetry scheduled tasks</value></data>
  <data name="action.privacy.error-reports.title" xml:space="preserve"><value>Sending error and usage reports</value></data>
  <data name="action.privacy.error-reports.name" xml:space="preserve"><value>Diagnostic data, Windows Error Reporting</value></data>
  <data name="action.privacy.ads.title" xml:space="preserve"><value>Showing ads and suggestions for you</value></data>
  <data name="action.privacy.ads.name" xml:space="preserve"><value>Advertising ID, tailored experiences</value></data>
  <data name="action.privacy.activity.title" xml:space="preserve"><value>Recording the files and sites you open</value></data>
  <data name="action.privacy.activity.name" xml:space="preserve"><value>Activity history</value></data>
  <data name="action.privacy.start-ads.title" xml:space="preserve"><value>Ads in the Start menu</value></data>
  <data name="action.privacy.start-ads.name" xml:space="preserve"><value>Start suggestions</value></data>
  <data name="action.privacy.start-web.title" xml:space="preserve"><value>Going online when you search Start</value></data>
  <data name="action.privacy.start-web.cost" xml:space="preserve"><value>No web search in Start.</value></data>
  <data name="action.privacy.start-web.name" xml:space="preserve"><value>Bing in Start search</value></data>
  <data name="action.privacy.speech.title" xml:space="preserve"><value>Sending your voice and typing</value></data>
  <data name="action.privacy.speech.cost" xml:space="preserve"><value>Windows voice typing stops working.</value></data>
  <data name="action.privacy.speech.name" xml:space="preserve"><value>Online speech recognition, inking and typing</value></data>
  <data name="action.privacy.feedback.title" xml:space="preserve"><value>"How do you like Windows?" questions</value></data>
  <data name="action.privacy.feedback.name" xml:space="preserve"><value>Feedback frequency</value></data>
  <data name="action.privacy.recall.title" xml:space="preserve"><value>Saving pictures of your screen</value></data>
  <data name="action.privacy.recall.name" xml:space="preserve"><value>Recall</value></data>
  <data name="action.privacy.copilot.title" xml:space="preserve"><value>Copilot</value></data>
  <data name="action.privacy.copilot.cost" xml:space="preserve"><value>Copilot turns off.</value></data>
  <data name="action.privacy.copilot.name" xml:space="preserve"><value>Windows Copilot</value></data>
  <data name="action.privacy.delivery-peers.title" xml:space="preserve"><value>Sharing updates with strangers</value></data>
  <data name="action.privacy.delivery-peers.name" xml:space="preserve"><value>Delivery Optimization</value></data>
  <data name="action.privacy.location.title" xml:space="preserve"><value>Sharing your location with apps</value></data>
  <data name="action.privacy.location.cost" xml:space="preserve"><value>Find my device stops working.</value></data>
  <data name="action.privacy.location.name" xml:space="preserve"><value>Location</value></data>
  <data name="action.privacy.uac.title" xml:space="preserve"><value>Programs becoming admin without asking</value></data>
  <data name="action.privacy.uac.cost" xml:space="preserve"><value>Programs ask for permission again.</value></data>
  <data name="action.privacy.uac.name" xml:space="preserve"><value>User Account Control</value></data>
  <data name="action.privacy.firewall.title" xml:space="preserve"><value>The firewall being off</value></data>
  <data name="action.privacy.firewall.name" xml:space="preserve"><value>Windows Firewall</value></data>
  <data name="action.privacy.defender-realtime.title" xml:space="preserve"><value>Virus protection being off</value></data>
  <data name="action.privacy.defender-realtime.name" xml:space="preserve"><value>Real-time protection</value></data>
  <data name="action.group.space" xml:space="preserve"><value>Free up space</value></data>
  <data name="action.group.speed" xml:space="preserve"><value>Speed up</value></data>
  <data name="action.group.privacy" xml:space="preserve"><value>Privacy</value></data>
  <data name="action.group.load" xml:space="preserve"><value>Slowing you down now</value></data>
  <data name="action.result.done" xml:space="preserve"><value>Done ✓</value></data>
  <data name="action.result.off" xml:space="preserve"><value>Off ✓</value></data>
  <data name="action.result.ignored" xml:space="preserve"><value>Windows ignored it</value></data>
  <data name="action.result.reverted" xml:space="preserve"><value>Turned back on</value></data>
  <data name="action.result.written" xml:space="preserve"><value>Written</value></data>
  <data name="action.result.pending" xml:space="preserve"><value>After restart</value></data>
  <data name="action.result.skipped" xml:space="preserve"><value>Skipped</value></data>
  <data name="action.result.failed" xml:space="preserve"><value>Didn't work</value></data>
  <data name="action.result.needs-admin" xml:space="preserve"><value>Needs administrator</value></data>
  <data name="action.result.deferred" xml:space="preserve"><value>Cleans when {0} closes</value></data>
  <data name="action.result.cannot-undo" xml:space="preserve"><value>Can't be undone</value></data>
  <data name="action.connections.footer" xml:space="preserve"><value>A connection doesn't mean data was sent.</value></data>
  <data name="action.connections.local" xml:space="preserve"><value>Local network</value></data>
  <data name="action.connections.unknown" xml:space="preserve"><value>Unknown</value></data>
  <data name="action.connections.telemetry" xml:space="preserve"><value>Telemetry</value></data>
  <data name="action.usage.footer" xml:space="preserve"><value>Amounts only, no contents.</value></data>
  <data name="action.usage.windows" xml:space="preserve"><value>Windows (system)</value></data>
  <data name="action.unread" xml:space="preserve"><value>Couldn't read.</value></data>
  <data name="action.load.close" xml:space="preserve"><value>Close</value></data>
  <data name="action.load.force" xml:space="preserve"><value>Force close</value></data>
  <data name="action.load.remove-startup" xml:space="preserve"><value>Remove from startup</value></data>
  <data name="action.load.note.defender" xml:space="preserve"><value>Defender is scanning</value></data>
  <data name="action.load.note.update" xml:space="preserve"><value>Windows is installing updates</value></data>
  <data name="action.load.note.search" xml:space="preserve"><value>Windows is indexing for search</value></data>
  <data name="action.load.note.memory" xml:space="preserve"><value>Windows is compressing memory</value></data>
  <data name="action.load.note.compat" xml:space="preserve"><value>Windows is collecting compatibility data</value></data>
  <data name="action.load.note.dwm" xml:space="preserve"><value>Windows is drawing the screen</value></data>
  <data name="action.load.note.system" xml:space="preserve"><value>Windows itself</value></data>
  <data name="action.load.note.services" xml:space="preserve"><value>Windows services</value></data>
  <data name="action.receipt.freed" xml:space="preserve"><value>{0} freed</value></data>
  <data name="action.receipt.deferred" xml:space="preserve"><value>{0} more when {1} closes</value></data>
  <data name="action.receipt.boot" xml:space="preserve"><value>Startup {0} s → {1} s</value></data>
  <data name="action.receipt.startup" xml:space="preserve"><value>Startup apps {0} → {1}</value></data>
  <data name="action.receipt.ram" xml:space="preserve"><value>{0} RAM freed</value></data>
  <data name="action.receipt.privacy" xml:space="preserve"><value>{0} settings off · Windows ignored {1}</value></data>
  <data name="action.receipt.telemetry" xml:space="preserve"><value>Telemetry connections {0} → {1}</value></data>
  <data name="action.restart" xml:space="preserve"><value>Restart for full effect</value></data>
  <data name="action.undo" xml:space="preserve"><value>Undo</value></data>
```

- [ ] **Step 4: Append to `Strings.tr.resx`**, immediately before the closing `</root>`:

```xml
  <data name="action.space.caches.title" xml:space="preserve"><value>Önbellekler ve geçici dosyalar</value></data>
  <data name="action.space.developer.title" xml:space="preserve"><value>Geliştirici önbellekleri</value></data>
  <data name="action.space.developer.cost" xml:space="preserve"><value>Sonra yeniden iner.</value></data>
  <data name="action.space.shaders.title" xml:space="preserve"><value>Oyun shader önbellekleri</value></data>
  <data name="action.space.shaders.cost" xml:space="preserve"><value>Oyunlar ilk açılışta takılabilir.</value></data>
  <data name="action.space.hibernation.title" xml:space="preserve"><value>Hazırda bekletme dosyası</value></data>
  <data name="action.space.hibernation.cost" xml:space="preserve"><value>Hızlı açılış kapanır.</value></data>
  <data name="action.space.hibernation.name" xml:space="preserve"><value>hiberfil.sys</value></data>
  <data name="action.space.hibernation.cost-laptop" xml:space="preserve"><value>Pil biterse açık işler kaybolur.</value></data>
  <data name="action.space.windows-old.title" xml:space="preserve"><value>Eski Windows kopyası</value></data>
  <data name="action.space.windows-old.cost" xml:space="preserve"><value>Eski Windows'a dönülemez.</value></data>
  <data name="action.space.windows-old.name" xml:space="preserve"><value>Windows.old</value></data>
  <data name="action.space.downloads.title" xml:space="preserve"><value>İndirilenler'deki kurulum dosyaları</value></data>
  <data name="action.space.downloads.cost" xml:space="preserve"><value>Geri alınamaz.</value></data>
  <data name="action.space.docker.title" xml:space="preserve"><value>Docker'ın kullanılmayan verisi</value></data>
  <data name="action.space.docker.cost" xml:space="preserve"><value>Durmuş container'lar silinir.</value></data>
  <data name="action.space.docker.name" xml:space="preserve"><value>docker system prune</value></data>
  <data name="action.space.recycle-bin.title" xml:space="preserve"><value>Geri Dönüşüm Kutusu</value></data>
  <data name="action.speed.power-mode.title" xml:space="preserve"><value>Şarjdayken tam güç</value></data>
  <data name="action.speed.power-mode.cost" xml:space="preserve"><value>Fan daha çok dönebilir.</value></data>
  <data name="action.speed.power-mode.name" xml:space="preserve"><value>Güç modu: En iyi performans</value></data>
  <data name="action.speed.game-recording.title" xml:space="preserve"><value>Oyunda arka planda video kaydı</value></data>
  <data name="action.speed.game-recording.name" xml:space="preserve"><value>Arka planda kaydet (Game DVR)</value></data>
  <data name="action.speed.gpu-scheduling.title" xml:space="preserve"><value>Oyunlarda gecikmeyi azalt</value></data>
  <data name="action.speed.gpu-scheduling.name" xml:space="preserve"><value>Donanım hızlandırmalı GPU zamanlaması</value></data>
  <data name="action.speed.windowed-games.title" xml:space="preserve"><value>Pencere modundaki oyunları hızlandır</value></data>
  <data name="action.speed.windowed-games.name" xml:space="preserve"><value>Pencereli oyunlar için iyileştirmeler</value></data>
  <data name="action.speed.animations.title" xml:space="preserve"><value>Pencere açılış animasyonları</value></data>
  <data name="action.speed.animations.cost" xml:space="preserve"><value>Pencereler anında açılır.</value></data>
  <data name="action.speed.animations.name" xml:space="preserve"><value>Simge durumuna küçültürken ve büyütürken pencereleri canlandır</value></data>
  <data name="action.speed.memory-integrity.title" xml:space="preserve"><value>Ekstra çekirdek koruması</value></data>
  <data name="action.speed.memory-integrity.cost" xml:space="preserve"><value>Güvenlik bir kademe düşer.</value></data>
  <data name="action.speed.memory-integrity.name" xml:space="preserve"><value>Bellek bütünlüğü</value></data>
  <data name="action.speed.vm-platform.title" xml:space="preserve"><value>Sanal makine desteği</value></data>
  <data name="action.speed.vm-platform.cost" xml:space="preserve"><value>WSL ve Docker çalışmaz.</value></data>
  <data name="action.speed.vm-platform.name" xml:space="preserve"><value>Sanal Makine Platformu</value></data>
  <data name="action.speed.startup.name" xml:space="preserve"><value>Başlangıç uygulamaları (Görev Yöneticisi)</value></data>
  <data name="action.speed.startup.cost.security" xml:space="preserve"><value>Koruma açılışta başlamaz.</value></data>
  <data name="action.speed.startup.cost.hardware" xml:space="preserve"><value>Donanım ayarı çalışmayabilir.</value></data>
  <data name="action.speed.startup.cost.sync" xml:space="preserve"><value>Dosya eşitleme durur.</value></data>
  <data name="action.speed.startup.cost.passwords" xml:space="preserve"><value>Şifre yöneticisi açılmaz.</value></data>
  <data name="action.speed.startup.cost.vpn" xml:space="preserve"><value>VPN otomatik bağlanmaz.</value></data>
  <data name="action.speed.startup.cost.keyboard" xml:space="preserve"><value>Kısayolların çalışmaz.</value></data>
  <data name="action.privacy.telemetry-services.title" xml:space="preserve"><value>Kullanım bilgilerini Microsoft'a göndermesi</value></data>
  <data name="action.privacy.telemetry-services.name" xml:space="preserve"><value>Bağlı Kullanıcı Deneyimleri ve Telemetri</value></data>
  <data name="action.privacy.telemetry-tasks.title" xml:space="preserve"><value>Arka planda bilgi toplaması</value></data>
  <data name="action.privacy.telemetry-tasks.name" xml:space="preserve"><value>Telemetri zamanlanmış görevleri</value></data>
  <data name="action.privacy.error-reports.title" xml:space="preserve"><value>Hata ve kullanım raporu göndermesi</value></data>
  <data name="action.privacy.error-reports.name" xml:space="preserve"><value>Tanılama verileri, Windows Hata Bildirimi</value></data>
  <data name="action.privacy.ads.title" xml:space="preserve"><value>Sana göre reklam ve öneri göstermesi</value></data>
  <data name="action.privacy.ads.name" xml:space="preserve"><value>Reklam kimliği, size özel deneyimler</value></data>
  <data name="action.privacy.activity.title" xml:space="preserve"><value>Açtığın dosya ve sitelerin kaydını tutması</value></data>
  <data name="action.privacy.activity.name" xml:space="preserve"><value>Etkinlik geçmişi</value></data>
  <data name="action.privacy.start-ads.title" xml:space="preserve"><value>Başlat menüsündeki reklamlar</value></data>
  <data name="action.privacy.start-ads.name" xml:space="preserve"><value>Başlat önerileri</value></data>
  <data name="action.privacy.start-web.title" xml:space="preserve"><value>Başlat'ta arama yapınca internete gitmesi</value></data>
  <data name="action.privacy.start-web.cost" xml:space="preserve"><value>Başlat'ta web araması olmaz.</value></data>
  <data name="action.privacy.start-web.name" xml:space="preserve"><value>Başlat aramasında Bing</value></data>
  <data name="action.privacy.speech.title" xml:space="preserve"><value>Sesini ve yazdıklarını göndermesi</value></data>
  <data name="action.privacy.speech.cost" xml:space="preserve"><value>Windows sesle yazma çalışmaz.</value></data>
  <data name="action.privacy.speech.name" xml:space="preserve"><value>Çevrimiçi konuşma tanıma, mürekkep ve yazma</value></data>
  <data name="action.privacy.feedback.title" xml:space="preserve"><value>"Windows'u nasıl buldun?" soruları</value></data>
  <data name="action.privacy.feedback.name" xml:space="preserve"><value>Geri bildirim sıklığı</value></data>
  <data name="action.privacy.recall.title" xml:space="preserve"><value>Ekranının sürekli görüntüsünü kaydetmesi</value></data>
  <data name="action.privacy.recall.name" xml:space="preserve"><value>Recall</value></data>
  <data name="action.privacy.copilot.title" xml:space="preserve"><value>Copilot</value></data>
  <data name="action.privacy.copilot.cost" xml:space="preserve"><value>Copilot kapanır.</value></data>
  <data name="action.privacy.copilot.name" xml:space="preserve"><value>Windows Copilot</value></data>
  <data name="action.privacy.delivery-peers.title" xml:space="preserve"><value>Güncellemeleri başkalarına dağıtması</value></data>
  <data name="action.privacy.delivery-peers.name" xml:space="preserve"><value>Teslim İyileştirme</value></data>
  <data name="action.privacy.location.title" xml:space="preserve"><value>Konumunu uygulamalarla paylaşması</value></data>
  <data name="action.privacy.location.cost" xml:space="preserve"><value>Cihazımı bul çalışmaz.</value></data>
  <data name="action.privacy.location.name" xml:space="preserve"><value>Konum</value></data>
  <data name="action.privacy.uac.title" xml:space="preserve"><value>Programların sormadan yönetici olması</value></data>
  <data name="action.privacy.uac.cost" xml:space="preserve"><value>Programlar yeniden izin ister.</value></data>
  <data name="action.privacy.uac.name" xml:space="preserve"><value>Kullanıcı Hesabı Denetimi</value></data>
  <data name="action.privacy.firewall.title" xml:space="preserve"><value>Güvenlik duvarının kapalı olması</value></data>
  <data name="action.privacy.firewall.name" xml:space="preserve"><value>Windows Güvenlik Duvarı</value></data>
  <data name="action.privacy.defender-realtime.title" xml:space="preserve"><value>Virüs korumasının kapalı olması</value></data>
  <data name="action.privacy.defender-realtime.name" xml:space="preserve"><value>Gerçek zamanlı koruma</value></data>
  <data name="action.group.space" xml:space="preserve"><value>Yer aç</value></data>
  <data name="action.group.speed" xml:space="preserve"><value>Hızlandır</value></data>
  <data name="action.group.privacy" xml:space="preserve"><value>Gizlilik</value></data>
  <data name="action.group.load" xml:space="preserve"><value>Şu an kasanlar</value></data>
  <data name="action.result.done" xml:space="preserve"><value>Yapıldı ✓</value></data>
  <data name="action.result.off" xml:space="preserve"><value>Kapandı ✓</value></data>
  <data name="action.result.ignored" xml:space="preserve"><value>Windows yok saydı</value></data>
  <data name="action.result.reverted" xml:space="preserve"><value>Geri açıldı</value></data>
  <data name="action.result.written" xml:space="preserve"><value>Yazıldı</value></data>
  <data name="action.result.pending" xml:space="preserve"><value>Yeniden başlatınca</value></data>
  <data name="action.result.skipped" xml:space="preserve"><value>Atlandı</value></data>
  <data name="action.result.failed" xml:space="preserve"><value>Olmadı</value></data>
  <data name="action.result.needs-admin" xml:space="preserve"><value>Yönetici gerekiyor</value></data>
  <data name="action.result.deferred" xml:space="preserve"><value>{0} kapanınca temizlenecek</value></data>
  <data name="action.result.cannot-undo" xml:space="preserve"><value>Geri alınamaz</value></data>
  <data name="action.connections.footer" xml:space="preserve"><value>Bağlantı, veri gönderdiği anlamına gelmez.</value></data>
  <data name="action.connections.local" xml:space="preserve"><value>Yerel ağ</value></data>
  <data name="action.connections.unknown" xml:space="preserve"><value>Bilinmiyor</value></data>
  <data name="action.connections.telemetry" xml:space="preserve"><value>Telemetri</value></data>
  <data name="action.usage.footer" xml:space="preserve"><value>Miktar var, içerik yok.</value></data>
  <data name="action.usage.windows" xml:space="preserve"><value>Windows (sistem)</value></data>
  <data name="action.unread" xml:space="preserve"><value>Okunamadı.</value></data>
  <data name="action.load.close" xml:space="preserve"><value>Kapat</value></data>
  <data name="action.load.force" xml:space="preserve"><value>Zorla kapat</value></data>
  <data name="action.load.remove-startup" xml:space="preserve"><value>Başlangıçtan çıkar</value></data>
  <data name="action.load.note.defender" xml:space="preserve"><value>Defender tarama yapıyor</value></data>
  <data name="action.load.note.update" xml:space="preserve"><value>Windows güncelleme kuruyor</value></data>
  <data name="action.load.note.search" xml:space="preserve"><value>Windows arama dizini kuruyor</value></data>
  <data name="action.load.note.memory" xml:space="preserve"><value>Windows belleği sıkıştırıyor</value></data>
  <data name="action.load.note.compat" xml:space="preserve"><value>Windows uyumluluk verisi topluyor</value></data>
  <data name="action.load.note.dwm" xml:space="preserve"><value>Windows ekranı çiziyor</value></data>
  <data name="action.load.note.system" xml:space="preserve"><value>Windows'un kendisi</value></data>
  <data name="action.load.note.services" xml:space="preserve"><value>Windows hizmetleri</value></data>
  <data name="action.receipt.freed" xml:space="preserve"><value>{0} açıldı</value></data>
  <data name="action.receipt.deferred" xml:space="preserve"><value>{0} daha {1} kapanınca açılacak</value></data>
  <data name="action.receipt.boot" xml:space="preserve"><value>Açılış {0} sn → {1} sn</value></data>
  <data name="action.receipt.startup" xml:space="preserve"><value>Başlangıç programları {0} → {1}</value></data>
  <data name="action.receipt.ram" xml:space="preserve"><value>{0} RAM boşaldı</value></data>
  <data name="action.receipt.privacy" xml:space="preserve"><value>{0} ayar kapandı · Windows {1} tanesini yok saydı</value></data>
  <data name="action.receipt.telemetry" xml:space="preserve"><value>Telemetri bağlantıları {0} → {1}</value></data>
  <data name="action.restart" xml:space="preserve"><value>Tam etki için yeniden başlat</value></data>
  <data name="action.undo" xml:space="preserve"><value>Geri al</value></data>
```

- [ ] **Step 5: Run the new test and the existing localization tests**

Run: `dotnet test src/Brisk.Tests/Brisk.Tests.csproj -c Release --filter "FullyQualifiedName~ActionStringsTests|FullyQualifiedName~LocTests"`
Expected: all pass (`ResxFiles_ExposeTheSameKeySet` included).

- [ ] **Step 6: Commit**

```bash
git add src/Brisk/Localization/Strings.resx src/Brisk/Localization/Strings.tr.resx src/Brisk.Tests/ActionStringsTests.cs
git commit -m "feat(loc): every one-click row and result in English and Turkish" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 22: End to end on the maintainer's PC

No new code. This proves the engine on real hardware through the CLI, before any GUI exists. **It changes the machine only at Step 5, and only with the maintainer's go.**

**Files:** none (a short results note goes into the final report, not the repo).

- [ ] **Step 1: Whole suite green**

Run: `dotnet test brisk.sln -c Release`
Expected: all green. Report the new total (1687 + the tests added by Tasks 1–21).

- [ ] **Step 2: Build the exes outside the repo**

Run (PowerShell): `& .\scripts\publish.ps1 -OutDir "$env:TEMP\brisk-action-engine"`
Expected: `brisk.exe`, `brisk-app.exe`, zip, sums. `& "$env:TEMP\brisk-action-engine\brisk.exe" version` prints the branch's version.

- [ ] **Step 3: Read-only runs, unelevated**

Run: `& "$env:TEMP\brisk-action-engine\brisk.exe" scan` then `... brisk.exe run` (no `--yes`) then `... brisk.exe live`.
Expected, and write down what you see for each:
- `scan` ends with `One click (brisk run --yes):` and the three groups; the caches bucket is several GB (not 2.2); shader caches and Docker are `[ ]`; on this laptop the hibernation file is `[ ]` with "Open work lost if battery dies.".
- `run` prints `PLAN (nothing changed)` and changes nothing (check `git status` is clean and no `runs` folder appeared under `%LOCALAPPDATA%\brisk`).
- `live` lists up to five apps with CPU and memory, connections with owners (no Telemetry tags unelevated — the line about administrator says why), and ten apps by bytes sent in 30 days with real names.

- [ ] **Step 4: Elevated read-only run**

Run `brisk.exe live` from an elevated terminal (UAC is silent on this machine; `Start-Process -Verb RunAs` works). Expected: host names appear and known Microsoft telemetry hosts carry `[Telemetry]` if any are connected.

- [ ] **Step 5: STOP — the maintainer applies it**

`brisk run --yes` deletes caches for good, disables startup programs and switches telemetry off on his PC. Do not run it yourself. Hand the maintainer the plan output from Step 3 and the exact command for an elevated terminal:

```powershell
& "$env:TEMP\brisk-action-engine\brisk.exe" run --yes
```

When he has run it, record the printed rows and receipt, then check the success criteria from the spec:
- free space rose by **at least 15 GB** (the `freed` line is the real change in C:'s free space);
- enabled startup programs are **at most 4**;
- after a restart and three minutes, `brisk receipt` shows the boot line and `RAM freed`; with an elevated `brisk live`, telemetry connections are **0**.

Report each criterion as met or not, with the number. If one is not met, say which rows were skipped and why (the run output names them) — do not change thresholds to make it pass.

- [ ] **Step 6: Undo works on the real machine**

With his go, run `brisk undo` (elevated) and confirm: startup programs are back in Task Manager's Startup tab, the switches read as before, and the output says deleted caches cannot come back. Then he can re-apply with `brisk run --yes` if he wants the result kept.

---

## After the last task

Run the whole-branch review the execution skill prescribes, on the strongest model available (memory: `use-the-strongest-model-on-brisk`). Nothing is merged, pushed or tagged: the engine ships together with sub-project 2's GUI, on the maintainer's word.
