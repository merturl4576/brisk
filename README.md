# brisk

**A free, open-source CCleaner alternative for Windows that tells you *why* your PC is slow — and refuses to claim anything it did not measure.**

*The tool that tells you the truth about your Windows PC.*

[![CI](https://github.com/merturl4576/brisk/actions/workflows/ci.yml/badge.svg)](https://github.com/merturl4576/brisk/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/merturl4576/brisk?include_prereleases)](https://github.com/merturl4576/brisk/releases)
[![License: GPL v3](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)
![Windows 10 / 11 x64](https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-0078d4)

No installer. No account. No telemetry. No AI. Every diagnosis is a deterministic rule you can read in the source, and every finding carries the evidence it was built from.

[Türkçe README](README.tr.md)

![The brisk cockpit — Overview page](docs/media/cockpit.png)

*Rendered by the test suite's offscreen harness over fixture data — the same code path the app draws with. (Yes, "Free up 2 KB": fixtures are small. A real machine's numbers are bigger and just as honestly sourced.)*

---

## Install

One line, if you trust it — and you should not have to, so the script is [41 readable lines](get.ps1) that download the latest release, **refuse to run it unless the SHA-256 digest matches** the one published beside it, and unpack into one folder (uninstall = delete the folder):

```powershell
irm https://raw.githubusercontent.com/merturl4576/brisk/main/get.ps1 | iex
```

Or do it by hand: download **`brisk-app.exe`** from [Releases](https://github.com/merturl4576/brisk/releases) and run it. One file. Nothing is installed, nothing is written to your machine until you tell brisk to fix something, and everything it changes is recorded so it can be undone.

Prefer a terminal? Download **`brisk.exe`** instead — the same diagnostics, no elevation prompt, `--json` included.

```
brisk scan
```

Neither file needs .NET installed and neither needs the other. Verify your download against `SHA256SUMS.txt` in the same release.

> brisk is not code-signed yet, so SmartScreen will warn the first time. That warning is honest — believe the checksum, not this sentence.

---

## What a scan actually looks like

Real output from the maintainer's machine, not a mockup:

```
[! ] Too many programs start with Windows (impact ***)
    13 programs start with Windows. Heavy ones that can be started manually
    instead: WhatsAppDesktop, MSTeams.
[! ] Disk space fragmented across system folders (impact **)
    AppData\Local: 53.9 GB (over threshold); AppData\Roaming: 28.6 GB (over
    threshold); Desktop: 57.7 GB (over threshold); Downloads: 8.7 GB
[i ] temperature: CPU not read — GPU only. Memory integrity is on here, and the
     driver that reads CPU temperature is on Microsoft's vulnerable-driver
     blocklist, so Windows will not load it at any privilege level. brisk does
     not switch that off, and cannot prove it is the only reason here.
Reclaimable — Safe: 2.3 GB, Developer: 3.6 GB, Deep: 5.1 GB (run 'brisk clean')
```

That third line is the whole point of the project. brisk could have printed a GPU temperature and called it "your PC". It said what it could not read, why it probably could not read it, and that it cannot prove the reason.

---

## The read-back — brisk re-checks its own work

Every tool in this category says "done". brisk is built on the idea that "done" is a claim, and claims get re-checked.

**Privacy:** when brisk turns a Windows data-collection setting off, every later scan re-reads it and reports one of four states — **Held**, **Reverted**, **WrittenButIgnored**, **WrittenButUnverified**. That third state exists because on Windows Home, some policy writes are silently ignored: the registry says off, Windows keeps collecting. brisk is the tool that tells you that instead of celebrating.

**Speed:** the same DNA, applied to time. brisk records *when* it changed something, then reads Windows' own boot measurements (the timings Windows writes for itself, not a stopwatch brisk invented) and puts the two side by side on the Performance page:

> Boot before brisk's changes: about 59 s (middle of 4 boots) → since the last change: about 41 s (2 boots). Windows' own timings.

No causal claim anywhere in that sentence — two measurements, their counts, and the reader's own conclusion.

**What Windows knows about you:** the Privacy page reads out what your machine has been recording — every USB device ever plugged in (model and dates), how many program launches Windows has counted, whether Recall is present, and how much Delivery Optimization uploaded to other machines this month, split into local network vs internet. On the maintainer's machine that counter read 302 MB — all of it LAN, zero internet. Knowing the split is the difference between a scare and a fact.

![The Privacy page, four-state read-back included](docs/media/privacy-readback.png)

*The bottom band is the read-back over fixture data: one switch held, one was written but ignored, one cannot be verified on this edition, one was reverted — four different sentences, because four different things happened.*

---

## What brisk refuses to do

Most of this category is built on claims nobody checks. brisk's list of *refusals* is the product:

- **No registry cleaning.** The single most criticised feature of the tools brisk is an alternative to. It has never been shown to make a Windows machine faster, and it can break installed software.
- **No unmeasured speed promises.** brisk never says a fix made your PC faster unless it can read a number that says so. Windows keeps its own boot measurements; brisk reads those, and where it has none, it says so.
- **No "we stopped Windows from collecting that".** The most brisk will ever say is "this setting reads as off right now, and here is when I last checked".
- **No telemetry, no account, no cloud, no AI.** Nothing leaves your machine. There is no server to send it to.
- **No silent action.** Every fix is written to an action log, and the undoable ones can be undone from the same screen that applied them.
- **No personal data on anything shareable.** The report card — the one artifact designed to be screenshotted — never carries a device name, a file path or anything else that identifies you or your hardware. That rule is enforced by tests, not by policy.
- **No fake urgency.** No red "1,247 problems found!", no countdown, no Pro version. There is nothing to upsell.

---

## What it does

- **26 deterministic diagnostic rules** — power plan, startup load, boot time as Windows itself measured it, display refresh rate running below what the monitor supports, memory running below its rated speed, thermals, disk pressure, ten privacy rules with the read-back above, and more. Each finding carries its evidence.
- **One-click fixes with undo.** Rules are classified Auto, Confirm or Advise; brisk never applies a Confirm rule without asking, and never offers a fix for something it only observed.
- **A cleaner that works from an allowlist**, not from a pattern. It touches caches and temp files that Windows and your applications rebuild on their own — and nothing else. Three levels: Safe, Developer, Deep.
- **Deep shelves that speak up.** Windows.old, the hibernation file, the superseded half of the component store, stale dev caches — sized, named on the front page ("32 GB more sits on the deep shelves"), each behind its own consent with the trade-off written on the row: what comes back, what does not, what stops working.
- **A display fix you can see.** A 144 Hz monitor running at 60 Hz is common, and brisk auto-reverts the change after 15 seconds if you do not confirm you can still see the screen.
- **Every applied fix says when you will feel it** — most speed changes show from the next restart, so brisk says exactly that, then measures the next boots and reports them.
- **English and Turkish**, in both the app and the command line.

### Safety model

The cleaner only ever touches its allowlist. The GUI's one-click clean frees space immediately — it recycles, then purges exactly the items it just recycled, and nothing else in your Recycle Bin is touched. That one has no undo, by design, and says so before it runs. The per-level cleans and the CLI move items to the Recycle Bin and leave emptying to you. Settings and startup fixes are always undoable.

---

## Commands

```
brisk — Windows performance diagnostics and cleanup

Usage: brisk <command> [options]

Commands:
  scan                       run diagnostics + cleaner scan
    --json                   emit JSON instead of text
  fix                        apply diagnostic rule fixes
    --all                    apply every Auto rule with a finding
    --rule <id>              apply/undo a single rule
    --undo                   undo the named rule's last fix
    --yes                    actually mutate (otherwise dry-run)
  clean                      reclaim disk space
    --level <safe|developer|deep>  which cleanup level to run
    --yes                    actually delete (otherwise print plan)
  targets                    list cleanup targets
  rules                      list diagnostic rules
  version                    print the engine version
```

Without `--yes`, `fix` and `clean` print what they would do and change nothing.

---

## The questions this category has earned

PC "optimizers" are one of the most distrusted software categories on the internet, for good reasons. Those reasons deserve answers before anyone asks:

**"Snake oil. These tools never make a measurable difference."**
Usually true. That is why brisk measures: Windows' own boot timings before and after, disk bytes counted after the move actually happened, a report that says "0 B freed" when that is what happened. Where brisk has no measurement, it says so — it will not print a speed claim it cannot source.

**"Registry cleaners break machines."**
They do. brisk does not have one and never will.

**"The cleaner will eat files I care about."**
The cleaner cannot step outside its allowlist — every path is validated against a registered template with junctions resolved, protected folders (Documents, Desktop, Pictures, OneDrive, system roots) win over any template, and deletions go through the Recycle Bin except where a row explicitly tells you otherwise and asks separately.

**"Closed-source binary that itself phones home."**
GPL-3.0, no network code, no analytics, and the source of every rule is a file you can read. The scan output tells you which rule produced every line.

**"It will flip settings behind my back."**
Dry-run is the default in the CLI (`--yes` to mutate), every change lands in an action log, undo is on the same screen, and the risky display change auto-reverts in 15 seconds unless you confirm you can still see.

**"Unsigned exe — instant no."**
Fair. The release ships SHA-256 digests, the installer script refuses a download that does not match, and code signing via SignPath is planned once the repository is public. Until then SmartScreen's warning is honest, and so is this sentence.

---

## Build from source

Requires the .NET 8 SDK. Windows only — brisk reads the registry, WMI, the Windows event log and hardware sensors, so there is nothing here that could honestly run anywhere else.

```
dotnet test brisk.sln -c Release      # 1323 tests
dotnet run --project src/Brisk.Cli -- scan
pwsh -File scripts/publish.ps1        # both single-file executables into artifacts/
```

---

## Status

Pre-release, and honest about it: brisk has been verified against real hardware on the machines its maintainer owns. If a rule reads your machine wrong, that is exactly the bug report worth opening — bring the output of `brisk scan --json`.

## License

[GPL-3.0](LICENSE). brisk stays open: anyone may use, study and change it, and anyone who distributes a changed version has to publish their source too.
