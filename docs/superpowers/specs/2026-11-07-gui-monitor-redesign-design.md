# GUI monitor redesign: design

**Status:** decided in chat on 2026-11-07. The maintainer approved the Home, the pages and the Yer aç levels mockups one by one, then said "projeyi artık bitirmek istiyorum, önerdiğin şekilde yapmaya full başla". He waived the last review round, so the calls marked *(my call)* below stand unless he objects.
**Branch:** `feat/monitor-gui`, cut from `feat/new-gui` @ d7eb8be (== origin/main, 0.11.0 code).
**Visual authority:** the three approved mockups beside this file, in `2026-11-07-gui-monitor-mockups/`. Each runs in a browser, has no build step, and has animated demo data:

| File | Shows | Status |
|---|---|---|
| `home.html` | Genel: the live system monitor | approved ("home kısmını beğendim") |
| `pages.html` | Home plus the Hızlandır and Gizlilik pages (its Yer aç page is superseded) | approved ("güzel olmuş") |
| `space-levels.html` | The full set again, with Yer aç rebuilt around MIN/MID/MAX | approved ("şimdilik böyle olsun") |

When this text and a mockup disagree on look or motion, the mockup wins. When they disagree on behaviour, this text wins. All numbers in the mockups are demo data from the maintainer's PC. The app computes real numbers on every machine.

## Why

He opened 0.11.0 next to Monster's Kontrol Merkezi and judged it as follows:
- The pages, a tick list plus a "do these" button, feel "junior… liseli çocukça".
- "En salak insan bile kolayca kullanabilmeli, her şey belli olmalı."
- He wants an elite, animated analysis instead of a selection form.

He rejected two earlier attempts as "çok AI slop": a disk ring, orbiting app icons, and a privacy radar with dashed leak lines.

What he accepted:
- A faithful Monster-style live monitor on Home.
- The same stage reused for the three action pages, with a scan animation and plain rows.

The project goal is GitHub stars. The window is the GIF and the screenshot, so the look carries the launch. Honesty is the brand: never invent a number, never inflate a total.

brisk must work on any Windows PC of any brand (his `briskhakkinda.txt`, point 1), not only on his Monster Abra.

## Goals

1. **Home = live system monitor** in the Monster style, with real hardware and real sensors.
2. **Pages = analysis you watch.** Entering a page plays a scan, rows arrive one by one, and the number climbs. One button acts. Tapping a row skips it. There are no checkboxes.
3. **Yer aç = three levels: MIN, MID, MAX.** The selected level is deleted directly. His personal big files are never touched.
4. **More space found.** New targets close the gap between today's 39 GB and the real ~93 GB on his PC (see *Space targets*).
5. Fix `brisk run --group` before release (it currently prints the whole plan).

## Non-goals (this wave)

- **Driver checker.** His pick: the first wave AFTER this GUI ships. Scoped in memory `disk-and-driver-audit-2026-11-07`: an honest Windows-Update-only checker. It fixes missing drivers, lists real WU offers with one plain line each, and supports undo. It hands GPUs off to NVIDIA App or Intel and never installs them with one click.
- **A separate Donanım (hardware) page.** It ships with the driver checker. This wave only adds battery health to Home.
- Fan speed or fan control. RPM needs the OEM driver, so it is not readable on his PC.
- A history or receipt screen. Settings additions. A welcome or onboarding screen.
- New Speed or Privacy rules beyond what the engine already has.

## Anti-goals: failure even if it works

- **Decoration that reads as AI slop:**
  - orbiting icons, radar sweeps, decorative glow lines, rings that only decorate;
  - satellites or the floor carousel on the pages (Home only);
  - motion that does not mean something.
- **A checkbox list plus "do these" as the primary UI.**
- **Invented sensor values.** Anything unreadable shows "—" or the block is hidden. Never a guess.
- **Inflated totals.** His personal files (VM disks, ISOs, emulator images) never count toward any GB number.
- **Scary counters.** No date-based red "N drivers outdated" style numbers.
- **A confirm dialog before deleting.** His explicit call: "kullanıcıya sormadan silmesi taraftarıyım", "max'ta da belirtir, silerse adam gene de siler". MAX warns visually; the button still acts on one press.

## Constraints

