# Report Card & Finding Workbench Implementation Plan

> **2026-08-24 — superseded in places; the shipped tree is authoritative.**
> This is the plan as it was written before implementation. Implementation and
> the review rounds after it changed a great deal, so every inline snippet that
> would be dangerous or simply wrong to copy has been replaced by a pointer to
> the code that actually shipped. In particular, **every workbench script body
> has been removed from this document**: the reviewed scripts live in
> `tools/workbench/` and are the only ones anyone should run. The drafts here
> scraped `powercfg`'s localized output with a regex that fails open, used
> `New-Item -Force` on registry keys (which REPLACES the key and everything
> under it), announced restores they had not verified, and called six real
> `HKCU\Run` values that execute at next logon "inert". The prose is kept
> as the record of what was intended. The snippets are not a source to build
> from.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One dark 16:9 PNG a user can share without thinking twice, produced from the GUI and from the merged executable's console face — plus a public workbench of plant/restore scripts that prove brisk catches what they plant.

**Architecture:** A `SensorStatus` record joins `ScanSnapshot`; a pure `ReportCardModel` builds the card's text (privacy filter enforced by test); a WPF `ReportCard` control rendered offscreen via `RenderTargetBitmap`; the `report` verb intercepted in `brisk-app.exe`'s entry point before the CLI parser (Brisk.Cli cannot reference Brisk); PowerShell scenario scripts under `tools/workbench/`.

**Tech Stack:** .NET 8 (`net8.0-windows`, x64), WPF, xUnit, resx localization, PowerShell 5.1.

**Spec:** `docs/superpowers/specs/2026-08-23-report-card-and-workbench-design.md`

## Global Constraints

- `TreatWarningsAsErrors` everywhere: **0 warnings**.
- Every user-visible string in BOTH `src/Brisk/Localization/Strings.resx` and `Strings.tr.resx`, pinned by tests.
- The `Brisk` project has `ImplicitUsings` **disabled**; `BriskEngine` and `Brisk.Cli` have them enabled.
- Verify with `dotnet test brisk.sln -c Release --nologo` (baseline on this branch: **727 green** = 379 Brisk.Tests + 348 BriskEngine.Tests).
- The card carries **headlines and titles, never raw evidence**; machine name, user name, and profile paths are banned from the model's output (test-enforced).
- Commit messages: long-form story style (see `git log`), trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Workbench scripts must be double-plant-proof: a plant script refuses to run when its state file already exists.

---

### Task 1: `SensorStatus` joins the snapshot

**Files:**
- Modify: `src/Brisk/Services/IEngineHost.cs`
- Modify: `src/Brisk/Services/EngineHost.cs`
- Modify: `src/Brisk.Tests/Fakes.cs`
- Test: `src/Brisk.Tests/EngineHostTests.cs`

**Interfaces:**
- Produces: `SensorStatus(bool CpuRead, bool GpuRead, bool? MemoryIntegrityOn)` in `Brisk.Services`; `ScanSnapshot` gains optional trailing `SensorStatus? Sensors = null`; `EngineHost.ScanAsync` fills it from the context's probes; `TestData.Snapshot` gains optional trailing `SensorStatus? sensors = null` passed through.

