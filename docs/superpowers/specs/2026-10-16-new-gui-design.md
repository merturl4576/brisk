# New GUI — Design

**Status:** design approved in chat 2026-10-16 (maintainer: "devam"); this
written spec awaits his review.
**Branch:** `feat/new-gui`, cut from `feat/action-engine` @ e40eb99 (engine
done, 1893 green, unmerged).
**Sub-project 2 of 2.** Sub-project 1 is `2026-10-16-action-engine-design.md`;
this GUI is the face of that engine. Both ship together as 0.11.0.
**Look:** `2026-10-16-new-gui-mockup.html` beside this file — the
"faithful" panel (top one) is the chosen look and the visual authority for
the home screen.

## Why

Testers clicked the old GUI, felt nothing, and could not tell what it did;
its cockpit was judged visually noisy. The engine now does the work (≈26 GB,
11 startup programs, privacy switches on the maintainer's PC). The GUI's job
is to make that one click obvious, visible while it happens, and provable
afterwards — "what Claude Code would do for a slow PC, visibly, no AI."

**Who uses it:** the maintainer, his father (Turkish, not technical), and
Reddit/GitHub visitors who decide in seconds from a GIF. Each must understand
the home screen without reading a sentence.

## What the user gets

One window, five places, reached from icon tiles on the left:

| Tile | Place | What it is |
|---|---|---|
| Genel / Home | Home | hero gauge, three satellites, two number columns, **Hepsini yap** |
| Yer aç / Free up space | Space page | plain checkbox rows; Downloads expands to its files |
| Hızlandır / Speed up | Speed page | "Şu an kasanlar" (close / force close) + startup and switch rows |
| Gizlilik / Privacy | Privacy page | switch rows + "Şu an konuştukları" + "30 günde gönderilenler" |
| ⚙ (small, bottom) | Settings | language, start with Windows, moving background |

Decoration (glow, rain, grid floor, spinning arcs, pulsing rings) lives only
on Home's hero and satellites. Every list is plain rows. No paragraphs
anywhere: titles ≤ ~6 words, cost lines ≤ 6 words (the engine's catalog
already obeys this).

## Home

### Layout

The mockup's layout, drawn on a fixed 1100×680 design canvas inside a
`Viewbox` (uniform), so every position from the mockup carries over and the
screen scales with the window. The atmosphere (background gradient, rain,
grid floor) is a separate full-window layer behind the Viewbox, so it fills
the window edge to edge at any aspect ratio.

- Top-left: brisk mark (from `Assets/brisk.ico`) + "brisk".
- Left: four 64 px outlined tiles with labels under them; the active one is
  filled and glowing. The ⚙ tile is smaller, at the bottom of the column.
- Centre: the hero — a 270° gauge of 64 ticks (open at the bottom), minor
  ticks every 4th, two spinning outer arcs (11 s and 17 s, opposite
  directions), a label above a huge thin numeral (86 px, weight 300, tabular
  figures) with its unit.
- Left column "Bu bilgisayar / This PC": Bulunan yer / Space found,
  Açılışta başlayan / Start with Windows, İnternete bağlı / Online now.
- Right column "Şu an kasanlar / Slowing you now": the top three rows of the
  load sampler, each "name · value".
- Floor: three satellite ellipses joined by a flowing dashed line — Speed
  (left), Space (centre, larger), Privacy (right).
- Bottom-left: the stateline. Bottom-right: the one glowing button.

### Numbers

| Element | Value |
|---|---|
| Bulunan yer | sum of `Bytes` of every space row in the plan, ticked or not |
| Hero (ready) | sum of `Bytes` of the **ticked** space rows ("Açılabilir yer") |
| Gauge fill | hero ÷ Bulunan yer, ticks lit white-blue |
| Açılışta başlayan | enabled startup entries now |
| İnternete bağlı | distinct apps in the connection snapshot, read every 10 s while Home is visible |
| Şu an kasanlar | `LoadSampler.Sample()` top 3; value = "%34 CPU" when CPU ≥ the sampler's threshold, else RAM; none → "Yok ✓"; sampled at open and every 30 s while Home is visible |
| Speed satellite | "{enabled} → {enabled − ticked startup rows}", caption "Hızlandır · açılışta" |
| Space satellite | the hero value, caption "Yer aç" |
| Privacy satellite | "{ticked privacy rows} ayar", caption "Gizlilik" |

On his PC that reads: found 41.4 GB, hero 26.4 GB (gauge ≈ 64%), 14 → 3,
and the privacy count.

### States

1. **Looking** — from the moment the window shows. The plan is built in two
   parallel halves (see Engine changes): privacy and speed rows arrive first
   and their satellites fill in; then space targets are sized one by one —
   the hero and "Bulunan yer" climb as each target lands, and the stateline
   names it ("Bakılıyor: Chrome önbelleği"). The button reads "Bakılıyor…"
   and is disabled until the whole plan is in.
2. **Ready** — stateline "Hazır · {n} iş seçili". Button "Hepsini yap".
   Nothing ticked anywhere → button disabled, "Yapacak bir şey yok ✓".
3. **Confirm** (only when needed, see below) — a sheet over the window.
4. **Running** — label becomes "Açıldı", ticks reset and refill **green** as
   real `BytesFreed` arrives (numeral tweens to each new total), satellites
   turn green in run order: Privacy when its last row finishes ("✓ {off}
   kapandı", off = privacy rows done or pending, not reverted or ignored),
   Speed when its last row finishes ("✓ {remaining} kaldı", remaining =
   enabled before − startup rows done), Space when the run ends. In Done the
   two numbers are replaced by the receipt's `PrivacyOff` and
   `StartupEnabledAfter`. Stateline "Yapılıyor: {row title}"; for the row that
   holds the Windows update cleanup, "{title} · birkaç dakika sürebilir".
   Button disabled, "Yapılıyor…".
5. **Done** — numeral settles on the receipt's `FreedBytes` (the real change
   in free space, what Explorer shows). Stateline "{freed} açıldı · {k}
   program açılıştan çıktı · {p} ayar kapandı", plus as they apply: "· {x}
   daha, {apps} kapanınca", "· {f} iş olmadı" (amber; clicking jumps to the
   first page with a failed row), "· tam etki için yeniden başlat". Button
   becomes "Geri al" (outlined, no glow); a small text link "Tekrar tara"
   sits beside it. While the window stays open, the receipt is refreshed
   every 30 s until its after-restart numbers are in.
