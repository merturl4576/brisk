# Report Card & Finding Workbench Implementation Plan

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

and extend the record with an optional trailing parameter:

```csharp
public sealed record ScanSnapshot(
    IReadOnlyList<DiagnosticFinding> Findings,
    ScanResult Cleaner,
    int Health,
    DateTime CompletedUtc,
    SensorStatus? Sensors = null);
```

In `EngineHost.ScanAsync`, change the return expression to fill it:

```csharp
        return new ScanSnapshot(findings, cleaner,
            HealthScore.Compute(findings), DateTime.UtcNow,
            new SensorStatus(
                CpuRead: _ctx.Sensors.CpuTempC() is { } c && double.IsFinite(c),
                GpuRead: _ctx.Sensors.GpuTempC() is { } g && double.IsFinite(g),
                MemoryIntegrityOn: _ctx.MemoryIntegrity.IsOn()));
```

In `Fakes.cs`, extend `TestData.Snapshot`:

```csharp
    public static ScanSnapshot Snapshot(IReadOnlyList<DiagnosticFinding>? findings = null,
        params TargetScanResult[] targets) => Snapshot(findings, null, targets);

    public static ScanSnapshot Snapshot(IReadOnlyList<DiagnosticFinding>? findings,
        SensorStatus? sensors, params TargetScanResult[] targets) => new(
        findings ?? Array.Empty<DiagnosticFinding>(),
        new ScanResult(targets), 72, new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc),
        sensors);
```

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

Create `src/Brisk/ViewModels/ReportCardModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Brisk.Localization;
using Brisk.Services;
using BriskEngine;
using BriskEngine.Diagnostics;
using BriskEngine.Models;

namespace Brisk.ViewModels;

public sealed record CardLine(string Lead, string Text);

/// Everything the report card says, as plain strings, built once per render.
/// The privacy rule is structural: this model reads headlines and titles and
/// nothing else a finding carries — evidence, fix descriptions, and every
/// engine-authored sentence that could name a program, a path, or the
/// machine simply have no route in. A test pins the ban on the output.
public sealed class ReportCardModel
{
    public required string DateText { get; init; }
    public required string VersionText { get; init; }
    public required int Health { get; init; }
    public required IReadOnlyList<CardLine> Findings { get; init; }
    public required string FindingsEmptyText { get; init; }
    public required IReadOnlyList<string> Unread { get; init; }
    public required IReadOnlyList<string> Fixes { get; init; }
    public bool HasFixes => Fixes.Count > 0;
    public string RepoLine => "github.com/merturl4576/brisk";

    public static ReportCardModel Build(ScanSnapshot snapshot,
        IReadOnlyList<UndoableFix> undoable, Loc loc)
    {
        var picked = RevelationPicker.Pick(snapshot.Findings);
        var findings = picked
            .Select(f => new CardLine(
                LocalizedText.Headline(f.Headline!, loc).Value,
                loc.Title(f.TitleKey, f.Title)))
            .ToList();

        // Old snapshots (and bare test fixtures) may predate SensorStatus;
        // "both answered" is the only reading that adds no claim.
        var sensors = snapshot.Sensors ?? new SensorStatus(true, true, null);

        return new ReportCardModel
        {
            DateText = snapshot.CompletedUtc.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            VersionText = EngineInfo.Version,
            Health = snapshot.Health,
            Findings = findings,
            FindingsEmptyText = findings.Count > 0 ? "" :
                loc.F("overview.revelation.none", DiagnosticRuleRegistry.All.Count),
            Unread = new[] { UnreadLine(sensors, loc) },
            Fixes = undoable
                .OrderByDescending(f => f.FixedAtUtc)
                .Select(f => loc.Title($"rule.{f.RuleId}.title", f.RuleId)
                    + " · " + f.FixedAtUtc.ToLocalTime()
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .ToList(),
        };
    }

    /// One line, always. The variant logic mirrors the CLI's SensorNotice —
    /// the same three-state honesty about memory integrity, in resx form.
    private static string UnreadLine(SensorStatus sensors, Loc loc) =>
        (sensors.CpuRead, sensors.GpuRead) switch
        {
            (true, true) => loc["report.unread.none"],
            (true, false) => loc["report.unread.gpu"],
            (false, _) when !sensors.GpuRead => loc["report.unread.neither"],
            (false, _) => sensors.MemoryIntegrityOn switch
            {
                true => loc["report.unread.cpu.integrity-on"],
                false => loc["report.unread.cpu.integrity-off"],
                null => loc["report.unread.cpu"],
            },
        };
}
```