- **Stack:** WPF on `net8.0-windows`. The app runs elevated (`requireAdministrator`). GUI in `src/Brisk`, engine in `src/BriskEngine`, CLI in `src/Brisk.Cli`. Build on the 0.11.0 shell (`Ui/Shell`, `Ui/Home`, `Ui/Pages`, `Ui/Controls`, `Ui/Theme/Monster.xaml`). Reuse its gauge, rain, floor and satellite controls where they fit.
- **Performance budget from 0.11.0:**
  - motion at 20 fps or less, rain in four lanes, glow without blur;
  - the pixel fire is a small canvas (about 40×14 cells) capped at 20 fps;
  - honour the Windows "show animations" setting and the existing "moving background" setting: reduced motion gives static frames and instant number changes.
    - *Revised 2026-11-08 (ruling R30):* brisk follows its own "Hareketli arka plan" setting instead of Windows' "show animations". That one switch covers the rain, the rings, the glow, Home's turn and the counting numbers. The reason: brisk's own `speed.animations` row (Hızlandır, on by default in Hepsini yap) turns the Windows flag off, so honouring that flag would freeze the stage after every Hepsini yap.
- **Localization:** all strings go through `Loc`, in TR and EN. The Turkish strings in this doc and the mockups are the final TR copy. EN gets plain equivalents.
- **Tests:** the existing suite stays green. Engine changes (new targets, level mapping, `--group`) get unit tests. View models get tests where the 0.11.0 code already has them.
- **Process:**
  - plain conventional commit messages;
  - no push or tag without the maintainer;
  - launch `brisk-app` for him only when the wave is done; it is his test surface.

## What the user sees

### The stage (every place)

- A dark panel with falling rain, a grid floor, and the big hero gauge in the centre:
  - thin rings and a crosshair;
  - a tick ring;
  - a segmented value bar on the left half;
  - a big number with a unit, a label above and a caption below.
- Left nav tiles: Genel, Yer aç, Hızlandır, Gizlilik, plus Ayarlar.
- Tiles carry badges:
  - Yer aç shows the selected level's GB;
  - Hızlandır and Gizlilik show their counts;
  - every tile shows ✓ once done.
- Left column and right column beside the gauge.
- The satellites must never overlap the side columns at any supported window size. At 1400×900 the mockup's right list collides with the "Ekran kartı" satellite label; the builder fixes the layout.

### Genel (Home): live system monitor

**Gauge**
- Shows the selected part's live load: İşlemci %, Ekran kartı %, or Bellek %.
- Shows the temperature under the number where one is readable.

**Floor**
- Three floor satellites, only on Home: İşlemci, Ekran kartı, Bellek. Each has a small live tag such as "58 % Bellek".
- Clicking a satellite rotates the floor carousel so it comes to the front. The gauge rings turn 120° and the number counts to the new value.
- Only the satellites rotate the gauge.

**Left column**
- **Ağ hızı:** upload and download.
- **Pil:** charge %, and plugged in or on battery.
  - **New:** pil sağlığı (health %, meaning full-charge capacity ÷ design capacity) and şarj döngüsü (cycle count), when readable.
  - Desktops without a battery hide the Pil block.
- **Isı:** CPU and GPU °C.

**Right column**
- Four small icons: Disk, İşlemci, Ekran kartı, Bellek. They ONLY highlight the matching spec block. His words: "o kısma basınca ana ekran dönmesin".
- Real spec blocks: CPU model, clock, cores and threads; GPUs; RAM total and usable; disk model and size.
- Below them, "Şu an en çok kullanan": the top 3 processes for the selected satellite (CPU %, GPU %, or RAM).

**Bottom strip** *(my call; he did not object)*
- At rest: "brisk hazır: 45 GB · 9 uygulama · 7 ayar [Hepsini yap]".
- **Hepsini yap runs:**
  - Yer aç at the MIN level;
  - all default Hızlandır rows;
  - all default Gizlilik rows.
- **After it finishes,** the strip reads "Son işlem: 45 GB · 9 uygulama · 7 ayar · Geri al".
  - Geri al reverts the Speed and Privacy parts only.
  - Deleted files do not come back, and the strip must not suggest they do.

**Sensors checked on his PC.** The rule everywhere: unreadable means "—" or hidden.
- GPU temperature: readable through NVIDIA (`nvidia-smi`). Other vendors may show "—".
- GPU 3D load: readable through performance counters.
- CPU temperature: ACPI `MSAcpi_ThermalZoneTemperature` is denied without elevation. brisk runs elevated, so it is probably readable.
- Battery: readable.
- Fan RPM: not readable, so it is not shown.