- [ ] **Step 1: Write the failing test** (append to `EngineHostTests`; the file's `Host(...)` fixture builds an `EngineHost` over Null probes — `NullSensors` returns null temperatures and `NullMemoryIntegrity.IsOn()` returns null)

```csharp
    /// The card's "what brisk could not read" section is built from the
    /// snapshot, so the scan records what the sensors answered at scan time.
    [Fact]
    public async Task ScanAsync_RecordsSensorStatus()
    {
        var host = Host(Array.Empty<IDiagnosticRule>());

        var snapshot = await host.ScanAsync();

        Assert.NotNull(snapshot.Sensors);
        Assert.False(snapshot.Sensors!.CpuRead);
        Assert.False(snapshot.Sensors.GpuRead);
        Assert.Null(snapshot.Sensors.MemoryIntegrityOn);
    }
```

(If the fixture method has a different name than `Host`, use the file's existing builder — the one `ScanAsync_CollectsFindings_SkipsThrowingRule_ComputesHealth` uses — without changing it.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Brisk.Tests/Brisk.Tests.csproj -c Release --nologo --filter RecordsSensorStatus`
Expected: build FAILS — `ScanSnapshot` has no `Sensors` member.

- [ ] **Step 3: Implement**

In `src/Brisk/Services/IEngineHost.cs`, above `ScanSnapshot`:

```csharp
/// What the temperature sensors answered at scan time, recorded into the
/// snapshot so the report card can say "could not read" about the scan it
/// is rendering rather than about some later moment.
public sealed record SensorStatus(bool CpuRead, bool GpuRead, bool? MemoryIntegrityOn);
```

and give it a trailing `SensorStatus? Sensors = null`, filled in
`EngineHost.ScanAsync` from the context's probes, with `TestData.Snapshot`
gaining a matching optional parameter.

> **Superseded.** `Sensors` shipped as a REQUIRED member. The optional
> parameter is what let `ReportCardModel` fall back to
> `new SensorStatus(true, true, null)`, so a snapshot that had recorded nothing
> about the sensors rendered "Everything brisk tried to read, answered." onto a
> shareable PNG. The finite-vs-null test also moved out of this expression into
> one shared predicate, `SensorReading.IsReal`, because `brisk scan`'s notice
> was asking `is not null` and disagreeing with the card about NaN. See
> `src/Brisk/Services/IEngineHost.cs`, `src/Brisk/Services/EngineHost.cs`,
> `src/BriskEngine/Diagnostics/Probes.cs` and `src/Brisk.Tests/Fakes.cs`.

- [ ] **Step 4: Run to verify green**

Run: `dotnet test brisk.sln -c Release --nologo`
Expected: all green (the parameter is optional; nothing else changes).

- [ ] **Step 5: Commit**

```bash
git add src/Brisk/Services/IEngineHost.cs src/Brisk/Services/EngineHost.cs src/Brisk.Tests/Fakes.cs src/Brisk.Tests/EngineHostTests.cs
git commit  # message: the scan records what the sensors answered, so the card can be honest about the scan it shows
```

---

### Task 2: `ReportCardModel` — the card's text, privacy enforced by test

**Files:**
- Create: `src/Brisk/ViewModels/ReportCardModel.cs`
- Modify: `src/Brisk/Localization/Strings.resx`, `src/Brisk/Localization/Strings.tr.resx`
- Test: `src/Brisk.Tests/ReportCardModelTests.cs` (new)

**Interfaces:**
- Consumes: `RevelationPicker.Pick`, `LocalizedText.Headline`, `Loc` (indexer, `F`, `Title`), `UndoableFix(RuleId, FixedAtUtc)`, `SensorStatus` (Task 1), `EngineInfo.Version`, `DiagnosticRuleRegistry.All.Count`.
- Produces: `CardLine(string Lead, string Text)`; `ReportCardModel` with `DateText`, `VersionText`, `Health`, `Findings` (IReadOnlyList<CardLine>), `FindingsEmptyText`, `Unread` (IReadOnlyList<string>), `Fixes` (IReadOnlyList<string>), `HasFixes`, `RepoLine`; static `Build(ScanSnapshot, IReadOnlyList<UndoableFix>, Loc)`.

- [ ] **Step 1: Write the failing tests**

Create `src/Brisk.Tests/ReportCardModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine;
using BriskEngine.Diagnostics;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

public class ReportCardModelTests
{
    private static Loc Loc(string lang)
    {
        var loc = new Loc();
        loc.SetLanguage(lang);
        return loc;
    }

    private static Headline H(string value) => new(value, "cap",
        "rule.fake.headline.value", new[] { value },
        "rule.fake.headline.caption", Array.Empty<string>());

    [Fact]
    public void Findings_AreHeadlinePlusTitle_InPickerOrder_NeverEvidence()
    {
        var snapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("aa-fake", cat: RuleCategory.Advise, canFix: false,
                headline: H("13")),
            TestData.Finding("zz-fake", sev: Severity.Critical,
                cat: RuleCategory.Advise, canFix: false, headline: H("57 s")),
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
        }, new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Equal(2, card.Findings.Count);                    // headline-less thermals excluded
        Assert.Equal("57 s", card.Findings[0].Lead);             // Critical outranks Warning
        Assert.Equal("Title zz-fake", card.Findings[0].Text);    // the TITLE, never the evidence
        Assert.Equal("13", card.Findings[1].Lead);
        Assert.Equal("", card.FindingsEmptyText);
        Assert.DoesNotContain(card.Findings, l => l.Text.Contains("Evidence"));
    }

    [Fact]
    public void NoHeadlines_KeepsTheSectionWithTheHonestEmptyLine()
    {
        var snapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("thermals", cat: RuleCategory.Advise, canFix: false),
        }, new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Empty(card.Findings);
        Assert.Equal(
            $"All {DiagnosticRuleRegistry.All.Count} rules looked — nothing on this machine leads with a number.",
            card.FindingsEmptyText);
    }

    [Theory]
    [InlineData(true, true, null, "en", "Everything brisk tried to read, answered.")]
    [InlineData(true, true, null, "tr", "brisk'in okumaya çalıştığı her şey cevap verdi.")]
    [InlineData(true, false, null, "en", "GPU temperature — not read; brisk cannot tell from here why.")]
    [InlineData(false, false, null, "en", "Temperatures — neither sensor answered.")]
    public void UnreadSection_NeverDrops_AndSpeaksTheVariant(
        bool cpu, bool gpu, bool? integrity, string lang, string expected)
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(cpu, gpu, integrity));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc(lang));

        Assert.Equal(new[] { expected }, card.Unread);
    }

    [Theory]
    [InlineData(true, "en", "CPU temperature — not read. Memory integrity is on; the driver that reads it is on Microsoft's vulnerable-driver blocklist.")]
    [InlineData(true, "tr", "CPU sıcaklığı — okunamadı. Bellek bütünlüğü açık; onu okuyan sürücü Microsoft'un güvenlik açığı listesinde.")]
    [InlineData(false, "en", "CPU temperature — not read. Memory integrity is off here, so the usual reason is ruled out; brisk cannot tell what did it.")]
    public void CpuUnread_CarriesTheMeasuredIntegrityVariant(
        bool? integrity, string lang, string expected)
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(false, true, integrity));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc(lang));

        Assert.Equal(new[] { expected }, card.Unread);
    }

    [Fact]
    public void CpuUnread_UnknownIntegrity_KeepsTheHedge()
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(false, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Equal(
            new[] { "CPU temperature — not read. brisk could not confirm the reason on this machine." },
            card.Unread);
    }

    [Fact]
    public void Fixes_AreTitleAndDate_AndTheSectionDropsWhenEmpty()
    {
        var fixes = new[]
        {
            new UndoableFix("power-plan", new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc)),
        };
        var snapshot = TestData.Snapshot(null, new SensorStatus(true, true, null));

        var with = ReportCardModel.Build(snapshot, fixes, Loc("en"));
        var without = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.True(with.HasFixes);
        Assert.Single(with.Fixes);
        // The localized rule title, never the raw id — the exact resx text is
        // pinned elsewhere; here the contract is "not the id, plus the date".
        Assert.DoesNotContain("power-plan", with.Fixes[0]);
        Assert.False(string.IsNullOrWhiteSpace(with.Fixes[0]));
        Assert.Contains("2026-08-20", with.Fixes[0]);
        Assert.False(without.HasFixes);
    }

    /// The privacy ban, enforced on output rather than on good intentions:
    /// plant the user's name, the machine name, and a profile path into every
    /// engine-authored string a finding carries, and prove none of them can
    /// reach the card.
    [Fact]
    public void PrivacyBan_EvidenceNamesAndPathsNeverReachTheCard()
    {
        // The markers live ONLY in the fields that carry user data in real
        // findings (evidence, fix description) — the title is rule-authored
        // static text and legitimately appears on the card.
        var poisoned = new DiagnosticFinding(
            "zz-fake", "rule.zz-fake.title", "Too many programs run at start",
            @"C:\Users\SECRETUSER\Desktop leaks from DESKTOP-SECRETPC via SecretApp.exe",
            Severity.Warning, RuleCategory.Advise, 3, CanFix: false,
            FixDescription: @"delete C:\Users\SECRETUSER\file",
            Headline: H("47"));
        var snapshot = TestData.Snapshot(new[] { poisoned },
            new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        var everything = string.Join("\n",
            card.Findings.Select(l => l.Lead + " " + l.Text)
                .Concat(card.Unread).Concat(card.Fixes)
                .Append(card.FindingsEmptyText).Append(card.DateText)
                .Append(card.VersionText).Append(card.RepoLine));
        Assert.Contains("47", everything);                       // the number survives
        Assert.DoesNotContain("SECRETUSER", everything);         // the user never does
        Assert.DoesNotContain("DESKTOP-SECRETPC", everything);
        Assert.DoesNotContain("SecretApp", everything);
        Assert.DoesNotContain(@"C:\Users", everything);
    }

    [Fact]
    public void TopStrip_CarriesLocalDateAndEngineVersion()
    {
        var snapshot = TestData.Snapshot(null, new SensorStatus(true, true, null));

        var card = ReportCardModel.Build(snapshot, Array.Empty<UndoableFix>(), Loc("en"));

        Assert.Equal(EngineInfo.Version, card.VersionText);
        Assert.Contains("2026-08-15", card.DateText);            // TestData's CompletedUtc date
        Assert.Equal("github.com/merturl4576/brisk", card.RepoLine);
        Assert.Equal(72, card.Health);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/Brisk.Tests/Brisk.Tests.csproj -c Release --nologo --filter ReportCardModel`
Expected: build FAILS — `ReportCardModel`/`CardLine` unknown.

- [ ] **Step 3: Implement**

Create `src/Brisk/ViewModels/ReportCardModel.cs`.

> **Superseded.** See the shipped file. Three differences worth naming: the
> `snapshot.Sensors ?? new SensorStatus(true, true, null)` fallback is gone —
> `Sensors` is required, because that fallback put a measurement claim on a
> card built from a snapshot that had measured nothing; `HealthBrushKey` is
> `ScoreBrushKey`, the name the app's own score styles already bind; and the
> fix list is capped at nine rows with an "and N more" line, because the card's
> frame does not clip gracefully — it clips silently.

Add the `report.*` block to `Strings.resx` and `Strings.tr.resx`.

> **Superseded — do not copy these keys from here.** The draft's
> `report.unread.neither` read only "Temperatures — neither sensor answered.",
> dropping the measured memory-integrity reason that the CPU half of the same
> silence carries: on an HVCI machine with no readable GPU sensor, `brisk scan`
> explained the blocklisted driver and the card explained nothing — two
> surfaces of one product disagreeing about the same silent sensor. The shipped
> set has three `report.unread.neither*` variants beside the three
> `report.unread.cpu*` ones. Read them out of
> `src/Brisk/Localization/Strings.resx` and `Strings.tr.resx`.

(Note: the `(false, _) when !sensors.GpuRead` arm must come before the cpu-only arm so "neither" wins; the tests pin exactly this.)

- [ ] **Step 4: Run to verify green**

Run: `dotnet test brisk.sln -c Release --nologo`
Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add src/Brisk/ViewModels/ReportCardModel.cs src/Brisk/Localization/Strings.resx src/Brisk/Localization/Strings.tr.resx src/Brisk.Tests/ReportCardModelTests.cs
git commit  # message: the card's words exist before its pixels, and the privacy ban is a test, not a promise
```

---

### Task 3: the card's pixels — `ReportCard.xaml` + renderer + smoke test

**Files:**
- Create: `src/Brisk/Views/ReportCard.xaml` + `src/Brisk/Views/ReportCard.xaml.cs`
- Create: `src/Brisk/Services/ReportCardRenderer.cs`
- Test: `src/Brisk.Tests/ReportCardRenderTests.cs` (new)

**Interfaces:**
- Consumes: `ReportCardModel` (Task 2), the Hero* vocabulary and theme keys from `Theming/Dark.xaml` + `Theming/Shared.xaml`, `Brisk.Views.SegmentedGauge`.
- Produces: `ReportCardRenderer.RenderToFile(ReportCardModel, string path)` and `RenderOnStaThread(ReportCardModel, string path)`; `ReportRunner.DefaultPath()` comes in Task 4.

- [ ] **Step 1: Write the failing smoke test**

Create `src/Brisk.Tests/ReportCardRenderTests.cs`:

```csharp
using System;
using System.IO;
using Brisk.Services;
using Brisk.ViewModels;
using Xunit;

namespace Brisk.Tests;

/// The pixel side gets a smoke test, not a pixel test: the PNG exists, is a
/// PNG, and is card-sized. Everything about the card's CONTENT is pinned on
/// the model in ReportCardModelTests.
public class ReportCardRenderTests
{
    [Fact]
    public void Render_WritesAValidPng()
    {
        var loc = new Brisk.Localization.Loc();
        loc.SetLanguage("en");
        var model = ReportCardModel.Build(
            TestData.Snapshot(null, new SensorStatus(true, true, null)),
            Array.Empty<UndoableFix>(), loc);
        var path = Path.Combine(
            Directory.CreateTempSubdirectory("brisk-card-").FullName, "card.png");

        ReportCardRenderer.RenderOnStaThread(model, path);

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 10_000, $"suspiciously small: {bytes.Length} bytes");
        // The eight-byte PNG signature.
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            bytes[..8]);
    }
}
```

(`UndoableFix` lives in `BriskEngine.Diagnostics`; add the using the compiler asks for.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Brisk.Tests/Brisk.Tests.csproj -c Release --nologo --filter ReportCardRender`
Expected: build FAILS — `ReportCardRenderer` unknown.

