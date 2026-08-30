# Deep visible cleanup — the wave the field test ordered

His father's machine went 47 -> ~90 and the human felt nothing, while an
estimated 50+ GB sat untouched. His ruling: brisk goes aggressive on
visible value — "risk alarak oynamaliyiz" — reframed as intelligence, not
risk: category-aware removal of regenerable or superseded bytes, revealed
loudly, taken with one consent, measured after. Never user files uninvited.

The engine already has the hard parts: a target registry with levels, a
scanner that sizes and lock-probes, a runner with dry-run, batching,
observation-based attribution, and an action log. This wave grows the
registry by the three heaviest system targets and makes the Overview say
what the Deep shelf could free instead of whispering it on the Clean page.

## Tasks

### T1 — three heavy targets in the registry

`CleanupTargetRegistry` gains, all `CleanupLevel.Deep`:

1. `windows-old` — `%SystemDrive%\Windows.old`. optIn, admin, noBin.
   Sized by the scanner like any directory. Consequence copy: removing it
   removes the ability to roll back to the previous Windows; Windows
   itself deletes it ~10 days after an upgrade.
2. `hibernation-file` — `%SystemDrive%\hiberfil.sys`. optIn, admin, noBin.
   Freed by `powercfg /hibernate off`, restored by `on`. Consequence
   copy: hibernation and Fast Startup stop working until re-enabled.
3. `component-store` — WinSxS superseded components via
   `Dism.exe /Online /Cleanup-Image /StartComponentCleanup` (never
   /ResetBase: updates stay uninstallable). optIn, admin, no size promise
   (no path templates — the docker-prune shape); copy says DISM decides
   and takes minutes.

### T2 — the runner executes them honestly

`CleanRunner.Clean` special-cases the three ids the way it does
`docker-prune`, with two house rules the existing cases never needed:

- **Elevation is checked inside the case** (the early `switch` sits above
  the loop's `blockedByElevation` check): unelevated -> `refused`.
- **Attribution stays observation-based**: `windows-old` and
  `hibernation-file` record their pre-measured bytes as freed ONLY if the
  path existed before the command and is gone after it; anything else is
  an `error` with the reason. `component-store` records `external` with
  0 bytes — brisk refuses to invent a number DISM never reported.

`windows-old` removal runs `takeown /r`, `icacls /grant /t`, `rd /s /q` —
three commands through IProcessRunner, existence-checked after, never
touched when dry-run.

### T3 — the Clean page names consequences

`TargetRow` gains a localized note (`clean.note.<id>`, empty for targets
without one) and the page template renders it under the row. The three
new targets and `docker-prune` get notes; EN + TR resx.

### T4 — the Overview says it out loud

Under the safe-clean button, a deep-reveal line: total Deep+Developer
reclaimable bytes with the largest target named —
"Derinlerde 34,2 GB daha var (Windows.old 28,1 GB) — dokumu gor" —
clicking navigates to the Clean page. Hidden when the shelves are empty.
The number is `ReclaimableBytes` (lock-honest) plus sized optIn targets
whose clean is a consent away; targets with no size promise are named,
never numbered.

**AMENDED at the closing review (2026-08-30):** the "named, never
numbered" clause is deliberately dropped. The shipped format has no slot
for an unnumbered name, and a reveal line reading "+ component store:
size unknown" adds a question, not a number, to the page whose job is one
loud figure. Promise-less targets live on the Depolama page with their
consequence notes; the Overview reveal speaks only when it has gigabytes
to name. Recorded here so plan and implementation agree on purpose, not
by accident. The same review also added the lock-probe exemption for
past-the-bin targets (T1's hiberfil was being zeroed by the shell's own
question) and made `BypassesRecycleBin`/`RequiresElevation` load-bearing
in the runner.

## Order

T1 -> T2 (engine, TDD in BriskEngine.Tests) -> T3 -> T4 (GUI, tests in
Brisk.Tests). Suite green between tasks; one commit per task.

## Out of scope, recorded

- JSON-defined targets (winutil's contribution surface) — post-launch.
- Large/old user-file reveal (WizTree shape) — report-only today via
  disk-breakdown; growing it is its own wave.
- cleanmgr StateFlags route for windows-old was considered and dropped:
  it pops UI brisk cannot narrate, and takeown+rd is what the category's
  honest tools do.
