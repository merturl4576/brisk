# Cockpit Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** brisk's window becomes a cockpit — one atmosphere layer, a navy-turquoise token set, custom chrome with floating nav tiles, corner-bracket panels the finding pages inherit, and an instrument that draws only what it actually measured.

**Architecture:** The shell is renewed and the pages inherit. A single `AtmosphereLayer` sits beneath everything in `MainWindow`; the 15-key theme contract is retuned, gains six keys, loses `BgRail` and renames `BgElevated` to `Surface`; `Shared.xaml` gains the panel language so Health/Performance/Storage change look without structural edits; the existing hero instrument gains a RAM arc and loses the empty-arc-when-unread behaviour it has today.

**Tech Stack:** .NET 8 (`net8.0-windows`, x64), WPF, `System.Windows.Shell.WindowChrome`, xUnit, resx localization.

**Spec:** `docs/superpowers/specs/2026-08-24-cockpit-design.md` — binding. Read it before Task 1; it carries the reasoning behind every constraint below, and the decisions marked "settled" there are not open.

**Branch:** `feat/cockpit-shell`, cut from `main` at `74d4eb7`.

## Global Constraints

- `TreatWarningsAsErrors` everywhere: **0 warnings**.
- Every user-visible string in BOTH `src/Brisk/Localization/Strings.resx` and `Strings.tr.resx`, single-line `<data>` format, pinned by tests (`LocTests` key-set parity plus the existence theory). This wave is not expected to add any string; if a task needs one, that is a signal to stop and report.
- The `Brisk` project has `ImplicitUsings` **disabled** — write explicit `using` directives.
- Verify with `dotnet test brisk.sln -c Release --nologo`. **Baseline entering this wave: 834 green** (365 BriskEngine.Tests + 469 Brisk.Tests).
- WPF objects require an STA thread and the xUnit runner does not have one. Any test that constructs a `FrameworkElement` must marshal onto an STA thread — follow the existing `OnStaThread` helper in `src/Brisk.Tests/ReportCardRenderTests.cs:344`.
- **Semantic colors are a product claim, not styling.** `Good` `#4ADE80`, `SeverityWarning` `#FBBF24`, `SeverityCritical` `#F87171` are never retuned. The signature turquoise never enters a surface that carries meaning.
- **Color values in this plan are starting values.** Task 5 is a gate where they get tuned against real renders. Relationships are fixed; exact hex is not.
- `EngineInfo.Version` advances to `0.5.0` in the last task. **No git tag is created. Nothing is pushed.**
- Commit messages: long-form story style (read `git log` for the voice — lowercase subject, a sentence saying what changed for the user), trailer `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`, no `Claude-Session:` trailer.

## File Structure

**Created:**
- `src/Brisk/Views/OffscreenLayout.cs` — the offscreen-rendering rules shared by the report card and the snapshot harness: lay out at a fixed size, settle animated gauges to their resting values, render. One responsibility: making a `FrameworkElement` photographable without a frame loop.
- `src/Brisk/Views/AtmosphereLayer.cs` — the window's ground: gradient, rain texture, grid floor, horizon glow, and a flat mode for the light theme. Draws; knows nothing about pages.
- `src/Brisk/Theming/Contrast.cs` — WCAG relative luminance and contrast ratio. Pure math, no WPF dependency beyond `Color`.
- `src/Brisk.Tests/Snapshots/SnapshotRenderer.cs` — test-side harness that writes page and window PNGs to a gitignored folder.
- `src/Brisk.Tests/Snapshots/SnapshotTests.cs` — the fact that produces the images and asserts they are not dead renders.
- `src/Brisk.Tests/ResourceKeyTests.cs` — proves every `DynamicResource` key referenced in XAML exists in both theme dictionaries.
- `src/Brisk.Tests/ContrastTests.cs`, `src/Brisk.Tests/AtmosphereLayerTests.cs`.

**Modified:** `src/Brisk/Theming/Dark.xaml`, `Light.xaml`, `Shared.xaml`; `src/Brisk/Windows/MainWindow.xaml` and `.xaml.cs`; `src/Brisk/Views/CleanPage.xaml`; `src/Brisk/Services/ReportCardRenderer.cs`; `src/Brisk/ViewModels/OverviewViewModel.cs`; `src/Brisk/Views/OverviewPage.xaml`; `src/BriskEngine/EngineInfo.cs`; `.gitignore`.

---

### Task 1: The snapshot harness

Built first because every later task is judged from its images, and an unsettled gauge photographs as a dead grey ring — the report card shipped exactly that once.

**Files:**
- Create: `src/Brisk/Views/OffscreenLayout.cs`
- Create: `src/Brisk.Tests/Snapshots/SnapshotRenderer.cs`, `src/Brisk.Tests/Snapshots/SnapshotTests.cs`
- Modify: `src/Brisk/Services/ReportCardRenderer.cs` (delegate its settling to the new shared helper)
- Modify: `.gitignore`