Add to `Strings.resx` (a new `report.*` block, beside the `overview.*` keys):

```xml
  <data name="report.section.findings" xml:space="preserve"><value>Findings</value></data>
  <data name="report.section.unread" xml:space="preserve"><value>What brisk could not read</value></data>
  <data name="report.section.fixes" xml:space="preserve"><value>Applied fixes</value></data>
  <data name="report.unread.none" xml:space="preserve"><value>Everything brisk tried to read, answered.</value></data>
  <data name="report.unread.gpu" xml:space="preserve"><value>GPU temperature — not read; brisk cannot tell from here why.</value></data>
  <data name="report.unread.neither" xml:space="preserve"><value>Temperatures — neither sensor answered.</value></data>
  <data name="report.unread.cpu" xml:space="preserve"><value>CPU temperature — not read. brisk could not confirm the reason on this machine.</value></data>
  <data name="report.unread.cpu.integrity-on" xml:space="preserve"><value>CPU temperature — not read. Memory integrity is on; the driver that reads it is on Microsoft's vulnerable-driver blocklist.</value></data>
  <data name="report.unread.cpu.integrity-off" xml:space="preserve"><value>CPU temperature — not read. Memory integrity is off here, so the usual reason is ruled out; brisk cannot tell what did it.</value></data>
```

Add to `Strings.tr.resx` at the same position:

```xml
  <data name="report.section.findings" xml:space="preserve"><value>Bulgular</value></data>
  <data name="report.section.unread" xml:space="preserve"><value>brisk'in okuyamadıkları</value></data>
  <data name="report.section.fixes" xml:space="preserve"><value>Uygulanan düzeltmeler</value></data>
  <data name="report.unread.none" xml:space="preserve"><value>brisk'in okumaya çalıştığı her şey cevap verdi.</value></data>
  <data name="report.unread.gpu" xml:space="preserve"><value>GPU sıcaklığı — okunamadı; brisk sebebini buradan söyleyemez.</value></data>
  <data name="report.unread.neither" xml:space="preserve"><value>Sıcaklıklar — iki sensör de cevap vermedi.</value></data>
  <data name="report.unread.cpu" xml:space="preserve"><value>CPU sıcaklığı — okunamadı. brisk sebebini bu makinede doğrulayamadı.</value></data>
  <data name="report.unread.cpu.integrity-on" xml:space="preserve"><value>CPU sıcaklığı — okunamadı. Bellek bütünlüğü açık; onu okuyan sürücü Microsoft'un güvenlik açığı listesinde.</value></data>
  <data name="report.unread.cpu.integrity-off" xml:space="preserve"><value>CPU sıcaklığı — okunamadı. Bellek bütünlüğü kapalı, yani bilinen sebep bu değil; brisk sebebini buradan söyleyemez.</value></data>
```

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

Create `src/Brisk/Views/ReportCard.xaml`:

