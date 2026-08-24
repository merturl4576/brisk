# FindingKind & Live-Use Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Findings brisk can only report stop lowering the health score (FindingKind, from the binding seven-rules spec), and the three defects the maintainer found in first live use get fixed: the revelation band's "see the evidence" link routes to the wrong page, the startup-bloat finding reads as "the fix did nothing" after a successful fix, and the band's evidence is a wall of text.

**Architecture:** `FindingKind { Problem, Notice }` on `DiagnosticFinding` (trailing, defaulted); `HealthScore` skips notices; four buttonless-advise rules opt in. GUI: a notices section below the advise section on both finding pages; the band's link carries the rule id and the window routes by `FindingSections`; the band shows only the first sentence of evidence.

**Tech Stack:** .NET 8 (`net8.0-windows`, x64), WPF, xUnit, resx localization.

**Spec:** `docs/superpowers/specs/2026-08-18-seven-rules-design.md` (the `FindingKind` section is binding: notices are excluded from `HealthScore`, render below the findings list, carry no fix affordance, and the default keeps existing rules untouched). The maintainer's decision of 2026-08-24 extends it: the four buttonless advise rules — `thermals`, `ram-pressure`, `boot-degradation`, `memory-speed` — become notices; the four storage-advise rules keep `Problem` because brisk has a real in-app follow-up for them.

## Global Constraints

- `TreatWarningsAsErrors` everywhere: **0 warnings**.
- Every user-visible string in BOTH `src/Brisk/Localization/Strings.resx` and `Strings.tr.resx`, single-line `<data>` format, pinned by tests (`LocTests` key-set parity plus the existence theory).
- The `Brisk` project has `ImplicitUsings` **disabled**.
- Verify with `dotnet test brisk.sln -c Release --nologo` (baseline on this branch: **819 green** = 361 BriskEngine.Tests + 458 Brisk.Tests).
- Notices stay VISIBLE — kind changes scoring and placement, never hides a finding. The revelation band still leads with a notice's headline (e.g. "54 sn").
- Commit messages: long-form story style (see `git log`), trailer `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`.

---

### Task 1: `FindingKind` in the engine

**Files:**
- Create: `src/BriskEngine/Models/FindingKind.cs`
- Modify: `src/BriskEngine/Models/DiagnosticFinding.cs`
- Modify: `src/BriskEngine/Diagnostics/HealthScore.cs`
- Modify: `src/BriskEngine/Diagnostics/Rules/ThermalsRule.cs`, `RamPressureRule.cs`, `BootDegradationRule.cs`, `MemorySpeedRule.cs`
- Test: `src/BriskEngine.Tests/HealthScoreTests.cs`, plus the four rules' test files (`SensorNoticeTests`/`AdviseRulesTests` host thermals and ram-pressure asserts; `BootDegradationRuleTests`; `MemorySpeedRuleTests`)

**Interfaces:**
- Produces: `public enum FindingKind { Problem, Notice }` in `BriskEngine.Models`; `DiagnosticFinding` gains trailing `FindingKind Kind = FindingKind.Problem` (after `Headline`); `HealthScore.Compute` ignores `Kind == Notice`; the four rules pass `Kind: FindingKind.Notice`.

- [ ] **Step 1: Write the failing tests**

