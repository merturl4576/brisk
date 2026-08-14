# brisk — Design Spec

Date: 2026-08-14
Status: Approved in chat; this document is the written record.

## One-liner

brisk scans a Windows PC, explains **why** it is slow with evidence, fixes the
findings in one click with full undo, and reclaims disk space through an
allowlist-only cleaner. Free, open source, MIT. No account, no cloud, no
telemetry, no AI API — every diagnosis and fix is a deterministic rule.

## Goals

1. Productize the "diagnostic report" experience: plain-language findings
   (power plan, GPU assignment, startup bloat, thermals, disk breakdown) with
   an expected-impact rating, and a one-click **Fix all** that is journaled and
   reversible.
2. A trustworthy disk cleaner in the dusty mold: fixed allowlist, preview
   sizes first, Recycle Bin + undo, written action log.
3. GitHub stars. Positioning: *"free, open-source CCleaner alternative that
   tells you why your PC is slow — and fixes it."* The trust story (open rules,
   undo everything, no telemetry) is the product.

## Non-goals (explicitly never / not in v1)

- **Never:** registry "cleaning", driver updater bundles, antivirus, dark
  patterns, telemetry. These define the scam category we position against.
- **Not in v1:** AI/LLM API layer (optional bring-your-own-key idea parked for
  later), scheduled auto-clean, duplicate finder, Windows.old removal,
  hibernation-file management.

## Stack decision