```xml
<UserControl x:Class="Brisk.Views.ReportCard"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:loc="clr-namespace:Brisk.Localization"
             xmlns:Brisk="clr-namespace:Brisk.Views"
             Width="1600" Height="900">
    <!-- Rendered offscreen, possibly with no Application object alive (the
         console face). The dictionaries are merged HERE so StaticResource
         and DynamicResource both resolve from the control's own tree. The
         card is dark by decision, so Dark.xaml is not a theme choice — it
         is the card. -->
    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="pack://application:,,,/Theming/Dark.xaml" />
                <ResourceDictionary Source="pack://application:,,,/Theming/Shared.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </UserControl.Resources>
    <Border Background="{StaticResource HeroSurface}" Padding="48,40">
        <DockPanel>
            <!-- Top strip: identity left, scan date + version right. -->
            <DockPanel DockPanel.Dock="Top" Margin="0,0,0,26">
                <TextBlock Text="brisk" FontSize="30" FontWeight="SemiBold"
                           FontFamily="Segoe UI Variable Display, Segoe UI"
                           Foreground="{StaticResource HeroText}" />
                <StackPanel HorizontalAlignment="Right" Orientation="Horizontal"
                            VerticalAlignment="Center">
                    <TextBlock FontSize="14" Foreground="{StaticResource HeroMuted}"
                               FontFamily="Cascadia Mono, Consolas"
                               Text="{Binding DateText}" />
                    <TextBlock FontSize="14" Foreground="{StaticResource HeroMuted}"
                               FontFamily="Cascadia Mono, Consolas" Margin="18,0,0,0"
                               Text="{Binding VersionText}" />
                </StackPanel>
            </DockPanel>
            <!-- Bottom strip: the one quiet line. -->
            <TextBlock DockPanel.Dock="Bottom" Margin="0,24,0,0" FontSize="13"
                       Foreground="{StaticResource HeroMuted}"
                       FontFamily="Cascadia Mono, Consolas"
                       Text="{Binding RepoLine}" />
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="300" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <!-- Left: the health gauge, static. -->
                <Grid Grid.Column="0" Width="240" Height="240" VerticalAlignment="Top">
                    <Brisk:SegmentedGauge Width="240" Height="240" Layer="Unlit"
                                          UnlitBrush="{StaticResource HeroUnlit}"
                                          Score="{Binding Health}" />
                    <Brisk:SegmentedGauge Width="240" Height="240" Layer="Lit"
                                          LitBrush="{StaticResource HeroGood}"
                                          GlowColor="{StaticResource HeroGoodColor}"
                                          Score="{Binding Health}" />
                    <TextBlock Style="{StaticResource HeroScore}" FontSize="52"
                               HorizontalAlignment="Center" VerticalAlignment="Center"
                               Text="{Binding Health}" />
                </Grid>
                <!-- Right: the three sections. -->
                <StackPanel Grid.Column="1" Margin="36,0,0,0">
                    <TextBlock FontSize="13" Typography.Capitals="AllSmallCaps"
                               Foreground="{StaticResource HeroMuted}"
                               Text="{Binding [report.section.findings], Source={x:Static loc:Loc.Instance}}" />
                    <ItemsControl Margin="0,8,0,0" ItemsSource="{Binding Findings}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <DockPanel Margin="0,0,0,9">
                                    <TextBlock DockPanel.Dock="Left" Width="170"
                                               FontSize="24" FontWeight="SemiBold"
                                               FontFamily="Segoe UI Variable Display, Segoe UI"
                                               Foreground="{StaticResource HeroText}"
                                               Text="{Binding Lead}" />
                                    <TextBlock FontSize="16" VerticalAlignment="Center"
                                               TextWrapping="Wrap"
                                               Foreground="{StaticResource HeroText}"
                                               Text="{Binding Text}" />
                                </DockPanel>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <TextBlock FontSize="15" TextWrapping="Wrap"
                               Foreground="{StaticResource HeroMuted}"
                               Text="{Binding FindingsEmptyText}">
                        <TextBlock.Style>
                            <Style TargetType="TextBlock">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding FindingsEmptyText}" Value="">
                                        <Setter Property="Visibility" Value="Collapsed" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </TextBlock.Style>
                    </TextBlock>
                    <TextBlock Margin="0,22,0,0" FontSize="13"
                               Typography.Capitals="AllSmallCaps"
                               Foreground="{StaticResource HeroMuted}"
                               Text="{Binding [report.section.unread], Source={x:Static loc:Loc.Instance}}" />
                    <ItemsControl Margin="0,8,0,0" ItemsSource="{Binding Unread}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <TextBlock FontSize="15" TextWrapping="Wrap" Margin="0,0,0,4"
                                           Foreground="{StaticResource HeroText}"
                                           Text="{Binding}" />
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <StackPanel Margin="0,22,0,0"
                                Visibility="{Binding HasFixes,
                                    Converter={x:Static Brisk:BoolToVis.Instance}}">
                        <TextBlock FontSize="13" Typography.Capitals="AllSmallCaps"
                                   Foreground="{StaticResource HeroMuted}"
                                   Text="{Binding [report.section.fixes], Source={x:Static loc:Loc.Instance}}" />
                        <ItemsControl Margin="0,8,0,0" ItemsSource="{Binding Fixes}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock FontSize="15" Margin="0,0,0,4"
                                               Foreground="{StaticResource HeroText}"
                                               Text="{Binding}" />
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>
                </StackPanel>
            </Grid>
        </DockPanel>
    </Border>
</UserControl>
```

