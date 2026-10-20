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

**On the maintainer's laptop, one click:** TODO-RUN GB freed (the change in free space Explorer shows) · startup programs TODO-RUN 14 → n · TODO-RUN telemetry settings off · boot TODO-RUN s → n s, by Windows' own boot timer.

---

## Install

One line, if you trust it. You should not have to, so the script is [63 readable lines](get.ps1). It downloads the latest release, **refuses to run it unless the SHA-256 digest matches** the one published beside it, and unpacks into one folder. Uninstall means deleting the folder.

```powershell
irm https://raw.githubusercontent.com/merturl4576/brisk/main/get.ps1 | iex
```

Or do it by hand: download **`brisk-app.exe`** from [Releases](https://github.com/merturl4576/brisk/releases) and run it. One file. Nothing is installed, and nothing on your machine changes until you press **Do it all**.

Prefer a terminal? Download **`brisk.exe`** instead. `brisk run` prints the same plan and changes nothing; `brisk run --yes` does it.

Neither file needs .NET installed, and neither needs the other. Verify your download against `SHA256SUMS.txt` in the same release.

> brisk is not code-signed yet, so SmartScreen will warn the first time. That warning is fair. Trust the checksum, not this sentence.
>
> If your Windows 11 has **Smart App Control** on, it blocks an unsigned program outright, with no "run anyway": the install script tells you when that is what happened, and brisk will not open until it is signed. Turning Smart App Control off is one-way (Settings › Privacy & security › Windows Security › App & browser control), so do not do it for brisk.

---

## What one click does

The window starts looking the moment it opens. Within seconds Home shows how much space it found, how many programs start with Windows, how many apps are online right now and what is slowing the PC at this moment. **Do it all** runs every ticked row. The three pages, **Free up space**, **Speed up** and **Privacy**, list the same rows, so you can untick anything first.

### Free up space

| Row | Ticked | Why |
|---|---|---|
| Caches and temporary files: every browser profile, app caches, thumbnails, crash dumps, Windows Update and Delivery Optimization downloads, update leftovers | yes | they rebuild themselves |
| Developer caches: npm, pnpm, pip, NuGet, Yarn, Cargo, Gradle | yes | they download again when needed |
| Installers in Downloads (`.exe` `.msi` `.iso`), each file listed with its own box | yes | asked first, see below |
| Recycle Bin | yes | asked first |
| Old Windows copy | yes | asked first; no going back to the old Windows |
| Hibernation file | desktops only | a laptop hibernates on a dying battery, and without the file open work is lost |
| Game shader caches | no | games stutter while they rebuild them |
| Docker's unused data | no | stopped containers are deleted |

Caches are deleted for real, not moved to the Recycle Bin, so the free space actually changes. A cache whose app is open, Chrome or Discord for example, waits in the tray and is cleaned when that app closes. Desktop, Documents and your large files are never touched: brisk names the biggest ones and leaves them alone.

### Speed up

- **Slowing you now.** The apps using the most CPU or memory, measured over ten seconds and grouped by app, so Chrome's thirty processes are one row. **Close** asks the app to close, so it can ask you to save. **Force close** appears only if it is still running five seconds later. Windows' own processes get a one-line explanation ("Windows is installing updates") instead of a button.
- **Startup programs.** Everything Task Manager lists, switched off the way Task Manager does it, so you can turn any of them back on there. All ticked except the ones you would miss: antivirus, audio, touchpad and GPU helpers, OneDrive and other sync clients, password managers, VPNs and keyboard tools. Those stay unticked, each with its reason.
- **Switches.** Full power while plugged in, background game recording off, GPU scheduling on, optimisations for windowed games, window animations off. Memory integrity and Virtual Machine Platform are listed because Microsoft's own gaming guidance names them, and they stay unticked: the first lowers security, the second breaks WSL and Docker.

### Privacy

- **Switches.** Usage data sent to Microsoft, background data-collection tasks, error reports, the advertising ID and tailored suggestions, activity history, Start menu ads, web results in Start search, online speech and typing, "How do you like Windows?" prompts, Recall, Copilot, sharing updates with strangers over the internet (sharing on your own network stays), and programs becoming administrator without asking. Location is listed but unticked, because Find my device needs it.
- **Talking to now.** Which app is connected to which company right now, refreshed every two seconds, with Microsoft's telemetry hosts tagged. A connection does not mean data was sent, and the page says so.
- **Sent in 30 days.** Windows' own per-app record of bytes sent and received. Amounts only, never contents.

### The one question it asks

Do it all asks nothing, except when files that cannot come back are about to go: installers in Downloads, the Recycle Bin, the old Windows copy. Then a sheet lists them with their sizes, every time, and offers **Continue**, **Skip these** or **See the list**. Nobody loses a file they were not shown.

---

## The proof

Every tool in this category says "done". brisk shows numbers it did not make up:

- **Space:** free bytes on the system drive before and after the run. The number Explorer shows, not "items selected".
- **Startup:** enabled startup programs before and after.
- **Boot time:** Windows' own boot timing, the one it writes to its event log, for recent boots against the first boot after the run. Open brisk after the restart and Home leads with it: *Last time: TODO-RUN GB freed · startup TODO-RUN s → n s · TODO-RUN GB RAM freed*, from the maintainer's laptop.
- **RAM:** the memory the switched-off startup programs were using. It is counted only after the next boot confirms none of them started; one that started anyway is named and left out of the number.
- **Privacy:** every switch is read back after the run and reports **Off ✓**, **Windows ignored it** (some editions write a policy down and then ignore it) or **Turned back on**.

A number that goes the wrong way is shown too. A tool that only shows you the good number is not measuring.

### Undo

**Undo** reverses every setting and startup change of the run, newest first, and says plainly that deleted files do not come back. Every setting is written to a journal before it is changed, so even a row that failed halfway can be undone. If an undo fails, brisk says so and the run stays undoable.

---

## What brisk refuses to do

- **No registry cleaning.** It has never been shown to make Windows faster, and it can break installed software.
- **No removing Windows' own apps.** Debloat scripts break things that are hard to bring back; brisk leaves the apps where they are.
- **No deleting your files without listing them first.** Caches rebuild themselves; files you made or downloaded are shown by name before they go.
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

---

## The questions this category has earned

PC "optimizers" are one of the most distrusted kinds of software on the internet, for good reasons. Those reasons deserve answers before anyone asks.

**"Snake oil. These tools never make a measurable difference."**
Usually true. That is why brisk measures: the free space Windows reports before and after, Windows' own boot timings before and after, and a startup RAM figure it counts only once the next boot confirms it. Where brisk has no measurement, it says so.

**"Registry cleaners break machines."**
They do. brisk does not have one and never will.

**"The cleaner will eat files I care about."**
It works from an allowlist of caches, not from a pattern. Every path is checked against a registered template with junctions resolved, protected folders (Documents, Desktop, Pictures, OneDrive, system roots) win over any template, and a Downloads file is deleted only if it is the exact file you were shown: if its size or date changed since, it is skipped.

**"Closed-source binary that itself phones home."**
GPL-3.0 and no network code. The connection list and the 30-day usage are read from Windows on your machine; nothing leaves it.

**"It will flip settings behind my back."**
One click does change settings: that is the point. Every row is on screen, ticked or not, before you press anything; nothing runs until you press; every change is journaled and can be undone from the same button.

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

- Every rule, switch and receipt runs against fake probes in 1,604 tests that pin what each one does and refuses to do.
- The specs and implementation plans that drove each release are public under [`docs/superpowers`](docs/superpowers).
- Every change was reviewed before merge, and the commit history records its own corrections instead of rewriting them.
- Every release ran on real machines the maintainer owns before it shipped.

If you find a place where the code claims more than it proves, that is the bug report this project most wants.

---

## Build from source

Requires the .NET 8 SDK. Windows only: brisk reads the registry, WMI, the Windows event log and hardware sensors, so there is nothing here that could honestly run anywhere else.

```
dotnet test brisk.sln -c Release      # 1,604 tests
dotnet run --project src/Brisk.Cli -- run
pwsh -File scripts/publish.ps1        # both single-file executables into artifacts/
```

---

## Status

Pre-release, and honest about it: brisk has been verified on the machines its maintainer owns. If a row reads your machine wrong, that is exactly the bug report worth opening. Bring the output of `brisk run` (it changes nothing without `--yes`).

## License

[GPL-3.0](LICENSE). brisk stays open: anyone may use, study and change it, and anyone who distributes a changed version has to publish their source too.
