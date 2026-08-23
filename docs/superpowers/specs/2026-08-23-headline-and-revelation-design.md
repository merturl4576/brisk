# Headline & Revelation — the measured number leads

**Status:** binding for the v0.2 presentation wave · **Date:** 2026-08-23
**Depends on:** the sixteen shipped rules; changes no measurement.

## Purpose

brisk's findings are honest but they speak in sentences, and the sentence
buries the number. `Desktop: 57.7 GB (over threshold)` reads as a log line;
57.7 GB *is* the finding. This wave restructures presentation so the measured
value leads on every surface, without adding a single new measurement.

Two additions to the engine, both permanent structure that later surfaces
(the report card, disclosure findings, before/after effect measurements)
will consume unchanged:

1. **Headline** — an optional, per-rule "lead value + caption" a finding can
   carry.
2. **RevelationPicker** — a deterministic selection of which headline leads
   a scan's presentation.

## Non-goals

- No new rules, probes, or measurements. Headlines dress findings that
  already exist.
- No report image, no scan history, no finding workbench — next wave.
- No change to health scoring, fix/undo flows, or the CLI's output.

## Design

### 1. Headline on `DiagnosticFinding` (engine)

An optional record: a short **value** (the number: `57.7 GB`, `54 s`, `13`,
`60 Hz`) and a **caption** (what the number is). Localization uses the same
mechanism findings already use for evidence — key plus arguments resolved
from the two resx files; no second localization system. Exact signatures are
pinned in the implementation plan against the existing evidence machinery.

Rules opting in this wave: `startup-bloat` (count), `disk-breakdown`
(largest folder), `boot-degradation` (boot seconds), `display-refresh`
(current vs supported Hz), `memory-speed` (configured vs rated speed).

`thermals` deliberately carries no headline: "could not read" is not a
number and must not be dressed as one. Its place is the finding card and,
next wave, the report card's "what brisk could not read" section.

The value field is plain text by design: a future before/after measurement
("54 s → 41 s") must fit without a schema change.

### 2. `RevelationPicker` (engine, pure)

Input: a scan's findings. Output: the headline-bearing findings in
presentation order. The order is a **declared priority list in one place in
code** — a product decision made visible, not a heuristic. Ties break by
severity, then impact stars, then rule id (ordinal). Deterministic: the same
scan always leads with the same number.

Lives in the engine so the GUI, the report card, and the CLI all make the
same choice.

### 3. Overview: the revelation band

A second always-dark strip directly under the cockpit, wearing the hero
vocabulary. Left: the top headline's value, large, via NumeralTick. Right:
the finding's one-sentence claim, the evidence line muted beneath it, and a
"see the evidence" link to the Health page. If more headlines exist, a quiet
"and N more findings" count.

The band is static — the cockpit already carries the page's ambient motion,
and a static band is free under reduce-motion.

**Empty state is part of the design:** with no headline-bearing findings the
band says, in both languages, that every rule looked and found nothing to
report on this machine — the rule count comes from the registry, never
hardcoded into the string. brisk's empty hand is shown, not hidden.

### 4. Finding cards: evidence-first

`FindingCard` gains a left value column when the finding carries a headline:
the value large, the title and evidence to its right. A finding without a
headline renders exactly as today. Fix/undo buttons do not move.

## Future structure (recorded so this wave does not paint over it)

- The report card (next wave) consumes RevelationPicker's order and the
  headline values; its "counts, never contents" rule is already structural
  here — a headline value carries a number, not a name.
- Disclosure-type findings ("what Windows keeps about you") are
  headline-native but are **notices, not problems**: they must not lower the
  health score. `FindingKind` (Problem/Notice) from the seven-rules spec is
  therefore a prerequisite of that wave, pulled forward from wave 3's
  leftovers. Nothing in this wave may assume every finding is a problem.
- A verified-hold rule ("what you disabled — did it stay disabled?") will
  produce headlines whose evidence is a read-back, not a write. The
  Headline/Picker structure carries it unchanged.

## Testing

- RevelationPicker: the declared order pinned one-to-one; tie-breaks; empty
  scan; a scan with no headline-bearing findings.
- Per opting rule: headline value and caption pinned, in both languages,
  through the existing resx-pinning pattern.
- FindingRow: headline presentation, fallback layout when absent.
- OverviewViewModel: band selection, "and N more" count, empty state.
- Suite stays green with zero warnings; the count only goes up.

## Acceptance

- A scan on the maintainer's machine leads with a real measured number on
  the Overview, in Turkish and in English.
- A machine with no findings shows the honest empty band.
- Every new string exists in both resx files, pinned by tests.