Create `src/Brisk/Views/ReportCard.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace Brisk.Views;

public partial class ReportCard : UserControl
{
    public ReportCard() { InitializeComponent(); }
}
```

Create `src/Brisk/Services/ReportCardRenderer.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brisk.ViewModels;
using Brisk.Views;

namespace Brisk.Services;

/// The card at 1600×900, written as a PNG at 2× (3200×1800, 192 DPI) so it
/// survives every platform's recompression. WPF is the renderer — the
/// cockpit look is inherited from the shared dictionaries, not imitated.
public static class ReportCardRenderer
{
    public const int Width = 1600;
    public const int Height = 900;

    public static void RenderToFile(ReportCardModel model, string path)
    {
        var card = new ReportCard { DataContext = model };
        card.Measure(new Size(Width, Height));
        card.Arrange(new Rect(0, 0, Width, Height));
        card.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            Width * 2, Height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(card);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// WPF objects demand an STA thread; the console face and the test
    /// runner do not have one. The GUI calls RenderToFile directly on the
    /// dispatcher; everyone else comes through here.
    public static void RenderOnStaThread(ReportCardModel model, string path)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RenderToFile(model, path); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
```

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

Create `src/Brisk/Services/ReportRunner.cs`:

```csharp
using System;
using System.IO;
using Brisk.Localization;
using Brisk.ViewModels;

namespace Brisk.Services;

/// The console face of the report card: scan, build the model, render, print
/// the path. Lives in the Brisk project because WPF does — Brisk.Cli cannot
/// reference this assembly, so brisk-app.exe's entry point routes the verb
/// here before the CLI parser ever sees it.
public static class ReportRunner
{
    public static int Run(string[] args)
    {
        string? outPath = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length) { outPath = args[++i]; }
            else
            {
                Console.Error.WriteLine($"brisk: bad argument '{args[i]}'");
                return 2;
            }
        }

        var composition = AppServices.Build();
        Loc.Instance.SetLanguage(composition.Settings.Language);
        var snapshot = composition.Host.ScanAsync().GetAwaiter().GetResult();
        var model = ReportCardModel.Build(
            snapshot, composition.Host.ListUndoable(), Loc.Instance);
        var path = outPath ?? DefaultPath();
        ReportCardRenderer.RenderOnStaThread(model, path);
        Console.WriteLine(path);
        return 0;
    }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "brisk",
        $"brisk-report-{DateTime.Now:yyyyMMdd-HHmm}.png");
}
```

`src/Brisk/Program.cs` — inside the `RoutesToConsole` branch, after `ParentConsole.Adopt()`:

```csharp
            // The report verb needs WPF, which only this executable carries,
            // and Brisk.Cli cannot reference this project — so it is answered
            // here, before the console entry point sees the arguments.
            if (args.Length > 0 && args[0] == "report")
                return Services.ReportRunner.Run(args);
```

- [ ] **Step 5: Implement the Overview button**

`OverviewViewModel`: add a trailing optional ctor parameter
`Action<ReportCardModel, string>? renderReport = null`, stored as:

```csharp
    private readonly Action<ReportCardModel, string> _renderReport;
```

initialized in the ctor:

```csharp
        _renderReport = renderReport ?? RenderAndCopy;
```

with, beside the other members:

```csharp
    private string _reportSavedText = "";
    public string ReportSavedText { get => _reportSavedText; private set => Set(ref _reportSavedText, value); }
    public RelayCommand SaveReportCommand { get; }

    /// The default surface behavior: write the PNG, then best-effort copy to
    /// the clipboard — a locked clipboard must not turn a saved card into an
    /// error.
    private static void RenderAndCopy(ReportCardModel model, string path)
    {
        ReportCardRenderer.RenderToFile(model, path);
        try
        {
            System.Windows.Clipboard.SetImage(
                new System.Windows.Media.Imaging.BitmapImage(new Uri(path)));
        }
        catch (Exception) { /* the file on disk is the deliverable */ }
    }
```

ctor, beside the other commands:

```csharp
        SaveReportCommand = new RelayCommand(SaveReport, () => HasSnapshot);
```

and the handler:

```csharp
    private void SaveReport()
    {
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        var path = ReportRunner.DefaultPath();
        _renderReport(ReportCardModel.Build(snapshot, _host.ListUndoable(), _loc), path);
        ReportSavedText = _loc.F("overview.report.card.saved", path);
    }
```