- [ ] **Step 3: Implement the control**

Create `src/Brisk/Views/ReportCard.xaml`.

> **Superseded.** See the shipped file. The draft's gauge triggers bound
> `HealthBrushKey`, while the score numeral's `HeroScore` style binds
> `ScoreBrushKey` — a name the model did not expose, so that binding failed
> silently and the numeral rendered white for no reason anyone had chosen. The
> model now exposes `ScoreBrushKey`, the card's triggers bind it, and the
> numeral sets its own `Foreground` so the band stays on the ring. The body
> column also carries a name, so a test can weigh what it asks for against the
> height the Grid actually gives it.

Create `src/Brisk/Views/ReportCard.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace Brisk.Views;

public partial class ReportCard : UserControl
{
    public ReportCard() { InitializeComponent(); }
}
```

Create `src/Brisk/Services/ReportCardRenderer.cs`.

> **Superseded.** See the shipped file. Two things the draft is missing, each
> with a bug behind it. It never settles the gauges: SegmentedGauge animates
> its lit arc up from zero when Score changes, and an animation clock only
> advances while a dispatcher is pumping frames — so offscreen the clock never
> leaves zero and the card comes out with a dead grey ring, in a perfectly
> valid 312 KB PNG. And it calls `Directory.CreateDirectory(Path.GetDirectoryName(path)!)`
> after the render, which throws on a bare `card.png` (GetDirectoryName returns
> an empty string, not null) — at the end of a 23 MB render, on the likeliest
> thing a user types.