**Interfaces:**
- Produces: `Brisk.Views.OffscreenLayout.Settle(DependencyObject root)` — walks the tree and parks every `SegmentedGauge` at its resting `LitCount`; `OffscreenLayout.LayOut(FrameworkElement element, Size size)` — measure, arrange, update, settle, update again.
- Produces: `Brisk.Tests.Snapshots.SnapshotRenderer.Capture(Func<FrameworkElement> build, Size size, string name)` — runs the build and render on an STA thread and writes `<repo>/.snapshots/<name>.png` at 96 DPI (scale 1.0), returning the full path.

- [ ] **Step 1: Read the existing settling code**

Open `src/Brisk/Services/ReportCardRenderer.cs`. Its private `SettleGauges` and the comment above it explain why this exists: an animation clock only advances while a dispatcher pumps frames, so offscreen the gauge's ignition sweep never leaves zero and the lit arc renders empty. You are extracting that logic, not reinventing it. Keep its comment with the code.

- [ ] **Step 2: Write the failing test**

Create `src/Brisk.Tests/Snapshots/SnapshotTests.cs`:

```csharp
using System.IO;
using System.Linq;
using System.Windows;
using Brisk.Tests.Snapshots;
using Xunit;
using Size = System.Windows.Size;

namespace Brisk.Tests;

/// The images exist so a human can look at them. What is asserted here is
/// only what can be stated: the page laid out without throwing, and the PNG
/// is not a dead render. "Not dead" is the check that matters — the report
/// card once produced a perfectly valid 312 KB PNG whose subject, the ring,
/// was blank, and a size-only smoke test passed over it.
public class SnapshotTests
{
    [Fact]
    public void OverviewPage_LaysOutAndRendersSomething()
    {
        var path = SnapshotRenderer.Capture(
            () => new Brisk.Views.OverviewPage(),
            new Size(1100, 700),
            "overview");

        Assert.True(File.Exists(path));
        var colors = SnapshotRenderer.DistinctColors(path);
        Assert.True(colors > 16,
            $"render has {colors} distinct colours — a flat fill means the page " +
            "drew nothing, which is what a dead render looks like");
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test src/Brisk.Tests/Brisk.Tests.csproj -c Release --nologo --filter SnapshotTests`
Expected: build FAILS — `SnapshotRenderer` does not exist.

- [ ] **Step 4: Create the shared offscreen helper**

Create `src/Brisk/Views/OffscreenLayout.cs`. Move `SettleGauges`' body here as `Settle`, carrying its explanatory comment verbatim, and add:

```csharp
        public static void LayOut(FrameworkElement element, Size size)
        {
            element.Measure(size);
            element.Arrange(new Rect(new Point(0, 0), size));
            element.UpdateLayout();
            Settle(element);
            element.UpdateLayout();
        }
```

Then change `ReportCardRenderer` to call `OffscreenLayout.LayOut(card, new Size(Width, Height))` in place of its inline measure/arrange/settle block, and delete its private copy. The card's own render tests must still pass unchanged — they are the proof the extraction did not change behaviour.

- [ ] **Step 5: Create the harness**

Create `src/Brisk.Tests/Snapshots/SnapshotRenderer.cs`. It must: find the repo root by walking up for `brisk.sln` (same technique as `ThemeDictionaryTests.ThemingDir`); create `<root>/.snapshots`; run the build-and-render on an STA thread (copy the shape of `OnStaThread` in `ReportCardRenderTests.cs:344`); merge `Theming/Shared.xaml` and `Theming/Dark.xaml` into the element's resources so `DynamicResource` lookups resolve outside a running `Application`; call `OffscreenLayout.LayOut`; render with `new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32)`; encode PNG. Also expose `DistinctColors(string path)` which decodes the PNG and counts distinct 32-bit pixels.

**96 DPI and scale 1.0 are required** — the report card renders at 2× deliberately, but these images exist to be compared with each other across sessions, so they must not move.

- [ ] **Step 6: Ignore the output**

Append to `.gitignore`:

```
# Design snapshots — evidence for human eyes, never committed
.snapshots/
```

- [ ] **Step 7: Run to verify green**

Run: `dotnet test brisk.sln -c Release --nologo`
Expected: all green, 835 (834 + 1). Confirm `ReportCardRenderTests` still passes — that is the extraction's proof.

- [ ] **Step 8: Prove the settling step is load-bearing**

Temporarily comment out the `Settle(element)` call inside `LayOut`, re-run `--filter ReportCardRenderTests`, and record what fails. The ring-colour assertions must break. Restore the call. Put this transcript in your report: it is the evidence that the harness inherited the protection rather than merely referencing it.