In `Refresh()`, beside the existing `FixAllCommand.RaiseCanExecuteChanged();` add
`SaveReportCommand.RaiseCanExecuteChanged();`.

resx additions — `Strings.resx`:

```xml
  <data name="overview.report.card" xml:space="preserve"><value>Save report card</value></data>
  <data name="overview.report.card.saved" xml:space="preserve"><value>Saved: {0} (copied to the clipboard)</value></data>
```

`Strings.tr.resx`:

```xml
  <data name="overview.report.card" xml:space="preserve"><value>Rapor kartını kaydet</value></data>
  <data name="overview.report.card.saved" xml:space="preserve"><value>Kaydedildi: {0} (panoya kopyalandı)</value></data>
```

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

Create `tools/workbench/README.md`:

```markdown
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
```

Create `tools/workbench/verify.ps1`:

```powershell
# Runs brisk and answers one question: did the expected rule fire?
param(
    [Parameter(Mandatory)] [string] $RuleId,
    [switch] $ExpectClean,
    [string] $BriskExe = (Join-Path $PSScriptRoot '..\..\artifacts\brisk.exe')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $BriskExe)) { throw "brisk exe not found: $BriskExe (pass -BriskExe)" }
$json = & $BriskExe scan --json | ConvertFrom-Json
$fired = @($json.findings | Where-Object { $_.RuleId -eq $RuleId }).Count -gt 0
if ($fired -and -not $ExpectClean) { Write-Host "OK: $RuleId fired."; exit 0 }
if (-not $fired -and $ExpectClean) { Write-Host "OK: $RuleId is clean."; exit 0 }
if ($fired) { Write-Host "FAIL: $RuleId still fires."; exit 1 }
Write-Host "FAIL: $RuleId did not fire."; exit 1
```

- [ ] **Step 2: The five registry/power scenarios**

Each pair follows one pattern: state directory `$PSScriptRoot\.state`, a
JSON state file named after the scenario, refuse-on-existing-state, record
exact prior values (including "absent"), plant, and restore puts back
exactly what was recorded (deleting values that were absent). Write them as
follows.

`plant-power-plan.ps1`:

```powershell
# power-plan fires when the active scheme is Balanced or Power saver.
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\power-plan.json'
if (Test-Path $state) { throw 'state file exists - restore first (double plant would lose the true original)' }
New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
$active = (powercfg /getactivescheme) -replace '^.*GUID:\s*([0-9a-f-]+).*$', '$1'
@{ scheme = $active.Trim() } | ConvertTo-Json | Set-Content $state -Encoding ascii
powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e
Write-Host "planted: active scheme -> Balanced (was $($active.Trim()))"
```

`restore-power-plan.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\power-plan.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$prior = (Get-Content $state -Raw | ConvertFrom-Json).scheme
powercfg /setactive $prior
Remove-Item $state
Write-Host "restored: active scheme -> $prior"
```

`plant-search-web.ps1` (the rule fires when the policy value is ABSENT and
the legacy value is not 0):

```powershell
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\search-web.json'
if (Test-Path $state) { throw 'state file exists - restore first (double plant would lose the true original)' }
New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
$policyKey = 'HKCU:\Software\Policies\Microsoft\Windows\Explorer'
$legacyKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search'
$policy = (Get-ItemProperty $policyKey -Name DisableSearchBoxSuggestions -ErrorAction SilentlyContinue).DisableSearchBoxSuggestions
$legacy = (Get-ItemProperty $legacyKey -Name BingSearchEnabled -ErrorAction SilentlyContinue).BingSearchEnabled
@{ policy = $policy; legacy = $legacy } | ConvertTo-Json | Set-Content $state -Encoding ascii
if ($null -ne $policy) { Remove-ItemProperty $policyKey -Name DisableSearchBoxSuggestions }
if ($legacy -eq 0)     { Set-ItemProperty $legacyKey -Name BingSearchEnabled -Value 1 -Type DWord }
Write-Host 'planted: Start-menu web search re-enabled'
```

