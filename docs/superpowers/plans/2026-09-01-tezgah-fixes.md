# Tezgâh fixes — what the live workbench found, corrected

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the eight defects the 2026-09-01 finding workbench exposed on a real machine — a deep-shelf target that promised 7.5 GB and delivered 0 B, a DISM step reported as "0 B", a report card that lists applied fixes under their *problem* titles, an orphan detector fooled by "Comm**unity**", raw shell/.NET error text reaching users, a badge that says "needs administrator" inside an elevated app, a lifetime counter that lags one scan, and read-back copy that says "0 days ago".

**Architecture:** No new subsystems. Engine fixes land in `CleanRunner` (two target cases), `WindowsRecycler` (error words), `FixRunner` (refusal words) and `OrphanedDataRule` (name match). App fixes land in `ReportCardModel`, `CleanViewModel`/`CleanPage.xaml` and `PrivacyViewModel` plus both resx files. Every change is pinned by a test in the project that owns it.

**Tech Stack:** .NET 8, WPF, xunit. Solution: `brisk.sln` at the repo root. Full suite: `dotnet test brisk.sln -c Release` (1326 green on `main @ a56a631` — that number must only go up).

**Spec:** the workbench report `.superpowers/tezgah/RAPOR.md` (local, gitignored) — its findings are restated in **Findings** below so this plan stands alone.

## Global Constraints

- Both resx files (`src/Brisk/Localization/Strings.resx`, `Strings.tr.resx`) must keep identical key sets — a test enforces parity. Every new key lands in BOTH.
- brisk never prints a claim it did not measure. A byte count is either observed (a path that existed and is gone, a free-space reading before and after) or it is 0 with a reason.
- "removed" entries never reach undo; "recycled" entries do. Do not blur them.
- No new NuGet packages. Windows-only APIs are fine (the product is Windows-only).
- Commit style: one commit per task, message in the repo's narrative voice (read `git log --oneline -15` first), body says what was wrong and what the test pins.
- Run the owning test project after every task (`dotnet test src/BriskEngine.Tests -c Release` or `dotnet test src/Brisk.Tests -c Release`), and the full suite before the last commit.

## Findings (the spec, restated)

1. `clean --target delivery-optimization --yes` (elevated, live): plan 7.5 GB, 14 folders, every one `SHFileOperation failed (120)` = DE_ACCESSDENIEDSRC; the cache sits under the NetworkService profile and the shell cannot move it to the user's Recycle Bin. Result 0 B. The Overview now headlines this target ("Derin raflarda 16.7 GB daha var (Teslim İyileştirme önbelleği: 7.5 GB)").
2. `component-store` ran DISM for 555 s and brisk printed "recycled: 0 items, 0 B"; the machine's free space rose ~9.5 GB that no other entry accounts for.
3. `OrphanedDataRule.IsInstalled` uses `DisplayName.Contains(toolName, OrdinalIgnoreCase)`; "PyCharm Community Edition" matched "Unity", so a 600 MB orphaned Unity folder was reported as installed.
4. Users see `SHFileOperation failed (120) for '…'`, `SHFileOperation failed (124) …`, and from the unelevated CLI `fix failed — Access to the registry key 'HKEY_LOCAL_MACHINE\…' is denied.`
5. The report card PNG (the one artifact built to be shared) lists applied fixes under "Uygulanan düzeltmeler" as "Konum erişimi kapalı değil · 2026-09-01", "Etkinlik geçmişi kapalı değil", "Görsel efektler bilgisayarı yavaşlatıyor" — the problem titles. The Overview's own "Yapılanlar raporu" uses the done text ("Konum erişimi kapatıldı").
6. Every Deep row shows "Yönetici gerekiyor" although brisk-app always runs elevated (`Visibility="{Binding NeedsElevation}"`).
7. After a Deep removal the banner said "1 öğe kalıcı olarak kaldırıldı (12.7 GB)" while the hero's "bugüne dek temizlendi" stayed at 19.9 GB until the next scan (then 32.8 GB).
8. Privacy read-back rows read "0 gün önce kapattın, hâlâ kapalı" / "You switched this off 0 days ago"; on day two they will read "1 days ago".

---

### Task 1: OrphanedDataRule matches whole words

**Files:**
- Modify: `src/BriskEngine/Diagnostics/Rules/OrphanedDataRule.cs` (the `IsInstalled` method, ~line 47-75)
- Test: `src/BriskEngine.Tests/OrphanedDataRuleTests.cs` (create)