- [ ] **Step 9: Commit**

```bash
git add src/Brisk/Views/OffscreenLayout.cs src/Brisk/Services/ReportCardRenderer.cs src/Brisk.Tests/Snapshots/ .gitignore
git commit  # message: the pages can be photographed, and the photographs are not lies
```

---

### Task 2: The token contract

**Files:**
- Modify: `src/Brisk/Theming/Dark.xaml`, `src/Brisk/Theming/Light.xaml`, `src/Brisk/Theming/Shared.xaml`
- Modify: `src/Brisk/Views/CleanPage.xaml`, `src/Brisk/Windows/MainWindow.xaml`
- Create: `src/Brisk.Tests/ResourceKeyTests.cs`

**Interfaces:**
- Produces: brush keys `Bg0`, `Surface`, `SurfaceHi`, `Hairline`, `AccentDim`, `AccentGlow` in both dictionaries; `BgElevated` and `BgRail` no longer exist.
- Consumes: nothing from Task 1.

- [ ] **Step 1: Write the guard test**

Create `src/Brisk.Tests/ResourceKeyTests.cs`. Parse every `.xaml` under `src/Brisk` for `{DynamicResource X}` occurrences (regex `\{DynamicResource\s+([A-Za-z0-9_]+)\}`), collect the brush keys declared in `Dark.xaml` and `Light.xaml`, and assert every referenced key exists in **both**. Exclude keys declared inside `Shared.xaml` itself (styles and templates, not brushes) by only asserting on names that neither dictionary declares — i.e. build the set of `Shared.xaml`'s own `x:Key` declarations and subtract it.

```csharp
    [Fact]
    public void EveryDynamicResourceBrushKey_ExistsInBothThemes()
    {
        var referenced = ReferencedKeys();          // from all XAML under src/Brisk
        var shared = SharedOwnKeys();               // x:Key declared in Shared.xaml
        var dark = BrushKeys("Dark.xaml");
        var light = BrushKeys("Light.xaml");

        var missing = referenced
            .Except(shared, StringComparer.Ordinal)
            .Where(k => !dark.Contains(k) || !light.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
    }
```

- [ ] **Step 2: Run it — it must PASS today**

Run: `dotnet test src/Brisk.Tests/Brisk.Tests.csproj -c Release --nologo --filter ResourceKeyTests`
Expected: PASS. This is a characterization test: today every key resolves, and that is the state we must not lose.

- [ ] **Step 3: Prove it is load-bearing before relying on it**

Delete the `BgRail` line from `Dark.xaml` only, re-run the filter, and confirm the test FAILS naming `BgRail`. Restore the line. Record the transcript — the whole point of this test is that `DynamicResource` failures are silent, and an untested guard against silent failure is decoration.

- [ ] **Step 4: Rename `BgElevated` → `Surface` across all 11 binding sites**

`Shared.xaml` lines 263, 298, 444, 489, 632, 952, 1185, 1189 (bindings) and the prose comments at 536, 560, 1145; `CleanPage.xaml` lines 12, 142, 371; the declarations in `Dark.xaml` and `Light.xaml` plus `Light.xaml`'s comment. The comments are part of the rename — a comment naming a key that no longer exists teaches the next reader the wrong vocabulary.

- [ ] **Step 5: Delete `BgRail` and remap its non-rail consumer**

Remove the key from both dictionaries. `MainWindow.xaml:56` is the rail's own `Border` and is deleted in Task 4 — for now point it at `Surface` so the tree stays valid. **`MainWindow.xaml:40` is the dismissible display-notice banner, not the rail**: remap it to `Surface` permanently. Rewrite `Shared.xaml:1145`'s comment, which explains a fill choice in terms of "the recessed BgRail".

- [ ] **Step 6: Retune the values**

`Dark.xaml`: `Bg` `#0A1626`, `Surface` `#0C2434`, `BgHover` `#123044`, `BorderBrushKey` `#2C4A58`, `Divider` `#1E3A48`, `Text` `#E8F0F4`, `TextMuted` `#7E93A0`, `TextFaint` `#54707E`, `AccentBrush` `#5FD4E8`, `SeverityInfo` `#5B8DEF`, `AccentTextBrush` unchanged `#0B0B0B`. Add `Bg0` `#050B16`, `SurfaceHi` `#1B3A4A`, `Hairline` `#2C4A58`, `AccentDim` `#3A8FA3`, `AccentGlow` `#5FD4E8`.

`Hairline` and `BorderBrushKey` deliberately start equal; they are separate keys expected to diverge in tuning, and neither may be folded into the other.

