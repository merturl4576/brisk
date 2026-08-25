# Report Card & Finding Workbench — the scan becomes a shareable image

**Status:** binding for the v0.3 wave · **Date:** 2026-08-23
**Depends on:** the Headline/RevelationPicker structure (v0.2); changes no measurement.

## Purpose

People do not share tools; they share their own results. fastfetch,
CrystalDiskMark and Speedtest spread because their output is a screenshot.
This wave gives brisk that surface: one PNG a user can post without thinking
twice — because the card is designed so that nothing on it can burn them.

Second deliverable, same wave: a public finding workbench — scripts that
plant a fully reversible misconfiguration, let brisk catch it, and undo it.
It is both the honest answer to "does this work on machines the maintainer
has never seen" (anyone can run the scripts and check) and the factory for
every demo GIF the README needs.

## Non-goals

- No new rules, probes, or measurements.
- No hardware summary on the card (that arrives with the hardware page).
- No measured fix-effect values (the self-grading wave fills that slot; the
  card's structure leaves room for it).
- No light theme, no theme choice — the card wears the app's dark cockpit
  language, full stop.
- No speed tests, no network calls of any kind. Producing a card touches
  nothing outside the machine.

## The card

Landscape 16:9, rendered at 1600×900 with a 2× scale factor. The app's dark
cockpit vocabulary (the existing Hero* resource family). All text in the
user's configured language.

**Top strip:** the product name, the scan's local date, the engine version.

**Left column:** the health score on the segmented gauge, static.

**Body, three sections:**

1. **Findings** — headline-bearing findings in RevelationPicker order, each
   as its lead value plus the rule's localized title. The picker and the
   headlines come from v0.2 unchanged. A scan with nothing to lead with keeps
   the section and says so — the same honest empty line the Overview band uses.

   > **Corrected 2026-08-26, by the Faz 3 wave.** This paragraph said *"the
   > card introduces no selection logic of its own."* **It does now, and it
   > had to.** `RevelationPicker.Pick` has no maximum — five was only how many
   > shipped rules carried a headline when this was written, and Faz 3's
   > disclosures took it to nine. The card's body column is a fixed 715 px
   > with nothing that scrolls, so a six-finding card was already being
   > sheared off silently, and the frame test could not see it because its
   > fixture carried the same stale five. The card now caps the finding rows
   > and prints a counted overflow line, which is selection logic. The
   > sentence is left quoted rather than deleted, because it is the sentence
   > that made the clipping invisible.
2. **What brisk could not read** — the card's signature. Fed **when this was
   written** by the sensor notice alone (the thermals story: which sensors did
   not answer and the measured reason when brisk has one); Faz 3 added the
   unreadable disclosure findings to the same section, deliberately through
   one channel rather than two. This section NEVER drops; when
   everything was readable it says so in one line. Honesty about the empty
   hand is the point of the section.
3. **Applied fixes** — the fix journal's entries, newest first, title and
   date. When the journal is empty the section drops, header and all. The
   measured before/after effect belongs to a later wave; the row layout
   leaves the slot open rather than faking it.

**Bottom strip:** one quiet line with the repository address.

### The privacy rule, sharpened

The card carries **headlines and titles, never raw evidence**. "13 programs
start with Windows" appears; the names of those programs do not. Folder
sizes appear; folder contents never existed anywhere in brisk to begin with.
Machine name, user name, and any path containing the user's profile
directory are banned from the card outright, and a test enforces the ban on
the card's view model rather than on good intentions.

## Rendering

A WPF `UserControl` template rendered through `RenderTargetBitmap` to PNG.
Zero new dependencies; the card shares the application's resource
dictionaries, so the cockpit look is inherited rather than imitated.

Testing splits along the seam: the card **view model** (section content,
ordering, the privacy filter, empty states) is fully unit-tested; the pixel
side gets a smoke test — the PNG exists, has a valid header, and has
non-trivial dimensions — run on an STA thread.

> **Corrected 2026-08-26, by the Faz 3 wave.** The seam held; "a smoke test"
> did not. The rendered side grew three jobs the model cannot do: the lit arc
> and the finding-row template are read off the pixels, and the body column's
> desired height is weighed against the height the Grid gives it — the one
> failure no pixel count can see, because a clipped card and a card that fits
> look equally tidy. Faz 3 added a fourth, the findings' overflow line, whose
> text is read off the laid-out control because a misspelled binding there
> renders an empty row instead of failing.

## Surfaces — and the architectural constraint that shapes them

WPF lives only in `brisk-app.exe`. The standalone `brisk.exe` does not carry
it and must not start to. Additionally, `Brisk.Cli` cannot reference the
`Brisk` project (the reference already points the other way), so the report
handler cannot live behind the CLI parser.

- **GUI:** an Overview button saves the PNG to `Pictures\brisk\`
  (`brisk-report-<date>.png`), copies it to the clipboard, and shows the
  path.
- **Console:** `brisk-app.exe report [--out <path>]`. The merged
  executable's entry point intercepts the `report` verb BEFORE delegating to
  the CLI's `Main`, attaches the parent console, and runs the same renderer
  the button uses.
- **`brisk.exe report`** answers honestly: the card needs the visual engine;
  use `brisk-app.exe report`. Exit code 2. The verb is recognized so the
  message can be precise — an unknown-command error would be a lie about
  why it refused.

## The finding workbench

`tools/workbench/` in the repository, public, PowerShell. Each scenario is a
pair — `plant-*.ps1` records the current state and plants the
misconfiguration; `restore-*.ps1` puts the recorded state back — plus one
shared `verify.ps1` that runs `brisk scan --json` and checks whether the
expected rule fired. The scripts leave nothing behind: plant → verify →
restore → verify-clean is the documented loop, and every plant script
refuses to run if its state file already exists (a double plant would
overwrite the record of the true original state).

Launch scenarios, all catchable by shipped rules and all fully reversible:

| Scenario | Rule that must catch it |
|---|---|
| Switch the power plan to Balanced | `power-plan` |
| Enable Start-menu web search | `search-web-results` |
| Disable Storage Sense | `storage-sense` |
| Set visual effects to best-appearance | `visual-effects` |
| Add inert entries to HKCU\Run | `startup-bloat` |
| Lower the display refresh rate (interactive, explicit confirmation — the picture visibly changes; brisk's own 15-second auto-revert is the safety net) | `display-refresh` |

The workbench is also a trust artifact: the announcement can say "we plant
the defects ourselves, catch them on camera, and publish the scripts — run
them on your machine and check us."

## Version

The wave ends with `EngineInfo.Version` at `0.3.0`. Tagging stays the
maintainer's move.

## Acceptance

- A card renders on the maintainer's machine in Turkish and in English,
  dark, 16:9, with all three sections behaving per this spec.
- The privacy test proves machine name, user name, and profile paths cannot
  reach the card's view model output.
- `brisk-app.exe report` writes a PNG from the console face;
  `brisk.exe report` refuses with the precise message.
- Every workbench scenario completes its plant → verify → restore →
  verify-clean loop on the maintainer's machine, leaving registry and
  settings byte-identical to the recorded originals.
- Suite green, 0 warnings, all new strings in both languages, pinned.