6. **Undone** — after "Geri al": stateline "{n} ayar geri alındı · silinen
   dosyalar geri gelmez", satellites return to neutral, the plan is rebuilt
   (back to Looking → Ready).

**Opening the app again after a run** (e.g. after the restart): Home starts in
Looking → Ready for a fresh plan, and while the latest run is under 7 days
old and not undone, the stateline leads with the last result — "Son sefer:
{freed} açıldı · açılış {a} sn → {b} sn · {ram} RAM boşaldı" (each part only
once the receipt has it) — with a small "Geri al" text link after it. This is
the second GIF moment: restart, open, see the boot time drop.

### The one confirmation

"Hepsini yap" runs every ticked row without asking — except user files that
cannot come back. When the selection includes any of these, a sheet appears
first, every time:

- the Downloads row with ≥ 1 file ticked — "İndirilenler'deki {n} kurulum
  dosyası · {bytes}"
- the Recycle Bin row — "Geri Dönüşüm Kutusu · {bytes}"
- the old-Windows row — "Eski Windows kopyası · {bytes}"

Sheet title "Bunlar kalıcı silinecek". Buttons: **Devam** (primary) runs
everything; **Bunları atla** unticks those rows and runs the rest; **Listeyi
gör** closes the sheet, runs nothing, and opens the Space page with the
Downloads row expanded. Caches, developer caches and shader caches never ask:
they regenerate. This is the GUI form of the engine review's critical
finding — nobody loses a file they were never shown.

## Detail pages

Each page lists its group's rows from the plan, in plan order, under a
one-line header (≤ 6 words). The ticks are the same selection Home uses — one
`ActionSession` holds it. Ticks live for the session; a new launch starts from
the engine's defaults.

**Row:** checkbox · title · (space) size right-aligned. Under the title, in
small grey: the cost line when the row has one, and "Yeniden başlatınca" when
`NeedsRestart`. Clicking the row body (not the box) expands it to one grey
line, "Windows'ta: {WindowsName}", when the row has one. After a run the
right side shows the result word (shared with the CLI, see Engine changes):
green for done/off, amber for "Windows yok saydı"/"Geri açıldı", grey for
skipped, red for failed with its reason underneath.

