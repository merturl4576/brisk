# brisk

**A free, open-source CCleaner alternative for Windows that tells you *why* your PC is slow — and refuses to claim anything it did not measure.**

[![CI](https://github.com/merturl4576/brisk/actions/workflows/ci.yml/badge.svg)](https://github.com/merturl4576/brisk/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/merturl4576/brisk?include_prereleases)](https://github.com/merturl4576/brisk/releases)
[![License: GPL v3](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)
![Windows 10 / 11 x64](https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-0078d4)

No installer. No account. No telemetry. No AI. Every diagnosis is a deterministic rule you can read in the source, and every finding carries the evidence it was built from.

[Türkçe README](README.tr.md)

<!-- SCREENSHOT: the Health page with findings visible goes here, plus a GIF of a
     one-click fix and its undo. Both are the maintainer's call to record, since
     they mean launching the app on a real machine. -->

---

## Install

Download **`brisk-app.exe`** from [Releases](https://github.com/merturl4576/brisk/releases) and run it. One file. Nothing is installed, nothing is written to your machine until you tell brisk to fix something, and everything it changes is recorded so it can be undone.

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

## What brisk refuses to do

Most of this category is built on claims nobody checks. brisk's list of *refusals* is the product:

- **No registry cleaning.** The single most criticised feature of the tools brisk is an alternative to. It has never been shown to make a Windows machine faster, and it can break installed software.
- **No unmeasured speed promises.** brisk never says a fix made your PC faster unless it can read a number that says so. Windows keeps its own boot measurements; brisk reads those, and where it has none, it says so.
- **No "we stopped Windows from collecting that".** The most brisk will ever say is "this setting reads as off right now, and here is when I last checked".
- **No telemetry, no account, no cloud, no AI.** Nothing leaves your machine. There is no server to send it to.
- **No silent action.** Every fix is written to an action log, and the undoable ones can be undone from the same screen that applied them.

---

## What it does

- **16 deterministic diagnostic rules** — power plan, startup load, boot time as Windows itself measured it, display refresh rate running below what the monitor supports, memory running below its rated speed, thermals, disk pressure, and more. Each finding carries its evidence.
- **One-click fixes with undo.** Rules are classified Auto, Confirm or Advise; brisk never applies a Confirm rule without asking, and never offers a fix for something it only observed.
- **A cleaner that works from an allowlist**, not from a pattern. It touches caches and temp files that Windows and your applications rebuild on their own — and nothing else. Three levels: Safe, Developer, Deep.
- **A display fix you can see.** A 144 Hz monitor running at 60 Hz is common, and brisk auto-reverts the change after 15 seconds if you do not confirm you can still see the screen.
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

## Build from source

Requires the .NET 8 SDK. Windows only — brisk reads the registry, WMI, the Windows event log and hardware sensors, so there is nothing here that could honestly run anywhere else.

```
dotnet test brisk.sln -c Release      # 702 tests
dotnet run --project src/Brisk.Cli -- scan
pwsh -File scripts/publish.ps1        # both single-file executables into artifacts/
```

---

## Status

Pre-release, and honest about it: brisk has been verified against real hardware on the machines its maintainer owns. If a rule reads your machine wrong, that is exactly the bug report worth opening — bring the output of `brisk scan --json`.

## License

[GPL-3.0](LICENSE). brisk stays open: anyone may use, study and change it, and anyone who distributes a changed version has to publish their source too.