Note on the pack URIs: with no `Application` alive, `pack://application:,,,` still resolves for resources in the executing assembly, but the test host loads `brisk-app.dll` as a library — if `InitializeComponent` throws on resource resolution in the test run, register the pack scheme by touching `System.IO.Packaging.PackUriHelper.UriSchemePack` once at the top of `RenderOnStaThread` (reading the field forces scheme registration):

```csharp
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
```

Include that line from the start — it is inert when an Application exists.

- [ ] **Step 4: Run to verify green**

Run: `dotnet test brisk.sln -c Release --nologo`
Expected: all green, 0 warnings. If the smoke test fails on resource resolution, the failure message names the exact URI — report it rather than improvising a different resource scheme.

- [ ] **Step 5: Commit**

```bash
git add src/Brisk/Views/ReportCard.xaml src/Brisk/Views/ReportCard.xaml.cs src/Brisk/Services/ReportCardRenderer.cs src/Brisk.Tests/ReportCardRenderTests.cs
git commit  # message: the card renders itself with the app's own dictionaries, alive even when no window is
```

---

### Task 4: the surfaces — `report` verb, honest refusal, Overview button

**Files:**
- Create: `src/Brisk/Services/ReportRunner.cs`
- Modify: `src/Brisk/Program.cs`
- Modify: `src/Brisk.Cli/CliParser.cs`, `src/Brisk.Cli/Program.cs`
- Modify: `src/Brisk/ViewModels/OverviewViewModel.cs`, `src/Brisk/Views/OverviewPage.xaml`
- Modify: `src/Brisk/Localization/Strings.resx`, `src/Brisk/Localization/Strings.tr.resx`
- Test: `src/BriskEngine.Tests/CliParserTests.cs`, `src/BriskEngine.Tests/CliHelpSwitchTests.cs`, `src/Brisk.Tests/OverviewViewModelTests.cs`

