# Action engine — Design

**Status:** approved in chat 2026-10-16, section by section (maintainer).
**Branch:** `feat/action-engine`, cut from `feat/honest-gauge` (0.10.0, unmerged).
**Sub-project 1 of 2.** Sub-project 2 is a from-scratch GUI built around this
engine; it gets its own spec. This engine does not ship with the current GUI.

## Why

Every outside tester — the maintainer's father, his friends, the maintainer
himself — installed brisk, clicked, and felt nothing. "At most a gigabyte."
Nobody said "wow, it freed this much, it did that." The GUI was judged hard
to understand and visually noisy. The diagnosis, measured on the
maintainer's own PC on 2026-10-16 without deleting anything:

| What the scan found | Size | Touched by one click today? |
|---|---|---|
| User temp + thumbnail cache | 2.2 GB | yes — the "one gigabyte" |
| Chrome / WhatsApp / Edge / Discord caches | 2.4 GB | no — skipped because the app is open |
| npm / pnpm / pip / NuGet caches | 7.2 GB | no — behind "your call" |
| Hibernation file | 13.7 GB | no — behind "your call" |
| Old installers in Downloads | 5.5 GB | no — behind "your call" |
| NVIDIA shader cache | 2.3 GB | not in the catalog |

One click touched 7% of what the scan itself found. Worse, what it did touch
went to the Recycle Bin (`CleanRunner` recycles by default), so the free
space Explorer shows did not change until the bin was emptied. On speed,
brisk saw 14 startup programs, matched none against a nine-name heavy list,
and disabled nothing. On privacy it counted ("403 entries") and switched
nothing off, and it never showed where data goes.

**The goal, in the maintainer's words:** do what Claude Code does when told
"my PC is slow, check my privacy settings" — measure what is happening now
and fix it, visibly — as a free exe with no AI. Every item in this spec
passes the test "would Claude Code look at this on this machine and do
this?"

This supersedes the "diagnostician, never an optimizer" positioning. What
stays from it is the honesty: brisk does things, and then proves them with
numbers Windows itself reports.

## Success criteria

Measured on the maintainer's PC:

- One click frees **at least 15 GB**, measured as the real change in free
  space on the system drive (today: 2.2 GB, and only after emptying the bin).
- Enabled startup programs go from **14 to at most 4**.
- Telemetry connections seen by the live connection reader drop to **0**.

The acceptance test that matters: the father and the friend say they felt it.

## Scope

In: the three action groups (Space, Speed, Privacy), the plan builder, the
runner with one-step undo, data-defined switches, the receipt (before/after
proof), three live readers (current load, current connections, 30-day
per-app network usage), the deferred clean queue, CLI verbs.

Out (each a candidate for its own later spec): how the score and the screens
look (sub-project 2), firewall blocking of connections, compacting WSL and
Docker virtual disks, removing preinstalled apps (debloat), FPS measurement.
The existing record revelations (run history, USB history, mic/camera use)
stay in the engine unchanged; whether and how they show is sub-project 2's
call.

## Architecture

A thin layer on top of what exists. Rules keep `Detect`/`Fix`/`Undo`;
`FixJournal` keeps prior states; `ReadBack` keeps verifying writes;
`CleanRunner` and `SafetyValidator` keep deleting safely. New pieces:

- **`ActionItem`** — one row a user sees: id, group, title key, size or
  effect, optional cost key, `DefaultOn`, `NeedsRestart`, `Reversible`, and
  exactly one link: a rule id, a cleanup target id (or a bucket of them), or
  a startup entry.
- **`ActionPlanBuilder`** — turns a scan snapshot into an `ActionPlan` of
  three `ActionGroup`s. Groups and rows with nothing in them are omitted.
  All machine-dependent defaults are decided here (laptop vs desktop,
  protected startup entries, features that are present or not).
- **`ActionRunner`** — runs the ticked rows of one or all groups under one
  run id. Per row: journal the prior state, apply, read back, report. One
  row failing never stops the others. Undo of a run reverses its reversible
  rows in reverse order.
- **`SwitchSpec`** — a data definition for the common shape of a tweak: a
  set of registry values, a service start type, or a scheduled task state,
  each with the value that means "on" and the value the fix writes. A
  generic `SwitchRule` built from a spec is an ordinary `IDiagnosticRule`,
  so it rides `FixRunner`, `FixJournal` and `ReadBack` with no new code
  path. A new switch is about five lines.
- **`Receipt`** — the before snapshot taken at plan time and the after
  values filled in when they become measurable (see each group). Persisted
  as JSON next to the fix journal.