**Interfaces:**
- Produces: `public static bool OrphanedDataRule.NameMatches(string displayName, string toolName)` — true when `toolName` appears in `displayName` as a whole word (case-insensitive, culture-invariant), never as a substring of a longer word.

- [ ] **Step 1: Write the failing test**

```csharp
using BriskEngine.Diagnostics.Rules;
using Xunit;

namespace BriskEngine.Tests;

public class OrphanedDataRuleTests
{
    [Theory]
    [InlineData("PyCharm Community Edition 2024.3", "Unity", false)]   // the live false positive
    [InlineData("Unity Hub", "Unity", true)]
    [InlineData("unity 2022.3.1f1", "Unity", true)]
    [InlineData("Docker Desktop", "Docker Desktop", true)]
    [InlineData("Docker Desktop 4.30", "Docker", true)]
    [InlineData("JetBrains dotPeek 2025.3", "JetBrains", true)]
    [InlineData("BlueStacks Services", "BlueStacks", true)]
    [InlineData("Immunity Debugger", "Unity", false)]
    public void NameMatches_requires_a_whole_word(string displayName, string tool, bool expected)
        => Assert.Equal(expected, OrphanedDataRule.NameMatches(displayName, tool));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/BriskEngine.Tests -c Release --filter "FullyQualifiedName~OrphanedDataRuleTests"`
Expected: build error — `NameMatches` does not exist.

- [ ] **Step 3: Write minimal implementation**

In `OrphanedDataRule.cs` add (with `using System.Text.RegularExpressions;`):

```csharp
    /// Whole-word match. The live workbench (2026-09-01) found the previous
    /// Contains() reading "PyCharm Community Edition" as proof that Unity
    /// is installed, so a 600 MB orphaned Unity folder went unreported.
    public static bool NameMatches(string displayName, string toolName) =>
        Regex.IsMatch(displayName,
            @"(?<![\p{L}\p{N}])" + Regex.Escape(toolName) + @"(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
```

and in `IsInstalled` replace

```csharp
displayName.Contains(toolName, StringComparison.OrdinalIgnoreCase)
```

with

```csharp
NameMatches(displayName, toolName)
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/BriskEngine.Tests -c Release --filter "FullyQualifiedName~OrphanedDataRuleTests"`
Expected: 8 passed.

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Diagnostics/Rules/OrphanedDataRule.cs src/BriskEngine.Tests/OrphanedDataRuleTests.cs
git commit -m "the orphan detector stops reading Community as Unity"
```

---

### Task 2: Shell and refusal errors reach the user in words

**Files:**
- Modify: `src/BriskEngine/Cleaning/WindowsRecycler.cs:41` and `:58` (the two `throw new IOException(...)`)
- Modify: `src/BriskEngine/Diagnostics/FixRunner.cs:30-34` (the catch)
- Test: `src/BriskEngine.Tests/WindowsRecyclerTests.cs` (create), `src/BriskEngine.Tests/FixRunnerTests.cs` (extend)

**Interfaces:**
- Produces: `public static string WindowsRecycler.Describe(int code)` — a short English phrase for an SHFileOperation return code.
- Produces: `FixRunner.Apply` returns `FixOutcome(false, "<id>: fix refused — needs administrator rights (run brisk as administrator, or use the app)")` when `Fix` throws `UnauthorizedAccessException` or `System.Security.SecurityException`.

- [ ] **Step 1: Write the failing tests**

`WindowsRecyclerTests.cs`:

```csharp
using BriskEngine.Cleaning;
using Xunit;

namespace BriskEngine.Tests;

public class WindowsRecyclerTests
{
    [Theory]
    [InlineData(0x78, "access denied at the source")]                 // DE_ACCESSDENIEDSRC — the live DO-cache failure
    [InlineData(0x7C, "the path is invalid or the item is in use")]   // DE_INVALIDFILES — the live thumbcache failure
    [InlineData(0x74, "the source is a root directory")]
    [InlineData(0x75, "the operation was cancelled")]
    [InlineData(0x79, "the path is too deep")]
    [InlineData(0x81, "the name is too long")]
    [InlineData(0x86, "a sharing violation")]
    [InlineData(0x402, "an unknown shell error")]
    [InlineData(999999, "an unknown shell error")]
    public void Describe_turns_shell_codes_into_words(int code, string expected)
        => Assert.Equal(expected, WindowsRecycler.Describe(code));
}
```

In `FixRunnerTests.cs`, next to the existing fake rules (copy the shape of the fake rule already used there — same interface members — and make its `Fix` throw):

```csharp
    [Fact]
    public void Apply_names_the_missing_right_when_the_fix_is_refused()
    {
        // arrange exactly like the neighbouring Apply test, but with a rule whose
        // Fix throws new UnauthorizedAccessException("Access to the registry key 'HKEY_LOCAL_MACHINE\\X' is denied.")
        var outcome = runner.Apply(refusingRule, ctx);
        Assert.False(outcome.Success);
        Assert.Contains("needs administrator rights", outcome.Message);
        Assert.DoesNotContain("HKEY_LOCAL_MACHINE", outcome.Message);
    }