**Footer:** "{n} seçili · {bytes}" and a **Bunları yap** button that runs only
this group's ticked rows (the CLI's `--group`), through the same confirmation
rule. Its result shows on the page's rows and on Home's Done state.

**Space page.** The Downloads row expands to its files: checkbox, name, size,
date (dd.MM.yyyy), largest first, with "Hepsini seç / Hiçbiri" above. Every
file is listed — the engine's critical fix, in GUI form.

**Speed page.** Top block "Şu an kasanlar": the load sampler's rows (≤ 5)
with name, CPU or RAM, and the sampler's note for Windows' own processes
("Windows güncelleme kuruyor"). Sampled when the page opens ("Ölçülüyor…"
during the 10-second window) and every 15 s while it stays visible. A row
that can be closed has **Kapat** (polite close). If the app is still running
5 s later, the row offers **Zorla kapat** — escalation instead of a dialog,
because a forced close can lose unsaved work. A row whose app has an enabled
startup entry shows an "Açılıştan çıkar" box bound to that startup row's tick.
System rows have no buttons. Below: the startup rows (friendly names,
protected ones unticked with their cost line), then the switch rows.

**Privacy page.** Header "İşaretliler durdurulur". The switch rows. Then
"Şu an konuştukları": "{app} → {owner} ({host}) · {count}", telemetry rows
tagged "Telemetri" in amber, refreshed every 2 s while visible, footer
"Bağlantı, veri gönderildiği anlamına gelmez." — and when DNS names could not
be read, one grey line saying telemetry is not told apart. Then "30 günde
gönderilenler": app · sent · received, top 10, read once per session when the
page first opens, footer "Sadece miktarlar, içerik yok."

## Settings

Three controls and a version line: **Dil** (Sistem / Türkçe / English;
default Sistem — Turkish Windows gets Turkish), **Windows ile başlat**
(existing `StartupLauncher`, default off), **Hareketli arka plan** (default
on). "brisk 0.11.0 · GitHub". The old `Theme` and `DryRun` keys stay readable
in `settings.json` and are ignored.

## Motion and its budget

Moving parts: rain (columns of dim Consolas glyphs falling at 7–16 s per
pass), the grid floor (perspective grid scrolling one cell per 3.5 s), the two
spinning arcs, the flowing satellite line, the satellites' pulse. WPF ticks
animation clocks on the UI thread, so the rule is that a frame costs a
property update and nothing else: storyboards on transforms/opacity of cached
layers (`BitmapCache`), `DesiredFrameRate` 30 — no per-frame
`CompositionTarget.Rendering` work, no layout, no re-rasterizing. The rain
glyphs and grid are built once.

- Budget: with Home visible and idle, brisk-app averages **≤ 3 % of total
  CPU** over 10 s on the maintainer's PC; minimized or hidden, the ambient
  clocks are stopped (not merely invisible).
- The gate is `AmbientMotionController`, fed by window visibility/state and
  by the **Hareketli arka plan** setting. It does not read Windows' animation
  flag: brisk's own "Window opening animations" row switches that flag off
  (`WindowAnimationsRule` writes `SPI_SETCLIENTAREAANIMATION`), and the look
  the maintainer chose would vanish after the first click.
- Data-driven motion (numeral tweens, ticks filling, satellites turning
  green) always runs; with the setting off the screen is the mockup's still
  frame.

## App shell

- **Elevation:** `app.manifest` keeps `requireAdministrator`; every row works
  without a second prompt.
- **Window:** 1100×680 default, minimum 960×600, centred; standard caption
  with DWM dark mode (existing `Dwm.cs`); title "brisk".
- **Single instance:** existing mutex `brisk-app-single` + signal
  `brisk-app-show`; a second launch shows the running window.
- **Tray:** kept for one job — the deferred queue (caches whose app was open
  during the run). Left click shows the window; menu: Aç / Çıkış. Tooltip
  "brisk", or "{bytes} temizlenecek · {apps} kapanınca" while entries wait.
  A 30 s timer calls `ProcessDeferred()` on a background thread while the
  queue has entries.