`restore-search-web.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\search-web.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$prior = Get-Content $state -Raw | ConvertFrom-Json
$policyKey = 'HKCU:\Software\Policies\Microsoft\Windows\Explorer'
$legacyKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search'
if ($null -ne $prior.policy) {
    New-Item -Path $policyKey -Force | Out-Null
    Set-ItemProperty $policyKey -Name DisableSearchBoxSuggestions -Value $prior.policy -Type DWord
} else {
    Remove-ItemProperty $policyKey -Name DisableSearchBoxSuggestions -ErrorAction SilentlyContinue
}
if ($null -ne $prior.legacy) {
    Set-ItemProperty $legacyKey -Name BingSearchEnabled -Value $prior.legacy -Type DWord
} else {
    Remove-ItemProperty $legacyKey -Name BingSearchEnabled -ErrorAction SilentlyContinue
}
Remove-Item $state
Write-Host 'restored: search-web values put back exactly'
```

`plant-storage-sense.ps1`:

```powershell
# storage-sense fires when the master toggle (value '01') is not 1.
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\storage-sense.json'
if (Test-Path $state) { throw 'state file exists - restore first (double plant would lose the true original)' }
New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy'
$prior = (Get-ItemProperty $key -Name '01' -ErrorAction SilentlyContinue).'01'
@{ value = $prior } | ConvertTo-Json | Set-Content $state -Encoding ascii
New-Item -Path $key -Force | Out-Null
Set-ItemProperty $key -Name '01' -Value 0 -Type DWord
Write-Host "planted: Storage Sense off (was: $(if ($null -eq $prior) { 'absent' } else { $prior }))"
```

`restore-storage-sense.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\storage-sense.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$prior = (Get-Content $state -Raw | ConvertFrom-Json).value
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy'
if ($null -ne $prior) { Set-ItemProperty $key -Name '01' -Value $prior -Type DWord }
else { Remove-ItemProperty $key -Name '01' -ErrorAction SilentlyContinue }
Remove-Item $state
Write-Host 'restored: Storage Sense value put back exactly'
```

`plant-visual-effects.ps1`:

```powershell
# visual-effects fires when VisualFXSetting is 1 (best appearance).
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\visual-effects.json'
if (Test-Path $state) { throw 'state file exists - restore first (double plant would lose the true original)' }
New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects'
$prior = (Get-ItemProperty $key -Name VisualFXSetting -ErrorAction SilentlyContinue).VisualFXSetting
@{ value = $prior } | ConvertTo-Json | Set-Content $state -Encoding ascii
New-Item -Path $key -Force | Out-Null
Set-ItemProperty $key -Name VisualFXSetting -Value 1 -Type DWord
Write-Host "planted: visual effects -> best appearance (was: $(if ($null -eq $prior) { 'absent' } else { $prior }))"
```

`restore-visual-effects.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\visual-effects.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$prior = (Get-Content $state -Raw | ConvertFrom-Json).value
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects'
if ($null -ne $prior) { Set-ItemProperty $key -Name VisualFXSetting -Value $prior -Type DWord }
else { Remove-ItemProperty $key -Name VisualFXSetting -ErrorAction SilentlyContinue }
Remove-Item $state
Write-Host 'restored: VisualFXSetting put back exactly'
```

`plant-startup-bloat.ps1`:

```powershell
# startup-bloat fires at six or more startup entries; six inert values make
# the trigger self-sufficient on any machine.
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\startup-bloat.json'
if (Test-Path $state) { throw 'state file exists - restore first (double plant would lose the true original)' }
New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$names = 1..6 | ForEach-Object { "brisk-workbench-$_" }
foreach ($n in $names) {
    if ($null -ne (Get-ItemProperty $runKey -Name $n -ErrorAction SilentlyContinue).$n) {
        throw "value $n already exists - refusing to overwrite"
    }
}
@{ names = $names } | ConvertTo-Json | Set-Content $state -Encoding ascii
foreach ($n in $names) {
    Set-ItemProperty $runKey -Name $n -Value "$env:WINDIR\System32\cmd.exe /c rem brisk workbench" -Type String
}
Write-Host 'planted: six inert startup entries'
```

`restore-startup-bloat.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\startup-bloat.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$names = (Get-Content $state -Raw | ConvertFrom-Json).names
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
foreach ($n in $names) { Remove-ItemProperty $runKey -Name $n -ErrorAction SilentlyContinue }
Remove-Item $state
Write-Host 'restored: workbench startup entries removed'
```

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