```

(`FixOutcome` field names: check the record in `FixRunner.cs`; use the real ones.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests -c Release --filter "FullyQualifiedName~WindowsRecyclerTests|FullyQualifiedName~FixRunnerTests"`
Expected: build error (`Describe` missing); after stubbing, the refusal test fails on the message text.

- [ ] **Step 3: Write minimal implementation**

`WindowsRecycler.cs`:

```csharp
    /// SHFileOperation return codes (shellapi.h DE_*), in words. The live
    /// workbench printed "SHFileOperation failed (120)" fourteen times for a
    /// cache the shell is not allowed to touch; nobody should have to look
    /// 0x78 up to learn that.
    public static string Describe(int code) => code switch
    {
        0x71 => "source and destination are the same file",
        0x72 => "multiple sources for one destination",
        0x73 => "source and destination are in different folders",
        0x74 => "the source is a root directory",
        0x75 => "the operation was cancelled",
        0x76 => "the destination is inside the source",
        0x78 => "access denied at the source",
        0x79 => "the path is too deep",
        0x7A => "more than one destination",
        0x7C => "the path is invalid or the item is in use",
        0x7D => "the destination is in the same tree as the source",
        0x7E => "the destination is a file, not a folder",
        0x80 => "the destination is a folder, not a file",
        0x81 => "the name is too long",
        0x82 or 0x83 or 0x84 => "the destination is optical media",
        0x85 => "the file is too large for the destination",
        0x86 => "a sharing violation",
        0x87 => "the source is optical media",
        0x88 => "the source is a recordable disc",
        _ => "an unknown shell error",
    };
```

and change the two throws to

```csharp
throw new IOException($"the shell refused: {Describe(code)} (code {code}) for '{path}'");
// ...
throw new IOException($"the shell refused: {Describe(code)} (code {code}) for a batch of {paths.Count} items");
```

`FixRunner.cs` — add before the general catch:

```csharp
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new FixOutcome(false,
                $"{rule.Id}: fix refused — needs administrator rights (run brisk as administrator, or use the app)");
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/BriskEngine.Tests -c Release`
Expected: all green; if any existing test asserted the old `SHFileOperation failed (` text, update its expectation to `the shell refused:`.

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Cleaning/WindowsRecycler.cs src/BriskEngine/Diagnostics/FixRunner.cs src/BriskEngine.Tests/WindowsRecyclerTests.cs src/BriskEngine.Tests/FixRunnerTests.cs
git commit -m "shell codes and refused rights reach the user as words"
```

---

### Task 3: The Delivery Optimization cache goes past the bin, through Windows' own command

**Files:**
- Modify: `src/BriskEngine/Cleaning/CleanupTargetRegistry.cs:108-110` (the `delivery-optimization` entry)
- Modify: `src/BriskEngine/Cleaning/CleanRunner.cs` (add a `case "delivery-optimization":` beside `case "hibernation-file":`, ~line 124)
- Modify: `src/Brisk/Localization/Strings.resx`, `src/Brisk/Localization/Strings.tr.resx` (new key `clean.note.delivery-optimization`)
- Test: `src/BriskEngine.Tests/CleanRunnerTests.cs` (extend), `src/BriskEngine.Tests/CleanupTargetRegistryTests.cs` (extend if it pins the noBin set)

**Interfaces:**
- Consumes: `RemoveOutsideTheBin(TargetScanResult scan, bool dryRun, Action<string,long,string,string?> record, List<CleanEntry> entries, Action<string> commands)` (`CleanRunner.cs:314`) — per item: refuses unelevated, records dry-run, runs `commands(path)`, then records `"removed"` with the item's bytes only if the path is observed gone, else `"error"`.
- Consumes: `FakeRunner`/`ScriptedRunner : IProcessRunner` (`CleanRunnerTests.cs:51, :61`).
- Produces: target `delivery-optimization` is `noBin: true`; the runner empties it with exactly one `powershell.exe … Delete-DeliveryOptimizationCache -Force` call and reports per-folder "removed" bytes by observation.

- [ ] **Step 1: Write the failing tests**

In `CleanRunnerTests.cs`, mirror the arrangement of the existing `windows-old` test (search the file for `"windows-old"`: it builds a `TargetScanResult` for a registry target over real temp folders and a `ScriptedRunner` whose script deletes the path). Add:

```csharp
    [Fact]
    public void DeliveryOptimization_runs_windows_own_command_once_and_counts_what_is_gone()
    {
        // two cache folders on disk, sized; a ScriptedRunner that deletes BOTH the
        // moment "Delete-DeliveryOptimizationCache" is asked for
        // ... arrange as in the windows-old test, target id "delivery-optimization",
        //     items = the two folders, elevated = true
        var report = runner.Clean(scan, dryRun: false);   // use the real entry point the windows-old test uses

        Assert.Single(runner_commands.Where(c => c.Contains("Delete-DeliveryOptimizationCache")));
        Assert.Equal(2, report.Entries.Count(e => e.Action == "removed"));
        Assert.Equal(bytesOfFolderA + bytesOfFolderB, report.Entries.Where(e => e.Action == "removed").Sum(e => e.Bytes));
        Assert.DoesNotContain(report.Entries, e => e.Action == "recycled");
        Assert.Empty(recycler.Recycled);   // the shell is never asked
    }

    [Fact]
    public void DeliveryOptimization_dry_run_touches_nothing()
    {
        // same arrangement, dryRun: true
        Assert.All(report.Entries, e => Assert.Equal("dry-run", e.Action));
        Assert.Empty(runner_commands);
    }

    [Fact]
    public void DeliveryOptimization_reports_what_the_command_left_behind()
    {
        // ScriptedRunner deletes only folder A
        Assert.Contains(report.Entries, e => e.Action == "removed" && e.Bytes == bytesOfFolderA);
        Assert.Contains(report.Entries, e => e.Action == "error" && e.Reason!.Contains("still present"));
    }
```

(Use the real property names of `CleanEntry`/`CleanReport` and the real `FakeRecycler` collection name — read them at the top of `CleanRunnerTests.cs`.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests -c Release --filter "FullyQualifiedName~DeliveryOptimization"`
Expected: FAIL — the target still goes through the recycler (`recycled` entries, no powershell command).

- [ ] **Step 3: Write minimal implementation**

`CleanupTargetRegistry.cs` — the entry becomes:

```csharp
        // Lives under the NetworkService profile: the shell cannot recycle it
        // (DE_ACCESSDENIEDSRC, seen live 2026-09-01 — 14 folders, 7.5 GB, 0 B
        // freed). Windows owns the supported way to empty it, so this target
        // goes past the bin through that command. Regenerable: the bytes come
        // back only as Windows re-downloads what it needs.
        T("delivery-optimization", "Delivery Optimization cache", CleanupLevel.Deep, "System",
            new[] { @"%SystemRoot%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache" },
            contents: true, regen: true, admin: true, noBin: true),
```

`CleanRunner.cs` — add after the `hibernation-file` case:

```csharp
            case "delivery-optimization":
            {
                // One command for the whole cache; the per-folder observation
                // afterwards is what earns each "removed" line its byte count.
                var ran = false;
                return RemoveOutsideTheBin(scan, dryRun, Record, entries, _ =>
                {
                    if (ran) return;
                    ran = true;
                    _processRunner.Run("powershell.exe",
                        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Delete-DeliveryOptimizationCache -Force\"");
                });
            }
```

Resx — add to BOTH files:

- EN `clean.note.delivery-optimization`: `Empties Windows' peer-update download cache with Windows' own command. Nothing goes to the Recycle Bin; the bytes come back only as Windows re-downloads what it needs.`
- TR `clean.note.delivery-optimization`: `Windows'un eş güncelleme indirme önbelleğini Windows'un kendi komutuyla boşaltır. Geri Dönüşüm Kutusu'na uğramaz; baytlar yalnızca Windows ihtiyaç duyduklarını yeniden indirdikçe geri gelir.`

