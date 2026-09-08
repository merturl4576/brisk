# Honest gauge — Design

**Status:** approved in chat 2026-09-09 (maintainer: "icraat", option A).
**Branch:** `feat/honest-gauge`, lands as 0.10.0.

## Why

Two outside tests on 2026-09-08/09 (a friend's laptop, the maintainer's
father's PC). Both machines are slow by their owners' account. brisk said
health 89 on a machine whose boot Windows itself timed at 106 s, and the
one-click Speed up produced nothing either owner could feel. Both owners'
verdict, unprompted: "your system is wrong."

They are right about the number. The score charged nothing for the boot
because the boot rule is a Notice, and a Notice is free by the rule "brisk
does not grade what it cannot fix". That rule was written to keep 100
reachable on hardware nobody can change; on a machine that takes 106 s to
start it makes the gauge describe brisk's reach instead of the machine.
People read the gauge as the machine. So the gauge has to mean the machine.

The one-click button did nothing felt because "heavy" startup programs
were a fixed list of nine names. On a machine whose startup is fourteen
programs brisk has never heard of, the rule sees nothing to disable, even
while Windows' own boot log names, per boot, exactly which programs were
late. brisk already reads that log for the boot rule and then does nothing
with it.

## What changes

1. **A measured Notice charges the score.** HealthScore skips a Notice only
   when it is Hygiene. The boot rule becomes Measured (Windows timed it),
   so it charges: Warning x4 = 12 over 40 s, and Critical x5 = 25 over
   90 s, a new tier. large-files stays Hygiene and free: a machine is not
   worse for holding its owner's work. Memory speed, RAM pressure, thermals
   stay as they are (Hygiene notices, free) — none carries a number
   brisk's own fixes can move.

2. **Windows' blame makes a startup program heavy.** A Run entry whose
   executable Windows blamed (event 101, at least 5 s late) on at least
   two of the last eight boots is heavy, exactly as a name on the known
   list is. StartupManager and StartupBloatRule compute it in one place, so
   the Performance page's heavy badge and the Speed up button never
   disagree. Store apps keep the name match only (no executable to match).
   A short never list (OneDrive, SecurityHealth) stays out: their absence
   at startup is a loss the user would not trace back to brisk.
   The evidence names the blamed programs with the count of boots that
   blamed them, so the sentence says where the judgement came from.

3. **The flyout shows what it will not touch.** A second cleaning line,
   "{n} more with your say-so", sums Developer, Deep, opt-in and app-held
   targets: the bytes brisk measured and will not take without a decision.
   Shown only when non-zero.

4. **The flyout shows the boot read-back.** When BootTrend exists, one
   line: "Boot: {before} before brisk → {after} since (Windows' timing)".
   The Performance page already had it; the tray, which is where the
   owner looks, did not.

## Not in this wave

- A system-disk-is-HDD rule (the likeliest explanation for the father's
  PC). Needs a new probe; next wave.
- Third-party antivirus detection (McAfee on the friend's laptop).
- Charging for large user files. Never.

## Testing

Engine: HealthScore (measured notice charges, hygiene notice free), boot
rule (Critical tier, Measured), StartupBloatRule (blamed program is heavy,
never list, threshold), StartupManager (heavy flag from blame).
GUI: FlyoutViewModel (say-so line sums the right shelves and hides at
zero; boot trend line present and absent), FixAllService HasWork with a
blamed-only heavy set.
