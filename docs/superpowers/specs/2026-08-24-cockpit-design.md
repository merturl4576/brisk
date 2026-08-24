# Cockpit Shell — Design

**Status:** approved 2026-08-24. Supersedes nothing; extends the visual layer
established in `2026-08-14-brisk-design.md`.

**Version:** lands as 0.5.0. No git tag (release policy: the version advances
per wave, tags wait for the announcement).

## Why

The maintainer ran brisk on his own machine and judged the interface — dark
theme included — "çok kötü, modern değil". Read against the code, the verdict
is not about the instrument: the Overview already carries a segmented gauge, an
orbiting comet with an opacity-falloff tail, sheen and a breathing glow, all
gated properly. The verdict is about everything around it:

1. **The shell.** A recessed grey nav rail, standard Windows chrome, and a flat
   page ground. Nothing about the window says instrument.
2. **The palette.** Graphite `#0E1116` with Windows blue `#4CC2FF` — the
   default dark theme of any WinUI app.
3. **The absence of atmosphere.** No depth, no ground, no horizon. The
   instrument floats on nothing.
4. **The pages.** Flat card lists where the reference has bracketed panels.

The direction is a Monster Kontrol Merkezi-style cockpit in brisk's own
navy-turquoise tone. 33 reference frames live at
`.superpowers/tasarim/monster-kareleri/` and the sampled palette (with a
correction for the source video's compression) at
`.superpowers/tasarim/renk-paleti.md`.

**On the resemblance.** The maintainer decided the colors may sit very close to
the reference. Palettes are not legally protectable; the hard line is the
reference's asset files, icons, textures and marks, which are never copied.
Every asset in this wave is drawn by us as vector geometry in XAML.

## Scope

**In:** the window shell, the theme token contract, one atmosphere layer, the
panel language in `Shared.xaml`, and the Overview instrument. Health,
Performance and Storage inherit their new look from `Shared.xaml` without
per-page redesign.

**Out, deliberately:** the saved report-card PNG (`ReportCard.xaml`,
`ReportCardRenderer`). It has its own pixel tests, its own privacy contract,
and a different reading distance — a social-media thumbnail, not a window. It
gets its own wave.

**Out, deliberately:** per-page layout redesign. Once the shell is real and
photographed, we will see from the images which page actually needs its layout
reworked, and decide then. Designing five page layouts before the shell exists
is deciding blind.

## Settled decisions

These were made by the maintainer (1-5) or by his stand-in while he was away
(6-7). They are inputs to this design, not open questions.

1. **Light theme: structure shared, atmosphere dark-only.** The cockpit
   skeleton renders in both themes; the gradient, rain, grid floor and glow are
   dark-only, expressed as one property on the atmosphere control rather than a
   second code path. Nothing is removed from Settings.
2. **Scope** as above.
3. **Method:** the snapshot harness is built first, then two or three real
   Overview variants are rendered and shown before the full build proceeds. A
   browser mockup would lie about what XAML makes cheap, and launching the app
   is the maintainer's test surface, not the implementer's checkpoint.
4. **Every ring carries real data. A sensor that cannot be read gets no ring —
   not an empty one, not a zero.**
5. **Approach:** renew the shell, let pages inherit.
6. **No temperature arc.** Temperature is a satellite readout, not a ring.
7. **No animation at window scale.** The atmosphere is static.

## Layer architecture

The window becomes three stacked layers.

**1. Atmosphere.** One control at the bottom of `MainWindow`'s grid, spanning
every row and column: a `Bg0→Bg1` vertical gradient, a tiled frozen
`DrawingBrush` carrying the digital-rain texture, a static `Path` describing the
perspective grid floor, and a horizon glow. It is drawn **once per window, not
per page**, so navigation never restarts or jumps it and each page costs
nothing to place on top. In the light theme the same control renders flat: no
rain, no grid, no glow.

**2. Page content**, with transparent backgrounds so the atmosphere shows
through.

**3. Chrome and overlays.** The custom title-bar content, the nav tiles, and
the three window-level overlays (identity warning, display notice,
display-confirm). The overlays sit above pages for a documented reason and the
atmosphere goes beneath them; none may become harder to see in the new palette.

## Token contract

`Dark.xaml` and `Light.xaml` expose the same brush keys — **15 today** — and
`ThemeDictionaryTests` pins that parity by parsing the XAML sources. The test
is load-bearing here: it fails loudly if a key is added to one file and
forgotten in the other, which is what keeps decision 1 honest.

**Retuned:**

| Key | Today | Cockpit |
|---|---|---|
| `Bg` | `#0E1116` | `#0A1626` |
| `BgElevated` → **renamed `Surface`** | `#151A21` | `#0C2434` |
| `BgHover` | `#1B2129` | `#123044` |
| `BorderBrushKey` | `#1E242C` | `#2C4A58` |
| `Divider` | `#1A2027` | `#1E3A48` |
| `AccentBrush` | `#4CC2FF` | `#5FD4E8` |
| `Text` | `#E8EAED` | `#E8F0F4` |
| `TextMuted` | `#9AA3AD` | `#7E93A0` |
| `TextFaint` | `#5E6772` | `#54707E` |

These are **starting values from the sampled palette, not final ones.** The
whole method of this wave is to look at rendered images; the variant step is
where they get tuned. What is fixed is the family and the relationships — a
later retune may not collapse two steps into one, and may not touch the
semantic colors.

**Added:** `Bg0` `#050B16` (gradient floor), `SurfaceHi` `#1B3A4A` (header
strips, active tiles), `Hairline` `#2C4A58` (panel edges), `AccentDim`
`#3A8FA3` (passive arcs, grid lines), `AccentGlow` (`Accent` at 35-50% opacity,
used only where the glow rule permits).

**`AccentTextBrush` stays dark** (`#0B0B0B`): it is the foreground painted on
accent-filled surfaces, and turquoise needs a dark foreground exactly as the
old blue did.

**`SeverityInfo` must not become the signature turquoise.** It is `#4CC2FF`
today, which is the same value the accent has today — so retuning the accent to
`#5FD4E8` while leaving `SeverityInfo` alone would silently split what is
currently one color into two near-identical blues. It gets its own starting
value, **`#5B8DEF`**: hue ~220° against the accent's ~187°, clearly separable,
still reading as "info" on navy. Tunable at the variant step like the rest, but
never toward the accent — an info finding carries a claim, and no
claim-carrying surface may wear the decorative signature color.

**`Hairline` and `BorderBrushKey` deliberately start at the same value**
(`#2C4A58`). They are separate keys serving different jobs — panel bracket edges
versus general control borders — and are expected to diverge during tuning.
Neither may be "simplified" into the other.

**Light theme values.** The parity test will force all six new keys into
`Light.xaml`. Their light values are chosen at the variant step, where a light
render is part of the variant set rather than an afterthought; `AccentGlow` is
near-transparent there, per settled decision 1.

**Untouched, and this is a product claim, not a style choice:** `Good`
`#4ADE80`, `SeverityWarning` `#FBBF24`, `SeverityCritical` `#F87171`. In brisk,
color carries a claim. The signature turquoise never enters a surface that
means something.

**Deleted: `BgRail`.** There is no rail. It has **two consumers plus a comment**
and all three must be handled explicitly in the plan — the parity test catches
the key, never the consumer:

- `MainWindow.xaml:56` — the nav rail's own `Border`, which is removed outright.
- `MainWindow.xaml:40` — the **dismissible display-notice banner**, which is not
  the rail and must be remapped to `Surface`.
- `Shared.xaml:1145` — a comment explaining a fill choice "on the recessed
  BgRail", which becomes false and must be rewritten.

**Renamed: `BgElevated` → `Surface`.** The panel language is written in terms of
`Surface` and `SurfaceHi`; leaving the old key beside a new `SurfaceHi` invites
an implementer to add a duplicate and split one fill into two. This is a
mechanical rename, but it is **not small — 11 binding sites**, not the three a
quick look at the pages suggests:

- `Shared.xaml` — eight `DynamicResource` bindings (lines 263, 298, 444, 489,
  632, 952, 1185, 1189) and three explanatory comments (536, 560, 1145) that
  name the key in prose.
- `CleanPage.xaml` — three bindings (12, 142, 371).
- `Dark.xaml` / `Light.xaml` — the declarations themselves, plus a comment in
  `Light.xaml`.

The comments are part of the rename, not decoration: a comment that names a key
which no longer exists is how the next reader learns the wrong vocabulary.

## Shell

**Window chrome.** `WindowChrome` (`System.Windows.Shell`), not
`WindowStyle="None"` with `AllowsTransparency="True"`. The reason is snap,
resize, maximize and the system menu: `WindowChrome` keeps all of them and lets
us draw only the title bar's *content*, while the transparency route
reimplements them by hand. Two traps are known and must be handled: a maximized
`WindowChrome` window is extended roughly 7px past each screen edge, so the
content margin binds to `WindowState`; and every interactive element in the
title bar needs `WindowChrome.IsHitTestVisibleInChrome="True"` or it is dead to
clicks. The atmosphere extends behind the title bar.

**Nav.** The rail disappears; the tiles float directly on the atmosphere, as in
the reference. The structure is **kept exactly as it is** and only restyled:
`RadioButton`s sharing `GroupName="nav"`, each with localized `Content`
(`nav.health`, `nav.performance`, `nav.clean`, `nav.settings`) and a Segoe
Fluent glyph in the `Tag` slot. That structure is what provides keyboard
arrow-key navigation, focus visuals and selected state, and the localized text
is what keeps the app navigable by someone who has not memorised four glyphs.
No new resx keys. The active tile is the only permanently glowing element in
the window.

**Size.** The default grows from 900×600 to **1100×700** — the empty space
around the instrument is half of the reference's effect, and 900 wide does not
have it. `MinWidth="900"` and `MinHeight="600"` keep every layout that is valid
today reachable. Size persistence is out of scope.

## Panel language

Added to `Shared.xaml`; pages inherit without structural edits.

A **cockpit panel**: `Surface` fill, a 1px `Hairline` border, and corner
brackets drawn as four short `Path`s where only the corner ~14px of each edge
takes `Accent`. An optional **header strip** in `SurfaceHi` carries the section
title in large light-weight type; it must **wrap rather than clip**, because
Turkish strings run longer than their English counterparts and the header is
the widest text in the panel.

The existing finding cards are reskinned into this language with no change to
their structure: ring at the left, headline, expander. The section labels added
in the previous wave (`health.advise.section`, `health.notice.section`) become
the header-strip style — no new strings.

## The instrument

**Outer ring: the health score**, in semantic green/amber/red. Unchanged in
meaning, and it remains the only claim-carrying element of the instrument. The
centre carries the score in large thin numerals.

**Inner arcs: CPU and RAM only**, rendered in `Accent`/`AccentDim` and **never**
in semantic colors. A RAM arc that turned amber at 80% would be a threshold
judgment no rule in this app has made.

**No reading, no arc.** This is a correction to existing behaviour, not only a
rule for new code. `OverviewViewModel.LiveCpuPercent` today documents itself as
"0 (an empty arc) until the CPU sensor has a delta to report" — so a machine
whose CPU counter has not spoken yet currently draws an empty ring, which is a
picture of a measurement that does not exist. After this wave the arc is
**absent** in that state. RAM has no numeric twin at all today
(`LiveRamText` is text-only) and gains one under the same rule.

**No temperature arc.** Temperature has no natural 0-100 range, so drawing it as
an arc requires inventing what a full sweep means, and every candidate makes a
claim brisk does not make:

- Mapped to `ThermalsRule`'s own thresholds (CPU ≥ 75, GPU ≥ 70), a 72°C CPU
  fills 96% of the arc while the rule says nothing at all about that machine.
  The picture screams "nearly maxed" about a machine brisk considers fine.
- It clamps: 90°C and 75°C draw the same full arc.
- The denominator changes silently with the sensor: the same fraction means 75°
  for a CPU and 70° for a GPU, with nothing on screen to say so.
- The rule is binary. "60% of the way to hot" is a claim made nowhere else in
  the product.

Temperature therefore stays a **satellite readout** with its number and source
badge — `LiveTempText`, `LiveTempBadgeText` and `LiveTempCaption` already exist.
**Satellites keep the existing convention: number and source when read, `"—"`
when not.** Decision 4's no-empty-ring rule governs rings, not readouts — a dash
is a statement that nothing was read, while an empty arc is a picture of a
measurement that does not exist. Free disk joins temperature as a satellite,
since it is not a percentage. Satellites sit on glowing
floor ellipses beneath the instrument and are part of it: decision 4's "the
Overview reads as one instrument" is satisfied by the composition, not by
forcing every value into a ring.

If a temperature arc is ever wanted, the thresholds must be **shared constants
read from the engine**, never copied into the view. This repo has already been
burned by a drifted predicate copy.

## Motion and performance

**The atmosphere is static.** A frozen tiled `DrawingBrush` and a static `Path`
— no storyboard at window scale. A full-window animated brush invalidates the
visual tree every frame, which is a different cost class from a small comet arc,
and the people who install a diagnostics tool are the people whose machines are
already slow. An animated background on that machine is self-refuting.

**The existing perpetual layer is unchanged.** Orbit, comet, sheen and breathing
glow keep their current contract under `AmbientMotionController`, which gates
on two signals: window visibility — the same signal that starts and stops
`LiveMetrics`, so nothing ticks while brisk is tray-only — and Windows'
reduce-motion setting, re-read on every start.

**Glow only on active and hover.** `DropShadowEffect` is expensive and compounds
per layer; no panel glows permanently.

**`CacheMode="BitmapCache"` on the atmosphere layer** so the texture rasterizes
once instead of recompositing per frame. At 1100×700 that is roughly 3MB of
video memory.

## Legibility

Pages have transparent backgrounds, so body text can land on the rain texture.
This is a hard rule, not a preference: either the composited texture keeps
`TextMuted` at **≥4.5:1 over bare atmosphere**, or long-form text always sits on
a `Surface` panel. The contrast check is computed against the **worst case** —
the brightest rain texel over the brightest gradient stop — because an average
passes while one glyph sits behind a bright column.

## Verification

**The snapshot harness (built first).** Renders a page, or the whole window, to
a PNG via `RenderTargetBitmap` — the technique `ReportCardRenderer` already
uses — into a gitignored folder, at a fixed 1.0 scale so images stay comparable
between sessions.

**It must inherit the gauge-settling step.** `ReportCardRenderer.SettleGauges`
exists because an animation clock only advances while a dispatcher is pumping
frames: offscreen there is no frame loop, animated values stay at zero, and the
lit arc renders **empty**. The report card once shipped exactly that — a dead
grey ring on the one image whose whole subject was the score. A harness without
this step produces lying photographs, and every design judgment made from them
is worthless.

**Images are for eyes; assertions are for claims.** These PNGs exist so a human
can look at them. They are not the test suite. This repo has been burned twice
in that exact spot: a size-only smoke test passed over a dead gauge, and a pixel
test written from a wrong explanation passed on the defect it was written to
catch. So the automated tests pin things that can be *stated*:

- theme key-set parity across `Dark.xaml` and `Light.xaml` (exists today);
- **every brush key referenced by `DynamicResource` in any XAML file exists in
  both theme dictionaries** — a new source-parsing test in the
  `ThemeDictionaryTests` style. This one is required by this wave specifically:
  `DynamicResource` lookups fail **silently**, so a consumer missed by the
  `BgRail` deletion or the `Surface` rename renders transparent with no
  exception and no failing test. Parity proves the two dictionaries agree with
  each other; this proves they agree with the app.
- a computed contrast ratio for `TextMuted` over the composited worst-case
  background — the brightest rain texel over the brightest gradient stop, not
  an average, because an average passes while one glyph sits behind a bright
  column — asserting ≥4.5:1;
- **no reading, no arc** — a null `CpuPercent` produces no CPU arc, asserted
  against the visual tree, not against pixels.

## Open for the maintainer

Recorded here rather than decided: after his own fix, the re-fired
startup-bloat finding is `CanFix: false` and its copy says the remainder is his
call, yet it still charges 9 points as a Problem. `FindingKind` is per-finding,
not per-rule, so that branch could opt into `Notice` without touching the heavy
branch. It is a product decision, not a defect, and it belongs to him.