`Light.xaml`: add the same six keys with light values — `Bg0` `#FFFFFF`, `SurfaceHi` `#E4E9EE`, `Hairline` `#1F000000`, `AccentDim` `#7FA9C4`, `AccentGlow` `#00000000` (near-transparent: the light theme has no glow). Keep every existing light value as it is; the light theme's retune belongs to the Task 5 gate.

- [ ] **Step 7: Run to verify green**

Run: `dotnet test brisk.sln -c Release --nologo`
Expected: all green, 836. `ThemeDictionaryTests` proves the two dictionaries still agree with each other; `ResourceKeyTests` proves they agree with the app.

- [ ] **Step 8: Photograph the result**

Run the snapshot fact and look at `.snapshots/overview.png`. The app will look wrong at this stage — new colors, no atmosphere, a rail that is still there — and that is expected. Note in your report whether anything rendered *transparent*, which would mean a `DynamicResource` key the guard test did not cover.

- [ ] **Step 9: Commit**

```bash
git add src/Brisk/Theming/ src/Brisk/Views/CleanPage.xaml src/Brisk/Windows/MainWindow.xaml src/Brisk.Tests/ResourceKeyTests.cs
git commit  # message: the palette turns navy, and a missing brush key stops failing in silence
```

---

### Task 3: The atmosphere layer

**Files:**
- Create: `src/Brisk/Views/AtmosphereLayer.cs`, `src/Brisk/Theming/Contrast.cs`
- Create: `src/Brisk.Tests/ContrastTests.cs`, `src/Brisk.Tests/AtmosphereLayerTests.cs`
- Modify: `src/Brisk/Windows/MainWindow.xaml`

**Interfaces:**
- Consumes: the brush keys from Task 2.
- Produces: `Brisk.Views.AtmosphereLayer`, a `FrameworkElement` with `public bool IsFlat { get; set; }` (a `DependencyProperty`) and `public Color BrightestComposite()` returning the brightest pixel colour the layer can produce, for the contrast test to read.
- Produces: `Brisk.Theming.Contrast.Ratio(Color a, Color b)` → `double`, and `Contrast.RelativeLuminance(Color c)` → `double`.

- [ ] **Step 1: Write the contrast test**

Create `src/Brisk.Tests/ContrastTests.cs`. Pin the maths against known WCAG values first — black on white is 21:1, a colour against itself is 1:1 — then the real assertion:

```csharp
    /// The rule from the spec: body text must stay legible on bare atmosphere.
    /// Worst case, not average — an average passes while one glyph sits behind
    /// the brightest column of rain.
    [Fact]
    public void TextMuted_OnTheBrightestAtmosphere_StaysLegible()
    {
        var layer = new AtmosphereLayer();          // dark mode default
        var worst = layer.BrightestComposite();
        var textMuted = (Color)ColorConverter.ConvertFromString("#7E93A0")!;

        Assert.True(Contrast.Ratio(textMuted, worst) >= 4.5,
            $"TextMuted on the brightest atmosphere is " +
            $"{Contrast.Ratio(textMuted, worst):F2}:1 — below the 4.5:1 floor");
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Brisk.Tests/Brisk.Tests.csproj -c Release --nologo --filter ContrastTests`
Expected: build FAILS — neither type exists.

- [ ] **Step 3: Implement `Contrast`**

Standard WCAG 2.x: channel → sRGB linearization (`c <= 0.03928 ? c/12.92 : ((c+0.055)/1.055)^2.4`), luminance `0.2126R + 0.7152G + 0.0722B`, ratio `(lighter + 0.05) / (darker + 0.05)`.

- [ ] **Step 4: Implement `AtmosphereLayer`**

A `FrameworkElement` overriding `OnRender`, drawing in this order: the `Bg0→Bg1` vertical `LinearGradientBrush`; the rain — a frozen `DrawingBrush` of short vertical `AccentDim` strokes on a tile, `TileMode.Tile`, at low opacity; the perspective grid floor — a static `StreamGeometry` of lines converging on a horizon in the lower third, stroked in `AccentDim` at 8-12% opacity; the horizon glow — a `RadialGradientBrush` of `AccentGlow` fading out.

`IsFlat` short-circuits everything after the gradient, and in flat mode the gradient collapses to a single `Bg` fill. Bind it in XAML to the active theme.

`BrightestComposite()` computes — not samples — the brightest result: the brightest gradient stop composited with the rain stroke colour at the rain's opacity. It must be a computation over the same constants `OnRender` uses, so the two can never drift.

- [ ] **Step 5: Place it in the window**

In `MainWindow.xaml`, add as the first child of the root `Grid`, spanning every row and column, with `Panel.ZIndex="-1"` and `CacheMode="BitmapCache"`. Page hosts and overlays keep their own z-order above it. Set page container backgrounds to `Transparent`.

- [ ] **Step 6: Write the flat-mode test**