- **Live readers** — `LoadSampler` (current CPU/RAM per app),
  `ConnectionReader` + `ConnectionOwnerResolver` (who this PC is talking to
  now), `NetworkUsageReader` (30-day per-app bytes sent/received).
- **`DeferredCleanQueue`** — cache targets skipped because their app was
  open; cleaned when the app closes.

The CLI uses the same engine: `brisk scan` prints the three groups,
`brisk run [--group space|speed|privacy] [--all]` prints the plan and
changes nothing without `--yes` (as `brisk clean` does today),
`brisk undo` reverses the latest run, `brisk receipt` prints the proof.
Existing verbs keep working unchanged.

## Copy rules (engine strings, both languages)

These rules apply to every row and carry into sub-project 2:

1. **Row title** says, in everyday words, what Windows does to you — never
   the technical name. About six words at most. A ticked box means "stop
   this" (privacy) or "do this" (space, speed).
2. **Cost line** — at most five or six words. Empty when there is no cost.
3. **Windows' own name** for the setting — shown only when the row is
   expanded, one grey line, so a user can find it in Settings.

Turkish is the maintainer's reference; English follows the same rules.

## Space

Buckets, each a row with a size and a checkbox. Expanding a bucket shows what
is inside. Empty buckets are hidden.

| Bucket | Contents | Default | Cost line (tr) |
|---|---|---|---|
| Caches and temporary files | user + Windows temp; caches of **every** browser profile (Default and `Profile *`; today only Default); app caches; thumbnails; crash dumps; WER queues (user + ProgramData); Windows Update downloads; Delivery Optimization cache; Windows update leftovers (`DISM /StartComponentCleanup`, no `/ResetBase`, runs last) | on | — |
| Developer caches | npm, pnpm, pip, NuGet, Yarn, Cargo, Gradle | on | "Sonra yeniden iner." |
| Game shader caches | NVIDIA DX/GL cache, AMD DX/GL/Dxc cache, `D3DSCache` | **off** | "Oyunlar ilk açılışta takılabilir." |
| Hibernation file | `powercfg /h off`; undo `powercfg /h on` | on for desktops, **off for laptops** | "Hızlı açılış kapanır." · laptop: "Pil biterse açık işler kaybolur." |
| Windows.old | the existing target | on | "Eski Windows'a dönülemez." |
| Installers in Downloads | `.exe` `.msi` `.iso`; expanding lists each file name and size, each with its own checkbox | on | "Geri alınamaz." |
| Docker unused data | `docker system prune`; only when Docker is installed and reports reclaimable data | off | "Durmuş container'lar silinir." |
| Recycle Bin | when not empty | on | — |

Two deliberate departures from "everything on", approved:

- **Shader caches are off** because deleting them makes games recompile
  shaders and stutter on first launch — the opposite of Speed.
- **Hibernation is off on laptops** because Windows hibernates a laptop on a
  dying battery; without the file, open work is lost. Laptop means
  `GetSystemPowerStatus` reports a system battery; an unknown answer counts
  as a laptop.

**How deletion changes:**

- Regenerating caches are **deleted for real**, not recycled. A locked file
  is skipped; the rest of the target is deleted.
- A cache target whose app is running goes to the `DeferredCleanQueue`
  instead of being skipped. brisk polls (every 30 s while it runs) for the
  target's process names; when none is running it cleans the target. The
  queue is persisted; work older than 7 days drops out.
- Downloads files the user left ticked are deleted for real. Only the exact
  files listed at plan time are eligible; a file whose size or last-write
  time changed since the plan is skipped. `SafetyValidator` checks every
  path, as today.
- Never touched: Desktop, Documents, and the large-files list (e.g. a 24 GB
  VM disk on the Desktop). Those are listed with a "show in folder" action,
  never deleted.

**Receipt:** free bytes on the system drive before and after the run
(`GetDiskFreeSpaceEx`), shown as the headline number. Deferred work reports
separately: "12 GB açıldı · 2.4 GB daha Chrome kapanınca açılacak."

## Speed