If `CleanupTargetRegistryTests` pins the set of `BypassesRecycleBin` targets (search for `BypassesRecycleBin`), add `delivery-optimization` to the expected set.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/BriskEngine.Tests -c Release` then `dotnet test src/Brisk.Tests -c Release` (resx parity).
Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Cleaning/CleanupTargetRegistry.cs src/BriskEngine/Cleaning/CleanRunner.cs src/BriskEngine.Tests/CleanRunnerTests.cs src/BriskEngine.Tests/CleanupTargetRegistryTests.cs src/Brisk/Localization/Strings.resx src/Brisk/Localization/Strings.tr.resx
git commit -m "the Delivery Optimization cache stops promising bytes the shell cannot move"
```

---

### Task 4: DISM's effect is observed, not assumed

**Files:**
- Modify: `src/BriskEngine/Cleaning/CleanRunner.cs:33-41` (constructor) and `:131-149` (the `component-store` case)
- Modify: `src/Brisk.Cli/Program.cs` (~line 473-491, the report printing loop)
- Test: `src/BriskEngine.Tests/CleanRunnerTests.cs` (extend)

**Interfaces:**
- Produces: `CleanRunner` constructor gains a trailing optional `Func<long>? systemDriveFreeBytes = null` (default reads `new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!).AvailableFreeSpace`).
- Produces: after a successful DISM run the entry is `("(component store)", gained, "removed", "free space rose by this much while DISM ran — DISM's own doing, observed on the drive, not a count of files")` when `gained > 0`, else `("(component store)", 0, "external", "DISM finished; free space did not rise")`.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void ComponentStore_reports_the_free_space_it_watched_rise()
    {
        var free = new Queue<long>(new[] { 100L << 30, 109L << 30 });   // 100 GB before, 109 GB after
        // construct the runner exactly as the existing component-store test does,
        // passing the new trailing argument: systemDriveFreeBytes: () => free.Dequeue()
        var entry = report.Entries.Single(e => e.Path == "(component store)");
        Assert.Equal("removed", entry.Action);
        Assert.Equal(9L << 30, entry.Bytes);
        Assert.Contains("observed", entry.Reason);
    }

    [Fact]
    public void ComponentStore_claims_nothing_when_free_space_did_not_rise()
    {
        var free = new Queue<long>(new[] { 100L << 30, 100L << 30 });
        var entry = report.Entries.Single(e => e.Path == "(component store)");
        Assert.Equal("external", entry.Action);
        Assert.Equal(0, entry.Bytes);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests -c Release --filter "FullyQualifiedName~ComponentStore"`
Expected: build error (no such constructor parameter).

- [ ] **Step 3: Write minimal implementation**

Constructor: add `Func<long>? systemDriveFreeBytes = null` as the last parameter, store `_systemDriveFreeBytes = systemDriveFreeBytes ?? (() => new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!).AvailableFreeSpace);`.

The success branch of the `component-store` case becomes:

```csharp
                    var before = _systemDriveFreeBytes();
                    var (exit, _) = _processRunner.Run("Dism.exe",
                        "/Online /Cleanup-Image /StartComponentCleanup");
                    if (exit != 0) { Record("(component store)", 0, "error", $"DISM exited {exit}"); }
                    else
                    {
                        // DISM owns the outcome; the drive is the only witness brisk
                        // has. A rise is reported as what it is — a reading, not a
                        // file count — and no rise is reported as nothing.
                        var gained = Math.Max(0, _systemDriveFreeBytes() - before);
                        if (gained > 0)
                            Record("(component store)", gained, "removed",
                                "free space rose by this much while DISM ran — DISM's own doing, observed on the drive, not a count of files");
                        else
                            Record("(component store)", 0, "external", "DISM finished; free space did not rise");
                    }
```

`Program.cs` — in the loop that prints report entries, where `entry.Action == "removed"` is handled, add one line so the caveat is printed:

```csharp
                    if (entry.Reason is not null) Console.WriteLine($"  note: {entry.Path} — {entry.Reason}");
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/BriskEngine.Tests -c Release`
Expected: all green (update any existing component-store expectation that asserted `"external"` with 0 bytes on success — keep it only for the no-rise case).

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Cleaning/CleanRunner.cs src/Brisk.Cli/Program.cs src/BriskEngine.Tests/CleanRunnerTests.cs
git commit -m "the component store step reports the rise it watched, or nothing"
```

---

### Task 5: The report card names fixes by what was done

**Files:**
- Modify: `src/Brisk/ViewModels/ReportCardModel.cs:143-148` (`FixRows`)
- Test: `src/Brisk.Tests/ReportCardModelTests.cs` (extend)