In `AtmosphereLayerTests.cs`, on an STA thread: an `AtmosphereLayer` with `IsFlat = true` renders to a bitmap containing exactly one distinct colour; with `IsFlat = false` it contains many. This is the light-theme contract from settled decision 1, asserted rather than trusted.

- [ ] **Step 7: Run to verify green**

Run: `dotnet test brisk.sln -c Release --nologo`
Expected: all green, 840-ish. If the contrast test fails, **lower the rain opacity — do not lower the floor.** The 4.5:1 number is from the spec and is not tunable at this task.

- [ ] **Step 8: Photograph and commit**

Render the snapshot and look at it before committing.

```bash
git add src/Brisk/Views/AtmosphereLayer.cs src/Brisk/Theming/Contrast.cs src/Brisk/Windows/MainWindow.xaml src/Brisk.Tests/ContrastTests.cs src/Brisk.Tests/AtmosphereLayerTests.cs
git commit  # message: the window gets a ground, and the text stays readable on it
```

---

### Task 4: The shell

**Files:**
- Modify: `src/Brisk/Windows/MainWindow.xaml`, `src/Brisk/Windows/MainWindow.xaml.cs`
- Modify: `src/Brisk/Theming/Shared.xaml` (the `NavRadio` and `NavBrand` styles)
- Create: `src/Brisk.Tests/ShellSourceTests.cs`

**Interfaces:**
- Consumes: `AtmosphereLayer` from Task 3.
- Produces: no new public API. The nav's `x:Name`s (`NavOverview`, `NavHealth`, `NavPerf`, `NavClean`, `NavSettings`), their `GroupName="nav"` and the `Nav_Checked` handler are unchanged — Task 7 and `MainWindow.xaml.cs`'s existing routing depend on them.

- [ ] **Step 1: Write the source-parsing guard**

`WindowChrome` behaviour cannot be asserted from a unit test, but its *configuration* can, and the two traps are configuration mistakes. Create `src/Brisk.Tests/ShellSourceTests.cs` parsing `MainWindow.xaml`:

```csharp
    /// A maximized WindowChrome window is extended ~7px past each screen edge,
    /// so content needs a WindowState-driven margin; and a title-bar control
    /// without IsHitTestVisibleInChrome is dead to clicks. Both are silent
    /// failures in a unit test and obvious ones in a user's hands.
    [Fact]
    public void TitleBarInteractives_AreHitTestVisibleInChrome()
```

Assert that every `Button` descendant of the element named `TitleBar` carries `WindowChrome.IsHitTestVisibleInChrome="True"`, and that a `WindowChrome.WindowChrome` element is declared with a non-zero `CaptionHeight`.

- [ ] **Step 2: Run to verify it fails**

Expected: FAIL — there is no `TitleBar` element yet.

- [ ] **Step 3: Add `WindowChrome`**

In `MainWindow.xaml`, add `xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"` and:

```xml
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="44" CornerRadius="0"
                            GlassFrameThickness="0" ResizeBorderThickness="6"
                            UseAeroCaptionButtons="False" />
    </shell:WindowChrome.WindowChrome>
```

Do **not** use `WindowStyle="None"` with `AllowsTransparency="True"` — snap, resize and the system menu come free with `WindowChrome` and would have to be reimplemented by hand otherwise.

- [ ] **Step 4: Draw the title bar**

A 44px row at the top named `TitleBar`: the brisk mark and `[app.name]` at the left, and minimize/maximize/close buttons at the right, each with `WindowChrome.IsHitTestVisibleInChrome="True"` and click handlers in `MainWindow.xaml.cs` (`WindowState` toggling for maximize, `Close()` for close). The atmosphere runs behind it — the title bar has no background of its own.

- [ ] **Step 5: Handle the maximize overhang**

Bind the root content `Margin` to `WindowState` through a converter so a maximized window gets `7,7,7,7` and a normal one `0`. Without this the window's edges sit off-screen when maximized.

- [ ] **Step 6: Float the nav tiles**

Delete the rail `Border` at `MainWindow.xaml:56` entirely (the one now pointing at `Surface` from Task 2 step 5). The `StackPanel` of `RadioButton`s stays exactly as it is — same names, same `GroupName`, same localized `Content` bindings, same `Tag` glyphs — and moves into the atmosphere with a left margin. In `Shared.xaml`, restyle `NavRadio`: a rounded-rect tile with a `Hairline` border, glyph above and the localized label below in small type, `SurfaceHi` fill plus an `AccentGlow` `DropShadowEffect` **only** on `IsChecked` and `IsMouseOver`. The label must wrap rather than clip.

- [ ] **Step 7: Resize the window**

`Width="1100" Height="700" MinWidth="900" MinHeight="600"`. No size persistence — out of scope.

- [ ] **Step 8: Run to verify green**

