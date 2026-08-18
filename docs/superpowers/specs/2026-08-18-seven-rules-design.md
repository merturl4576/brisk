# brisk — Seven New Rules, Design Spec

Date: 2026-08-18
Status: Approved in chat; this document is the written record.

## One-liner

Seven new diagnostic rules that find the slowness people actually complain
about — plus the three pieces of infrastructure they need: a display probe, a
hardware probe, and a scan history. Every rule stays deterministic, evidenced,
and reversible.

## Provenance

These rules are not invented. Each one traces to a documented, high-signal
complaint from real users:

| Rule | Evidence it matters |
|---|---|
| Display refresh rate | r/buildapc "Monitors are not 144Hz Out of the Box" (9,285 pts); "spent $800 on a 165Hz monitor and never set it" (4,993 pts) |
| Start menu web search | Windows 11 Start blocks local results on a network round-trip; Microsoft shipped an opt-out in 2026 after years of complaints |
| Memory speed (XMP/EXPO) | r/buildapc "my ram has been set to a slow speed this entire time" (703 pts); top reply: "a common mistake" |
| Boot degradation | r/sysadmin "What do you use to analyze slow startup after windows update?" — answers were Task Manager, Sysinternals, and "I've given up" |
| Update settling | Technician consensus: post-update indexing/Defender/NGEN work is mistaken for a broken machine |
| Hardware wall | r/sysadmin "Computers slow *because they're old*" (783 pts); top reply "modern software is bloated" (1,028 pts) — 8 GB is the recurring verdict |
| Change detection | Updates silently re-enable startup entries the user already disabled; no one-shot script can see this |

This is the "rule factory" working as intended: real complaints in,
deterministic rules out. The AI does the discovery; the product ships rules.

## Goals

1. Add seven rules that a user *feels*. Today's twelve rules are mostly correct
   but invisible (temp files, power plan). Refresh rate and Start menu search
   produce a visible, immediate "oh —" the moment they are fixed.
2. Let brisk say things no competitor says: *"nothing is wrong, Windows is
   still settling"* and *"no tweak will fix this — the bottleneck is 8 GB of
   RAM"*. Honesty is the differentiator.
3. Fix the silently-broken sensor path by running elevated.

## Non-goals

- **VBS / Memory Integrity is deliberately excluded.** Every other rule
  corrects something nobody chose; VBS is a security feature Microsoft enabled
  on purpose and it is doing its job. Turning it off is a trade, not a fix, and
  brisk must not make that trade for the user. Revisit only if a separate
  "gaming profile" concept is ever designed.
- Fan control. There is no general Windows API; NoteBook FanControl needs a
  config file per laptop model, which is the proof. Thermal *diagnosis* stays.
- Code signing. Required before public release (Certum Cloud Open Source, €49
  per ~15 months, or SignPath Foundation free if accepted, ~1 week). Tracked
  separately; it does not block this work.

## Delivery: three waves

Each wave ends with a real run on the maintainer's machine. Findings are
reported first; fixes are applied only after explicit approval.

| Wave | Rules | New infrastructure |
|---|---|---|
| 1 | `display-refresh`, `search-web-results` | `IDisplayProbe`, elevation manifest, scheduled-task autostart |
| 2 | `boot-degradation`, `memory-speed` | `IEventLogProbe`, `IHardwareProbe` |
| 3 | `hardware-wall`, `update-settling`, change detection | `FindingKind`, `ScanHistory` |

**Waves 2 and 3 were swapped after wave 1 shipped.** The original order put both
advisory-only rules in wave 2, so it would have delivered a release where nothing
could be acted on. Boot attribution and memory speed are the two findings a user
screenshots and sends to someone — *"your boot takes 31 s and 19 s of it is these
three"*, *"your RAM has been at half its rated speed"* — and the project's goal is a
repository people star, which advisory-only work does not serve. The unfixable
notices and the history store move to wave 3, where they belong together.

Elevation lands in wave 1: thermals are silently dead without it today, and
wave 3's event log needs it too.

## New model concept: `FindingKind`

`DiagnosticFinding` currently means "a problem, possibly fixable". Two new
rules do not fit that shape, so the record gains one field with a default:

```csharp
public enum FindingKind { Problem, Notice }
```

- `Notice` findings are **excluded from `HealthScore`** — 100 stays reachable,
  so a user is never permanently penalised for hardware they cannot change.
- The GUI renders notices in a separate band below the findings list, with no
  fix affordance.
- The default keeps all twelve existing rules untouched.

