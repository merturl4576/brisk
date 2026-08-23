# Headline & Revelation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every finding that owns a measured number leads with it — on the Overview as a revelation band, on the finding cards as a lead value — chosen by a deterministic picker the report card will later reuse.

**Architecture:** Two engine additions (`Headline` on `DiagnosticFinding`, `RevelationPicker`), five rules opting in, one shared GUI text resolver, one new Overview band. No new measurements, no scoring changes, no CLI changes.

**Tech Stack:** .NET 8 (`net8.0-windows`, x64), WPF, xUnit, resx localization.

**Spec:** `docs/superpowers/specs/2026-08-23-headline-and-revelation-design.md`

## Global Constraints

- `TreatWarningsAsErrors` is on in every project: the build must finish with **0 warnings**.
- Every user-visible string exists in BOTH `src/Brisk/Localization/Strings.resx` and `Strings.tr.resx`, pinned by a test.
- The `Brisk` project has `ImplicitUsings` **disabled** — new files there need explicit `using` lines. `BriskEngine` has them enabled.
- Verify with `dotnet test brisk.sln -c Release --nologo` (currently 702 green: 337 + 365).
- Commit messages follow the repo's long-form voice (a short story of why, not a label) and end with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- The `thermals` rule must NOT gain a headline (spec: "could not read" is not a number).

---

### Task 1: `Headline` model and `RevelationPicker` (engine)

**Files:**
- Create: `src/BriskEngine/Models/Headline.cs`
- Modify: `src/BriskEngine/Models/DiagnosticFinding.cs`
- Create: `src/BriskEngine/Diagnostics/RevelationPicker.cs`
- Test: `src/BriskEngine.Tests/RevelationPickerTests.cs`

**Interfaces:**
- Consumes: `DiagnosticFinding`, `Severity` (existing).
- Produces: `Headline(string Value, string Caption, string ValueKey, IReadOnlyList<string> ValueArgs, string CaptionKey, IReadOnlyList<string> CaptionArgs)`; `DiagnosticFinding` gains optional final parameter `Headline? Headline = null`; `RevelationPicker.Pick(IEnumerable<DiagnosticFinding>) -> IReadOnlyList<DiagnosticFinding>`; `RevelationPicker.Priority` (internal `string[]`).

- [ ] **Step 1: Write the failing tests**

Create `src/BriskEngine.Tests/RevelationPickerTests.cs`:

```csharp
using System;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

public class RevelationPickerTests
{
    private static DiagnosticFinding F(string id, Severity sev = Severity.Warning,
        int stars = 3, bool withHeadline = true) => new(
        id, $"rule.{id}.title", $"Title {id}", $"Evidence {id}",
        sev, RuleCategory.Advise, stars, CanFix: false, FixDescription: null,
        Headline: withHeadline
            ? new Headline("1", "cap",
                $"rule.{id}.headline.value", new[] { "1" },
                $"rule.{id}.headline.caption", new[] { "1" })
            : null);

    [Fact]
    public void DeclaredOrder_DecidesAmongListedRules()
    {
        var picked = RevelationPicker.Pick(new[]
        {
            F("memory-speed"), F("disk-breakdown"), F("boot-degradation"),
        });
        Assert.Equal(new[] { "boot-degradation", "disk-breakdown", "memory-speed" },
            picked.Select(f => f.RuleId).ToArray());
    }

    [Fact]
    public void FindingsWithoutHeadlines_AreNotPicked()
    {
        var picked = RevelationPicker.Pick(new[]
        {
            F("boot-degradation", withHeadline: false), F("startup-bloat"),
        });
        Assert.Equal(new[] { "startup-bloat" }, picked.Select(f => f.RuleId).ToArray());
    }

    [Fact]
    public void UnlistedRules_SortAfterListed_BySeverityImpactThenId()
    {
        var picked = RevelationPicker.Pick(new[]
        {
            F("zz-custom", Severity.Critical, stars: 5),
            F("memory-speed"),
            F("bb-custom", Severity.Warning, stars: 5),
            F("aa-custom", Severity.Warning, stars: 5),
        });
        Assert.Equal(new[] { "memory-speed", "zz-custom", "aa-custom", "bb-custom" },
            picked.Select(f => f.RuleId).ToArray());
    }

    [Fact]
    public void EmptyInput_EmptyOutput() =>
        Assert.Empty(RevelationPicker.Pick(Array.Empty<DiagnosticFinding>()));

    /// The declared order IS the product decision — pinned so a change to it
    /// is a deliberate edit here, never a drive-by.
    [Fact]
    public void Priority_IsExactlyTheOptingRules() =>
        Assert.Equal(new[]
        {
            "boot-degradation", "display-refresh", "startup-bloat",
            "disk-breakdown", "memory-speed",
        }, RevelationPicker.Priority);
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --nologo --filter RevelationPicker`
Expected: build FAILS with `'Headline' not found` / `'RevelationPicker' not found`.

- [ ] **Step 3: Implement**

Create `src/BriskEngine/Models/Headline.cs`:

```csharp
namespace BriskEngine.Models;

/// The one number a finding leads with, and what that number is. English
/// prose plus stable key + args — exactly the evidence convention on
/// DiagnosticFinding: a consumer without a resource table reads
/// Value/Caption, a GUI rebuilds both in the user's language.
public sealed record Headline(
    string Value,                       // formatted, English units: "57.7 GB"
    string Caption,                     // English: "Desktop — the largest measured folder"
    string ValueKey,
    IReadOnlyList<string> ValueArgs,
    string CaptionKey,
    IReadOnlyList<string> CaptionArgs);
```

In `src/BriskEngine/Models/DiagnosticFinding.cs`, add the optional final
parameter after `EvidenceArgs`:

```csharp
    string? EvidenceKey = null,
    IReadOnlyList<string>? EvidenceArgs = null,
    // The measured number this finding leads with on presentation surfaces.
    // Optional and per-rule: a finding whose honest content is a sentence
    // (thermals) carries none, and no surface invents one for it.
    Headline? Headline = null);
```

Create `src/BriskEngine/Diagnostics/RevelationPicker.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics;

/// Chooses which measured number leads a scan's presentation. The order is
/// a product decision made visible in one list, not a heuristic — the same
/// scan always leads with the same number on every surface that asks.
public static class RevelationPicker
{
    /// Declared order. A rule with a headline but no entry here still shows
    /// — after the listed ones, by severity, impact, then id — so a new
    /// rule is never invisible just because nobody edited this file.
    internal static readonly string[] Priority =
    {
        "boot-degradation",
        "display-refresh",
        "startup-bloat",
        "disk-breakdown",
        "memory-speed",
    };

    public static IReadOnlyList<DiagnosticFinding> Pick(
        IEnumerable<DiagnosticFinding> findings) =>
        findings.Where(f => f.Headline is not null)
            .OrderBy(Rank)
            .ThenByDescending(f => SeverityRank(f.Severity))
            .ThenByDescending(f => f.ImpactStars)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ToList();

    private static int Rank(DiagnosticFinding f)
    {
        var i = Array.IndexOf(Priority, f.RuleId);
        return i < 0 ? int.MaxValue : i;
    }

    /// Explicit, so the sort never leans on the enum's numeric order.
    private static int SeverityRank(Severity s) => s switch
    {
        Severity.Critical => 2,
        Severity.Warning => 1,
        _ => 0,
    };
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --nologo --filter RevelationPicker`
Expected: 5 PASS. Then the full suite: `dotnet test brisk.sln -c Release --nologo` — everything green (the new parameter is optional; no construction site changes).

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Models/Headline.cs src/BriskEngine/Models/DiagnosticFinding.cs src/BriskEngine/Diagnostics/RevelationPicker.cs src/BriskEngine.Tests/RevelationPickerTests.cs
git commit  # message in repo voice: a finding can now carry the number it leads with, and one declared list decides which number leads a scan
```

---

### Task 2: `boot-degradation` opts in

**Files:**
- Modify: `src/BriskEngine/Diagnostics/Rules/BootDegradationRule.cs`
- Modify: `src/Brisk/Localization/Strings.resx`, `src/Brisk/Localization/Strings.tr.resx`
- Test: `src/BriskEngine.Tests/Rules/BootDegradationRuleTests.cs`

**Interfaces:**
- Consumes: `Headline` (Task 1).
- Produces: finding with `Headline.Value` = the median ("57 s"), `ValueKey` `rule.boot-degradation.headline.value`, `ValueArgs` = bare digits, `CaptionKey` `rule.boot-degradation.headline.caption`, `CaptionArgs` = sampled count.

- [ ] **Step 1: Write the failing test** (append to `BootDegradationRuleTests`; the fixture — `Context`, `Boot`, `Blamed` — already exists in the file)

```csharp
    /// The headline is the median (57089 ms of these three -> "57 s"), never
    /// the worst boot — the same number the evidence sentence leads with.
    [Fact]
    public void Headline_CarriesTheMedian_WithBareDigitsForLocalization()
    {
        var ctx = Context(
            Boot(51237),
            Boot(111814, Blamed("Spotify.exe", "Spotify", 37141)),
            Boot(57089));

        var h = new BootDegradationRule().Detect(ctx)!.Headline;

        Assert.NotNull(h);
        Assert.Equal("57 s", h!.Value);
        Assert.Equal("boot time — the middle of the last 3 boots", h.Caption);
        Assert.Equal("rule.boot-degradation.headline.value", h.ValueKey);
        Assert.Equal(new[] { "57" }, h.ValueArgs);
        Assert.Equal("rule.boot-degradation.headline.caption", h.CaptionKey);
        Assert.Equal(new[] { "3" }, h.CaptionArgs);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --nologo --filter Headline_CarriesTheMedian`
Expected: FAIL (`Headline` is null).

- [ ] **Step 3: Implement**

In `BootDegradationRule.cs`, split the seconds formatter (bottom of the file) so the digits exist separately for the localizable template:

```csharp
    private static string SecondsDigits(int ms) =>
        ((int)Math.Round(ms / 1000.0, MidpointRounding.AwayFromZero))
            .ToString(CultureInfo.InvariantCulture);

    private static string Seconds(int ms) => SecondsDigits(ms) + " s";
```

In `Detect`, right after `var median = Seconds(medianMs);` add:

```csharp
        var medianDigits = SecondsDigits(medianMs);
```

Change the `Finding` helper to take and use it (both call sites in `Detect` pass `medianDigits` as the new second argument):

```csharp
    private DiagnosticFinding Finding(string median, string medianDigits,
        string sampled, string? names, string evidence) =>
        new(Id, $"rule.{Id}.title",
            "Windows takes a long time to start", evidence,
            Severity.Warning, Category, ImpactStars: 4, CanFix: false, FixDescription: null,
            EvidenceKey: names is null ? $"rule.{Id}.evidence.nobody" : $"rule.{Id}.evidence",
            EvidenceArgs: names is null
                ? new[] { median, sampled }
                : new[] { median, sampled, names },
            Headline: new Headline(
                median, $"boot time — the middle of the last {sampled} boots",
                $"rule.{Id}.headline.value", new[] { medianDigits },
                $"rule.{Id}.headline.caption", new[] { sampled }));
```

Add to `Strings.resx` (next to the existing `rule.boot-degradation.*` entries):

```xml
  <data name="rule.boot-degradation.headline.value" xml:space="preserve"><value>{0} s</value></data>
  <data name="rule.boot-degradation.headline.caption" xml:space="preserve"><value>boot time — the middle of the last {0} boots</value></data>
```

Add to `Strings.tr.resx` at the same position:

```xml
  <data name="rule.boot-degradation.headline.value" xml:space="preserve"><value>{0} sn</value></data>
  <data name="rule.boot-degradation.headline.caption" xml:space="preserve"><value>açılış süresi — son {0} açılışın ortası</value></data>
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --nologo`
Expected: all green (the existing boot tests exercise both `Finding` call sites).

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Diagnostics/Rules/BootDegradationRule.cs src/Brisk/Localization/Strings.resx src/Brisk/Localization/Strings.tr.resx src/BriskEngine.Tests/Rules/BootDegradationRuleTests.cs
git commit  # message: the boot rule leads with its median, carried as digits so the unit can speak Turkish
```

---

### Task 3: `display-refresh` and `startup-bloat` opt in

**Files:**
- Modify: `src/BriskEngine/Diagnostics/Rules/DisplayRefreshRule.cs`
- Modify: `src/BriskEngine/Diagnostics/Rules/StartupBloatRule.cs`
- Modify: `src/Brisk/Localization/Strings.resx`, `src/Brisk/Localization/Strings.tr.resx`
- Test: `src/BriskEngine.Tests/Rules/DisplayRefreshRuleTests.cs`, `src/BriskEngine.Tests/Rules/StartupBloatRuleTests.cs`

**Interfaces:**
- Consumes: `Headline` (Task 1).
- Produces: `rule.display-refresh.headline.value/.caption` and `rule.startup-bloat.headline.value/.caption` keys; display headline names the display furthest behind.

- [ ] **Step 1: Write the failing tests**

Append to `DisplayRefreshRuleTests` (fixture `Context` exists):

```csharp
    /// Two displays behind: the headline belongs to the one furthest behind
    /// its panel, not to whichever enumerated first.
    [Fact]
    public void Headline_NamesTheDisplayFurthestBehind()
    {
        var (ctx, _) = Context(
            new DisplayInfo(@"\\.\DISPLAY1", "Laptop panel", 60, 90),
            new DisplayInfo(@"\\.\DISPLAY2", "Dell U2720Q", 60, 144));

        var h = new DisplayRefreshRule().Detect(ctx)!.Headline;

        Assert.NotNull(h);
        Assert.Equal("60 Hz", h!.Value);
        Assert.Equal("the display supports 144 Hz", h.Caption);
        Assert.Equal("rule.display-refresh.headline.value", h.ValueKey);
        Assert.Equal(new[] { "60" }, h.ValueArgs);
        Assert.Equal("rule.display-refresh.headline.caption", h.CaptionKey);
        Assert.Equal(new[] { "144" }, h.CaptionArgs);
    }
```

Append to `StartupBloatRuleTests` (fixture `Ctx` exists):

```csharp
    [Fact]
    public void Headline_IsTheTotalCount()
    {
        var (ctx, _) = Ctx("A", "B", "C", "D", "E", "F");

        var h = new StartupBloatRule().Detect(ctx)!.Headline;

        Assert.NotNull(h);
        Assert.Equal("6", h!.Value);
        Assert.Equal("programs start with Windows", h.Caption);
        Assert.Equal("rule.startup-bloat.headline.value", h.ValueKey);
        Assert.Equal(new[] { "6" }, h.ValueArgs);
        Assert.Equal("rule.startup-bloat.headline.caption", h.CaptionKey);
        Assert.Empty(h.CaptionArgs);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --nologo --filter Headline`
Expected: the two new tests FAIL (null headline); Task 2's passes.

- [ ] **Step 3: Implement**

`DisplayRefreshRule.Detect` — after `var readings = ...`, before `return`:

```csharp
        // The headline belongs to the display furthest behind its panel.
        // OrderByDescending is stable, so equal gaps keep enumeration order.
        var worst = behind.OrderByDescending(d => d.MaxHz - d.CurrentHz).First();
        var current = worst.CurrentHz.ToString(CultureInfo.InvariantCulture);
        var max = worst.MaxHz.ToString(CultureInfo.InvariantCulture);
```

and add to the `new DiagnosticFinding(...)` call:

```csharp
            EvidenceKey: $"rule.{Id}.evidence", EvidenceArgs: new[] { readings },
            Headline: new Headline(
                $"{current} Hz", $"the display supports {max} Hz",
                $"rule.{Id}.headline.value", new[] { current },
                $"rule.{Id}.headline.caption", new[] { max }));
```

(`using System.Globalization;` if the file lacks it — implicit usings do not cover it.)

`StartupBloatRule.Detect` — extend the `new DiagnosticFinding(...)` call:

```csharp
            EvidenceArgs: heavy.Count > 0
                ? new[] { totalText, heavyNames } : new[] { totalText },
            Headline: new Headline(
                totalText, "programs start with Windows",
                $"rule.{Id}.headline.value", new[] { totalText },
                $"rule.{Id}.headline.caption", Array.Empty<string>()));
```

`Strings.resx` additions (beside each rule's existing keys):

```xml
  <data name="rule.display-refresh.headline.value" xml:space="preserve"><value>{0} Hz</value></data>
  <data name="rule.display-refresh.headline.caption" xml:space="preserve"><value>the display supports {0} Hz</value></data>
  <data name="rule.startup-bloat.headline.value" xml:space="preserve"><value>{0}</value></data>
  <data name="rule.startup-bloat.headline.caption" xml:space="preserve"><value>programs start with Windows</value></data>
```

`Strings.tr.resx` additions:

```xml
  <data name="rule.display-refresh.headline.value" xml:space="preserve"><value>{0} Hz</value></data>
  <data name="rule.display-refresh.headline.caption" xml:space="preserve"><value>ekran {0} Hz destekliyor</value></data>
  <data name="rule.startup-bloat.headline.value" xml:space="preserve"><value>{0}</value></data>
  <data name="rule.startup-bloat.headline.caption" xml:space="preserve"><value>program Windows ile başlıyor</value></data>
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --nologo`
Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Diagnostics/Rules/DisplayRefreshRule.cs src/BriskEngine/Diagnostics/Rules/StartupBloatRule.cs src/Brisk/Localization/Strings.resx src/Brisk/Localization/Strings.tr.resx src/BriskEngine.Tests/Rules/DisplayRefreshRuleTests.cs src/BriskEngine.Tests/Rules/StartupBloatRuleTests.cs
git commit  # message: the refresh rule leads with the display furthest behind; startup bloat leads with its count
```

---

### Task 4: `disk-breakdown` and `memory-speed` opt in

**Files:**
- Modify: `src/BriskEngine/Diagnostics/Rules/DiskBreakdownRule.cs`
- Modify: `src/BriskEngine/Diagnostics/Rules/MemorySpeedRule.cs`
- Modify: `src/Brisk/Localization/Strings.resx`, `src/Brisk/Localization/Strings.tr.resx`
- Test: `src/BriskEngine.Tests/Rules/AdviseRulesTests.cs`, `src/BriskEngine.Tests/Rules/MemorySpeedRuleTests.cs`

**Interfaces:**
- Consumes: `Headline` (Task 1), `Fmt.Bytes` (existing).
- Produces: `rule.disk-breakdown.headline.value/.caption`, `rule.memory-speed.headline.value/.caption` keys.

- [ ] **Step 1: Write the failing tests**

Append to `AdviseRulesTests` (uses the file's `TestContext.Empty()` + `FakeFiles` pattern):

```csharp
    /// 71 GB in Local, 25 GB in Roaming — the headline is the largest
    /// over-threshold folder, and Fmt.Bytes keeps its invariant formatting.
    [Fact]
    public void DiskBreakdown_Headline_IsTheLargestOverThresholdFolder()
    {
        var ctx = TestContext.Empty();
        var files = (FakeFiles)ctx.Files;
        files.Sizes[PathExpander.Expand("%LOCALAPPDATA%")!] = 71L << 30;
        files.Sizes[PathExpander.Expand("%APPDATA%")!] = 25L << 30;

        var h = new DiskBreakdownRule().Detect(ctx)!.Headline;

        Assert.NotNull(h);
        Assert.Equal("71.0 GB", h!.Value);
        Assert.Equal("AppData\\Local — the largest measured folder", h.Caption);
        Assert.Equal("rule.disk-breakdown.headline.value", h.ValueKey);
        Assert.Equal(new[] { "71.0 GB" }, h.ValueArgs);
        Assert.Equal("rule.disk-breakdown.headline.caption", h.CaptionKey);
        Assert.Equal(new[] { "AppData\\Local" }, h.CaptionArgs);
    }
```

Append to `MemorySpeedRuleTests` (fixture `With` exists; `MemoryModule` is `(Slot, RatedMts, ConfiguredMts, CapacityBytes)`):

```csharp
    /// Two genuinely slow modules (both under the 0.80 ratio): the headline
    /// belongs to the one furthest below its rating — 2133/3200 (0.67) beats
    /// 2400/3200 (0.75) for the lead. 2933/3200 would not qualify at all;
    /// that is the maintainer's machine, where this rule stays silent.
    [Fact]
    public void Headline_IsTheWorstModulesConfiguredSpeed()
    {
        var ctx = With(
            new MemoryModule("DIMM0", 3200, 2400, 16L << 30),
            new MemoryModule("DIMM1", 3200, 2133, 16L << 30));

        var h = new MemorySpeedRule().Detect(ctx)!.Headline;

        Assert.NotNull(h);
        Assert.Equal("2133 MT/s", h!.Value);
        Assert.Equal("rated for 3200 MT/s", h.Caption);
        Assert.Equal("rule.memory-speed.headline.value", h.ValueKey);
        Assert.Equal(new[] { "2133" }, h.ValueArgs);
        Assert.Equal("rule.memory-speed.headline.caption", h.CaptionKey);
        Assert.Equal(new[] { "3200" }, h.CaptionArgs);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --nologo --filter Headline`
Expected: the two new tests FAIL with null headlines.

- [ ] **Step 3: Implement**

`DiskBreakdownRule.Detect` — track the largest over-threshold folder inside the existing loop:

```csharp
        var evidence = new List<string>();
        var hasOverage = false;
        (string Label, long Size)? largest = null;

        foreach (var (label, path, threshold) in folders)
        {
            var size = ctx.Files.DirectorySizeBytes(path);
            var sizeStr = Fmt.Bytes(size);
            var line = $"{label}: {sizeStr}";
            if (size >= threshold)
            {
                line += " (over threshold)";
                hasOverage = true;
                if (largest is null || size > largest.Value.Size)
                    largest = (label, size);
            }
            evidence.Add(line);
        }

        if (!hasOverage) return null;

        var (topLabel, topSize) = largest!.Value;
        var topBytes = Fmt.Bytes(topSize);
        return new DiagnosticFinding(
            Id, "rule.disk-breakdown.title",
            "Disk space fragmented across system folders",
            string.Join("; ", evidence),
            Severity.Warning, Category, ImpactStars: 2, CanFix: false, FixDescription: null,
            Headline: new Headline(
                topBytes, $"{topLabel} — the largest measured folder",
                $"rule.{Id}.headline.value", new[] { topBytes },
                $"rule.{Id}.headline.caption", new[] { topLabel }));
```

`MemorySpeedRule.Detect` — after `var readings = ...`:

```csharp
        // The headline belongs to the module furthest below its rating.
        var worst = slow.OrderBy(m => (double)m.ConfiguredMts / m.RatedMts).First();
        var configured = worst.ConfiguredMts.ToString(CultureInfo.InvariantCulture);
        var rated = worst.RatedMts.ToString(CultureInfo.InvariantCulture);
```

and extend the finding:

```csharp
            EvidenceKey: $"rule.{Id}.evidence", EvidenceArgs: new[] { readings },
            Headline: new Headline(
                $"{configured} MT/s", $"rated for {rated} MT/s",
                $"rule.{Id}.headline.value", new[] { configured },
                $"rule.{Id}.headline.caption", new[] { rated }));
```

`Strings.resx`:

```xml
  <data name="rule.disk-breakdown.headline.value" xml:space="preserve"><value>{0}</value></data>
  <data name="rule.disk-breakdown.headline.caption" xml:space="preserve"><value>{0} — the largest measured folder</value></data>
  <data name="rule.memory-speed.headline.value" xml:space="preserve"><value>{0} MT/s</value></data>
  <data name="rule.memory-speed.headline.caption" xml:space="preserve"><value>rated for {0} MT/s</value></data>
```

`Strings.tr.resx` (rated = "anma hızı", matching the rule's existing TR evidence):

```xml
  <data name="rule.disk-breakdown.headline.value" xml:space="preserve"><value>{0}</value></data>
  <data name="rule.disk-breakdown.headline.caption" xml:space="preserve"><value>{0} — ölçülen en büyük klasör</value></data>
  <data name="rule.memory-speed.headline.value" xml:space="preserve"><value>{0} MT/s</value></data>
  <data name="rule.memory-speed.headline.caption" xml:space="preserve"><value>anma hızı {0} MT/s</value></data>
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --nologo`
Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Diagnostics/Rules/DiskBreakdownRule.cs src/BriskEngine/Diagnostics/Rules/MemorySpeedRule.cs src/Brisk/Localization/Strings.resx src/Brisk/Localization/Strings.tr.resx src/BriskEngine.Tests/Rules/AdviseRulesTests.cs src/BriskEngine.Tests/Rules/MemorySpeedRuleTests.cs
git commit  # message: disk leads with its largest folder, memory with its slowest module
```

---

### Task 5: the GUI resolves headlines, cards lead with them

**Files:**
- Create: `src/Brisk/ViewModels/LocalizedText.cs`
- Modify: `src/Brisk/ViewModels/HealthViewModel.cs` (FindingRow)
- Modify: `src/Brisk/Theming/Shared.xaml` (FindingCard template)
- Modify: `src/Brisk.Tests/Fakes.cs` (TestData.Finding)
- Test: `src/Brisk.Tests/HeadlineLocalizationTests.cs` (new)

**Interfaces:**
- Consumes: `Headline`, keys from Tasks 2–4; `Loc` (existing: indexer, `F(key, params object[])`).
- Produces: `LocalizedText.Evidence(DiagnosticFinding, Loc) -> string`; `LocalizedText.Headline(Headline, Loc) -> (string Value, string Caption)`; `FindingRow.HasHeadline` (bool), `FindingRow.HeadlineValue` (string); `TestData.Finding(..., Headline? headline = null)`.

- [ ] **Step 1: Write the failing tests**

Create `src/Brisk.Tests/HeadlineLocalizationTests.cs`:

```csharp
using Brisk.Localization;
using Brisk.ViewModels;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

/// The headline twin of EvidenceLocalizationTests: keys resolve in both
/// languages, a missing key falls back to the engine's English, and the
/// finding row exposes exactly what the card binds.
public class HeadlineLocalizationTests
{
    private static Loc Loc(string lang)
    {
        var loc = new Loc();
        loc.SetLanguage(lang);
        return loc;
    }

    private static Headline Boot() => new("57 s", "boot time — the middle of the last 8 boots",
        "rule.boot-degradation.headline.value", new[] { "57" },
        "rule.boot-degradation.headline.caption", new[] { "8" });

    [Fact]
    public void BootHeadline_SpeaksBothLanguages()
    {
        var (trValue, trCaption) = LocalizedText.Headline(Boot(), Loc("tr"));
        Assert.Equal("57 sn", trValue);
        Assert.Equal("açılış süresi — son 8 açılışın ortası", trCaption);

        var (enValue, enCaption) = LocalizedText.Headline(Boot(), Loc("en"));
        Assert.Equal("57 s", enValue);
        Assert.Equal("boot time — the middle of the last 8 boots", enCaption);
    }

    [Theory]
    [InlineData("rule.display-refresh.headline.value", "60", "60 Hz", "60 Hz")]
    [InlineData("rule.display-refresh.headline.caption", "144",
        "the display supports 144 Hz", "ekran 144 Hz destekliyor")]
    [InlineData("rule.startup-bloat.headline.value", "13", "13", "13")]
    [InlineData("rule.disk-breakdown.headline.value", "57.7 GB", "57.7 GB", "57.7 GB")]
    [InlineData("rule.disk-breakdown.headline.caption", "Desktop",
        "Desktop — the largest measured folder", "Desktop — ölçülen en büyük klasör")]
    [InlineData("rule.memory-speed.headline.value", "2933", "2933 MT/s", "2933 MT/s")]
    [InlineData("rule.memory-speed.headline.caption", "3200",
        "rated for 3200 MT/s", "anma hızı 3200 MT/s")]
    public void EveryHeadlineKey_ExistsInBothLanguages(
        string key, string arg, string en, string tr)
    {
        Assert.Equal(en, Loc("en").F(key, arg));
        Assert.Equal(tr, Loc("tr").F(key, arg));
    }

    [Fact]
    public void UnknownKeys_FallBackToTheEnginesEnglish()
    {
        var h = new Headline("42 things", "of some kind",
            "rule.custom-x.headline.value", new[] { "42" },
            "rule.custom-x.headline.caption", new[] { "x" });
        var (value, caption) = LocalizedText.Headline(h, Loc("tr"));
        Assert.Equal("42 things", value);
        Assert.Equal("of some kind", caption);
    }

    [Fact]
    public void FindingRow_ExposesTheHeadline_OrSaysItHasNone()
    {
        var loc = Loc("en");
        var with = new FindingRow(TestData.Finding("boot-degradation",
            cat: RuleCategory.Advise, canFix: false, headline: Boot()),
            loc, canUndo: false, _ => { }, _ => { });
        Assert.True(with.HasHeadline);
        Assert.Equal("57 s", with.HeadlineValue);

        var without = new FindingRow(TestData.Finding("thermals",
            cat: RuleCategory.Advise, canFix: false),
            loc, canUndo: false, _ => { }, _ => { });
        Assert.False(without.HasHeadline);
        Assert.Equal("", without.HeadlineValue);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/Brisk.Tests/Brisk.Tests.csproj -c Release --nologo --filter HeadlineLocalization`
Expected: build FAILS (`LocalizedText` unknown, `TestData.Finding` has no `headline` parameter).

- [ ] **Step 3: Implement**

Create `src/Brisk/ViewModels/LocalizedText.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Brisk.Localization;
using BriskEngine.Models;

namespace Brisk.ViewModels;

/// One resolver for the engine's "English prose + stable key + args"
/// convention: render in the user's language when the key exists, fall
/// back to the engine's English when it does not. FindingRow used to own
/// the evidence half privately; the revelation band needs the same rules,
/// so it lives here once.
public static class LocalizedText
{
    public static string Evidence(DiagnosticFinding finding, Loc loc) =>
        finding.EvidenceKey is { } key
            ? Resolve(key, finding.EvidenceArgs, finding.Evidence, loc)
            : finding.Evidence;

    public static (string Value, string Caption) Headline(Headline headline, Loc loc) => (
        Resolve(headline.ValueKey, headline.ValueArgs, headline.Value, loc),
        Resolve(headline.CaptionKey, headline.CaptionArgs, headline.Caption, loc));

    private static string Resolve(string key, IReadOnlyList<string>? args,
        string english, Loc loc)
    {
        var template = loc[key];   // the indexer returns the key when missing
        if (string.Equals(template, key, StringComparison.Ordinal)) return english;
        return loc.F(key, (args ?? Array.Empty<string>()).Cast<object>().ToArray());
    }
}
```

In `FindingRow` (HealthViewModel.cs): replace `Evidence = LocalizedEvidence(finding, loc);` with `Evidence = LocalizedText.Evidence(finding, loc);`, delete the now-unused private `LocalizedEvidence` method, and add in the constructor (after `Evidence = ...`):

```csharp
        HasHeadline = finding.Headline is not null;
        HeadlineValue = finding.Headline is { } headline
            ? LocalizedText.Headline(headline, loc).Value : "";
```

with the properties beside the other get-only ones:

```csharp
    public bool HasHeadline { get; }
    /// The lead value in the user's language ("57 sn"); "" when the finding
    /// carries no headline — the card collapses the column then.
    public string HeadlineValue { get; }
```

In `Fakes.cs`, extend `TestData.Finding` with a final optional parameter:

```csharp
    public static DiagnosticFinding Finding(string ruleId, Severity sev = Severity.Warning,
        RuleCategory cat = RuleCategory.Auto, int stars = 3, bool canFix = true,
        string? evidenceKey = null, IReadOnlyList<string>? evidenceArgs = null,
        Headline? headline = null) => new(
        ruleId, $"rule.{ruleId}.title", $"Title {ruleId}", $"Evidence {ruleId}",
        sev, cat, stars, canFix, canFix ? $"Fix {ruleId}" : null,
        evidenceKey, evidenceArgs, headline);
```

In `Shared.xaml`, inside the FindingCard header `DockPanel` — directly after the closing `</Grid>` of the severity-dot Grid (the one containing `SeverityDot` and `FixedDot`) — insert:

```xml
                        <!-- The measured number leads: findings that carry a
                             headline show it before their title. No headline,
                             no column — the row reads exactly as before. -->
                        <TextBlock DockPanel.Dock="Left" Margin="12,0,0,0"
                                   VerticalAlignment="Center"
                                   FontFamily="Segoe UI Variable Display, Segoe UI"
                                   FontSize="19" FontWeight="SemiBold"
                                   Style="{StaticResource Body}"
                                   Text="{Binding HeadlineValue}"
                                   Visibility="{Binding HasHeadline,
                                       Converter={x:Static views:BoolToVis.Instance}}" />
```

(`views:` is Shared.xaml's existing prefix for `Brisk.Views` — see the `views:BoolToVis` usages already in this template. Local `FontSize`/`FontWeight`/`FontFamily` values override the `Body` style's setters.)

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test brisk.sln -c Release --nologo`
Expected: all green, including the untouched `EvidenceLocalizationTests` (the extraction must not change evidence behavior).

- [ ] **Step 5: Commit**

```bash
git add src/Brisk/ViewModels/LocalizedText.cs src/Brisk/ViewModels/HealthViewModel.cs src/Brisk/Theming/Shared.xaml src/Brisk.Tests/Fakes.cs src/Brisk.Tests/HeadlineLocalizationTests.cs
git commit  # message: finding cards lead with the number, resolved by the one resolver both surfaces share
```

---

### Task 6: the Overview revelation band

**Files:**
- Modify: `src/Brisk/ViewModels/OverviewViewModel.cs`
- Modify: `src/Brisk/Views/OverviewPage.xaml`
- Modify: `src/Brisk/Windows/MainWindow.xaml.cs`
- Modify: `src/Brisk/Localization/Strings.resx`, `src/Brisk/Localization/Strings.tr.resx`
- Test: `src/Brisk.Tests/OverviewViewModelTests.cs`

**Interfaces:**
- Consumes: `RevelationPicker.Pick`, `LocalizedText`, `DiagnosticRuleRegistry.All.Count`, `TestData.Finding(..., headline:)` (Task 5).
- Produces: on `OverviewViewModel` — `HasRevelation` (bool), `RevelationValue`, `RevelationCaption`, `RevelationClaim`, `RevelationEvidence`, `RevelationMoreText`, `RevelationEmptyText` (all string), `OpenHealthCommand` (RelayCommand), `event Action? OpenHealthRequested`.

- [ ] **Step 1: Write the failing tests** (append to `OverviewViewModelTests`; `Build()`, `TestData`, `state.ScanAsync()` are the file's existing fixture)

```csharp
    /// Fake rule ids on purpose: unlisted rules rank by severity, and their
    /// missing resx keys prove the English fallback carries the band.
    [Fact]
    public async Task Revelation_LeadsWithTheTopHeadline_AndCountsTheRest()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(
            new[]
            {
                TestData.Finding("aa-fake", cat: RuleCategory.Advise, canFix: false,
                    headline: new Headline("13", "programs start with Windows",
                        "rule.aa-fake.headline.value", new[] { "13" },
                        "rule.aa-fake.headline.caption", Array.Empty<string>())),
                TestData.Finding("zz-fake", sev: Severity.Critical,
                    cat: RuleCategory.Advise, canFix: false,
                    headline: new Headline("57 s", "boot time — the middle of the last 8 boots",
                        "rule.zz-fake.headline.value", new[] { "57" },
                        "rule.zz-fake.headline.caption", new[] { "8" })),
            });

        await state.ScanAsync();

        Assert.True(vm.HasRevelation);
        Assert.Equal("57 s", vm.RevelationValue);
        Assert.Equal("boot time — the middle of the last 8 boots", vm.RevelationCaption);
        Assert.Equal("Title zz-fake", vm.RevelationClaim);
        Assert.Equal("Evidence zz-fake", vm.RevelationEvidence);
        Assert.Equal("and 1 more", vm.RevelationMoreText);
    }

    [Fact]
    public async Task Revelation_NoHeadlines_ShowsTheHonestEmptyLine()
    {
        var (vm, host, state) = Build();   // default snapshot carries no headlines

        await state.ScanAsync();

        Assert.False(vm.HasRevelation);
        Assert.Equal(
            $"All {DiagnosticRuleRegistry.All.Count} rules looked — nothing on this machine leads with a number.",
            vm.RevelationEmptyText);
        Assert.Equal("", vm.RevelationMoreText);
    }

    [Fact]
    public void OpenHealth_RaisesTheNavigationEvent()
    {
        var (vm, _, _) = Build();
        var fired = false;
        vm.OpenHealthRequested += () => fired = true;
        vm.OpenHealthCommand.Execute(null);
        Assert.True(fired);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/Brisk.Tests/Brisk.Tests.csproj -c Release --nologo --filter Revelation`
Expected: build FAILS (unknown members).

- [ ] **Step 3: Implement the view model**

In `OverviewViewModel` add fields beside the existing ones:

```csharp
    private bool _hasRevelation;
    private string _revelationValue = "";
    private string _revelationCaption = "";
    private string _revelationClaim = "";
    private string _revelationEvidence = "";
    private string _revelationMoreText = "";
    private string _revelationEmptyText = "";
```

properties beside `StatusText` (same `Set` pattern):

```csharp
    public bool HasRevelation { get => _hasRevelation; private set => Set(ref _hasRevelation, value); }
    public string RevelationValue { get => _revelationValue; private set => Set(ref _revelationValue, value); }
    public string RevelationCaption { get => _revelationCaption; private set => Set(ref _revelationCaption, value); }
    public string RevelationClaim { get => _revelationClaim; private set => Set(ref _revelationClaim, value); }
    public string RevelationEvidence { get => _revelationEvidence; private set => Set(ref _revelationEvidence, value); }
    public string RevelationMoreText { get => _revelationMoreText; private set => Set(ref _revelationMoreText, value); }
    public string RevelationEmptyText { get => _revelationEmptyText; private set => Set(ref _revelationEmptyText, value); }
    public RelayCommand OpenHealthCommand { get; }
    /// MainWindow subscribes and flips the nav — the same contract
    /// HealthViewModel.CrossNavigateRequested already uses.
    public event Action? OpenHealthRequested;
```

in the constructor, beside the other commands:

```csharp
        OpenHealthCommand = new RelayCommand(() => OpenHealthRequested?.Invoke());
```

in `Refresh()`, after `ScoreBrushKey = ...`:

```csharp
        // The revelation band: the scan's leading measured number, chosen by
        // the same picker every other surface will use.
        var revelations = RevelationPicker.Pick(snapshot.Findings);
        HasRevelation = revelations.Count > 0;
        if (revelations.Count > 0)
        {
            var top = revelations[0];
            var (value, caption) = LocalizedText.Headline(top.Headline!, _loc);
            RevelationValue = value;
            RevelationCaption = caption;
            RevelationClaim = _loc.Title(top.TitleKey, top.Title);
            RevelationEvidence = LocalizedText.Evidence(top, _loc);
            RevelationMoreText = revelations.Count > 1
                ? _loc.F("overview.revelation.more", revelations.Count - 1) : "";
            RevelationEmptyText = "";
        }
        else
        {
            RevelationValue = ""; RevelationCaption = "";
            RevelationClaim = ""; RevelationEvidence = ""; RevelationMoreText = "";
            RevelationEmptyText = _loc.F("overview.revelation.none",
                DiagnosticRuleRegistry.All.Count);
        }
```

(`using BriskEngine.Diagnostics;` is already imported by this file for other types; add it if missing.)

`Strings.resx`:

```xml
  <data name="overview.revelation.more" xml:space="preserve"><value>and {0} more</value></data>
  <data name="overview.revelation.none" xml:space="preserve"><value>All {0} rules looked — nothing on this machine leads with a number.</value></data>
  <data name="overview.revelation.see" xml:space="preserve"><value>See the evidence</value></data>
```

`Strings.tr.resx`:

```xml
  <data name="overview.revelation.more" xml:space="preserve"><value>ve {0} bulgu daha</value></data>
  <data name="overview.revelation.none" xml:space="preserve"><value>{0} kuralın hepsi baktı — bu makinede manşete çıkacak bir sayı yok.</value></data>
  <data name="overview.revelation.see" xml:space="preserve"><value>Kanıtı gör</value></data>
```

- [ ] **Step 4: Run the view-model tests**

Run: `dotnet test src/Brisk.Tests/Brisk.Tests.csproj -c Release --nologo --filter "Revelation|OpenHealth"`
Expected: 3 PASS.

- [ ] **Step 5: Add the band to `OverviewPage.xaml`**

Directly after the `HeroPanel` Border's closing `</Border>` (before the scanning-progress `StackPanel`):

```xml
            <!-- The revelation band: the scan's leading measured number on
                 the same always-dark surface as the cockpit. Static by
                 design — the cockpit already carries this page's motion. -->
            <Border Style="{StaticResource HeroStrip}" Margin="0,10,0,0"
                    Visibility="{Binding HasSnapshot,
                        Converter={x:Static Brisk:BoolToVis.Instance}}">
                <Grid>
                    <DockPanel Visibility="{Binding HasRevelation,
                            Converter={x:Static Brisk:BoolToVis.Instance}}">
                        <StackPanel DockPanel.Dock="Left" Width="180"
                                    VerticalAlignment="Center">
                            <TextBlock Style="{StaticResource HeroScore}" FontSize="40"
                                       Brisk:NumeralTick.Value="{Binding RevelationValue}" />
                            <TextBlock Margin="0,4,0,0" FontSize="11" TextWrapping="Wrap"
                                       FontFamily="Segoe UI Variable Text, Segoe UI"
                                       Foreground="{StaticResource HeroMuted}"
                                       Text="{Binding RevelationCaption}" />
                        </StackPanel>
                        <StackPanel Margin="20,0,0,0" VerticalAlignment="Center">
                            <TextBlock FontSize="14" TextWrapping="Wrap"
                                       FontFamily="Segoe UI Variable Text, Segoe UI"
                                       Foreground="{StaticResource HeroText}"
                                       Text="{Binding RevelationClaim}" />
                            <TextBlock Margin="0,5,0,0" FontSize="12" TextWrapping="Wrap"
                                       FontFamily="Segoe UI Variable Text, Segoe UI"
                                       Foreground="{StaticResource HeroMuted}"
                                       Text="{Binding RevelationEvidence}" />
                            <StackPanel Orientation="Horizontal" Margin="0,7,0,0">
                                <Button Style="{StaticResource LinkButton}"
                                        Command="{Binding OpenHealthCommand}"
                                        Content="{Binding [overview.revelation.see], Source={x:Static loc:Loc.Instance}}" />
                                <TextBlock Margin="12,0,0,0" VerticalAlignment="Center"
                                           FontSize="11"
                                           FontFamily="Segoe UI Variable Text, Segoe UI"
                                           Foreground="{StaticResource HeroMuted}"
                                           Text="{Binding RevelationMoreText}" />
                            </StackPanel>
                        </StackPanel>
                    </DockPanel>
                    <TextBlock VerticalAlignment="Center" FontSize="13" TextWrapping="Wrap"
                               FontFamily="Segoe UI Variable Text, Segoe UI"
                               Foreground="{StaticResource HeroMuted}"
                               Text="{Binding RevelationEmptyText}">
                        <TextBlock.Style>
                            <Style TargetType="TextBlock">
                                <Setter Property="Visibility" Value="Collapsed" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding HasRevelation}" Value="False">
                                        <Setter Property="Visibility" Value="Visible" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </TextBlock.Style>
                    </TextBlock>
                </Grid>
            </Border>