- **Close (X):** hides to tray when the deferred queue has entries, a run is
  in progress, or Windows ile başlat is on; otherwise exits. The app exits by
  itself once a hidden-only reason (run, queue) is gone and the window is not
  shown.
- **`--tray`** (autostart): no window; tray + deferred timer.
- **CLI and `report`:** untouched. `report` keeps its own composition
  (`EngineHost`, `ReportCardModel`, `ReportCard.xaml`).

## Engine changes

Each is test-first in BriskEngine.Tests; the CLI keeps its behaviour.

1. **Plan in halves, with progress.** `ActionPlanBuilder` exposes the
   privacy+speed rows (no cleanup scan needed) separately from the space rows
   (need the scan); `Build` composes both as today. `Scanner.Scan` takes an
   optional per-target callback. The first plan task **profiles** today's
   45 s plan (measured 2026-10-16 on his PC, unelevated: plan 44.6 s, scan
   34.7 s) and fixes what dominates (parallel target sizing; the deferred
   "lock-probe short-circuit" minor if it shows up). **Target, elevated on
   his PC from window shown:** privacy+speed satellites ≤ 5 s, full plan
   ≤ 20 s.
2. **Row-start callback.** `ActionRunner.Run` gains `onRowStart(ActionItem)`
   so the stateline names the row being worked on.
3. **Undone runs.** `RunRecord` gains nullable `UndoneUtc` (old JSON still
   loads); `Undo` sets it; the GUI hides "Geri al" for undone runs.
4. **Shared result words.** The CLI's private `ResultWord` moves to
   `BriskEngine.Actions.ActionResultWords` returning `(key, English)`; CLI
   and GUI both use it.
5. **Friendly startup names** (deferred minor). A startup row's title is the
   file description of the command's exe (`IAppNamer`) when readable and not
   generic (Update, Updater, Launcher, Helper, Application, Electron);
   otherwise the entry name with an id tail removed (`_` + ≥ 8 hex digits, or
   a GUID). `MicrosoftEdgeAutoLaunch_F6C5…` → "Microsoft Edge".
6. **Intel graphics helper protected** (deferred minor). "IntelGraphics",
   "Intel Graphics", "IGCC" join the Hardware words.
7. **Honest privacy count** (deferred minor). `Receipt.PrivacyOff` counts
   done/pending rows whose read-back is not `Reverted` and not
   `WrittenButIgnored` (ignored ones are already their own number).
8. **Deleter root reparse check** (deferred minor). Cleaning refuses a target
   root that is a junction or symlink.

## Architecture (GUI)

| Unit | Does | Depends on |
|---|---|---|
| `IActionBackend` / `RealActionBackend` | the engine calls the GUI makes: fast rows, space scan with progress, run (row start/finish), undo, latest run + receipt refresh, deferred process/pending, load sample, connections, usage, close/kill | `ActionEngine`, `Scanner` |
| `ActionSession` | the single state machine: plan, selection (rows + files), state (Looking/Ready/Running/Done/Undone/Failed), run results, receipt, confirmation set, group runs, one operation at a time; raises `Changed` through an injected `post` | `IActionBackend` only — no WPF |
| `HomeFigures` | pure: session → every Home string and number (hero, gauge fraction, satellites, columns, stateline) in the current language | `ActionSession` snapshot, `Loc` |
| ViewModels | `ShellViewModel` (current place), `HomeViewModel`, `GroupPageViewModel` (one class for the three pages, row VMs, file VMs), `LoadPanelViewModel`, `ConnectionsViewModel`, `UsageViewModel`, `ConfirmSheetViewModel`, `SettingsViewModel` | session, figures, timers via injected scheduler |
| Views | `MainWindow` (atmosphere, tiles, content host, sheet host), `HomeView`, `GroupPage`, `SettingsView`, controls `TickGauge`, `Satellite`, `AtmosphereLayer` (rewritten, animated) | ViewModels |
| `App` | composition, single instance, tray, deferred timer, close rules | all above |

**Threading:** backend calls run on `Task.Run`; `ActionSession` marshals
`Changed` to the dispatcher through the injected `post`. Scan, run and undo
are exclusive — the session refuses a second one and the buttons show it.
Live readers (load, connections, usage) run independently of the session lock
and never block it.