**Interfaces:**
- Consumes: `DoneLabel.For(Loc loc, string ruleId, string titleKey, string english)` (`OverviewViewModel.cs`, internal static, same assembly) — returns `rule.<id>.done` when present, else "Fixed: <title>".
- Consumes: `ReportCardModel.Build(snapshot, IReadOnlyList<UndoableFix>, Loc)` and `UndoableFix(string RuleId, DateTime FixedAtUtc)`.

- [ ] **Step 1: Write the failing test**

```csharp
    [Theory]
    [InlineData("tr", "Görsel efektler performansa göre ayarlandı")]
    [InlineData("en", null)]   // fill from Strings.resx: the value of rule.visual-effects.done
    public void Fixes_read_as_outcomes_not_as_the_problems_they_fixed(string lang, string? expectedStart)
    {
        var loc = Loc(lang);
        expectedStart ??= loc["rule.visual-effects.done"];
        var snapshot = /* the same minimal snapshot the neighbouring tests build */;
        var card = ReportCardModel.Build(snapshot,
            new[] { new UndoableFix("visual-effects", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)) }, loc);

        Assert.StartsWith(expectedStart, card.Fixes[0]);
        Assert.DoesNotContain(loc["rule.visual-effects.title"], card.Fixes[0]);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Brisk.Tests -c Release --filter "FullyQualifiedName~ReportCardModelTests"`
Expected: FAIL — `card.Fixes[0]` starts with the title ("Görsel efektler bilgisayarı yavaşlatıyor").

- [ ] **Step 3: Write minimal implementation**

In `FixRows`, replace

```csharp
            .Select(f => loc.Title($"rule.{f.RuleId}.title", f.RuleId)
```

with

```csharp
            // The card is the one artifact built to be shared; under "Applied
            // fixes" it listed "Location access is not switched off" — the
            // problem, not the outcome (live workbench, 2026-09-01). Same
            // words as the Overview's own report now.
            .Select(f => DoneLabel.For(loc, f.RuleId, $"rule.{f.RuleId}.title", f.RuleId)
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Brisk.Tests -c Release --filter "FullyQualifiedName~ReportCard"`
Expected: green, including `ReportCardRenderTests`.

- [ ] **Step 5: Commit**

```bash
git add src/Brisk/ViewModels/ReportCardModel.cs src/Brisk.Tests/ReportCardModelTests.cs
git commit -m "the report card stops listing fixes under the problems they cured"
```

---

### Task 6: The Clean page's badges and names tell the truth about the running app

**Files:**
- Modify: `src/Brisk/ViewModels/CleanViewModel.cs` (`TargetRow` constructor ~line 44-70, and every `new TargetRow(` call site)
- Modify: `src/Brisk/Views/CleanPage.xaml:436` (badge Visibility) and the deep-shelf `ToggleButton` whose template holds the `clean.advanced` TextBlock (~line 340-361)
- Test: `src/Brisk.Tests/CleanViewModelTests.cs` (extend)

**Interfaces:**
- Consumes: `FakeEngineHost.Elevated` (`Brisk.Tests/Fakes.cs:362`) and `IEngineHost.IsElevated()`.
- Produces: `TargetRow.ShowsElevationBadge` (bool) = `NeedsElevation && !isElevated`; the `TargetRow` constructor gains a `bool isElevated` parameter.

- [ ] **Step 1: Write the failing test**

Mirror the arrangement of the test at `CleanViewModelTests.cs:673` (it builds a `CleanViewModel` over a snapshot that includes Deep targets), then:

```csharp
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Elevation_badge_shows_only_when_the_app_is_not_elevated(bool elevated, bool badge)
    {
        host.Elevated = elevated;
        var vm = new CleanViewModel(/* same arguments as the test at :673 */);
        var row = /* the TargetRow with Id == "hibernation-file", reached the way that test reaches rows */;
        Assert.True(row.NeedsElevation);
        Assert.Equal(badge, row.ShowsElevationBadge);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Brisk.Tests -c Release --filter "FullyQualifiedName~Elevation_badge"`
Expected: build error — `ShowsElevationBadge` missing.

- [ ] **Step 3: Write minimal implementation**

`TargetRow`: add the constructor parameter `bool isElevated`, the property

```csharp
    /// The badge answers "will this row refuse?", not "does this target need
    /// rights?" — inside the elevated app the second is always yes and the
    /// first is always no, and the workbench found every Deep row wearing it.
    public bool ShowsElevationBadge { get; }
```