```

In `MainWindow.xaml.cs`, beside the two existing `CrossNavigateRequested` lines:

```csharp
        // The revelation band's "see the evidence" lands on Sağlık.
        overview.OpenHealthRequested += () => NavHealth.IsChecked = true;
```

- [ ] **Step 6: Run the full suite**

Run: `dotnet test brisk.sln -c Release --nologo`
Expected: all green, 0 warnings (XAML compiles in the Brisk build).

- [ ] **Step 7: Commit**

```bash
git add src/Brisk/ViewModels/OverviewViewModel.cs src/Brisk/Views/OverviewPage.xaml src/Brisk/Windows/MainWindow.xaml.cs src/Brisk/Localization/Strings.resx src/Brisk/Localization/Strings.tr.resx src/Brisk.Tests/OverviewViewModelTests.cs
git commit  # message: the overview leads with the scan's one number, and says so honestly when there is none
```

---

### Task 7: version 0.2.0 and the final sweep

**Files:**
- Modify: `src/BriskEngine/EngineInfo.cs`

**Interfaces:**
- Produces: `EngineInfo.Version == "0.2.0"` — the release workflow verifies a future `v0.2.0` tag against exactly this string.

- [ ] **Step 1: Bump the version**

```csharp
    public const string Version = "0.2.0";
```

- [ ] **Step 2: Full verification**

Run: `dotnet test brisk.sln -c Release --nologo`
Expected: all green, 0 warnings. Record the final test count.

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1`
Expected: both executables build; `.\artifacts\brisk.exe version` prints `0.2.0`.

- [ ] **Step 3: Commit**

```bash
git add src/BriskEngine/EngineInfo.cs
git commit  # message: 0.2.0 — the wave where the number leads
```

---

## Self-review notes

- Spec coverage: Headline model (T1), picker with declared order (T1), five opting rules with thermals excluded (T2–T4), shared resolver replacing FindingRow's private one (T5), cards lead with the value (T5), revelation band with empty state and derived rule count (T6), both languages pinned (T2–T6). The spec's "future structure" section requires no code by design.
- The tag itself (`v0.2.0`) is NOT part of this plan — the maintainer tags after review and the README screenshot.