### Pages: Yer aç, Hızlandır, Gizlilik

- **Stage:** the same as Home, with the satellites hidden.
- **Entering a page plays the scan:**
  - the rings turn and a sweep arc circles under "Taranıyor…";
  - rows arrive one by one and the number climbs;
  - the real scan result drives this; the animation must not fake progress beyond the data.
- **Rows** are plain, Monster style: a name, a small grey reason, and a value on the right. Irreversible rows carry their reason in amber, e.g. "geri alınamaz".
- **Tap a row** to skip it: strikethrough, and the total drops. Tap again to include it. The header hint reads "dokunursan atlarım".
- **One glowing button** sits under the gauge:
  - "N GB boşalt", "N uygulamayı durdur", "N ayarı kapat";
  - with nothing selected it reads "Hiçbir şey seçili değil" and is disabled.
- **Press:**
  - rows turn green one by one, each with "✓ silindi", "✓ durdu" or "✓ kapandı";
  - the number counts down;
  - then the done state: green "Boşaldı / Durduruldu / Kapatıldı", the button becomes "✓ Tamam", and the nav badge becomes ✓.
- **Geri al** appears after done on Hızlandır and Gizlilik only.
- **Hızlandır right column:**
  - "Bırakıyorum": the kept startup apps, each with its reason and "kalsın";
  - "İstersen ekle" for costly extras, with honest warnings. Example: virtual machine support says "Docker ve WSL çalışmaz".
- **Gizlilik right column:** "İstersen ekle", for example Konum.

### Yer aç: MIN / MID / MAX (his idea)

**Right column**
- **The "C: diski" block:**
  - "Şu an boş" = the current free space;
  - "Sonra boş" = free + the level total, in green. After deleting it reads "Şimdi boş".
- **Below it, a "Seviye" heading and three stacked, keyboard-focusable level tiles:**

| Level | Colour | Tagline | Contents |
|---|---|---|---|
| **MIN** | cyan `#5fd4e8` | "Kendiliğinden geri gelir" | things that regenerate or are never needed again |
| **MID** | amber `#ffb020` | "Küçük bir bedeli var" | MIN + things with a small cost (slower first launch, a reinstall, a re-download) |
| **MAX** | red `#ff3b3b` | "Geri alınamayanlar dahil" | MID + things that cannot be brought back |

**How the levels behave**
- **Each level includes everything below it.** A tile shows its cumulative GB.
- **The default selection is MIN** *(my call; he said "mantıklı")*.
- **The MAX tile:**
  - a ⚠ triangle and red pixel flames (his ask): a Doom-fire canvas behind the tile content;
  - the flames rest at about 0.3 opacity and rise to about 0.85 when selected, focused or hovered;
  - a dark top-to-bottom scrim keeps the text readable;
  - reduced motion shows one static frame.
- **Selecting a level:**
  - the accent colour cross-fades;
  - the gauge number counts up or down (about 600 ms);
  - the caption stays "N GB gereksiz dosya bulundu", where N is the MAX total;
  - the button reads "N GB boşalt" and glows in the level colour;
  - the nav badge shows the level's GB.
- **The left list "Sileceklerim":**
  - rows are grouped under MIN / MID / MAX headers, each row tagged with its level;
  - rows above the selected level show dimmed and locked (no tap);
  - when a higher level is selected, its rows light up one by one;
  - tap-to-skip works on included rows.
- **Under the list, a locked section "Senin dosyaların · brisk dokunmaz":**
  - his large personal files, with name and size;
  - never counted in any total, never deletable from brisk.
- **Done caption, per level:**
  - MIN "Önbellekler zamanla geri gelir";
  - MID "Kurulum dosyaları geri gelmez";
  - MAX "Silinenlerin bir kısmı geri gelmez".
- **No Geri al on Yer aç.** Deleted means deleted. brisk keeps no copies, and the freed space is real.

### Ayarlar *(my call, A)*
- The same three settings as 0.11.0: language (Sistem / Türkçe / English), start with Windows, moving background.
- Plus the GitHub link.
- Restyled to the new stage. No additions.

### First launch *(my call, A)*
- The window opens straight on Genel with the live monitor.
- The read-only scan runs in the background, and the nav badges show a small spinner until their numbers arrive.
- No welcome screen.

## Space targets and level mapping

