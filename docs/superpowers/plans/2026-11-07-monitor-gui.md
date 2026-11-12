# Monitor GUI, Yer aç levels and new space targets: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the approved Monster-style live monitor on Home, scan-reveal pages without checkboxes, Yer aç MIN/MID/MAX levels, and the new space targets that close the 39 → ~93 GB gap.

**Architecture:** The level is a property of a Space *row* (`SpaceLevel` on `RowDef`/`ActionItem`), not of a cleanup target; `CleanupLevel` and `clean --level safe|developer|deep` stay as they are. New targets are either path-template targets (registry entries) or *dynamic* targets whose items come from a finder that the `SafetyValidator` re-checks. Live monitor data comes from a new engine sampler (`BriskEngine/Live/SystemMonitor.cs`) plus one-shot specs and battery reads; the GUI only binds.

**Tech Stack:** C# / .NET 8, WPF (`net8.0-windows`), xUnit. Build: `dotnet build brisk.sln -c Debug`. Tests: `dotnet test brisk.sln`.

**Spec:** `docs/superpowers/specs/2026-11-07-gui-monitor-redesign-design.md` (read it first). Visual authority: `docs/superpowers/specs/2026-11-07-gui-monitor-mockups/` (`home.html`, `pages.html`, `space-levels.html`; grep them, do not read them whole).

## Global Constraints

- Stack: WPF on `net8.0-windows`, app elevated (`requireAdministrator`). GUI `src/Brisk`, engine `src/BriskEngine`, CLI `src/Brisk.Cli`.
- Motion ≤ 20 fps, rain in four lanes, glow without blur; pixel fire ≈ 40×14 cells capped at 20 fps; honour Windows "show animations" and the "moving background" setting (reduced motion = static frames, instant numbers).
- All strings through `Loc`, TR and EN. TR copy in the spec and mockups is final. Every new key in both `Strings.resx` and `Strings.tr.resx` (the resx test checks `ActionCatalog.AllKeys()`).
- Unreadable sensor/value = "—" or hidden. Never a guess. Never count an unknown size.
- Personal files (VM disks, `.iso`, AVDs, BlueStacks) never count toward any GB number and are never deletable.
- No confirm dialog before deleting. MAX warns visually only.
- Yer aç has no undo. Geri al reverts Speed and Privacy only.
- Colours: MIN `#5fd4e8`, MID `#ffb020`, MAX `#ff3b3b`.
- Existing suite stays green. Plain conventional commits ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. No push, no tag.
- Every subagent seat runs on opus.

## Review Focus

1. **A dynamic target deleting outside its rule** (a `node_modules` that is a junction, a project touched last week, a folder under Desktop that only *looks* like a venv): the validator must re-check the rule on the real path at delete time, not trust the scan. Tests in Task 3.
2. **Restore points with 0 or 1 shadow copy, or `vssadmin` missing/denied:** no row, nothing deleted, never the newest. Tests in Task 4.
3. **Desktop PC with no battery, VM with no GPU counters, non-NVIDIA GPU:** blocks hide or show "—", nothing throws on the sampler thread. Tests in Task 6 and 7.
4. **A level switched while a run or scan is going:** the selection used by the run is the one at press time; the button stays disabled while busy. Test in Task 8.
5. **`.iso` in Downloads:** today `old-installers` deletes it; after this wave it is a personal file, locked and uncounted. Test in Task 1 and Task 5.

---

### Task 1: Space levels on rows, catalog re-sort, `brisk run --level`, `--group` fix

**Files:**
- Modify: `src/BriskEngine/Actions/ActionCatalog.cs`, `src/BriskEngine/Actions/ActionModels.cs`, `src/BriskEngine/Actions/ActionPlanBuilder.cs`, `src/BriskEngine/Cleaning/CleanupTargetRegistry.cs` (drop `*.iso` from `old-installers`), `src/Brisk.Cli/CliParser.cs`, `src/Brisk.Cli/Program.cs` (`RunActions`, `PrintPlan`), both resx files
- Test: `src/BriskEngine.Tests/ActionPlanBuilderTests.cs`, `src/BriskEngine.Tests/CliActionTests.cs`, `src/BriskEngine.Tests/CliParserTests.cs`, `src/BriskEngine.Tests/CleanupTargetRegistryTests.cs`