Run: `dotnet test brisk.sln -c Release --nologo`
Expected: all green. `ResourceKeyTests` from Task 2 will catch any brush key the deleted rail took with it.

- [ ] **Step 9: Photograph the window**

Extend the snapshot fact to capture the whole `MainWindow` at 1100x700, not only the page. Look at it. **Report anything that surprises you rather than fixing it silently** — the next task is a decision gate and the maintainer's stand-in needs an honest picture.

- [ ] **Step 10: Commit**

```bash
git add src/Brisk/Windows/ src/Brisk/Theming/Shared.xaml src/Brisk.Tests/ShellSourceTests.cs src/Brisk.Tests/Snapshots/
git commit  # message: the rail is gone and the tiles float on the ground the window now has
```

---

### Task 5: The variant gate — DECISION, NOT CODE

This task writes no production code. It exists because the maintainer decided the look is judged from real renders before the full build proceeds, and because Tasks 6 and 7 are expensive to redo.

**Files:**
- Create: `.snapshots/` images (gitignored) and a short written comparison in the SDD workspace.

- [ ] **Step 1: Produce three variants of the whole window**

Render `MainWindow` at 1100x700 three times, changing only tuning constants — never structure:
- **A, quiet:** rain opacity at the low end, grid barely visible, brackets 1px.
- **B, middle:** the spec's starting values as written.
- **C, loud:** rain and grid at the top of what the contrast test permits, brackets 2px, a wider horizon glow.

Every variant must still pass `ContrastTests`; a variant that fails it is not a variant, it is a bug.

- [ ] **Step 2: Render the light theme too**

One light-theme render of variant B, proving the flat mode looks deliberate rather than unfinished. The light theme's own values are tuned here, not earlier.

- [ ] **Step 3: Write the comparison**

A short note naming what differs between A, B and C in words, plus the measured contrast ratio of each. The images are the evidence; the note is what makes them comparable.

- [ ] **Step 4: STOP and present**

Hand the four images and the note to the maintainer's stand-in and wait for a decision. **Do not proceed to Task 6 without one.** The chosen variant's constants become the values in `Dark.xaml`/`Light.xaml`, committed in this task.

- [ ] **Step 5: Commit the chosen values**

```bash
git add src/Brisk/Theming/Dark.xaml src/Brisk/Theming/Light.xaml
git commit  # message: the ground settles at the weight that was chosen from the pictures
```

---

### Task 6: The panel language

**Files:**
- Modify: `src/Brisk/Theming/Shared.xaml`
- Create: `src/Brisk.Tests/PanelSourceTests.cs`

**Interfaces:**
- Produces: `CockpitPanel` (a `Style` targeting `Border` or a `ControlTemplate`) and `PanelHeader` (a `Style` for the header strip), both keyed in `Shared.xaml`.
- Consumes: `Surface`, `SurfaceHi`, `Hairline`, `Accent` from Task 2, tuned in Task 5.

- [ ] **Step 1: Write the wrap guard**

Turkish strings run longer than their English counterparts, and the header carries the widest text in the panel. Create `src/Brisk.Tests/PanelSourceTests.cs` asserting the header template's `TextBlock` sets `TextWrapping="Wrap"` and does not set `TextTrimming`. Parse the XAML — this is a claim about the template, and templates are source.

- [ ] **Step 2: Run to verify it fails**

Expected: FAIL — no such template.

- [ ] **Step 3: Build the panel**

In `Shared.xaml`: a `Border` with `Surface` background, 1px `Hairline` border, and four corner brackets as `Path`s in `Accent` covering roughly the corner 14px of each edge. The bracket geometry is drawn, never an image — no asset from the reference is copied.

- [ ] **Step 4: Build the header strip**

A `SurfaceHi` band with the title in large light-weight type, wrapping. Point the existing section labels at it — `health.advise.section` and `health.notice.section` from the previous wave already exist and already carry the right words; this is a style change with no new strings.

- [ ] **Step 5: Reskin the finding cards**

The existing `FindingCard` template keeps its structure exactly — ring at the left, headline, expander — and adopts the panel's surface and hairline. Do not touch the ring's colours: they are semantic.

- [ ] **Step 6: Run to verify green**

Run: `dotnet test brisk.sln -c Release --nologo`
Expected: all green. `HealthViewModelTests` and the finding-row tests must pass untouched — if a view-model test breaks, you have changed behaviour, not appearance, and should stop and report.

- [ ] **Step 7: Photograph Health and Performance**

Add both pages to the snapshot fact and look at them. This is the moment the "pages inherit" claim is either true or false; say which in your report.

- [ ] **Step 8: Commit**

```bash
git add src/Brisk/Theming/Shared.xaml src/Brisk.Tests/PanelSourceTests.cs src/Brisk.Tests/Snapshots/
git commit  # message: the panels get their brackets, and three pages change look without changing
```