Rejected: a bare `bool` (does not name the concept); a separate `SystemNote`
type (a second path through rules, scan, CLI, and GUI for very little gain).

## The rules

### 1. `display-refresh` — Auto, Critical, 5 stars

**Detect.** For each attached display, compare the current refresh rate against
the highest rate available *at the current resolution and colour depth*. Report
only when the gap is real: the maximum must exceed the current rate by at least
10 Hz. This threshold exists specifically so brisk never reports the 59.94 Hz
vs 60 Hz distinction, which is a unit-rounding artefact and not a defect.

**Fix.** Set the display to the highest available rate at the current
resolution, per display.

**Undo.** Restore the recorded prior rate per display.

**Safety — the countdown.** This is the only fix in brisk whose failure removes
the user's ability to undo it: a driver may advertise a mode the cable, adapter
or KVM cannot carry, and the screen goes black. So after applying, the GUI
shows a 15-second confirmation ("Is the picture back?"). Without confirmation
the prior mode is restored automatically. The rule stays `Auto` — it is swept
up by *Fix all* like everything else — because this is a safety net, not a
consent gate. When the countdown expires, brisk reports honestly what it tried
and that it rolled back, naming the likely cause (cable or adapter).

### 2. `search-web-results` — Auto, Warning, 4 stars

**Detect.** `HKCU\Software\Policies\Microsoft\Windows\Explorer\DisableSearchBoxSuggestions`
is absent or not `1`. On Windows 10, also consider
`HKCU\Software\Microsoft\Windows\CurrentVersion\Search\BingSearchEnabled`.

**Fix.** Set the policy value to `1` (and clear the Windows 10 value where
present). Evidence text must state that the change applies after Explorer
restarts or the user signs in again — claiming an instant effect would be a
lie the user can catch.

**Undo.** Restore prior values; delete values that did not previously exist.

**Managed machines.** If the policy key is already present with a conflicting
value, brisk assumes an administrator set it and produces no finding. brisk
does not fight Group Policy.

### 3. `memory-speed` — Advise, Warning, 4 stars

**Detect.** Via WMI `Win32_PhysicalMemory`: `ConfiguredClockSpeed` at least
200 MT/s below `Speed` means the XMP/EXPO profile was never enabled and the
modules run at the JEDEC fallback. The margin absorbs vendor rounding; a gap
smaller than that is noise, not a misconfiguration.

**Report in MT/s, never MHz.** DDR performs two transfers per clock, so a
module reported as "2400" is running at 4800 MT/s. The single most-upvoted
reply in the source thread was a correction of exactly this confusion; getting
the unit wrong would mark brisk as amateur on first contact.

**No fix.** This lives in firmware. brisk names the module, the running rate,
the rated rate, and says the setting is called XMP or EXPO in the BIOS.

**Unavailable data.** If WMI returns nothing, zero, or equal values, there is
no finding. Soldered laptop memory legitimately reports equal values.

### 4. `hardware-wall` — Notice, Info, no score impact

**Detect.** Total physical memory at or below 8 GB **and** memory load at or
above 80% at scan time. Both conditions are required: 8 GB alone is not a
defect, and brisk must not lecture a machine that is coping.

Note this is a point-in-time sample, the only thing a scan can honestly
measure — the evidence text must say "at scan time" rather than imply
continuous observation.

The 80% bar is deliberately the same one `ram-pressure` uses, so both fire
together on such a machine. They say complementary things: `ram-pressure`
names the processes worth closing, `hardware-wall` says that closing them is
not a cure.

**Message.** That the software side is exhausted and the real bottleneck is
RAM. No fix, no stars, no score penalty.

This is the rule that proves brisk is not selling anything. Every optimizer in
the category reports a win on a machine like this; brisk says the honest thing
instead.

### 5. `boot-degradation` — Advise, Warning, 4 stars

**Detect.** Read `Microsoft-Windows-Diagnostics-Performance/Operational`
(requires elevation). Event ID 100 carries boot duration; 101, 102 and 103 name
the application, driver and service that delayed it. Read the last five boots
and report the median, requiring at least three to be present — one bad boot
after an update is normal and must not raise a finding.

**Report.** Not "you have 43 startup items" but "boot takes 31 s, and 19 s of
it belongs to these three". Windows measured this itself, which makes it
stronger than any heuristic.

**Fixable where the evidence lines up.** When an offender Windows named is also a
disableable startup entry, offer the switch: *"Windows measured this costing you
12 s at every boot"* next to a button that turns it off, undoably, through the
existing `StartupManager`. Where the name does not match a startup entry — a
driver, a service — the finding stays advisory and says so rather than guessing.

