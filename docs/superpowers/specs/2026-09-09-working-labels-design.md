# The button that is working says so — Design

**Status:** approved in chat 2026-09-09 ("devam et" on the open UX gap).
**Branch:** `feat/honest-gauge`, lands with 0.10.0.

## Why

The 2026-09-07 live run on the maintainer's own laptop: after the one-click
clean, the purge and the follow-up scan (one to two minutes on a real disk)
greyed every button and moved nothing else. On the Storage page the card sat
on the purge label and a full bar for the whole rescan. The owner read the
app as stuck; a process dump proved nothing was pending. Being right about
the dump does not help — a reader on Reddit reacts exactly as the owner did.

## What changes

1. **Storage: the rescan is a named phase.** `CleanSimpleAsync` calls
   `Phase(clean.rescanning)` — "Measuring what changed…" / "Değişeni yeniden
   ölçüyor…" — before the follow-up scan, so the card shows the sweeping bar
   and that line until the measured truth lands. The run ending takes the
   sweep off, as before.

2. **Overview and tray: the working button wears its own label.**
   `IsCleaningSafe` and `IsFixingAll` on both view models, true only while
   that button's own job runs. The XAML moves each button's Content into
   its style (a local value would beat the trigger) and swaps it for
   `clean.cleaning` / `finding.fixing` on the flag. The flags clear BEFORE
   `ScanAsync`: the follow-up scan already has its own line on both
   surfaces, and a button reading "Cleaning…" under a line reading
   "Scanning…" would be two stories at once.

## What does not change

- `IsBusy`, `State.IsCleaning` and `State.IsScanning` keep disabling the
  buttons exactly as before; the new flags only change what a disabled
  button says.
- No new animation. The sweeping bar and the scanning line are the ones the
  app already had.

## Tests

- `CleanViewModelTests`: the rescan phase shows its name and the sweep the
  moment the follow-up scan starts (`FakeEngineHost.OnScan`); the phase
  order is purge, then rescan.
- `OverviewViewModelTests`, `FlyoutViewModelTests`: the clean flag is true
  while the purge is provably in flight (a gated `OnClean`) and false when
  the rescan starts; the fix flag goes true then false, and is false when
  the rescan starts. Each surface's other flag stays false.
- `LocTests`: `clean.rescanning` exists in both languages.