**Interfaces:**
- Consumes: `ReportCardModel.Build`, `ReportCardRenderer` (Tasks 2–3), `AppServices.Build()`, `EntryRouter.RoutesToConsole`.
- Produces: `ReportRunner.Run(string[] args) -> int` and `ReportRunner.DefaultPath() -> string`; CLI verb `report` (recognized, refused in `brisk.exe`); `OverviewViewModel.SaveReportCommand` + `ReportSavedText`.

- [ ] **Step 1: Write the failing tests**

Append to `src/BriskEngine.Tests/CliParserTests.cs`:

```csharp
    [Fact]
    public void Report_IsAKnownVerb() =>
        Assert.Equal("report", CliParser.Parse(new[] { "report" }).Verb);
```

Append to `src/BriskEngine.Tests/CliHelpSwitchTests.cs` (its `Capture` helper already exists):

```csharp
    /// brisk.exe cannot render the card (no WPF) and says exactly why and
    /// where to go — an unknown-command error would be a lie about the
    /// reason for refusing.
    [Fact]
    public void Report_InTheStandaloneCli_RefusesWithThePreciseMessage()
    {
        var (code, output) = Capture(() => Program.Run(new[] { "report" }));

        Assert.Equal(2, code);
        Assert.Contains("brisk-app.exe report", output);
    }
```

Append to `src/Brisk.Tests/OverviewViewModelTests.cs` (fixture as in the existing revelation tests; the new ctor parameter is trailing and optional, so `Build()` keeps compiling — add an overload hook):

```csharp
    [Fact]
    public async Task SaveReport_RendersTheCardAndAnnouncesThePath()
    {
        var rendered = new List<(ReportCardModel Model, string Path)>();
        var (vm, host, state) = Build(renderReport: (m, p) => rendered.Add((m, p)));
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("zz-fake", cat: RuleCategory.Advise, canFix: false,
                headline: new Headline("57 s", "cap",
                    "rule.zz-fake.headline.value", new[] { "57" },
                    "rule.zz-fake.headline.caption", Array.Empty<string>())),
        }, new SensorStatus(true, true, null));
        await state.ScanAsync();

        vm.SaveReportCommand.Execute(null);

        var (model, path) = Assert.Single(rendered);
        Assert.Equal("57 s", model.Findings[0].Lead);
        Assert.EndsWith(".png", path);
        Assert.Contains(path, vm.ReportSavedText);
    }

    [Fact]
    public void SaveReport_WithoutASnapshot_CannotExecute()
    {
        var (vm, _, _) = Build();
        Assert.False(vm.SaveReportCommand.CanExecute(null));
    }
```