---

### Task 7: The instrument stops drawing what it did not measure

**Files:**
- Modify: `src/Brisk/ViewModels/OverviewViewModel.cs`, `src/Brisk/Views/OverviewPage.xaml`
- Modify: `src/Brisk.Tests/OverviewViewModelTests.cs`

**Interfaces:**
- Consumes: `LiveReading` (`CpuPercent`, `RamPercent`, `TempC`, `TempSource`, `FreeDiskBytes` — all nullable except the last).
- Produces: `OverviewViewModel.HasCpuArc` / `HasRamArc` (`bool`) and `LiveRamPercent` (`double`), alongside the existing `LiveCpuPercent`.

- [ ] **Step 1: Write the failing tests**

```csharp
    /// Decision 4, and a correction to shipped behaviour: LiveCpuPercent
    /// documents itself today as "0 (an empty arc) until the CPU sensor has a
    /// delta to report". An empty arc is a picture of a measurement that does
    /// not exist. Absent is the honest state.
    [Fact]
    public async Task NoCpuReading_DrawsNoCpuArc()
    {
        var live = new FakeLive { Next = new LiveReading(null, 42.0, null, null, 0) };
        var (vm, _, _) = Build(live: live);

        await vm.LiveTickAsync();

        Assert.False(vm.HasCpuArc);
        Assert.True(vm.HasRamArc);
        Assert.Equal(42.0, vm.LiveRamPercent);
    }

    /// Satellites keep the product's dash convention — the no-empty-ring rule
    /// governs rings, not readouts. A dash states that nothing was read; an
    /// empty arc is a picture of a measurement that does not exist.
    [Fact]
    public async Task NoTemperatureReading_StillShowsTheDash()
    {
        var live = new FakeLive { Next = new LiveReading(12.0, 42.0, null, null, 0) };
        var (vm, _, _) = Build(live: live);

        await vm.LiveTickAsync();

        Assert.Equal("—", vm.LiveTempText);
    }
```

**An existing test pins the behaviour you are removing.** `LiveTick_MissingSensors_ShowDashPlaceholders` asserts `Assert.Equal(0.0, vm.LiveCpuPercent);` with the comment *"CPU ring rests as an empty arc"*. That assertion and its comment are the old contract. Replace them with `Assert.False(vm.HasCpuArc);` and a comment saying the arc is absent rather than empty — do **not** delete the test, and do not leave the comment describing behaviour the code no longer has. Its dash assertions stay exactly as they are: they are the satellite convention this task preserves.

- [ ] **Step 2: Run to verify they fail**

Expected: build FAILS — `HasCpuArc`, `HasRamArc` do not exist.

- [ ] **Step 3: Implement**

Add `HasCpuArc`/`HasRamArc`, set from whether the corresponding `LiveReading` field is non-null, and `LiveRamPercent` as RAM's numeric twin. Rewrite `LiveCpuPercent`'s doc comment — it currently documents the behaviour being removed, and a stale comment about an honesty rule is worse than none. Bind each inner arc's `Visibility` to its `Has…Arc` flag in `OverviewPage.xaml`.

- [ ] **Step 4: Draw the arcs and satellites**

Inner CPU and RAM arcs inside the health ring, stroked in `Accent` and `AccentDim` — **never** semantic colours; the outer health ring is the instrument's only claim-carrier. Temperature and free disk render as satellite readouts on `AccentGlow` radial floor ellipses beneath the instrument, using the existing `LiveTempText`, `LiveTempBadgeText`, `LiveTempCaption` and `LiveDiskText`.

- [ ] **Step 5: Run to verify green**

Run: `dotnet test brisk.sln -c Release --nologo`
Expected: all green.

- [ ] **Step 6: Prove the arc really disappears**

Render a snapshot with a null-CPU reading and confirm from the image that no CPU arc is drawn — not a faint one, not a zero-length one. Record it; this is the wave's headline honesty claim and a view-model flag alone does not prove the picture.

- [ ] **Step 7: Commit**

```bash
git add src/Brisk/ViewModels/OverviewViewModel.cs src/Brisk/Views/OverviewPage.xaml src/Brisk.Tests/OverviewViewModelTests.cs
git commit  # message: the ring for a sensor that never spoke stops being drawn at zero
```

---

### Task 8: the hero joins the palette

Added after Task 7 disclosed the gap, and ruled into this wave by the
maintainer's stand-in: the instrument at the centre of the Overview draws from
a separate `Hero*` colour family that Task 2's retune never touched, so the
cockpit's centrepiece is still graphite and Windows blue inside a navy app. It
is the element every screenshot leads with, and screenshots were the stated
reason for pinning the signature accent in the first place.