**A. Current load.** `LoadSampler` samples for 10 s and groups processes by
app (Chrome's thirty processes are one row: "Chrome · 2.1 GB"). Rows: the top
five above 10% average CPU (of the whole machine, all cores) or 500 MB
private memory. Each row has a
**Close** button and is never ticked by default, because closing loses
unsaved work. Close asks the app to close (`CloseMainWindow`, so it can
prompt to save); force-kill is a second, explicit click. If the app is also
a startup entry, the row gets a ticked "remove from startup" box. Windows'
own processes get no Close button; known ones get a one-line explanation
instead (e.g. `MsMpEng` → "Defender tarama yapıyor", `TiWorker` → "Windows
güncelleme kuruyor", `SearchIndexer` → "Windows arama dizini kuruyor").
Closing is not journaled (it cannot be undone) and is not part of a run.

**B. Startup programs.** Everything Task Manager lists: Run keys, the
Startup folder, Store apps. Disabled through `StartupApproved`, the same
mechanism Task Manager uses, so the user can see and re-enable them there.
**All ticked except the protected set**, which stays unticked with a reason:

| Protected | Cost line (tr) |
|---|---|
| security software (incl. the exe of any antivirus registered in SecurityCenter2) | "Koruma açılışta başlamaz." |
| audio, touchpad, GPU, Bluetooth helpers (by publisher) | "Donanım ayarı çalışmayabilir." |
| OneDrive, Google Drive, Dropbox, iCloud, MEGA | "Dosya eşitleme durur." |
| password managers | "Şifre yöneticisi açılmaz." |
| VPN clients | "VPN otomatik bağlanmaz." |
| keyboard and shortcut tools (IME, PowerToys, AutoHotkey) | "Kısayolların çalışmaz." |

The protected set is a data list (publisher and product names, plus the
registered antivirus path), tested like the existing heavy list.

**C. Games and power.**

| Row (tr) | Windows' name | Default | Cost line (tr) |
|---|---|---|---|
| Şarjdayken tam güç | power mode "Best performance", plugged-in only; battery untouched | on | "Fan daha çok dönebilir." |
| Oyunda arka planda video kaydı | Game DVR background recording | on | — |
| Oyunlarda gecikmeyi azalt | Hardware-accelerated GPU scheduling (only when supported) | on | — |
| Pencere modundaki oyunları hızlandır | Optimizations for windowed games (Windows 11) | on | — |
| Pencere açılış animasyonları | window animations (visual effects) | on | "Pencereler anında açılır." |
| Ekstra çekirdek koruması | Memory integrity | **off** | "Güvenlik bir kademe düşer." |
| Sanal makine desteği | Virtual Machine Platform (only when enabled) | **off** | "WSL ve Docker çalışmaz." |

Memory integrity and VMP are listed because Microsoft's own gaming guidance
names them; they stay unticked because each removes a protection or breaks
WSL/Docker. Rows that need a restart say so; the run ends with a "restart
for full effect" action.

**Receipt — three numbers that are actually comparable:**

1. **Boot time:** Windows' own boot record (event 100) — the recent boots
   before the run vs the first boot after it. "48 sn → 31 sn."
2. **Enabled startup programs:** "14 → 3."
3. **RAM freed:** at plan time, the private memory of the running processes
   belonging to the startup entries being disabled ("these 11 programs use
   1.8 GB right now"). After the next boot, brisk confirms none of them
   started; only then does it say "1.8 GB RAM boşaldı". If one did start,
   it is named and excluded from the number.

Comparing "idle RAM before" with "idle RAM after" was rejected: the before
reading is taken with the user's apps open, so the comparison would flatter
brisk.

## Privacy

**A. Switch telemetry off.** One list, same rules. Rows reuse the existing
switch rules where they exist (diagnostic level, advertising ID, tailored
experiences, activity history, Start suggestions, Start web search, speech
and typing, Recall, location, Delivery Optimization, UAC); the rest are new
`SwitchSpec`s.

| Row (tr) | Windows' name | Default | Cost line (tr) |
|---|---|---|---|
| Kullanım bilgilerini Microsoft'a göndermesi | DiagTrack, dmwappushservice services | on | — |
| Arka planda bilgi toplaması | telemetry scheduled tasks (Compatibility Appraiser, ProgramDataUpdater, CEIP Consolidator, UsbCeip, DiskDiagnosticDataCollector, Feedback DmClient) | on | — |
| Hata ve kullanım raporu göndermesi | diagnostic data level, Windows Error Reporting | on | — |
| Sana göre reklam ve öneri göstermesi | advertising ID, tailored experiences | on | — |
| Açtığın dosya ve sitelerin kaydını tutması | activity history | on | — |
| Başlat menüsündeki reklamlar | Start suggestions | on | — |
| Başlat'ta arama yapınca internete gitmesi | Bing in Start search | on | "Başlat'ta web araması olmaz." |
| Sesini ve yazdıklarını göndermesi | online speech recognition, inking and typing | on | "Windows sesle yazma çalışmaz." |
| "Windows'u nasıl buldun?" soruları | feedback frequency | on | — |
| Ekranının sürekli görüntüsünü kaydetmesi | Recall (when present) | on | — |
| Copilot | Windows Copilot policy | on | "Copilot kapanır." |
| Güncellemeleri başkalarına dağıtması | Delivery Optimization (internet peers off, LAN kept) | on | — |
| Konumunu uygulamalarla paylaşması | location | **off** | "Cihazımı bul çalışmaz." |
| Programların sormadan yönetici olması | UAC (shown only when it is silent) | on | "Programlar yeniden izin ister." |

Firewall off and Defender real-time off, when the posture reader finds them,
appear at the bottom of this list the same way.

After a run, each row reports in one or two words, mapped from the existing
`ReadBack` states: **Kapandı ✓**, **Windows yok saydı** (a policy the edition
writes down and ignores), **Geri açıldı**.

**B. Who this PC is talking to now.** Refreshes every 2 s while shown.

- `ConnectionReader`: `GetExtendedTcpTable` (IPv4 and IPv6, owner PID),
  established, loopback excluded, private ranges labelled "local network".
  PIDs are grouped by app as in Current load.
- `ConnectionOwnerResolver`: first the Windows DNS client cache
  (`MSFT_DNSClientCache`): the name that recently resolved to that address,
  mapped to a company through a curated domain-suffix list. When the cache
  has no entry, an offline IP-to-organization table bundled in the exe.
  Known Microsoft telemetry host names are tagged **Telemetri**.
- Example row: "Başlat menüsü → Microsoft (Bing) · 2 bağlantı". Footer, one
  line: "Bağlantı, veri gönderdiği anlamına gelmez."
- **brisk itself makes no network connection.** Everything is offline.

**C. Who sent what in the last 30 days.** The record behind Settings >
Network > Data usage. Top ten apps by bytes sent: "OneDrive · 4.1 GB
gönderdi · 12 GB aldı". Footer: "Miktar var, içerik yok."

Two ways to read it: the WinRT API
`ConnectionProfile.GetAttributedNetworkUsageAsync` (no elevation, simple, but
needs a Windows-versioned target framework and may not attribute desktop
apps) or the SRUM database (`SRUDB.dat`, elevation, ESE parsing). **The
plan's first task is a spike that settles this on the maintainer's PC.**
WinRT if it attributes desktop apps; SRUM otherwise.

**Receipt:** switches turned off, how many Windows ignored, and telemetry
connections at plan time vs two minutes after the run: "11 ayar kapandı ·
1'ini Windows yok saydı · 3 telemetri bağlantısı → 0". Numbers are shown
whatever they turn out to be.

## Error handling

- Every row ends as **done**, **skipped** (one-line reason, e.g. "Chrome is
  open — cleans when it closes") or **failed** (one-line reason). Missing
  elevation in the CLI is a skip with "needs administrator".
- **Journal before write.** Today `FixRunner` journals the prior state only
  after `Fix` returns, so a multi-value switch that throws partway leaves
  its first writes unrecorded and un-undoable (documented in
  `TelemetrySwitchRule.cs`). The runner reads and journals the prior state
  first, then writes, so a half-applied row can still be undone.
- Undo never claims what it cannot do: deleted caches and files are listed
  as "cannot be undone".
- Rows that need a restart stay **pending restart** and are read back after
  the next boot.
- A live reader that fails (DNS cache, 30-day usage, connection table) shows
  one line, "Okunamadı", in its section. No estimate is ever shown in its
  place.

## Testing

Unit tests in the existing style, with fake probes:

- plan builder: bucket composition, empty buckets hidden, laptop vs desktop
  defaults, protected startup entries, features present or absent;
- runner: order, one row failing, journal-before-write, undo in reverse,
  irreversible rows reported;
- every `SwitchSpec`: apply, undo and read back against a fake registry,
  service and task store;
- receipts: free-space delta, boot comparison, startup RAM excluded when a
  program still starts;
- deferred queue: persisted, cleaned on app exit, expiry;
- connection owner: DNS hit, table fallback, telemetry tag, local network;
- load sampler: grouping by app, thresholds, system processes get no Close.

The 30-day usage spike runs first on real hardware. End-to-end through the
CLI on the maintainer's PC (`brisk run --all` without `--yes`, then with),
before any GUI exists. The father's PC and the friend's laptop are the
acceptance runs.

## Delivery

Engine work lands on `feat/action-engine`, on top of 0.10.0. No release
until sub-project 2 (the new GUI) is done: the current pages cannot show
these lists. 0.10.0 folds into that release; its version number is the
maintainer's call.
