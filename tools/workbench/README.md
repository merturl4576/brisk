# Finding workbench

Scripts that plant a fully reversible misconfiguration, let brisk catch it,
and put the machine back byte-identical. They exist so that anyone — not
just the maintainer — can verify brisk's rules on their own hardware, and
so the demo GIFs in the README show defects we planted on camera rather
than staged screenshots.

## The loop

    .\plant-<scenario>.ps1        # records the true state, plants the defect
    .\verify.ps1 <rule-id>        # runs brisk scan --json, expects the rule to fire
    .\restore-<scenario>.ps1      # puts the recorded state back
    .\verify.ps1 <rule-id> -ExpectClean   # expects the rule NOT to fire

Every plant script refuses to run while its state file exists — a second
plant would overwrite the record of your machine's true original state.
State files live in `.state/` and never leave your machine.

`verify.ps1` needs a brisk CLI; pass `-BriskExe <path>` or leave the default
(`..\..\artifacts\brisk.exe`, the tree's own publish output).

| Scenario | Rule it must trigger | Notes |
|---|---|---|
| power-plan | `power-plan` | switches the active scheme to Balanced |
| search-web | `search-web-results` | removes the policy that keeps Start local |
| storage-sense | `storage-sense` | turns Storage Sense off |
| visual-effects | `visual-effects` | sets visual effects to best appearance |
| startup-bloat | `startup-bloat` | adds six inert HKCU\Run entries |
| display-refresh | `display-refresh` | INTERACTIVE — visibly changes the screen; asks first |

## When the last step says FAIL and nothing is wrong

`-ExpectClean` asks whether the rule is silent, not whether the restore
worked. A rule can already be true of your machine before the workbench touches
it, and then it is still true after everything is put back:

- **power-plan** — if your machine already runs on Balanced or Power saver,
  the rule fires before the plant and after the restore. That is the rule
  doing its job.
- **startup-bloat** — the rule fires at six startup entries, or at one
  entry it recognises as heavy. Most real machines are already past that.

One rule needs a second condition the workbench cannot plant:

- **storage-sense** — it fires only when `C:` is under 15% free *and*
  Storage Sense is off. The plant turns the toggle off honestly, but on a
  machine with room to spare the finding will not appear, because a disk
  that is not filling up does not need automatic cleanup. Watch the
  registry value change and read `StorageSenseRule.cs` alongside it.

In every one of these cases the restore is still exact. If you want proof
independent of brisk, compare the registry value the plant printed against
the one the restore printed.

## The one that changes what you see

`plant-display-refresh.ps1` drops the primary display to the highest mode at
least 10 Hz below its current rate — the gap the rule calls a real defect
rather than unit rounding. It prints exactly what it is about to do and
waits for you to type `evet` or `yes`.

The mode is applied for this session only: nothing is written to the
registry, so a reboot undoes it even if you never run the restore script.
brisk's own `display-refresh` fix, with its 15-second auto-revert, is the
second safety net.