The engine already has `clean --level safe|developer|deep` (`CleanupLevel`).

**Re-sort the targets into MIN / MID / MAX.** The builder chooses one of two paths:
- rename `CleanupLevel` to `Min/Mid/Max`, and keep `safe|developer|deep` as CLI aliases so existing scripts work;
- or map the existing levels onto MIN/MID/MAX.

Every target must be a generic rule that works on any PC. The examples are what it found on his.

| Level | Target (UI name) | Rule | On his PC |
|---|---|---|---|
| MIN | Önbellekler | existing temp and app caches | 22.4 GB |
| MIN | Geliştirici önbellekleri | existing npm, pip, NuGet and similar caches | 11.2 GB |
| MIN | Eski çökme raporu | **new:** kernel dumps in `%SystemRoot%\LiveKernelReports` (and old `Minidump`/`MEMORY.DMP` if not already covered), older than 7 days | 10.3 GB (WATCHDOG, 2025-10-03) |
| MIN | Windows kurulum kayıtları | **new:** `%SystemRoot%\Panther` setup logs (`setupact.log` and similar) from finished upgrades | 0.6 GB |
| MIN | Eski sürücü kurulumu | **new:** leftover vendor driver installers, e.g. NVIDIA App `ota-artifacts` (builder confirms the exact path and the safety) | 0.7 GB |
| MID | Hazırda bekletme dosyası | existing: hibernation off, "Hızlı başlatma kapanır · geri açılabilir" | 12.7 GB |
| MID | Oyun gölgelendirici önbelleği | existing shader caches, "Oyunların ilk açılışı biraz yavaşlar" | 8.3 GB |
| MID | Eski projelerin indirme klasörleri | **new:** project dependency folders (`node_modules`, `.next`, Python venvs) not touched for 3+ months, inside user project folders, "gerekince npm install" | 10.7 GB |
| MID | İndirilenler: kurulum dosyaları | existing installers in Downloads, "geri alınamaz" (amber) | 5.1 GB |
| MID | Geri dönüşüm kutusu | existing | 0.355 GB |
| MAX | Arşiv (açılmış hali yanında) | **new:** an archive (`.7z/.zip/.rar`) at least 1 GB whose extracted content sits beside it | 3.74 GB (Kali .7z) |
| MAX | Ev klasöründeki git deposu | **new:** a `.git` directory at the user-profile root. It slows every git-aware tool, e.g. VS Code. Marked "geri alınamaz" | 6.54 GB |
| MAX | Eski geri yükleme noktaları | **new:** delete all restore points except the newest (`vssadmin delete shadows /oldest` style, or the WMI equivalent). The size may be unknown before deleting, so show "—" and never count a guess | 16.2 GB measured by my walk |

On his PC the totals come to MIN ≈ 45, MID ≈ 82 and MAX ≈ 93 GB.

**Personal files: locked, never counted.**
- VM disks (`.vdi`, `.vhdx`, `.vmdk`) outside the system's own WSL and Docker stores.
- `.iso` images.
- Android emulator AVDs.
- BlueStacks data.

All four are listed at 1 GB or more, by name and size. On his PC: Kali 24 GB, BlueStacks 21 GB, Server ISO 7.8 GB, Android 10 GB. The detection rules are the builder's.

**Left out on purpose:**
- WinSxS cleanup: the size is unknown until run, and it is slow.
- WSL and Docker `vhdx` compaction: the gain is unknown and it can break running containers.
- His personal files.

## Undo model

- Hızlandır and Gizlilik keep the 0.11.0 journal undo, now shown as the page's Geri al and the Home strip's Geri al.
- Yer aç has no undo.
- Hibernation off can be reversed by turning it back on. That is a Settings-level fact; it is not offered as undo.
- No history screen.

## Left to the builder

- XAML structure, control reuse vs new controls, animation technique, view model split.
- Exact detection heuristics for the new targets and for personal files, within the rules above, plus their tests.
- How the scan animation is paced against real scan results.
- How battery health is read: WMI `BatteryFullChargedCapacity` / `BatteryStaticData`, or `powercfg /batteryreport`.
- EN copy.
- Window size behaviour within the no-overlap rule.

## Open items carried along

- `brisk run --group speed|privacy` prints the whole plan. `CliParser` accepts the flag, but the output ignores it. Fix it and add a test.
- Release after this wave: his recorded run fills the README `TODO-RUN` markers, then the tag. Both are outside this spec.