**.NET 8 (C#) + WPF**, engine as a UI-free class library, CLI sharing the
engine. Considered Tauri/Rust (sensor access would require driver-level work;
LibreHardwareMonitorLib is .NET) and WinUI 3 (packaging friction for unsigned
distribution, tooling churn). WPF's dated default look is overcome with a
custom design system, as dusty does with SwiftUI.

Runtime target: Windows 10 1809+ and Windows 11, x64. Distribution builds:
framework-dependent (small, needs .NET runtime) and self-contained single exe
(portable). If antivirus false-positives hit the single-file exe, the
framework-dependent build is the documented fallback.

## Architecture

```
Brisk (WPF app + tray icon)        brisk CLI (scan / fix / clean, --json)
        └───────────────┬───────────────┘
                  BriskEngine        class library, no UI, unit tested
                  ├── DiagnosticRule registry   (rules are data)
                  ├── CleanupTarget registry    (targets are data)
                  ├── Scanner (concurrent, live progress, cancellable)
                  ├── FixRunner (+ fix journal, undo)
                  ├── CleanRunner (+ Recycle Bin, restore)
                  └── ActionLog (JSONL)
                          │  every mutation MUST pass through
                  SafetyValidator    single authorization point
```

Dependency direction: UI → Engine → SafetyValidator. No UI code bypasses the
engine; no mutation bypasses the validator. Rules and targets are **data, not
code** — a community PR for a new rule or cleanup target is one registry entry
plus a test.

### Solution layout

```
src/BriskEngine/          class library (net8.0)
src/BriskEngine.Tests/    xUnit
src/Brisk/                WPF app
src/Brisk.Cli/            console app ("brisk")
docs/                     specs, architecture, growth assets
```

## Diagnostics

A `DiagnosticRule` is a registry entry: `id`, `severity`, detection (reads a
data source, produces evidence), localized explanation template (EN + TR),
expected-impact rating (1–5), fix action + undo action, and a **category**:

- **auto** — included in "Fix all", journaled, one-click undo.
- **confirm** — fixable, but applied only per-item with explicit consent
  (visible behavior changes, per-item choices).
- **advise** — no programmatic fix (physical work, user data). Never touched.

Every fix is idempotent, records prior state in `fix-journal.jsonl`, and
degrades gracefully (a rule whose data source fails reports "could not read",
never crashes the scan).

### v1 rule set (12)

| id | Detects | Fix | Category |
|---|---|---|---|
| power-plan | Active scheme is Balanced/Power saver on AC (`powercfg /getactivescheme`) | `powercfg /setactive` High performance; undo = previous GUID | auto |
| browser-gpu | On hybrid-GPU machines, installed browsers lack `GpuPreference=2` in `HKCU\...\DirectX\UserGpuPreferences` | Write preference per browser; undo = delete value | auto |
| hw-acceleration | Browser hardware acceleration disabled (Chrome/Edge `Local State`/Preferences) | Enable (requires browser closed) | confirm |
| startup-bloat | Enumerate HKCU/HKLM Run keys, Startup folders, logon scheduled tasks; measure count + known-heavy list (Steam, Discord, Spotify, Docker, Teams, BlueStacks…) | Per-item disable via `StartupApproved` bytes / task disable; undo = re-enable | confirm (per item) |
| ram-pressure | Top RAM consumers snapshot; total pressure | Points at startup-bloat items | advise |
| thermals | Idle CPU/GPU temps via LibreHardwareMonitorLib; thresholds | Fan cleaning / thermal paste guidance | advise |
| disk-breakdown | Sizes of `AppData\Local`, `Roaming`, Desktop, Downloads, Docker/WSL vhdx, BlueStacks data | Jumps to the cleaner tab | advise |
| disk-forecast | Free-space samples stored per scan; linear trend once ≥3 samples | "Disk full in ~N weeks" | advise |
| orphaned-data | Caches of tools no longer installed (Docker, BlueStacks, Unity, JetBrains vs. installed-apps list) | Jumps to the matching cleanup target | advise |
| stale-dev-caches | Regenerating caches untouched ≥60 days (bounded deep-walk, like dusty's SmartAdvisor) | Jumps to the matching cleanup target | advise |
| visual-effects | `VisualFXSetting` = appearance-optimized on weak hardware | Set balanced/performance; undo = previous value | confirm |
| storage-sense | Storage Sense off while free space is low | Enable; undo = disable | confirm |

Sensor caveat: some LibreHardwareMonitor sources need admin; without elevation
the thermals card shows "sensors unavailable" and everything else still works.

## Cleaner

Same model as dusty: three levels, every path and size shown before anything
is removed, per-item checkboxes, running lifetime total.

### v1 target set (~30)

- **Safe** (regenerates, zero functional impact, no elevation): user `%TEMP%`,
  browser caches (Chrome, Edge, Firefox, Brave, Opera), Explorer thumbnail
  cache, app caches (Discord, Spotify, Teams, Slack, VS Code, WhatsApp,
  Telegram media), `%LOCALAPPDATA%\CrashDumps`, Windows Error Reporting
  queues.
- **Developer** (re-downloads/rebuilds): npm, pip, yarn, pnpm, NuGet
  http-cache, Cargo registry cache, Gradle caches, `docker system prune`
  (opt-in, explicit).
- **Deep** (look before you leap; per-item checklist): `C:\Windows\Temp`
  (admin), `SoftwareDistribution\Download` (admin), Delivery Optimization
  cache (admin), old installers in Downloads (`.exe/.msi/.iso` ≥30 days,
  individual selection only), Empty Recycle Bin (bypasses-recycle-bin flag,
  cannot incoherently recycle itself).

A `CleanupTarget` mirrors dusty's shape: `id`, level, path templates, category,
`deletesContentsNotDirectory`, `regenerates`, `requiresAppClosed` (+ process
name for detection), `requiresIndividualSelection`, `requiresExplicitOptIn`,
`bypassesRecycleBin`, `requiresElevation`.

## Safety model

- **Allowlist only.** A path is deletable only if it descends from a
  registered target. No "delete everything except" logic anywhere.
- **Protected prefixes rejected outright:** Documents, Desktop contents,
  Pictures, Music, Videos, OneDrive roots, user profile roots, `System32`,
  `WinSxS`, `Program Files`. The only exceptions are the specific cache
  subfolders named by registered targets.
- **Junction/symlink escapes blocked.** NTFS junctions, symlinks, and other
  reparse points are resolved and the real path re-validated against the
  allowlist before deletion; reparse points are never traversed into.
- **Recycle Bin first.** Deletions go to the Recycle Bin with an undo window;
  Safe items then purge to actually reclaim space (Developer/Deep purge or
  stay recycled per setting). Restores are validated like deletes.
- **Least privilege.** Runs as the normal user. Fixes/targets that need admin
  elevate per-action via UAC with the action named in the prompt — the app
  never runs wholesale as administrator.
- **Restore point.** "Fix all" offers creating a System Restore point first.
- **Dry run** toggle: scan and report, mutate nothing.
- **Written record.** Every mutation (timestamp, rule/target id, path or
  command, bytes, prior state) appends to
  `%LOCALAPPDATA%\brisk\action-log.jsonl`.
- **In-use skip.** Targets whose app is running are skipped (process check),
  reported as skipped, never forced.
- A permission error on one file skips that file and continues the run.

## UI / UX

Main window app (the report needs room; dusty's narrow panel does not fit it)
plus an optional tray icon showing free space (tooltip + open-window action).

- **Health tab:** health score, Scan button, findings as cards — severity,
  evidence line ("Balanced plan is holding your i7 at 2.6 GHz; Turbo reaches
  4.5"), impact stars, Fix / Undo buttons; **Fix all (safe)** on top.
- **Clean tab:** dusty-style level sections with fold-open per-item
  checkboxes, reclaimable total, Clean per level.
- Custom WPF design system (own identity, defined at implementation time with
  the design skills; light + dark).
- Languages: English default, Turkish built-in (resx), follows OS with manual
  override.

## CLI

`brisk` console app sharing the engine and every safety rule:

```
brisk scan [--json]        diagnostics + cleaner measurement, mutates nothing
brisk fix [--all|--rule id] [--yes]
brisk clean [--level safe|developer|deep] [--yes]
brisk targets | brisk rules
```

Nothing mutates without `--yes`. `clean` only auto-selects what the app would
(individual-selection and opt-in targets stay manual).

## Testing & CI

- Engine unit tests (xUnit): rule detection against faked data sources
  (registry/WMI/filesystem abstractions injected), SafetyValidator
  (protected paths, junction escapes, allowlist), fix journal round-trips,
  undo/restore validation, CLI parser.
- GitHub Actions: build + test on `windows-latest`, warnings as errors.
  Release workflow publishes both build flavors + checksums.

## Distribution & growth

- GitHub Releases (portable exe + framework-dependent zip), winget manifest,
  Scoop bucket. Build-from-source one-liner documented.
- README playbook (proven by dusty): demo GIF ("found 23 GB + explained why
  this PC is slow"), explicit star ask, honest CCleaner comparison table, FAQ,
  safety writeup, `llms.txt`, "suggest a rule / target" issue templates,
  CONTRIBUTING with the one-entry registry pitch.
- Code signing deferred (SmartScreen warning mitigated via winget/Scoop/
  build-from-source). Signing (e.g. Azure Trusted Signing) is a paid decision
  for Mert when downloads justify it.

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| Antivirus false positives on self-contained exe | Framework-dependent build documented as primary alternative; no obfuscation; submit false positives to Microsoft |
| "PC optimizer = scam" perception | Transparency as the headline: open rules, undo, log, no telemetry; never touch the scam categories (registry cleaner, driver updater) |
| Sensor access needs admin | Degrade to "sensors unavailable"; never block the rest of the scan |
| SmartScreen on unsigned downloads | winget/Scoop install paths front and center; signing revisited later |
| Fix breaks something user-visible (e.g. visual effects) | Category system: visible-behavior fixes are confirm-only; everything journaled and undoable |