That link is the point of the rule. Everything else in the category tells you how
many programs start with Windows; this tells you which ones you are actually
paying for, using Windows' own measurement, and hands you the switch.

### 6. `update-settling` — Notice, Info, no score impact

**Detect.** Windows updated within the last 24 hours (per the Windows Update
install `LastSuccessTime`) **and** at least one known post-update worker is
consuming CPU — Windows Search indexing, Defender scanning, NGEN/.NET
compilation, storage optimisation. Both conditions are required: a recent
update with an idle machine is not a storm.

**Message.** That the machine is not broken, what it is doing, and to wait
rather than change settings.

When present this notice sorts above everything else. Every competing tool
"optimises" during this window and claims credit when the storm passes on its
own; refusing that is the point.

### 7. Change detection — `ScanHistory` service, not a rule

A rule about other rules would be the wrong shape. Instead a service runs after
the rule pass:

- Appends each scan's finding ids and timestamp to an append-only file under
  the existing `DiagnosticContext.DataDirectory`, retaining the most recent
  50 scans.
- Diffs the current scan against the previous one.
- Enriches each finding with `IsNew` and `FirstSeen`.

The GUI marks new findings. This produces the sentence no one-shot script can
produce: *"this startup entry was not here last month."* Its value scales with
how regularly brisk runs, which is why autostart is worth keeping.

## Elevation and autostart

The app currently ships no manifest, so it runs as a standard user. The stated
reason for elevating was `RealSensorProbe` returning nothing, leaving a user with a
genuine heat problem told nothing.

**That reason turned out to be wrong, and this record corrects it.** Measured on the
maintainer's machine after wave 1 shipped: GPU temperature reads fine *without*
elevation, through the vendor API. CPU temperature does not read *with* elevation
either, because LibreHardwareMonitor gets it through the WinRing0 kernel driver, and
WinRing0 is on Microsoft's vulnerable-driver blocklist. That machine has
`VulnerableDriverBlocklistEnable = 1` and Memory Integrity running, so the driver
cannot load at any privilege level. On a default Windows 11, thermals will be
GPU-only regardless of what brisk asks for.

There is an exact irony here worth keeping: Memory Integrity is the feature this spec
deliberately refuses to let brisk switch off. It is also the thing blocking brisk's
own CPU temperature reading. That was the right call and this is its price.

Elevation is still required — the `admin: true` cleanup targets under
`%SystemRoot%` need it, and so does wave 2's boot event log, which is only readable
elevated. So the manifest stays; only its justification changes. `ThermalsRule` must
also stop reporting GPU-only in silence and say that CPU temperature is unavailable
and why.

**Manifest.** `requestedExecutionLevel = requireAdministrator`. Every serious
tool in this category does the same (winutil, Optimizer, System Informer,
Autoruns); users of maintenance tools expect it. This also closes the Microsoft
Store as a channel — Store policy rejects apps requiring elevation — which is
acceptable: winget is the realistic channel, and winutil reaches 60k stars
without the Store.

**Autostart.** `requireAdministrator` breaks `HKCU\Run` launching, so
`StartupLauncher` is rewritten to create and remove a Scheduled Task with "run
with highest privileges", which starts elevated at logon without a UAC prompt.
It stays **off by default**, and when the user turns it on, brisk lists itself
among its own startup entries. A tool that criticises startup bloat may join
startup only if it holds itself to the same standard.

## Testing

Every new probe gets a fake in `TestContext.cs` alongside the existing ones, so
each rule is unit-tested against synthetic machines: dual 144 Hz monitors, a
single 60 Hz panel, a laptop with soldered memory, 8 GB under pressure, a
machine three hours past an update.

Beyond per-rule tests, the fakes let us assemble **machine profiles** — office
desktop, gaming laptop, six-year-old 4 GB laptop — and assert what brisk says
about each. This is stronger than physical hardware, which cannot be made to
run at 90 °C on demand.

Real-machine verification closes each wave: run against the maintainer's PC,
report findings, apply fixes only after approval.

## Risks

| Risk | Mitigation |
|---|---|
| Black screen from a refresh-rate change | 15-second automatic revert; honest report of the rollback |
| Group Policy conflict on the search key | Produce no finding when a policy value is already set |
| WMI or event log unavailable | Probes return empty; rules produce no finding rather than guessing |
| Scheduled task flagged as persistence by AV | Resolves on signing; until then autostart is off by default |
| False "hardware wall" on a coping machine | Requires sustained pressure, not just installed RAM |