Append to `src/BriskEngine.Tests/HealthScoreTests.cs` (use the file's existing finding factory; if it builds findings inline, follow that pattern — the only new argument is `Kind`):

```csharp
    /// A notice is a fact brisk can only report — 47 USB devices, memory
    /// below its rating on a board that will not change. Charging the score
    /// for it tells the user to fix hardware brisk itself says it cannot.
    [Fact]
    public void Notices_DoNotLowerTheScore()
    {
        var problem = new DiagnosticFinding("a", "rule.a.title", "A", "ev",
            Severity.Warning, RuleCategory.Advise, 4, false, null);
        var notice = new DiagnosticFinding("b", "rule.b.title", "B", "ev",
            Severity.Warning, RuleCategory.Advise, 4, false, null,
            Kind: FindingKind.Notice);

        Assert.Equal(
            HealthScore.Compute(new[] { problem }),
            HealthScore.Compute(new[] { problem, notice }));
    }

    /// The spec's stated reason for the enum: 100 stays reachable, so a
    /// user is never permanently penalised for what they cannot change.
    [Fact]
    public void AllNotices_ScoreIsAHundred()
    {
        var notices = new[]
        {
            new DiagnosticFinding("a", "rule.a.title", "A", "ev",
                Severity.Critical, RuleCategory.Advise, 5, false, null,
                Kind: FindingKind.Notice),
            new DiagnosticFinding("b", "rule.b.title", "B", "ev",
                Severity.Warning, RuleCategory.Advise, 4, false, null,
                Kind: FindingKind.Notice),
        };
        Assert.Equal(100, HealthScore.Compute(notices));
    }
```

And in each of the four rules' test files, extend an existing detecting test (do not build new fixtures) with one assert on the produced finding:

```csharp
        Assert.Equal(FindingKind.Notice, finding.Kind);
```

Plus one guard in `HealthScoreTests` that the default stays `Problem`:

```csharp
    [Fact]
    public void TheDefaultKind_IsProblem() =>
        Assert.Equal(FindingKind.Problem,
            new DiagnosticFinding("a", "rule.a.title", "A", "ev",
                Severity.Info, RuleCategory.Auto, 1, false, null).Kind);
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/BriskEngine.Tests/BriskEngine.Tests.csproj -c Release --nologo`
Expected: build FAILS — `FindingKind` unknown.

- [ ] **Step 3: Implement**

Create `src/BriskEngine/Models/FindingKind.cs`:

```csharp
namespace BriskEngine.Models;

/// Problem: something brisk judges wrong and, where it can, fixes.
/// Notice: a measured fact brisk can only report — it never lowers the
/// health score, because a score that punishes unchangeable hardware tells
/// the user to fix what brisk itself says it cannot.
public enum FindingKind { Problem, Notice }
```

`DiagnosticFinding.cs` — add the trailing parameter after `Headline`:

```csharp
    Headline? Headline = null,
    FindingKind Kind = FindingKind.Problem);
```

`HealthScore.cs` — skip notices inside the loop:

```csharp
        foreach (var f in findings)
        {
            // Notices are facts, not faults — the spec excludes them so 100
            // stays reachable on hardware the user cannot change.
            if (f.Kind == FindingKind.Notice) continue;
            penalty += f.ImpactStars * f.Severity switch
```

Each of the four rules: add `Kind: FindingKind.Notice` as the final argument of its `new DiagnosticFinding(...)` (for `BootDegradationRule` that is the shared `Finding` helper — one site covers both paths).

- [ ] **Step 4: Run to verify green**

Run: `dotnet test brisk.sln -c Release --nologo`
Expected: all green. Note: no GUI test pins the numeric health of a mixed snapshot (`TestData.Snapshot` hardcodes 72), so the engine change should not ripple; if a Brisk.Tests assert does break on a score, report it rather than adjusting it silently.

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine/Models/FindingKind.cs src/BriskEngine/Models/DiagnosticFinding.cs src/BriskEngine/Diagnostics/HealthScore.cs src/BriskEngine/Diagnostics/Rules/ThermalsRule.cs src/BriskEngine/Diagnostics/Rules/RamPressureRule.cs src/BriskEngine/Diagnostics/Rules/BootDegradationRule.cs src/BriskEngine/Diagnostics/Rules/MemorySpeedRule.cs src/BriskEngine.Tests/
git commit  # message: the score stops charging for facts nobody can change
```

---

### Task 2: the notices section on both finding pages

**Files:**
- Modify: `src/Brisk/ViewModels/HealthViewModel.cs`
- Modify: `src/Brisk/Views/HealthPage.xaml`, `src/Brisk/Views/PerfPage.xaml`
- Modify: `src/Brisk/Localization/Strings.resx`, `Strings.tr.resx`
- Test: `src/Brisk.Tests/HealthViewModelTests.cs`, `src/Brisk.Tests/LocTests.cs`

**Interfaces:**
- Consumes: `FindingKind` (Task 1); `TestData.Finding` needs a trailing `FindingKind kind = FindingKind.Problem` parameter added in `src/Brisk.Tests/Fakes.cs` (passed through as the record's final positional).
- Produces: `HealthViewModel.NoticeRows` (ObservableCollection<FindingRow>); resx keys `health.notice.section` (EN "What brisk can only report" / TR "brisk'in yalnızca bildirebildikleri").

- [ ] **Step 1: Write the failing tests** (append to `HealthViewModelTests`, using the file's existing fixture the way its advise-section tests do)

```csharp
    /// Kind decides placement: a Notice leaves the advise section for its
    /// own band. It is NEVER hidden — kind changes scoring and placement.
    [Fact]
    public async Task NoticeFindings_LandInTheNoticeBand_NotTheAdviseSection()
    {
        var (vm, host, state) = Build();   // use the file's actual builder name
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("disk-breakdown", cat: RuleCategory.Advise, canFix: false),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false,
                kind: FindingKind.Notice),
        }, new SensorStatus(true, true, null));

        await state.ScanAsync();

        Assert.Single(vm.AdviseRows);
        Assert.Equal("disk-breakdown", vm.AdviseRows[0].RuleId);
        Assert.Single(vm.NoticeRows);
        Assert.Equal("thermals", vm.NoticeRows[0].RuleId);
        Assert.Empty(vm.Rows.Where(r => r.RuleId == "thermals"));
    }