**Scope is binding and narrow: the colour family and its contrast checks only.
No geometry, no motion, no layout. Any creep and this task is abandoned to a
follow-up wave rather than allowed to grow.**

**Files:**
- Modify: the `Hero*` brush definitions in `src/Brisk/Theming/Shared.xaml`
- Test: a contrast `[Theory]` alongside the satellite checks Task 7 added

- [ ] **Step 1: Write the failing contrast checks**

Pin, with the same computed machinery the atmosphere and satellites use and
every value read from source rather than copied: the score numerals and the
caption text against the new hero fill at **≥4.5:1**, and the arcs and ring
segments at **≥3:1**.

- [ ] **Step 2: Run to verify they fail**

Expected: FAIL against the graphite fill, on the ratios rather than on a
missing symbol.

- [ ] **Step 3: Retune the family**

The hero's own accent takes the signature turquoise `#5FD4E8` — numerically
equal to the dark theme's accent, which is what lets the inner arcs speak the
signature colour without binding a theme key the hero panel cannot use. The
fills move into the navy family. **The semantic ring colours are untouched.**

The hero panel stays deliberately theme-independent — dark in both themes — so
the retune must not introduce a light-theme branch here.

- [ ] **Step 4: Verify the budget is untouched**

The hero sits on the instrument panel, not on bare atmosphere, so the spent
4.5068:1 atmosphere budget must be unaffected. Confirm `ContrastTests` does not
move. If it fires, something reached the atmosphere that should not have.

- [ ] **Step 5: Render before calling it done**

A dark Overview render showing the retuned instrument. The wave's method is
renders before commitment, and this task is not exempt.

- [ ] **Step 6: Commit**

```bash
git add src/Brisk/Theming/Shared.xaml src/Brisk.Tests/
git commit  # message: the instrument stops wearing the palette the app left behind
```

---

### Task 9: 0.5.0, and the light theme finally gets photographed

**Files:**
- Modify: `src/BriskEngine/EngineInfo.cs`
- Modify: `README.md` if it shows a screenshot or names the version

- [ ] **Step 0: Produce a light-theme Overview render**

**A condition of closing the wave, set by the maintainer's stand-in.** This
wave's whole method is renders before commitment, yet every light-theme
decision in it — `#0F6E7E`, `SeverityInfo` left unchanged — was made on
arithmetic alone, because the harness installs Dark once per AppDomain. The
tuning gate already proved isolated per-invocation captures work, so a second
process with Light installed is enough. The maintainer must be able to see the
deliberately dark hero panel sitting on the white page before 0.5.0 ships.

- [ ] **Step 1: Bump**

`EngineInfo.Version` → `"0.5.0"`. **No tag. No push.** The version advances per wave; tags wait for the announcement.

- [ ] **Step 2: Check the README**

Search for a version string or a screenshot reference. If a screenshot is shown, note in your report that it is now stale — do not regenerate it, because the screenshot for the announcement is the maintainer's call and belongs to a later wave.

- [ ] **Step 3: Full verification**

Run: `dotnet test brisk.sln -c Release --nologo` — all green, 0 warnings; record the count.

- [ ] **Step 4: Commit**

```bash
git add src/BriskEngine/EngineInfo.cs README.md
git commit  # message: 0.5.0 — the wave where the window became an instrument
```

---

## Self-review notes

- **Spec coverage.** Layer architecture → T3. Token contract, including the `BgRail` delete and the `Surface` rename with all 11 sites → T2. Shell, `WindowChrome`, both its traps, nav floating, sizes → T4. Panel language → T6. Instrument, no-arc-without-reading, no temperature arc, satellites keeping the dash → T7. Motion and performance: the atmosphere is static by construction in T3 and `BitmapCache` is set in T3 step 5; the existing `AmbientMotionController` contract is deliberately untouched by every task. Legibility → T3's contrast test. Verification: harness → T1, `DynamicResource` guard → T2, contrast → T3, no-arc → T7. Version → T8. The variant gate → T5.
- **Deliberate non-changes, stated so reviewers judge them as decisions:** `AmbientMotionController`, `CometTail`, `SweepRing` and `SegmentedGauge` are not modified — the instrument gains arcs around them, and their tests must pass untouched. The report card and its renderer change only where T1 extracts shared settling code; its pixel tests are the proof that extraction was behaviour-preserving.
- **Ordering constraints.** T1 before everything, because every later task is judged from its images. T2 before T3 and T4, which consume its keys. T5 gates T6 and T7 — the tuning constants it fixes are the ones those tasks build on. T8 last.
- **The riskiest task is T4**, because `WindowChrome`'s two traps are invisible to unit tests and visible immediately to a user. Its source-parsing guard covers the configuration; the window snapshot covers the rest, and its step 9 explicitly asks the implementer to report surprises rather than resolve them.
