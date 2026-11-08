<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/media/logo-dark.svg">
    <img src="docs/media/logo-light.svg" width="128" height="128" alt="brisk: a gauge arc with one measurement tick">
  </picture>
</p>

# brisk

**One click for a slow Windows PC. brisk frees space, takes programs off startup and switches telemetry off, then proves each part with numbers Windows itself reports.**

*A free, open-source CCleaner alternative with nothing to sell.*

[![CI](https://github.com/merturl4576/brisk/actions/workflows/ci.yml/badge.svg)](https://github.com/merturl4576/brisk/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/merturl4576/brisk?include_prereleases)](https://github.com/merturl4576/brisk/releases)
[![License: GPL v3](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)
![Windows 10 / 11 x64](https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-0078d4)

No installer. No account. No telemetry, and brisk itself makes no network connection. No AI in the product: every row is a deterministic rule you can read in the source.

[Türkçe README](README.tr.md)

<!-- TODO-RUN: docs/media/demo.gif from the maintainer's recorded run: open, Do it all, restart, open again. -->

<!-- TODO-RUN: **On the maintainer's laptop, one click:** n GB freed (the change in free space Explorer shows) · startup programs 14 → n · n telemetry settings off · boot n s → n s, by Windows' own boot timer. -->

---

## Install

One line, if you trust it. You should not have to, so the script is [63 readable lines](get.ps1). It downloads the latest release, **refuses to run it unless the SHA-256 digest matches** the one published beside it, and unpacks into one folder. Uninstall means deleting the folder.

```powershell
irm https://raw.githubusercontent.com/merturl4576/brisk/main/get.ps1 | iex
```

Or do it by hand: download **`brisk-app.exe`** from [Releases](https://github.com/merturl4576/brisk/releases) and run it. One file. Nothing is installed, and nothing on your machine changes until you press **Do it all** or a page's own button.

Prefer a terminal? Download **`brisk.exe`** instead. `brisk run` prints the same plan and changes nothing; `brisk run --yes` does it. `--level mid` or `--level max` takes Free up space further, as described below.

Neither file needs .NET installed, and neither needs the other. Verify your download against `SHA256SUMS.txt` in the same release.

> brisk is not code-signed yet, so SmartScreen will warn the first time. That warning is fair. Trust the checksum, not this sentence.
>
> If your Windows 11 has **Smart App Control** on, it blocks an unsigned program outright, with no "run anyway": the install script tells you when that is what happened, and brisk will not open until it is signed. Turning Smart App Control off is one-way (Settings › Privacy & security › Windows Security › App & browser control), so do not do it for brisk.

---

## What one click does

Home is a live view of the machine: CPU, graphics and memory gauges, temperature, network, battery (on a laptop), the machine's specs and the apps using the most right now. A strip at the bottom says what brisk found, for example *brisk is ready: 12 GB · 9 apps · 14 settings*, next to **Do it all**. Do it all frees space at the MIN level, takes the listed programs off startup and turns off the listed settings. The three pages, **Free up space**, **Speed up** and **Privacy**, list the same rows, and each one runs only its own group. Tap a row to skip it.

### Free up space

Three levels, each one including the levels before it. **Do it all** always runs MIN. On the Free up space page you pick the level, and its button runs that one.

| Level | Row | What goes, and the cost |
|---|---|---|
| MIN | Caches and temporary files | every browser profile's cache, app caches, thumbnails, app crash dumps and error reports, Windows temp, Windows Update and Delivery Optimization downloads, update leftovers. They rebuild themselves. |
| MIN | Developer caches | npm, pnpm, pip, NuGet, Yarn, Cargo, Gradle. They download again when needed. |
| MIN | Old crash reports | Windows' kernel dumps (LiveKernelReports, Minidump, MEMORY.DMP) older than 7 days |
| MIN | Windows setup logs | `.log` and `.etl` files in Windows' setup folder (Panther) older than 7 days, none while an upgrade runs |
| MIN | Old driver installers | the extracted NVIDIA and AMD installers left behind, older than 7 days |
| MID | Hibernation file | on a desktop, Fast startup goes off, and you can switch it back on; on a laptop, open work is lost if the battery dies |
| MID | Game shader caches | games may stutter on first launch |
| MID | Installers in Downloads | `.exe` and `.msi` files written 30 or more days ago, each file with its own box. Can't be undone. |
| MID | Recycle Bin | can't be undone |
| MID | Dependency folders of old projects | `node_modules` or `.next` beside a `package.json`, or a Python venv, in a project whose other top-level files and folders are all 90 or more days old. `npm install` brings them back. |
| MAX | Old Windows copy | no going back to the old Windows |
| MAX | Docker's unused data | `docker system prune`: stopped containers are deleted |
| MAX | Git repository in your home folder | the `.git` folder directly in your user folder, with that repository's history. Can't be undone. |
| MAX | Archives already extracted beside them | a `.7z`, `.zip` or `.rar` of 1 GiB or more next to a folder of the same name holding at least as many bytes. Only the archive goes. |
| MAX | Old restore points | every restore point but the newest. Windows does not report their size, so the row shows "—". |

Caches are deleted for real, not moved to the Recycle Bin, so the free space actually changes. A cache whose app is open, Chrome or Discord for example, waits in the tray and is cleaned when that app closes.

Two rows look inside your own folders: old projects' dependency folders and extracted archives. They walk Desktop, Documents, Downloads and the usual project folders (`source\repos`, `Projects`, `dev`, `code`, `repos`), four levels deep, and never follow a junction. Each path is checked against its rule again, on the disk, at the moment it would be deleted.

Large files that are yours are listed under *Your files · brisk won't touch them*: virtual machine disks (`.vdi`, `.vhdx`, `.vmdk`, but not the WSL and Docker stores), `.iso` images, Android emulators and BlueStacks, each 1 GiB or more. They never count toward any number and are never deleted.

### Speed up

- **Startup programs.** Everything Task Manager lists, switched off the way Task Manager does it, so you can turn any of them back on there. All of them go except the ones you would miss: antivirus, audio, touchpad and GPU helpers, OneDrive and other sync clients, password managers, VPNs and keyboard tools. Those are listed under *Leaving alone*, each with its reason.
- **Switches.** Full power while plugged in, background game recording off, GPU scheduling on, optimisations for windowed games, window animations off. Memory integrity and Virtual Machine Platform are listed under *Add if you like*, because Microsoft's own gaming guidance names them, and they run only if you add them: the first lowers security, the second breaks WSL and Docker.

### Privacy

- **Switches.** Usage data sent to Microsoft, background data-collection tasks, error reports, the advertising ID and tailored suggestions, activity history, Start menu ads, web results in Start search, online speech and typing, "How do you like Windows?" prompts, Recall, Copilot, sharing updates with strangers over the internet (sharing on your own network stays), and programs becoming administrator without asking. Location is listed under *Add if you like*, because Find my device needs it.
- **In a terminal.** `brisk live` prints, once, the apps using the most CPU and memory, which app is connected to which company right now (Microsoft's telemetry hosts tagged), and Windows' own 30-day per-app record of bytes sent and received. A connection does not mean data was sent, and the amounts are amounts only, never contents.

### Nothing asks before deleting

There is no dialog. Do it all runs MIN, and MIN holds nothing that cannot come back. A row that cannot come back says so on the row, in amber ("Can't be undone.", or exactly what is lost), and those rows sit at MID and MAX, which run only when you pick that level on the Free up space page. MAX is warned on screen, not asked. Every row is on screen before you press anything, and a tap skips it.

---

## The proof

Every tool in this category says "done". brisk shows numbers it did not make up. Right after a run, the strip on Home sums it up (*Last run: …*); `brisk receipt` prints these numbers whenever you ask:

- **Space:** free bytes on the system drive before and after the run. The number Explorer shows, not "items selected".
- **Startup:** enabled startup programs before and after.
- **Boot time:** Windows' own boot timing, the one it writes to its event log, for recent boots against the first boot after the run. After the restart, `brisk receipt` prints it.<!-- TODO-RUN: *Last time: n GB freed · startup n s → n s · n GB RAM freed*, from the maintainer's laptop. -->
- **RAM:** the memory the switched-off startup programs were using. It is counted only after the next boot confirms none of them started; one that started anyway is named and left out of the number.
- **Privacy:** every switch is read back after the run and reports **Off ✓**, **Windows ignored it** (some editions write a policy down and then ignore it) or **Turned back on**.

A number that goes the wrong way is shown too. A tool that only shows you the good number is not measuring.

### Undo

**Undo**, on Home after a run and on the Speed up and Privacy pages, reverses every setting and startup change of the run, newest first, and says plainly that deleted files do not come back. Free up space has no undo: deleted files are gone, and Undo does not switch hibernation back on. Every setting is written to a journal before it is changed, so even a row that failed halfway can be undone. If an undo fails, brisk says so and the run stays undoable.

---

## What brisk refuses to do

- **No registry cleaning.** It has never been shown to make Windows faster, and it can break installed software.
- **No removing Windows' own apps.** Debloat scripts break things that are hard to bring back; brisk leaves the apps where they are.
- **No deleting what cannot come back without showing it first.** Caches rebuild themselves. A row that cannot come back is on screen, marked in amber, before anything runs, and Do it all never includes one.
- **No speed claim it cannot source.** The boot time is Windows' own record. Where brisk has no measurement, it shows none.
- **No telemetry, no account, no cloud, no AI in the product.** brisk opens no network connection. There is no server to send anything to.
- **No fake urgency.** No red "1,247 problems found!", no countdown, no Pro version. There is nothing to upsell.

---

## Command line

```
brisk — Windows performance diagnostics and cleanup

Usage: brisk <command> [options]

Commands:
  scan                       run diagnostics + cleaner scan
    --json                   emit JSON instead of text
  fix                        apply diagnostic rule fixes
    --all                    apply every Auto rule with a finding
    --rule <id>               apply/undo a single rule
    --undo                   undo the named rule's last fix
    --keep                   commit the display mode currently on screen
    --yes                    actually mutate (otherwise dry-run)
  clean                      reclaim disk space
    --level <safe|developer|deep>  which cleanup level to run
    --target <id>            clean a single target by id (see 'brisk targets')
    --yes                    actually delete (otherwise print plan)
  run                        one click: free space, speed up, privacy
    --level <min|mid|max>    how far Free up space goes (default min)
    --group <space|speed|privacy>  only that group
    --yes                    apply (without it: show the plan, change nothing)
  undo                       undo the latest run
  receipt                    before/after numbers of the latest run
  live                       what is slowing you down, who you talk to, who sent what
  targets                    list cleanup targets
  rules                      list diagnostic rules
  report                     save the scan as a shareable PNG (brisk-app.exe only)
  version                    print the engine version
```

Without `--yes`, `run`, `fix` and `clean` print what they would do and change nothing.

`brisk run` frees space at MIN, like Do it all. `brisk clean --level deep` leaves out the targets you have to ask for by name: the old Windows copy, the hibernation file, the component store, the shader caches, Docker's unused data, the git repository in your home folder, old projects' dependency folders, extracted archives and restore points. Name one with `--target <id>`; `brisk targets` lists the ids.

---

## The questions this category has earned

PC "optimizers" are one of the most distrusted kinds of software on the internet, for good reasons. Those reasons deserve answers before anyone asks.

**"Snake oil. These tools never make a measurable difference."**
Usually true. That is why brisk measures: the free space Windows reports before and after, Windows' own boot timings before and after, and a startup RAM figure it counts only once the next boot confirms it. Where brisk has no measurement, it says so.

**"Registry cleaners break machines."**
They do. brisk does not have one and never will.

**"The cleaner will eat files I care about."**
It works from an allowlist, not from a pattern. Every path is checked against a registered template with junctions resolved, and protected folders (Documents, Desktop, Pictures, OneDrive, system roots) win over any template. The two rows that look inside Desktop and Documents, old projects' dependency folders and extracted archives, have no template: each path must sit inside a folder they walk and still fit its rule, read again from the disk at the moment of deletion. Your VM disks, `.iso` images and emulators are listed and never deleted. A Downloads file is deleted only if it is the exact file you were shown: if its size or date changed since, it is skipped.

**"Closed-source binary that itself phones home."**
GPL-3.0 and no network code. The connection list and the 30-day usage that `brisk live` prints are read from Windows on your machine; nothing leaves it.

**"It will flip settings behind my back."**
One click does change settings: that is the point. Every row is on screen before you press anything, including the ones that will not run; nothing runs until you press; every setting change is journaled and can be undone with Undo.

**"Unsigned exe. Instant no."**
Fair. The release ships SHA-256 digests, the install script refuses a download that does not match, and code signing is being applied for. Until then SmartScreen's warning is honest, and so is this paragraph.

**"This was built with AI. Why should I trust it?"**
It was, in part, and the next section says exactly which part. Trust the tests, the public specs and the source, not the author's word.

---

## How this was built

brisk was written by one person in 2026, with AI assistance. That deserves a plain statement, because "no AI" above means no AI inside the product, not none in its making.

Until 0.10, brisk only diagnosed. Every outside tester, the maintainer's father and friends among them, installed it, clicked, and felt nothing: at most a gigabyte freed. 0.11 is the rebuild that answer asked for. The test it was built against: do what a careful technician would do on this machine, visibly, and prove it afterwards.

What the maintainer owns: the product decisions, the safety rules, the architecture (a pure engine with no UI code, and a WPF window on top of it), every refusal on this page, and every judgement about what brisk is allowed to claim. What the AI (Claude Code) did: most of the typing, under written specs, on reviewed branches.

What keeps that honest:

- Every rule, switch and receipt runs against fake probes in 1,777 tests that pin what each one does and refuses to do.
- The specs and implementation plans that drove each release are public under [`docs/superpowers`](docs/superpowers).
- Every change was reviewed before merge, and the commit history records its own corrections instead of rewriting them.
- Every release ran on real machines the maintainer owns before it shipped.

If you find a place where the code claims more than it proves, that is the bug report this project most wants.

---

## Build from source

Requires the .NET 8 SDK. Windows only: brisk reads the registry, WMI, the Windows event log and hardware sensors, so there is nothing here that could honestly run anywhere else.

```
dotnet test brisk.sln -c Release      # 1,777 tests
dotnet run --project src/Brisk.Cli -- run
pwsh -File scripts/publish.ps1        # both single-file executables into artifacts/
```

---

## Status

Pre-release, and honest about it: brisk has been verified on the machines its maintainer owns. If a row reads your machine wrong, that is exactly the bug report worth opening. Bring the output of `brisk run` (it changes nothing without `--yes`).

## License

[GPL-3.0](LICENSE). brisk stays open: anyone may use, study and change it, and anyone who distributes a changed version has to publish their source too.