and extend the file's `Build`/`BuildWithBin` helpers with a trailing
`Action<ReportCardModel, string>? renderReport = null` parameter passed into
the `OverviewViewModel` constructor.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test brisk.sln -c Release --nologo --filter "Report_IsAKnownVerb|Report_InTheStandaloneCli|SaveReport"`
Expected: parser test FAILS (`error` verb); the others fail to build.

- [ ] **Step 3: Implement the CLI side**

`CliParser.cs` — add `"report"` to the `Verbs` set.

`Brisk.Cli/Program.cs` — in `Run`'s verb switch, before the default arm:

```csharp
                "report" => Refuse(),
```

and add beside `PrintHelp`:

```csharp
    /// The card needs the visual engine, which ships only in brisk-app.exe.
    /// The verb is recognized so the refusal can be precise — an
    /// unknown-command error would lie about why.
    private static int Refuse()
    {
        Console.Error.WriteLine(
            "brisk: the report card needs the visual engine that ships in "
            + "brisk-app.exe — run: brisk-app.exe report");
        return 2;
    }
```

Also add one line to `PrintHelp`'s command list:

```
  report                     save the scan as a shareable PNG (brisk-app.exe only)
```

- [ ] **Step 4: Implement the merged executable's console path**

Create `src/Brisk/Services/ReportRunner.cs`.

> **Superseded.** See the shipped file. Two differences with bugs behind them:
> `DefaultPath()` stamps `yyyyMMdd-HHmmss`, not the draft's `HHmm` — the
> renderer writes with `File.Create` (FileShare.None), so two saves inside one
> minute aimed at the same filename and the second failed on a sharing
> violation, which a double-click on the button was enough to cause; and the
> whole dispatch is wrapped so an unwritable `--out` prints one sentence,
> the way every other brisk failure does, instead of a stack trace.

`src/Brisk/Program.cs` — inside the `RoutesToConsole` branch, after `ParentConsole.Adopt()`:

```csharp
            // The report verb needs WPF, which only this executable carries,
            // and Brisk.Cli cannot reference this project — so it is answered
            // here, before the console entry point sees the arguments.
            if (args.Length > 0 && args[0] == "report")
                return Services.ReportRunner.Run(args);
```

- [ ] **Step 5: Implement the Overview button**

`OverviewViewModel`: add a trailing optional ctor parameter for the renderer,
held in a field and defaulted to `RenderAndCopy` — the seam a test comes in
through. (It shipped as `Func<ReportCardModel, string, bool>?`, not the
`Action` the draft named; see the note below for why.)

With, beside the other members, `_reportSavedText` / `ReportSavedText`,
`SaveReportCommand`, and the default `RenderAndCopy`.

> **Superseded.** See `src/Brisk/ViewModels/OverviewViewModel.cs`. The draft's
> `RenderAndCopy` returned `void`, so the page could not tell a clipboard copy
> that happened from one that did not — which is what made the saved line
> above untrue; and its `BitmapImage` took the `OnDemand` default, holding the
> PNG open until a GC got round to it, so the next Save met a sharing
> violation on a handle nothing was using. The shipped one returns `bool` and
> loads the bitmap `OnLoad`, frozen.

ctor, beside the other commands:

```csharp
        SaveReportCommand = new RelayCommand(SaveReport, () => HasSnapshot);
```

and the handler.

> **Superseded.** See `SaveReport` in
> `src/Brisk/ViewModels/OverviewViewModel.cs`. The draft had no `try` at all,
> and the one added during implementation started after the model was built —
> so a corrupt `fix-journal.jsonl`, which throws in the journal read that
> builds the model, went straight past the catch and out of a `RelayCommand`
> as an unhandled dialog. The shipped version covers the path, the model and
> the render, and answers all three with the sentence the console verb gives.

In `Refresh()`, beside the existing `FixAllCommand.RaiseCanExecuteChanged();` add
`SaveReportCommand.RaiseCanExecuteChanged();`.

resx additions: `overview.report.card`, plus the lines the button's three
outcomes need.

> **Superseded.** The draft had one saved line — `Saved: {0} (copied to the
> clipboard)` — printed whether or not the clipboard copy had happened,
> including in the exact failure its own catch exists to absorb. The shipped
> set is four keys: `overview.report.card`, `.saved`, `.saved.fileonly` and
> `.failed`, in both `Strings.resx` and `Strings.tr.resx`.

`OverviewPage.xaml` — in the action-buttons `StackPanel` (the one holding the Scan button), append after the last button:

```xml
                <Button Margin="8,0,0,0" Command="{Binding SaveReportCommand}"
                        Style="{StaticResource GhostButton}"
                        Content="{Binding [overview.report.card], Source={x:Static loc:Loc.Instance}}" />
```

and directly under that StackPanel, a quiet confirmation line:

```xml
            <TextBlock Margin="0,8,0,0" FontSize="12" TextWrapping="Wrap"
                       Foreground="{DynamicResource TextMuted}"
                       Text="{Binding ReportSavedText}">
                <TextBlock.Style>
                    <Style TargetType="TextBlock">
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding ReportSavedText}" Value="">
                                <Setter Property="Visibility" Value="Collapsed" />
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </TextBlock.Style>
            </TextBlock>