set `ShowsElevationBadge = NeedsElevation && !isElevated;` in the constructor, and pass `_host.IsElevated()` at every `new TargetRow(` site.

`CleanPage.xaml:436`: `Visibility="{Binding ShowsElevationBadge, Converter={x:Static Brisk:BoolToVis.Instance}}"`.

The deep-shelf `ToggleButton` (the one whose template contains the `Chevron` Path and the `[clean.advanced]` TextBlock): add on the ToggleButton element itself

```xml
AutomationProperties.Name="{Binding [clean.advanced], Source={x:Static loc:Loc.Instance}}"
```

(UI Automation saw this control as `[Button] ''` — a nameless button the deep shelf hides behind.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Brisk.Tests -c Release`
Expected: green (the XAML resource guards parse `CleanPage.xaml`; a typo shows up here).

- [ ] **Step 5: Commit**

```bash
git add src/Brisk/ViewModels/CleanViewModel.cs src/Brisk/Views/CleanPage.xaml src/Brisk.Tests/CleanViewModelTests.cs
git commit -m "the Clean page stops warning an administrator about administrators"
```

---

### Task 7: The hero counter moves when the bytes do

**Files:**
- Modify: `src/Brisk/ViewModels/CleanViewModel.cs:645-687` (`Refresh`), `:376` (end of `CleanSimpleAsync`) and the matching `ShowReport(...)` call in `CleanLevelAsync` (~line 521-600)
- Test: `src/Brisk.Tests/CleanViewModelTests.cs` (extend)

**Interfaces:**
- Consumes: `FakeEngineHost.Lifetime` (settable; `LifetimeReclaimedBytes()` returns it).
- Produces: `private void RefreshHero()` in `CleanViewModel` — recomputes `LifetimeText`, `LifetimeValueText`, `FreeDiskText` from the host; called at the end of `Refresh()` and after each `ShowReport(...)`.

- [ ] **Step 1: Write the failing test**

Mirror the `CleanLevelAsync` test at `CleanViewModelTests.cs:735` (fake clean service that completes a level clean). In its fake service callback set `host.Lifetime = 12_700_000_000L;` (the value the engine would have logged), then after `await vm.CleanLevelAsync(section)`:

```csharp
        Assert.Equal(Fmt.Bytes(12_700_000_000L), vm.LifetimeValueText);
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Brisk.Tests -c Release --filter "FullyQualifiedName~Lifetime"`
Expected: FAIL — `LifetimeValueText` still shows the pre-clean value ("—" or 0 B).

- [ ] **Step 3: Write minimal implementation**

Extract from `Refresh()`:

```csharp
    /// The banner says "12.7 GB removed" the moment it happens; the hero next
    /// to it said 19.9 GB until the next scan (live workbench, 2026-09-01).
    /// Same host, same numbers, same moment.
    private void RefreshHero()
    {
        var lifetime = _host.LifetimeReclaimedBytes();
        LifetimeText = _loc.F("clean.lifetime", Fmt.Bytes(lifetime));
        LifetimeValueText = Fmt.Bytes(lifetime);
        FreeDiskText = Fmt.Bytes(_host.FreeDiskBytes());
    }
```

call `RefreshHero();` where those four lines were in `Refresh()`, and add `RefreshHero();` immediately after each `ShowReport(...)` call (`CleanSimpleAsync` and `CleanLevelAsync`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Brisk.Tests -c Release --filter "FullyQualifiedName~CleanViewModelTests"`
Expected: green.

- [ ] **Step 5: Commit**

```bash
git add src/Brisk/ViewModels/CleanViewModel.cs src/Brisk.Tests/CleanViewModelTests.cs
git commit -m "the lifetime counter moves in the same breath as the banner"
```

---

### Task 8: Read-back rows say "today" and "yesterday"

**Files:**
- Modify: `src/Brisk/Localization/Strings.resx` and `Strings.tr.resx` (lines ~347-350: `readback.held`, `readback.unverified`; add `.today` and `.yesterday` variants of both)
- Modify: `src/Brisk/ViewModels/PrivacyViewModel.cs:60-70` (where `readback.held` / `readback.unverified` are formatted)
- Test: `src/Brisk.Tests/PrivacyViewModelTests.cs` (extend the expectation builder at ~line 395-410)

**Interfaces:**
- Consumes: `ReadBackRow.DaysAgo(DateTime fixedAtUtc, DateTime nowUtc)` (int).
- Produces: keys `readback.held.today`, `readback.held.yesterday`, `readback.unverified.today`, `readback.unverified.yesterday` in both languages; the view model picks the variant by `DaysAgo`: 0 → `.today`, 1 → `.yesterday`, else the `{0}` form.

- [ ] **Step 1: Write the failing test**

In the expectation builder used around `PrivacyViewModelTests.cs:395` (it computes `days` and then `loc.F("readback.held", days)`), make the expected text follow the same rule the view model will:

```csharp
        static string HeldText(Loc loc, int days) => days switch
        {
            0 => loc["readback.held.today"],
            1 => loc["readback.held.yesterday"],
            _ => loc.F("readback.held", days),
        };
```

and add cases where `fixedAt == now` and `fixedAt == now.AddDays(-1)` for a Held row, asserting the row's text equals `HeldText(loc, 0)` / `HeldText(loc, 1)` and does NOT contain `"0 "` or `"1 days"`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Brisk.Tests -c Release --filter "FullyQualifiedName~PrivacyViewModelTests"`
Expected: FAIL — the row still reads "0 gün önce" / "0 days ago" (and the parity test fails until both resx have the keys).

- [ ] **Step 3: Write minimal implementation**

Resx (both files):

- EN `readback.held.today`: `You switched this off today; it still reads as off`
- EN `readback.held.yesterday`: `You switched this off yesterday; it still reads as off`
- TR `readback.held.today`: `Bugün kapattın, hâlâ kapalı`
- TR `readback.held.yesterday`: `Dün kapattın, hâlâ kapalı`
- `readback.unverified.today` / `.yesterday` in both languages: copy the existing `readback.unverified` value verbatim and replace only its `{0} days ago` / `{0} gün önce` clause with `today` / `yesterday` and `bugün` / `dün` (keep every other word — that sentence carries the policy caveat).

`PrivacyViewModel.cs` — a small helper next to the formatting:

```csharp
    private static string ByAge(Loc loc, string key, int days) => days switch
    {
        0 => loc[$"{key}.today"],
        1 => loc[$"{key}.yesterday"],
        _ => loc.F(key, days),
    };
```

and use `ByAge(loc, "readback.held", DaysAgo(...))` and `ByAge(loc, "readback.unverified", DaysAgo(...))` where those two keys are formatted today.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Brisk.Tests -c Release`
Expected: green, parity included.

- [ ] **Step 5: Commit**

```bash
git add src/Brisk/Localization/Strings.resx src/Brisk/Localization/Strings.tr.resx src/Brisk/ViewModels/PrivacyViewModel.cs src/Brisk.Tests/PrivacyViewModelTests.cs
git commit -m "the read-back learns the words today and yesterday"
```

---

### Task 9: Whole-suite gate

**Files:** none new.

- [ ] **Step 1: Run the full suite**

Run: `dotnet test brisk.sln -c Release`
Expected: 0 failed, 0 warnings, total ≥ 1326 + the tests added above.

- [ ] **Step 2: Publish a copy and exercise the two live-only paths (no --yes)**

Run: `dotnet publish src/Brisk.Cli/Brisk.Cli.csproj -c Release -r win-x64 --self-contained -o C:\Users\MERT\AppData\Local\Temp\claude\C--Users-MERT-Desktop-brisk\a1697257-bf32-423d-a374-a2f67fb36093\scratchpad\run` (if the project path differs, take it from `scripts/publish.ps1`), then from that folder:

`brisk.exe clean --target delivery-optimization` — expected: `PLAN (nothing deleted)` listing the cache folders, no error lines.
`brisk.exe fix --all` — expected: dry-run lines only.

- [ ] **Step 3: Report**

State the final test count and paste the two command outputs. Do not tag, do not push, do not merge — those are the maintainer's.

## Self-review

- Coverage: findings 1→T3, 2→T4, 3→T1, 4→T2, 5→T5, 6→T6, 7→T7, 8→T8. The nameless deep-shelf toggle rides in T6.
- Placeholders: the tests in T2, T3, T5, T6, T7 tell the engineer to copy an *existing named test's arrangement* because those fixtures are large; every assertion is spelled out. No TBDs.
- Types: `ShowsElevationBadge`, `RefreshHero`, `Describe`, `NameMatches`, `ByAge`, `systemDriveFreeBytes` are each defined once and used with the same name everywhere.