**Errors:** a failed plan → hero "—", stateline "Bakamadım: {message}",
button "Tekrar dene". A live reader that throws → that block shows
"Okunamadı.". Failed rows → red row result + amber stateline part. Window
closed mid-run → hides to tray, the run finishes (the engine's WAL covers a
crash). Unexpected exceptions keep the existing dispatcher handler.

## Removed

The old surfaces and their tests: `FlyoutWindow`, `OverviewPage`,
`HealthPage`, `PerfPage`, `StartupViewModel`/startup UI, `CleanPage`,
`PrivacyPage`, the old `SettingsPage`, `AppState`, `CleanService`,
`FixAllService`, `SafeCleanRunner`, `ShellRecycleBinSession`,
`IRecycleBinSession`, `RefreshConfirmation`, `WindowsSettingsLink`,
`LiveMetrics`, `CometTail`, `SweepRing`, `SegmentedGauge`, `Light.xaml`,
`ThemeResolver`, the old `Shared.xaml` styles not used by `ReportCard`, and
resource keys referenced by nothing that remains. Kept: `Program`,
`EntryRouter`, `ParentConsole`, `Loc`, `Settings`, `StartupLauncher`,
`TrayIcon`, `Dwm`, `OffscreenLayout`, `NumeralTick` (the slide-in for the
small column numbers), `AmbientMotionController`, the report pipeline. The
hero's count-up is a new `CountUp` attached property (animates a byte value
from the old to the new total over 600 ms, ease-out, formatted with
`Fmt.Bytes`). A test of kept code is never
deleted; tests of removed code go with it.

## Localization

New keys under `gui.*`, English in `Strings.resx`, Turkish in
`Strings.tr.resx`; the engine's `action.*` keys (122, both languages) are
reused for row titles, costs, results and live notes. The existing resx parity
test covers both files. CPU is formatted per language ("%34 CPU" /
"34% CPU"), sizes with `Fmt.Bytes`.

## Testing

- Engine changes: TDD in BriskEngine.Tests.
- `ActionSession` with a fake backend: progressive Looking, selection,
  confirmation set, Devam / Bunları atla / Listeyi gör, group runs,
  running → done, undo flag, receipt polling, refusal of a second operation.
- `HomeFigures`: every state's strings in both languages, gauge fraction,
  satellite texts, the "Son sefer" line.
- Views: each view rendered offscreen (`OffscreenLayout`) at Ready, Running,
  Done and Confirm with a fake session; the test fails on any WPF binding
  error, and writes PNGs I look at before handing the build over.
- Motion: a manual-category test hosts `MainWindow` for 10 s visible and 10 s
  minimized and reports CPU; run once on his PC before handover.
- Suite green; the Brisk.Tests count drops with the removed code and grows
  with the new.

## Success criteria (on the maintainer's PC)

1. Window shows instantly; satellites ≤ 5 s; full plan ≤ 20 s.
2. Hepsini yap → one confirmation → visible progress → Done with real freed
   space ≥ 15 GB, ≤ 4 startup programs left, satellites green.
3. After a restart, opening brisk shows the boot-time drop and the RAM line.
4. Geri al restores the settings and says deleted files do not come back.
5. Idle Home ≤ 3 % CPU; minimized, no ambient clocks.
6. His father understands Home without explanation (his call).

## Out of scope

README, screenshots and the new demo GIF (next, after his test); light theme;
the flyout; the health score page; a report-card redesign; signing,
installer, release and tag (his word only).

## Decisions taken on the maintainer's behalf (overrule any)

1. New window inside the existing WPF project; no WebView2.
2. Home faithful to the mockup on a scaled 1100×680 canvas.
3. Three detail pages as plain rows; Downloads lists every file.
4. One confirmation, only for unrecoverable user files, every time.
5. Running shows real bytes; Done shows Explorer's real free-space change.
6. Moving background tied to its own setting, not to Windows' flag.
7. Tray kept only for the deferred queue and autostart; X exits otherwise.
8. Dark only; Settings has three controls.
9. Ticks are per session, not saved.
10. Force close only after a polite close failed for 5 s.
11. Plan building gets faster and shows progress (45 s → ≤ 20 s target).
12. GUI-visible deferred minors fixed: friendly startup names, Intel helper
    protected, honest privacy count, deleter root reparse check.
13. Version 0.11.0 for engine + GUI together.