```

- [ ] **Step 6: Run the full suite**

Run: `dotnet test brisk.sln -c Release --nologo`
Expected: all green, 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add src/Brisk/Services/ReportRunner.cs src/Brisk/Program.cs src/Brisk.Cli/CliParser.cs src/Brisk.Cli/Program.cs src/Brisk/ViewModels/OverviewViewModel.cs src/Brisk/Views/OverviewPage.xaml src/Brisk/Localization/Strings.resx src/Brisk/Localization/Strings.tr.resx src/BriskEngine.Tests/CliParserTests.cs src/BriskEngine.Tests/CliHelpSwitchTests.cs src/Brisk.Tests/OverviewViewModelTests.cs
git commit  # message: one verb, two faces, and an honest refusal from the executable that cannot draw
```

---

### Task 5: the finding workbench

**Files:**
- Create: `tools/workbench/README.md`
- Create: `tools/workbench/verify.ps1`
- Create: `tools/workbench/plant-power-plan.ps1`, `restore-power-plan.ps1`
- Create: `tools/workbench/plant-search-web.ps1`, `restore-search-web.ps1`
- Create: `tools/workbench/plant-storage-sense.ps1`, `restore-storage-sense.ps1`
- Create: `tools/workbench/plant-visual-effects.ps1`, `restore-visual-effects.ps1`
- Create: `tools/workbench/plant-startup-bloat.ps1`, `restore-startup-bloat.ps1`
- Create: `tools/workbench/plant-display-refresh.ps1`, `restore-display-refresh.ps1`
- Modify: `.gitignore` (add `tools/workbench/.state/`)

**Interfaces:**
- Consumes: `brisk.exe scan --json` (findings carry PascalCase `RuleId`); the rules' exact registry surfaces (verified against the code): power scheme via `powercfg`; `HKCU\Software\Policies\Microsoft\Windows\Explorer` / `DisableSearchBoxSuggestions` + `HKCU\Software\Microsoft\Windows\CurrentVersion\Search` / `BingSearchEnabled`; `HKCU\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy` / `01`; `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects` / `VisualFXSetting`; `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (startup-bloat threshold: 6 entries).
- Produces: the documented plant → verify → restore → verify-clean loop.

- [ ] **Step 1: Shared pieces**

Append to `.gitignore`:

```
# Workbench state files record the machine's true pre-plant state — local only.
tools/workbench/.state/
```

Create `tools/workbench/README.md` and `tools/workbench/verify.ps1`.

> **Bodies removed.** Read the shipped `tools/workbench/README.md` — it
> documents the plant → verify → restore → verify-clean loop, the `.state/`
> contract, and what each scenario actually changes — and
> `tools/workbench/verify.ps1`, which runs `brisk scan --json` and answers the
> one question of whether a given rule fired. The draft README described the
> startup scenario as adding "six inert values"; they are real `Run` entries
> that run at the next logon.

- [ ] **Step 2: The five registry/power scenarios**

Each pair follows one pattern: state directory `$PSScriptRoot\.state`, a
JSON state file named after the scenario, refuse-on-existing-state, record
exact prior values (including "absent"), plant, and restore puts back
exactly what was recorded (deleting values that were absent).

Ten scripts, one per direction:

| Scenario | Plants | Restores |
|---|---|---|
| power-plan | `plant-power-plan.ps1` | `restore-power-plan.ps1` |
| search-web | `plant-search-web.ps1` | `restore-search-web.ps1` |
| storage-sense | `plant-storage-sense.ps1` | `restore-storage-sense.ps1` |
| visual-effects | `plant-visual-effects.ps1` | `restore-visual-effects.ps1` |
| startup-bloat | `plant-startup-bloat.ps1` | `restore-startup-bloat.ps1` |