**Interfaces:**
- Produces: `public enum SpaceLevel { Min, Mid, Max }` (in `ActionModels.cs`); `RowDef.Level` and `ActionItem.Level` of type `SpaceLevel?` (null for Speed/Privacy rows, set on every Space row); `RowDef.Lost` / `ActionItem.Lost` (`bool`, true = amber "geri alınamaz"); `ActionSelection.ForLevel(ActionPlan plan, SpaceLevel level, IReadOnlySet<string> skipped)` returning a selection with Space rows of `Level <= level` minus `skipped`, plus the default Speed/Privacy rows; `CliCommand.SpaceLevel` (`string?`, "min|mid|max").

Row ids and levels (TR title from the spec table; cost = the spec's grey reason; EN copy is yours):

| Level | Row id | Links | Lost |
|---|---|---|---|
| Min | `space.caches` | unchanged | no |
| Min | `space.developer` | unchanged | no |
| Min | `space.crash-dumps` | `live-kernel-reports` (Task 2) | no |
| Min | `space.setup-logs` | `panther-logs` (Task 2) | no |
| Min | `space.driver-leftovers` | `driver-installer-leftovers` (Task 2) | no |
| Mid | `space.hibernation` | unchanged, now `reversible: false` (spec: not offered as undo) | no |
| Mid | `space.shaders` | unchanged | no |
| Mid | `space.project-deps` | `stale-project-deps` (Task 3) | no |
| Mid | `space.downloads` | unchanged | yes |
| Mid | `space.recycle-bin` | unchanged | yes |
| Max | `space.archives` | `archive-beside-extracted` (Task 3) | yes |
| Max | `space.profile-git` | `profile-git` (Task 2) | yes |
| Max | `space.restore-points` | `restore-points` (Task 4) | yes |
| Max | `space.windows-old` | unchanged | yes |
| Max | `space.docker` | unchanged | yes |

`DefaultOn` of a Space row = `Level == Min` (replaces the per-row `on:` flags; the laptop hibernation cost string stays). Rows whose targets do not exist yet are simply absent from plans until their task lands (the builder's default case already yields nothing at 0 bytes).

- [ ] **Step 1: Write failing tests**
  - `Every_space_row_has_a_level_and_no_other_row_does` — `ActionCatalog.Rows.Where(r => r.Group == Space).All(r => r.Level is not null)` and the reverse for Speed/Privacy.
  - `Default_selection_ticks_only_min_space_rows` — plan with one Min, one Mid, one Max row → `ActionSelection.Default` contains only the Min id.
  - `ForLevel_is_cumulative_and_honours_skips` — Mid selects Min+Mid, not Max; a skipped Min id is absent.
  - `Old_installers_never_takes_an_iso` — no template of `old-installers` ends in `.iso`.
  - `Run_level_parses_min_mid_max` — for `run`, `--level` accepts only `min|mid|max` into `CliCommand.SpaceLevel` (`CliParser.Parse(["run","--level","mid"]).SpaceLevel == "mid"`; `run --level deep` → error); for `clean`, `--level` accepts only `safe|developer|deep`, exactly as today.
  - `Run_group_prints_only_that_group` — `PrintPlan` with a Space+Speed plan and `--group speed` writes no Space title (fixes the spec's open item).
- [ ] **Step 2:** `dotnet test src/BriskEngine.Tests --filter "FullyQualifiedName~ActionPlanBuilderTests|FullyQualifiedName~CliActionTests|FullyQualifiedName~CliParserTests|FullyQualifiedName~CleanupTargetRegistryTests"` → the new tests FAIL.
- [ ] **Step 3: Implement.** `PrintPlan` gets the group filter (`plan.Groups.Where(g => group is null || g.Kind == group)`); `RunActions` builds its selection with `ForLevel(plan, level ?? Min, empty)` then `Only(group)` when `--group` is set. Add resx keys for every new row (title, cost) in both languages.
- [ ] **Step 4:** Same filter → PASS; then `dotnet test brisk.sln` → all green.
- [ ] **Step 5:** `git commit -m "feat(engine): space rows carry MIN/MID/MAX levels; run --level; run --group prints only its group"`

### Task 2: Path-template targets: kernel dumps, setup logs, driver leftovers, profile .git

**Files:**
- Modify: `src/BriskEngine/Models/CleanupTarget.cs` (add `int? MinAgeDays = null` as the last positional parameter), `src/BriskEngine/Cleaning/CleanupTargetRegistry.cs`, `src/BriskEngine/Cleaning/Scanner.cs` (replace the `old-installers` id check with `target.MinAgeDays`)
- Test: `src/BriskEngine.Tests/CleanupTargetRegistryTests.cs`, `src/BriskEngine.Tests/ScannerTests.cs`, `src/BriskEngine.Tests/SafetyValidatorTests.cs`

**Interfaces:**
- Produces: target ids `live-kernel-reports`, `panther-logs`, `driver-installer-leftovers`, `profile-git` (consumed by Task 1's rows); `CleanupTarget.MinAgeDays`.

Rules (all `CleanupLevel.Deep`, `admin: true` except `profile-git`):
- `live-kernel-reports`: `%SystemRoot%\LiveKernelReports\*.dmp`, `%SystemRoot%\LiveKernelReports\*\*.dmp`, `%SystemRoot%\Minidump\*.dmp`, `%SystemRoot%\MEMORY.DMP`; `MinAgeDays = 7`; skip any path already covered by an existing target (check `crash-dumps`, `wer-*`).
- `panther-logs`: `*.log` and `*.etl` directly in `%SystemRoot%\Panther` and `%SystemRoot%\Panther\UnattendGC`; `MinAgeDays = 7`; the scanner yields nothing while `%SystemDrive%\$WINDOWS.~BT` exists (an upgrade in progress). Never `*.xml`.
- `driver-installer-leftovers`: extracted vendor installers — `%SystemDrive%\NVIDIA\DisplayDriver`, `%SystemDrive%\AMD\*` (directories), and the NVIDIA App OTA folder. **Confirm the OTA path read-only on this PC first** (`Get-ChildItem "$env:ProgramData\NVIDIA Corporation" -Recurse -Directory -Filter ota-artifacts`) and use the found parent pattern; if it cannot be confirmed, leave it out and say so in the commit body.
- `profile-git`: `~\.git` as a whole directory (not contents), only when it is a directory and not a reparse point; `noBin` stays false (Space runs delete permanently already).
- `old-installers` gets `MinAgeDays = 30` (keeps `Scanner.OldInstallerMinAgeDays` as the value).

- [ ] **Step 1: Write failing tests** — `New_targets_are_registered_with_rules` (ids, admin flags, `MinAgeDays`); `Min_age_skips_young_files` (temp dir with a 1-day and a 10-day `.dmp`, target with `MinAgeDays=7` → only the old one); `Panther_target_is_empty_during_an_upgrade` (inject the `$WINDOWS.~BT` check through a `Func<bool>` on the scanner or an internal seam — your choice, tested); `Profile_git_is_authorized_only_as_itself` (validator allows `~\.git`, denies `~\.gitconfig` and `~\Documents\x\.git`).
- [ ] **Step 2:** run them → FAIL.
- [ ] **Step 3:** implement.
- [ ] **Step 4:** `dotnet test brisk.sln` → green.
- [ ] **Step 5:** `git commit -m "feat(engine): clean old kernel dumps, setup logs, driver installer leftovers and a home-folder .git"`

### Task 3: Dynamic targets: stale project deps and archives beside their extracted copy

**Files:**
- Create: `src/BriskEngine/Cleaning/DynamicTargets.cs`
- Modify: `CleanupTarget.cs` (add `bool Dynamic = false`), `CleanupTargetRegistry.cs`, `Scanner.cs` (dynamic targets take items from `DynamicTargets.Find`), `src/BriskEngine/Safety/SafetyValidator.cs`
- Test: `src/BriskEngine.Tests/DynamicTargetsTests.cs`, `SafetyValidatorTests.cs`

**Interfaces:**
- Produces: `public static class DynamicTargets { IEnumerable<string> Find(string targetId, IReadOnlyList<string> roots, DateTime nowUtc, CancellationToken ct); bool Matches(string targetId, string realPath, DateTime nowUtc); IReadOnlyList<string> DefaultRoots(); }`; target ids `stale-project-deps` (Mid row) and `archive-beside-extracted` (Max row).

Rules:
- Roots (`DefaultRoots`): `%USERPROFILE%\Desktop`, `Documents`, `Downloads`, `source\repos`, `Projects`, `dev`, `code`, `repos` — the existing ones only. Walk depth ≤ 4 below a root, never follow reparse points, never descend into a matched folder.
- `stale-project-deps`: a directory named `node_modules` or `.next` whose parent holds `package.json`, or a directory holding `pyvenv.cfg` (any name); stale when the newest `LastWriteUtc` of the parent folder's top-level entries **other than the dep folder** is ≥ 90 days before `nowUtc`.
- `archive-beside-extracted`: a `.7z`/`.zip`/`.rar` file ≥ 1 GiB with a sibling directory named exactly its stem (case-insensitive) whose size ≥ the archive's size. The archive is the item; the extracted copy is never touched.
- Validator: for a `Dynamic` target, `Authorize` = real path resolvable **and** not under System32/WinSxS/ProgramFiles(x86) **and** under one of `DefaultRoots()` **and** `DynamicTargets.Matches` is true at that moment. This is the only path allowed under Desktop/Documents; the profile-root equality rule still denies.

- [ ] **Step 1: Write failing tests** (temp-dir fixtures, injected roots and `nowUtc`): `Finds_stale_node_modules_with_package_json`; `Ignores_project_touched_within_90_days`; `Ignores_node_modules_without_package_json`; `Finds_venv_by_pyvenv_cfg`; `Never_follows_a_junction` (create a junction with `mklink /J` via `cmd`; skip the test with a reason if creation fails); `Finds_archive_only_with_bigger_sibling_folder`; `Validator_rechecks_rule_at_delete_time` (match, then touch `package.json` → `Authorize` denies); `Validator_denies_dynamic_path_outside_roots`.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement. **Step 4:** `dotnet test brisk.sln` → green.
- [ ] **Step 5:** `git commit -m "feat(engine): find stale project dependency folders and archives already extracted beside them"`

### Task 4: Restore points (all but the newest, size shown as "—")

**Files:**
- Modify: `src/BriskEngine/Actions/SpaceProbe.cs` (`int? RestorePointCount()` on `ISpaceProbe`, parsed from `vssadmin list shadows /for=%SystemDrive%`), `CleanupTargetRegistry.cs` (`restore-points`: no templates, `admin: true`, `noBin: true`), `src/BriskEngine/Cleaning/CleanRunner.cs` (case `restore-points`: run `vssadmin delete shadows /for=<drive> /oldest /quiet` exactly `count - 1` times; record the observed free-space rise as `removed`, like `component-store`), `ActionPlanBuilder.cs` (row `space.restore-points` yields with `Bytes = null` when count ≥ 2), `ActionRunner.IsPathless` (add the id)
- Test: `ActionPlanBuilderTests.cs`, `CleanRunnerTests.cs`, a parser test beside `SpaceProbe` tests

**Interfaces:** Consumes Task 1's row. Produces `ISpaceProbe.RestorePointCount()`; Space rows may carry `Bytes == null`, which every total treats as unknown (not 0, not counted).

- [ ] **Step 1: Failing tests** — `No_row_with_zero_or_one_restore_point`; `Row_has_unknown_size`; `Deletes_count_minus_one_oldest` (fake `IProcessRunner` records exactly N-1 delete calls, never a call without `/oldest`); `Parse_counts_shadow_ids` on a captured `vssadmin` text sample; `Probe_failure_means_no_row`.
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** green.
- [ ] **Step 5:** `git commit -m "feat(engine): remove old restore points, keep the newest, never guess their size"`

### Task 5: Personal files (locked, uncounted)

**Files:**
- Create: `src/BriskEngine/Cleaning/PersonalFiles.cs`
- Test: `src/BriskEngine.Tests/PersonalFilesTests.cs`

**Interfaces:** Produces `public sealed record PersonalFile(string Name, string Path, long Bytes);` and `public static IReadOnlyList<PersonalFile> PersonalFiles.Find(IReadOnlyList<string> roots, CancellationToken ct)` (≥ 1 GiB each, largest first) plus `PersonalFiles.DefaultRoots()`.

Rules: `.vdi/.vhdx/.vmdk` anywhere under the roots except WSL (`%LOCALAPPDATA%\Packages\*\LocalState\ext4.vhdx`) and Docker (`%LOCALAPPDATA%\Docker\wsl\**`) stores; `.iso`; Android AVDs = each `~\.android\avd\*.avd` directory as one entry; BlueStacks = `%ProgramData%\BlueStacks_nxt` as one entry. Roots: the user folders (Desktop, Documents, Downloads, Videos), `~\VirtualBox VMs`, plus the AVD and BlueStacks locations. Name = file or folder name.

- [ ] **Step 1: Failing tests** — `Lists_vm_disk_and_iso_over_1_GiB` (sparse files via `FileStream.SetLength`); `Skips_wsl_and_docker_stores`; `Avd_folder_is_one_entry`; `Below_1_GiB_is_ignored`.
- [ ] **Steps 2–4:** FAIL → implement → green.
- [ ] **Step 5:** `git commit -m "feat(engine): list the user's own large files so the space page can show them locked"`

### Task 6: Battery health and hardware specs

**Files:**
- Create: `src/BriskEngine/Live/Battery.cs`, `src/BriskEngine/Live/HardwareSpecs.cs`
- Test: `src/BriskEngine.Tests/BatteryTests.cs`, `src/BriskEngine.Tests/HardwareSpecsTests.cs`

**Interfaces:** Produces
- `public sealed record BatteryInfo(int ChargePercent, bool PluggedIn, int? HealthPercent, int? CycleCount);` `public interface IBatteryProbe { BatteryInfo? Read(); }` (null = no battery) and `RealBatteryProbe` (charge/AC from `GetSystemPowerStatus`; WMI `root\wmi` `BatteryFullChargedCapacity`, `BatteryStaticData.DesignedCapacity`, `BatteryCycleCount`). `public static int? Battery.Health(long? full, long? design)` = `round(100*full/design)`, null when either is null or ≤ 0, not clamped.
- `public sealed record SpecsInfo(string? CpuName, int? CpuMhz, int? Cores, int? Threads, IReadOnlyList<string> Gpus, long? RamInstalledBytes, long? RamUsableBytes, string? DiskModel, long? DiskBytes);` `public interface ISpecsProbe { SpecsInfo Read(); }` and `RealSpecsProbe` (WMI `Win32_Processor`, `Win32_VideoController` names, `Win32_PhysicalMemory` sum, `GlobalMemoryStatusEx`, the physical disk that holds `%SystemDrive%`). Every field independently null/empty on failure; `Read` never throws.

- [ ] **Step 1: Failing tests** — `Health_is_full_over_design`; `Health_unknown_without_design`; `No_battery_reads_null` (fake); `Specs_probe_never_throws` (real probe, asserts only no exception and `Gpus != null`).
- [ ] **Steps 2–4:** FAIL → implement → green.
- [ ] **Step 5:** `git commit -m "feat(engine): read battery health and cycle count, and the machine's hardware specs"`

### Task 7: Live system sampler

**Files:**
- Create: `src/BriskEngine/Live/SystemMonitor.cs`
- Test: `src/BriskEngine.Tests/SystemMonitorTests.cs`

**Interfaces:** Consumes `ISensorProbe` (existing), `IBatteryProbe` (Task 6). Produces
`public sealed record ProcLoad(string Name, double Value);`
`public sealed record MonitorSample(double? CpuPercent, double? GpuPercent, double? RamPercent, long RamUsedBytes, double? CpuTempC, double? GpuTempC, long? UpBytesPerSec, long? DownBytesPerSec, BatteryInfo? Battery, IReadOnlyList<ProcLoad> TopCpu, IReadOnlyList<ProcLoad> TopGpu, IReadOnlyList<ProcLoad> TopRam);`
`public sealed class SystemMonitor { MonitorSample Sample(); }` — call once a second from a background thread; the first call primes deltas (CPU from `GetSystemTimes`, network from `NetworkInterface` byte counters of up, non-loopback adapters, per-process CPU from `Process.TotalProcessorTime`), GPU % = sum of PDH `GPU Engine(*engtype_3D)\Utilization Percentage` clamped to 100 (null when the category is missing), per-process GPU from the `pid_N` instance names, RAM from `GlobalMemoryStatusEx`, temps from `ISensorProbe` through `SensorReading.IsReal`. Top lists: 3 entries, display names via the existing `AppNamer`. Each reader isolated: one failing reader leaves its fields null, `Sample` never throws. The internal readers sit behind small interfaces so the test can fake them.

- [ ] **Step 1: Failing tests** — `First_sample_has_no_cpu_delta_second_has`; `Gpu_null_when_counters_missing`; `Top_lists_hold_three_largest`; `A_throwing_reader_nulls_only_its_fields`.
- [ ] **Steps 2–4:** FAIL → implement → green.
- [ ] **Step 5:** `git commit -m "feat(engine): live CPU, GPU, memory, network and top-process sampler"`

### Task 8: Session: levels, skip, Hepsini yap, undo scope, no confirm

**Files:**
- Modify: `src/Brisk/Session/ActionSession.cs`, `IActionBackend.cs`, `RealActionBackend.cs`, `HomeFigures.cs`; `src/Brisk.Tests/Session/FakeBackend.cs`
- Test: `src/Brisk.Tests/Session/ActionSessionTests.cs`, `HomeFiguresTests.cs`

**Interfaces:** Consumes Task 1 (`SpaceLevel`, `ForLevel`, `Lost`), Task 5 (`PersonalFiles.Find`). Produces on `ActionSession`:
- `SpaceLevel Level { get; }`, `void SetLevel(SpaceLevel level)` (refused while busy);
- `bool IsSkipped(string rowId)`, `void ToggleSkip(string rowId)` (a Space row above the level is locked: toggling it does nothing);
- `long LevelBytes(SpaceLevel level)` = Σ known `Bytes` of non-skipped Space rows with `Level <= level`;
- `IReadOnlyList<PersonalFile> PersonalFiles { get; }` (filled during the look, off the UI thread);
- `Task RunAllAsync()` = one run with `ForLevel(plan, Min, skipped)` (Speed/Privacy default rows minus skipped);
- `Task RunAsync(ActionGroupKind group)` uses the level for Space; the selection is captured at press time.
- `CanUndo` true only when the last run holds an undoable Speed or Privacy rule.
- Remove `Unrecoverable`/`NeedsConfirmation`/`skipUnrecoverable` and the confirm sheet (`Ui/Shell/ConfirmSheetViewModel.cs` and its XAML use).
- `IActionBackend` gains `IReadOnlyList<PersonalFile> PersonalFiles()`, `long SystemDriveFreeBytes()`, `SpecsInfo Specs()`, `MonitorSample SampleMonitor()`.

- [ ] **Step 1: Failing tests** — `Level_selection_is_cumulative`; `Locked_row_above_level_cannot_be_toggled`; `Run_uses_selection_at_press_time` (change level during the fake run → run's selection unchanged); `Run_all_uses_min_and_defaults`; `Space_only_run_offers_no_undo`; `Unknown_size_rows_add_nothing_to_level_bytes`.
- [ ] **Steps 2–4:** FAIL → implement → green (fix existing tests that used the confirm path).
- [ ] **Step 5:** `git commit -m "feat(gui): session runs space by level, rows skip by tap, run-all at MIN, undo only speed and privacy"`

### Task 9: Shell, nav badges, Ayarlar restyle, first launch

**Files:** `src/Brisk/Ui/Shell/ShellViewModel.cs`, `ShellWindow.xaml`, `SettingsView.xaml`, `src/Brisk/Ui/Theme/Monster.xaml`; test `src/Brisk.Tests/Ui/ShellViewModelTests.cs`

**Interfaces:** Consumes Task 8. Produces `ShellViewModel.Badge(Place)` → `NavBadge(string? Text, bool Spinning, bool Done)`: Space = level GB (`Fmt`-style "45 GB"), Speed/Privacy = counts of non-skipped rows, spinner until the look delivers, `Done` after that page's run.

- [ ] Tests first: `Badges_spin_until_scan_arrives`, `Space_badge_follows_level`, `Badge_done_after_page_run`, `Starts_on_home`. Then implement: nav tiles per `home.html`; Settings keeps its three settings + GitHub link, restyled to the stage. Build, `dotnet test brisk.sln`, commit `feat(gui): nav badges and restyled settings on the monitor stage`.

### Task 10: Home = live monitor

**Files:** `src/Brisk/Ui/Home/HomeView.xaml`, `HomeViewModel.cs`, `src/Brisk/Ui/Controls/*` (reuse `TickGauge`, `SegmentBar`, `SatelliteRing`, `HeroNumeral`, `Atmosphere`); test `src/Brisk.Tests/Ui/HomeViewModelTests.cs`

**Interfaces:** Consumes Task 6–8. `HomeViewModel` exposes `Part Selected` (`enum Part { Cpu, Gpu, Ram }`), `SelectPart(Part)` (satellites only), `HighlightSpec(SpecBlock)` (right icons only, never changes `Selected`), gauge value/temperature strings ("—" when null), left column (net, battery block hidden when `Battery is null`, health/cycles lines hidden when null, heat), right column specs and top 3 for `Selected`, bottom strip (`"brisk hazır: {gb} · {n} uygulama · {m} ayar"` + Hepsini yap → `"Son işlem: … · Geri al"`; Geri al shown only when `CanUndo`). Sampling: `IUiTimers` 1 s tick, sample off the UI thread.

- [ ] Tests first: `Spec_icon_never_rotates_gauge`, `Satellite_rotates_gauge`, `No_battery_hides_block`, `Unread_temp_shows_dash`, `Strip_after_run_all_offers_undo_only_for_speed_privacy`. Implement per `home.html`; satellites must not overlap side columns at 1400×900 and at the minimum window size (`FirstWindowSize`) — check with an offscreen render (`Views/OffscreenLayout.cs`) and assert no bounds intersection in a test. Build, test, commit `feat(gui): home becomes a live system monitor`.

### Task 11: Hızlandır and Gizlilik pages: scan reveal, tap to skip, one button

**Files:** `src/Brisk/Ui/Pages/GroupPage.xaml`, `GroupPageViewModel.cs`, `RowViewModel.cs`; test `src/Brisk.Tests/Ui/GroupPageViewModelTests.cs`

**Interfaces:** Consumes Task 8. Rows reveal one by one as results exist (never ahead of the data; reduced motion = all at once); `RowViewModel.Skipped`, `Tap()`; button text `"{n} uygulamayı durdur"` / `"{n} ayarı kapat"`, `"Hiçbir şey seçili değil"` disabled at 0; run turns rows green with "✓ durdu"/"✓ kapandı", then "Durduruldu"/"Kapatıldı" and "✓ Tamam"; Geri al after done. Right column: Hızlandır "Bırakıyorum" = protected startup rows (non-default with cost) with "kalsın", "İstersen ekle" = other non-default rows with their honest cost; Gizlilik "İstersen ekle".

- [ ] Tests first: `Reveal_never_outruns_results`, `Tap_toggles_skip_and_total`, `Button_disabled_when_nothing_selected`, `Undo_visible_after_done`. Implement per `pages.html`. Build, test, commit `feat(gui): speed and privacy pages scan, reveal and act with one button`.

### Task 12: Yer aç page: MIN/MID/MAX

**Files:** `src/Brisk/Ui/Pages/SpacePage.xaml` (+ `.cs`), `src/Brisk/Ui/Pages/SpacePageViewModel.cs`, `src/Brisk/Ui/Controls/PixelFire.cs`; test `src/Brisk.Tests/Ui/SpacePageViewModelTests.cs`

**Interfaces:** Consumes Task 5, 8. Level tiles (keyboard-focusable, cumulative GB, colours from Global Constraints), C: block ("Şu an boş" / "Sonra boş" → "Şimdi boş"), "Sileceklerim" grouped by level with locked rows above the level, Lost rows with amber reason, unknown size shows "—", locked "Senin dosyaların · brisk dokunmaz" list, caption "N GB gereksiz dosya bulundu" (N = MAX total of known sizes), button "N GB boşalt" in the level colour, done captions per level from the spec. `PixelFire`: Doom-fire on a ≈40×14 cell `WriteableBitmap`, ≤ 20 fps, opacity 0.3 rest / 0.85 selected-focused-hovered, dark top scrim, one static frame under reduced motion; no Geri al.

- [ ] Tests first: `Tiles_show_cumulative_known_bytes`, `Rows_above_level_are_locked`, `Personal_files_never_in_totals`, `Done_caption_per_level`, `Fire_static_under_reduced_motion` (extend `MotionBudgetTests` for the ≤ 20 fps cap). Implement per `space-levels.html`. Build, test, commit `feat(gui): free-space page with MIN, MID and MAX levels`.

### Task 13: Whole-branch verification and hand-over

- [ ] `dotnet build brisk.sln -c Release` → 0 errors; `dotnet test brisk.sln` → all green, count reported.
- [ ] `brisk run --group speed` (no `--yes`) prints only Speed rows; `brisk run --level max` lists the MAX rows (read-only).
- [ ] Render each page headless or offscreen at 1400×900 and at minimum size; compare with the mockups; fix overlaps.
- [ ] Whole-branch review (superpowers:requesting-code-review, opus), fix findings, commit.
- [ ] Build `brisk-app` with `scripts/publish.ps1` and launch it for him; do not press Hepsini yap while his VS Code agents run.