```

(If the fixture builder has a different shape, keep the assertions and adapt only the construction. The finding routed to `NoticeRows` must not appear in `Rows` or `AdviseRows`.)

Add the two new resx keys to the `LocTests` existence theory alongside the wave's other keys.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/Brisk.Tests/Brisk.Tests.csproj -c Release --nologo --filter NoticeFindings`
Expected: build FAILS — no `NoticeRows`, no `kind:` parameter.

- [ ] **Step 3: Implement**

`Fakes.cs` — `TestData.Finding` gains trailing `FindingKind kind = FindingKind.Problem`, passed through after `headline`.

`HealthViewModel.cs`:
- beside `AdviseRows`: `public ObservableCollection<FindingRow> NoticeRows { get; } = new();`
- in the refresh that clears/fills rows, clear `NoticeRows` too and route:

```csharp
            (finding.Kind == FindingKind.Notice ? NoticeRows
                : finding.Category == RuleCategory.Advise ? AdviseRows
                : Rows)
                .Add(new FindingRow(...));   // existing arguments unchanged
```

- the status line keeps counting notices among "öneri" (they are still worth reviewing); leave `StatusLine` untouched and note it in the commit message as deliberate.

Both `HealthPage.xaml` and `PerfPage.xaml`: directly below the advise `ItemsControl`, the same section pattern the advise band uses — a `SectionLabel` bound to `[health.notice.section]` that collapses when `NoticeRows.Count` is 0 (copy the advise label's `DataTrigger` shape), then `<ItemsControl ItemsSource="{Binding NoticeRows}" ItemTemplate="{StaticResource FindingCard}" />`. Notices are all advise-category, so the card template already renders them with the hollow ring and no fix button — no template change.

resx:

```xml
  <data name="health.notice.section" xml:space="preserve"><value>What brisk can only report</value></data>
```

```xml
  <data name="health.notice.section" xml:space="preserve"><value>brisk'in yalnızca bildirebildikleri</value></data>
```

- [ ] **Step 4: Run to verify green**

Run: `dotnet test brisk.sln -c Release --nologo`
Expected: all green (XAML compiles in the Brisk build).

- [ ] **Step 5: Commit**

```bash
git add src/Brisk/ViewModels/HealthViewModel.cs src/Brisk/Views/HealthPage.xaml src/Brisk/Views/PerfPage.xaml src/Brisk/Localization/Strings.resx src/Brisk/Localization/Strings.tr.resx src/Brisk.Tests/Fakes.cs src/Brisk.Tests/HealthViewModelTests.cs src/Brisk.Tests/LocTests.cs
git commit  # message: what brisk can only report gets its own band, and keeps its visibility
```

---

### Task 3: "see the evidence" routes to the finding, not to a page name

**Files:**
- Modify: `src/Brisk/ViewModels/OverviewViewModel.cs`
- Modify: `src/Brisk/ViewModels/HealthViewModel.cs`
- Modify: `src/Brisk/Windows/MainWindow.xaml.cs`
- Test: `src/Brisk.Tests/OverviewViewModelTests.cs`, `src/Brisk.Tests/HealthViewModelTests.cs`

**Interfaces:**
- Consumes: `FindingSections.IsPerformance(string ruleId)` (existing).
- Produces: `OverviewViewModel.OpenFindingRequested` (`event Action<string>?`, payload = rule id) and `OpenFindingCommand` replacing `OpenHealthRequested`/`OpenHealthCommand`; `HealthViewModel.ExpandFinding(string ruleId)`.

The defect, verbatim from live use: the band led with the boot finding, its link said "Kanıtı gör", and it navigated to Sağlık — but `boot-degradation` lives on Performans, so the user never saw the evidence. The link must go to the page that hosts the finding AND open its card.

- [ ] **Step 1: Write the failing tests**

`OverviewViewModelTests` — replace the existing `OpenHealth_RaisesTheNavigationEvent` with:

```csharp
    [Fact]
    public async Task OpenFinding_CarriesTheTopRevelationsRuleId()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("zz-fake", cat: RuleCategory.Advise, canFix: false,
                headline: new Headline("57 s", "cap",
                    "rule.zz-fake.headline.value", new[] { "57" },
                    "rule.zz-fake.headline.caption", Array.Empty<string>())),
        }, new SensorStatus(true, true, null));
        await state.ScanAsync();

        string? requested = null;
        vm.OpenFindingRequested += id => requested = id;
        vm.OpenFindingCommand.Execute(null);

        Assert.Equal("zz-fake", requested);
    }
```

`HealthViewModelTests`:

```csharp
    [Fact]
    public async Task ExpandFinding_OpensTheNamedCard_WhereverItLives()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false,
                kind: FindingKind.Notice),
        }, new SensorStatus(true, true, null));
        await state.ScanAsync();

        vm.ExpandFinding("thermals");

        Assert.True(vm.NoticeRows.Single(r => r.RuleId == "thermals").IsExpanded);
        Assert.False(vm.Rows.Single(r => r.RuleId == "power-plan").IsExpanded);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/Brisk.Tests/Brisk.Tests.csproj -c Release --nologo --filter "OpenFinding|ExpandFinding"`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`OverviewViewModel`: rename the event and command; keep the raise inside the command; when setting the revelation, store the top finding's rule id in a private field the command raises with:

```csharp
    public event Action<string>? OpenFindingRequested;
    public RelayCommand OpenFindingCommand { get; }
    private string _revelationRuleId = "";
```

ctor: `OpenFindingCommand = new RelayCommand(() => { if (_revelationRuleId.Length > 0) OpenFindingRequested?.Invoke(_revelationRuleId); });`
In `Refresh()`'s revelation block: `_revelationRuleId = top.RuleId;` (and `""` in the else branch). `OverviewPage.xaml`'s link button binding changes to `OpenFindingCommand`.

`HealthViewModel`:

```csharp
    /// The revelation band's "see the evidence" lands here: open the named
    /// card so the user reads the evidence instead of hunting for it.
    public void ExpandFinding(string ruleId)
    {
        foreach (var row in Rows.Concat(AdviseRows).Concat(NoticeRows))
            if (string.Equals(row.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))
            {
                row.IsExpanded = true;
                return;
            }
    }
```

(`using System.Linq;` is already present in the file.)

`MainWindow.xaml.cs` — replace the `overview.OpenHealthRequested` line:

```csharp
        // The band's "see the evidence" goes to the page that HOSTS the
        // finding — the boot finding lives on Performans, and sending its
        // reader to Sağlık was the first defect live use found.
        overview.OpenFindingRequested += ruleId =>
        {
            if (FindingSections.IsPerformance(ruleId))
            {
                NavPerf.IsChecked = true;
                performance.ExpandFinding(ruleId);
            }
            else
            {
                NavHealth.IsChecked = true;
                health.ExpandFinding(ruleId);
            }
        };
```

- [ ] **Step 4: Run to verify green**

Run: `dotnet test brisk.sln -c Release --nologo`
Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add src/Brisk/ViewModels/OverviewViewModel.cs src/Brisk/ViewModels/HealthViewModel.cs src/Brisk/Windows/MainWindow.xaml.cs src/Brisk/Views/OverviewPage.xaml src/Brisk.Tests/OverviewViewModelTests.cs src/Brisk.Tests/HealthViewModelTests.cs
git commit  # message: see-the-evidence goes to the evidence, not to a page that does not hold it
```

---

### Task 4: honest post-fix copy, a one-sentence band, and 0.4.0

**Files:**
- Modify: `src/BriskEngine/Diagnostics/Rules/StartupBloatRule.cs`
- Modify: `src/Brisk/Localization/Strings.resx`, `Strings.tr.resx`
- Modify: `src/Brisk/ViewModels/OverviewViewModel.cs`
- Modify: `src/BriskEngine/EngineInfo.cs` (0.3.0 → 0.4.0; no tag)
- Test: `src/BriskEngine.Tests/Rules/StartupBloatRuleTests.cs`, `src/Brisk.Tests/OverviewViewModelTests.cs`

**Defect A, verbatim from live use:** the fix disabled the heavy startup programs (13 → 11, toggles off, done-report ✓) and the finding re-fired looking identical, so the maintainer read it as "the fix did nothing". The count-only branch fires whenever total ≥ 6 — before OR after a fix — so its copy must say whose call the remaining programs are.

- [ ] **Step 1: Change the count-only evidence** (engine English + both resx; the `.heavy` variant is untouched):

Engine, in `Detect`'s count-only path:

```csharp
        var evidence = $"{total} programs start with Windows.";
        if (heavy.Count > 0)
            evidence += $" Heavy ones that can be started manually instead: {heavyNames}.";
        else
            evidence += " None of them is on brisk's heavy list, so which ones "
                + "you actually need is your call — review them in the startup "
                + "list below.";
```

`Strings.resx`:

```xml
  <data name="rule.startup-bloat.evidence" xml:space="preserve"><value>{0} programs start with Windows. None of them is on brisk's heavy list, so which ones you actually need is your call — review them in the startup list below.</value></data>
```

`Strings.tr.resx`:

```xml
  <data name="rule.startup-bloat.evidence" xml:space="preserve"><value>Windows ile birlikte {0} program başlıyor. Hiçbiri brisk'in ağır listesinde değil; hangilerinin sana gerçekten gerekli olduğu senin kararın — aşağıdaki açılış listesinden gözden geçir.</value></data>
```

Extend `ManyItems_IsAFinding_EvenWithoutHeavyOnes` to pin the new guidance (`Assert.Contains("your call", finding.Evidence);`), and update any test pinning the old exact string (search both test projects for `programs start with Windows.` first).

**Defect B:** the band's evidence is a wall of text. In `OverviewViewModel.Refresh()`:

```csharp
            RevelationEvidence = FirstSentence(LocalizedText.Evidence(top, _loc));
```

with, beside the other private helpers:

```csharp
    /// The band is a glance surface: one sentence of evidence, with the full
    /// text one click away behind "see the evidence". Nothing is lost —
    /// it moves to the layer built for reading.
    private static string FirstSentence(string text)
    {
        var cut = text.IndexOf(". ", StringComparison.Ordinal);
        return cut < 0 ? text : text[..(cut + 1)];
    }
```

Test (`OverviewViewModelTests`): a fake finding whose English evidence is two sentences (`TestData.Finding` produces `"Evidence zz-fake"` — construct the finding inline with `Evidence: "First claim. Second claim."` via `new DiagnosticFinding(...)` plus a headline) → `vm.RevelationEvidence == "First claim."`.

- [ ] **Step 2: Bump the version**

`EngineInfo.Version` → `"0.4.0"`. Per the maintainer's release policy (2026-08-24): the version advances each wave, **no tag is created**.

- [ ] **Step 3: Full verification**

Run: `dotnet test brisk.sln -c Release --nologo` — all green, 0 warnings; record the count.

- [ ] **Step 4: Commit**

```bash
git add src/BriskEngine/Diagnostics/Rules/StartupBloatRule.cs src/Brisk/Localization/Strings.resx src/Brisk/Localization/Strings.tr.resx src/Brisk/ViewModels/OverviewViewModel.cs src/BriskEngine/EngineInfo.cs src/BriskEngine.Tests/Rules/StartupBloatRuleTests.cs src/Brisk.Tests/OverviewViewModelTests.cs
git commit  # message: the finding says whose call the rest is, and the band says one sentence
```

---

## Self-review notes

- Spec coverage: enum + score exclusion + separate band + no fix affordance (notices are advise-category, already buttonless) + default untouched — all four spec clauses have tasks. The maintainer's three live-use defects map to T3 (routing+expand), T4-A (copy), T4-B (one sentence).
- Deliberate non-changes, stated for reviewers: the status lines keep counting notices among "öneri" (a notice is still worth reviewing); the revelation band still leads with notice headlines; `TestData.Snapshot`'s hardcoded health of 72 means no GUI test rides on the scoring change.
- T2/T3 both touch `HealthViewModel.cs` — sequential tasks, no conflict; T3 consumes T2's `NoticeRows` in `ExpandFinding`.