> **The drafts that stood here have been removed, and must not be
> reconstructed from this document.** Read `tools/workbench/` instead — those
> are the reviewed, shipped scripts, and they differ from the drafts in ways
> that matter on somebody's real machine:
>
> - the active power scheme is read without regex-scraping `powercfg`'s
>   localized output. The draft's `-replace` failed open: on a non-English
>   Windows the pattern does not match, `-replace` returns the whole line, and
>   that line went into the state file as the "GUID" to restore;
> - the power-plan restore refuses a state file that does not hold a scheme
>   GUID, and keeps the file when `powercfg` rejects the switch. The draft
>   deleted it unconditionally, so a restore `powercfg` had refused left no
>   record of the original scheme at all — a genuine no-path-back recipe;
> - `New-Item -Path <registryKey> -Force` never runs against a key that is
>   already there. On this provider `-Force` REPLACES the key, and the drafts
>   used it unconditionally at three sites, where it would have taken
>   `Explorer\VisualEffects`'s nineteen subkeys, Storage Sense's sibling
>   values, or a GPO-managed Explorer policy key with it. Three shipped scripts
>   still reach for it, because a genuinely absent key has to be created
>   somehow — but each one first checks that the key is absent, and the plants
>   record which keys they invented so the restore can remove those, and only
>   those, and only while they are still as empty as the plant left them;
> - the four registry restores read the value back and compare it to the record
>   before they say "put back exactly", through a read that tells absent from
>   unreadable and keeps the state file when it cannot see. The other two have
>   nothing to read back that way and do not pretend otherwise:
>   `restore-power-plan.ps1` checks `powercfg`'s exit code — a native
>   executable is outside `$ErrorActionPreference` — and
>   `restore-display-refresh.ps1` checks the `DISP_CHANGE` code; both keep the
>   state file when the call was refused. Each script's own comment says what
>   it actually verifies, and that is the phrasing to trust;
> - `restore-startup-bloat.ps1` removes only values it can confirm are the ones
>   this workbench planted, rather than any name that turns up in a state file;
> - and the planted `HKCU\Run` values are not "six inert values", as the
>   draft called them. They are real startup entries that execute at the next
>   logon — harmless by construction, and the shipped scripts say what they are
>   rather than what would be reassuring.

- [ ] **Step 3: The interactive display scenario**

`plant-display-refresh.ps1` — interactive by design; it must (1) print what
it is about to do and require the user to type `evet` (or `yes`), (2) use
`Add-Type` P/Invoke over `EnumDisplaySettings`/`ChangeDisplaySettings` on
the primary display, (3) record the current refresh rate to
`.state\display-refresh.json`, (4) enumerate the modes matching the current
width×height×bpp and pick the highest frequency at least 10 Hz below the
current one (the rule's minimum gap), failing with a clear message when no
such mode exists, and (5) apply it with `ChangeDisplaySettings` and report
the change. `restore-display-refresh.ps1` reapplies the recorded frequency
through the same P/Invoke and deletes the state file. brisk's own
`display-refresh` fix (with its 15-second auto-revert) is the safety net if
anything goes visibly wrong.

- [ ] **Step 4: Validate the loop on this machine — registry scenarios only**

For each of `storage-sense`, `visual-effects`, `search-web`,
`startup-bloat`, `power-plan` (in that order), run:
`plant` → `verify.ps1 <rule-id>` → `restore` → `verify.ps1 <rule-id> -ExpectClean`,
using the tree's own `artifacts\brisk.exe` (run `scripts/publish.ps1` first
if `artifacts/` is stale — the workbench must test the code on this branch).
Every loop must end OK/OK with the state directory empty.
**Do NOT run the display-refresh scenario** — it visibly changes the screen
and is reserved for the maintainer's own session.
Caveat: `verify.ps1 power-plan -ExpectClean` passes only if the machine's
original scheme is not itself Balanced/Power saver; if it is, the honest
result is that the rule fires before AND after — record that outcome in the
report instead of forcing a green.

- [ ] **Step 5: Commit**

```bash
git add tools/workbench/ .gitignore
git commit  # message: the defects are planted on camera, and the scripts that plant them are public
```

---

### Task 6: version 0.3.0 and the final sweep

**Files:**
- Modify: `src/BriskEngine/EngineInfo.cs`

- [ ] **Step 1: Bump**

```csharp
    public const string Version = "0.3.0";
```

- [ ] **Step 2: Verify**

Run: `dotnet test brisk.sln -c Release --nologo` — all green, 0 warnings; record the count.
Run: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1` — both exes build.
Run: `.\artifacts\brisk.exe version` — prints `0.3.0`.
Run: `.\artifacts\brisk.exe report` — exits 2 and names `brisk-app.exe report`.
Do NOT create any tag.

- [ ] **Step 3: Commit**

```bash
git add src/BriskEngine/EngineInfo.cs
git commit  # message: 0.3.0 — the wave where the scan becomes an image
```

---

## Self-review notes

- Spec coverage: card layout/sections/empty-states (T2+T3), privacy ban test-enforced (T2), dark cockpit inherited via merged dictionaries (T3), GUI button with clipboard best-effort + saved path (T4), `report` verb intercepted pre-parser with honest standalone refusal (T4), workbench with double-plant refusal + documented loop + interactive display scenario (T5), 0.3.0 (T6).
- The `SensorNotice` variant logic is intentionally mirrored (CLI prose vs card resx) rather than extracted — the CLI's English strings and the card's localized lines share a 4-arm switch, and an extraction would touch the CLI for no behavioral gain. Reviewers may flag the duplication; that is the recorded trade.
- The clipboard call is untested by design (GUI-only side effect, guarded); the injected renderer keeps the command's logic fully tested.
