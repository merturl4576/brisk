# Faz 3 — Disclosure and the Telemetry Triple — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** brisk shows what Windows records about this machine, offers to turn the telemetry it can act on off reversibly, and — alone in its category — reads back whether what it turned off stayed off.

**Architecture:** No new architecture. Ten new rules behind the existing `IDiagnosticRule` contract, reading through the existing `IRegistryProbe` and `IFileProbe`; one new probe for Delivery Optimization. The read-back is a comparison between a rule's live `Detect` and the existing `FixJournal`, so no scheduler, no background service and no new store is introduced. A sixth page renders findings the pages already know how to render.

**Tech Stack:** C# / .NET 8, WPF, xUnit. No new NuGet dependency.

**Spec:** `docs/superpowers/specs/2026-08-25-faz3-disclosure-design.md` — read it first; it carries the four red lines and the reasoning behind the score decision.

## Global Constraints

Copied verbatim from the spec. Every task's requirements implicitly include these.

- **Every finding this wave produces is `FindingKind.Notice`, including the fixable ones.** The health score grades performance and hygiene; privacy is a second axis brisk shows and can act on but does not grade.
- **brisk never says "Microsoft can no longer see this."** The only sentence available is *"this setting currently reads as off; I last confirmed it on this date"*.
- **Numbers, never contents.** "47 USB devices" yes; device names never. "1,284 program records" yes; the program list never.
- **Policies that do not apply on this edition are said so** — written but ignored reads as ignored, never as protection.
- **What could not be read goes to "unreadable", never a zero.** A probe that throws or finds nothing reports unreadable.
- **No network call of any kind exists in this wave.** No speed test, not as an option.
- **`Strings.resx` and `Strings.tr.resx` must end this wave with identical key sets.** They hold 253 keys each today; `ResxFiles_ExposeTheSameKeySet` already guards this.
- **Version:** `EngineInfo.Version` ends at `0.6.0`. **No git tag. Nothing pushed.** Both are the maintainer's.
- **Two task groups on one branch:** Tasks 1-6 are read-only or reversible engine work; Tasks 7-10 are surface and closing.

## File Structure

**New engine files**

- `src/BriskEngine/Diagnostics/Rules/Privacy/TelemetrySwitchRule.cs` — the shared base for the six fixable privacy rules. One `Detect`/`Fix`/`Undo` shape over a list of registry values, because six near-identical rules copied six times is the duplication this codebase's review rubric rejects.
- `src/BriskEngine/Diagnostics/Rules/Privacy/AdvertisingIdRule.cs`, `DiagnosticLevelRule.cs`, `TailoredExperiencesRule.cs`, `SpeechTypingRule.cs`, `LocationRule.cs`, `ActivityHistoryRule.cs` — thin subclasses, one per switch.
- `src/BriskEngine/Diagnostics/Rules/Privacy/RecallStatusRule.cs` — report only.
- `src/BriskEngine/Diagnostics/Rules/Privacy/UsbHistoryRule.cs`, `RunHistoryRule.cs` — report only, counts.
- `src/BriskEngine/Diagnostics/Rules/Privacy/DeliveryOptimizationRule.cs` — report only.
- `src/BriskEngine/Diagnostics/IDeliveryOptimizationProbe.cs` + `RealProbes/RealDeliveryOptimizationProbe.cs`.

**New app files**

- `src/Brisk/ViewModels/PrivacyViewModel.cs`
- `src/Brisk/Views/PrivacyPage.xaml` + `.xaml.cs`

**Modified**

- `src/BriskEngine/Diagnostics/DiagnosticContext.cs` — one new probe member
- `src/BriskEngine/Diagnostics/DiagnosticRuleRegistry.cs` — register the ten rules
- `src/Brisk/ViewModels/FindingSections.cs` — `IsPrivacy`
- `src/Brisk/Windows/MainWindow.xaml` + `.xaml.cs` — sixth nav tile
- `src/Brisk/App.xaml.cs` — build and wire `PrivacyViewModel`
- `src/Brisk/Localization/Strings.resx` + `Strings.tr.resx`
- `src/BriskEngine/EngineInfo.cs` — 0.6.0

**New tests**

- `src/Brisk.Tests/PrivacyRuleTests.cs`, `src/Brisk.Tests/ReadBackTests.cs`, `src/Brisk.Tests/PrivacyRedLineTests.cs`, `src/Brisk.Tests/PrivacyViewModelTests.cs`
- `src/BriskEngine.Tests/` gains rule tests beside the existing ones for whichever project already holds rule tests — check where `SearchWebResultsRule` is tested and follow it.

---

### Task 1: The privacy section, and the guard that the score never moves

**Files:**
- Modify: `src/Brisk/ViewModels/FindingSections.cs`
- Create: `src/Brisk.Tests/PrivacyRedLineTests.cs`

**Interfaces:**
- Produces: `FindingSections.IsPrivacy(DiagnosticFinding)` and `FindingSections.IsPrivacy(string ruleId)`; `PrivacyRuleIds.All` — the single list every later task adds its rule id to.
- Consumes: nothing.

The privacy rule ids, in one place, because three files need the same list and three copies will drift:

```csharp
public static class PrivacyRuleIds
{
    public static readonly string[] All =
    {
        "advertising-id", "diagnostic-level", "tailored-experiences",
        "speech-typing", "location", "activity-history",
        "recall-status", "usb-history", "run-history",
        "delivery-optimization",
    };
}
```

- [ ] **Step 1: Add `PrivacyRuleIds` and `IsPrivacy` to `FindingSections.cs`**

Mirror the existing `Performance` set exactly: a `HashSet<string>` with `StringComparer.OrdinalIgnoreCase`, and both overloads (`DiagnosticFinding` and `string ruleId`) because the journal only carries ids.

- [ ] **Step 2: Health and Performance must stop claiming privacy findings**

`IsHealth` is currently `!IsPerformance`, which would sweep every privacy finding onto the Sağlık page. Change it to `!IsPerformance(f) && !IsPrivacy(f)` for both overloads. The doc comment says "Unknown future rules default to Sağlık" — update it to say privacy rules are the named exception, or the comment outruns the code.

- [ ] **Step 3: Write the score guard, and watch it fail**

Create `src/Brisk.Tests/PrivacyRedLineTests.cs`:

```csharp
    [Fact]
    public void NoPrivacyFinding_LowersTheHealthScore()
    {
        var withoutPrivacy = HealthScore.For(new[] { TestData.Finding("power-plan") });   // NOTE: no such method
        var withPrivacy = HealthScore.For(new[]
        {
            TestData.Finding("power-plan"),
            TestData.Finding("advertising-id", Severity.Warning,
                RuleCategory.Auto, kind: FindingKind.Notice),
        });

        Assert.Equal(withoutPrivacy, withPrivacy);
    }
```

Check `HealthScore`'s actual entry-point signature before writing this — `src/BriskEngine/Diagnostics/HealthScore.cs:21` reads `if (f.Kind == FindingKind.Notice) continue;`, so the method exists and skips notices; use its real name and parameters.

Run: `dotnet test src/Brisk.Tests/Brisk.Tests.csproj -c Release --nologo --filter PrivacyRedLineTests`
Expected: PASS immediately — this is a characterization test, and it must be proved load-bearing in the next step rather than trusted.

- [ ] **Step 4: Prove it is load-bearing**

Temporarily change the planted finding's `kind:` argument to `FindingKind.Problem` and re-run. It must FAIL. Restore `Notice`. Record the failure message in the report — a guard against a silent regression that was never watched fail is decoration, and this branch has shipped that mistake before.

- [ ] **Step 5: Commit**

```bash
git add src/Brisk/ViewModels/FindingSections.cs src/Brisk.Tests/PrivacyRedLineTests.cs
git commit
```

Body must state that `IsHealth` changed meaning, and that the guard was watched red by flipping the Kind.

---

### Task 2: The four switches with no visible consequence

**Files:**
- Create: `src/BriskEngine/Diagnostics/Rules/Privacy/TelemetrySwitchRule.cs`
- Create: `AdvertisingIdRule.cs`, `DiagnosticLevelRule.cs`, `TailoredExperiencesRule.cs`, `SpeechTypingRule.cs` in the same folder
- Modify: `src/BriskEngine/Diagnostics/DiagnosticRuleRegistry.cs`
- Modify: `src/Brisk/Localization/Strings.resx`, `Strings.tr.resx`
- Test: the project that already holds `SearchWebResultsRule`'s tests

**Interfaces:**
- Consumes: `PrivacyRuleIds.All` from Task 1.
- Produces: `TelemetrySwitchRule` with `protected abstract IReadOnlyList<RegistryValue> Values { get; }` where `RegistryValue` is `record RegistryValue(string KeyPath, string ValueName, int OnValue, int OffValue)`; and `public bool IsOn(DiagnosticContext ctx)` used by Task 6's read-back.

**Copy the shape of `src/BriskEngine/Diagnostics/Rules/SearchWebResultsRule.cs` exactly** — the `Prior` record, `JsonSerializer.Serialize`, and an `Undo` that deletes a value that did not exist before rather than writing a zero. That last detail is the one that makes an undo honest: writing `0` where there was nothing is not a restoration.

The registry surfaces, verbatim:

| rule id | key | value | on | fix writes |
|---|---|---|---|---|
| `advertising-id` | `HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo` | `Enabled` | `1` | `0` |
| `diagnostic-level` | `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` | `AllowTelemetry` | `>= 2` or absent | `1` |
| `tailored-experiences` | `HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy` | `TailoredExperiencesWithDiagnosticDataEnabled` | `1` or absent | `0` |
| `speech-typing` | `HKCU\Software\Microsoft\InputPersonalization` | `RestrictImplicitTextCollection` **and** `RestrictImplicitInkCollection` | `0` or absent | `1` (both) |

Two of these are "absent means on", which is the trap: a machine that has never been touched has no value at all, and Windows treats that as consent. `Detect` must therefore return a finding when the value is **absent**, and `Undo` must **delete** rather than write back a value that was never there.

`diagnostic-level` is the one with an edition trap. Read the effective consumer value at `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection\AllowTelemetry` as well; Task 6 uses the difference between what brisk wrote and what the machine reports to produce the "written but ignored" sentence.

- [ ] **Step 1: Write the failing tests, one per rule, against a fake registry**

Use the existing fake registry from the test project (`FakeRegistry` is already used in `SecondaryViewModelTests`). Four tests, each asserting three things: absent → finding; on → finding; off → `null`. Plus one round-trip test per rule: `Fix` then `Undo` leaves the registry byte-identical to where it started, **including the case where the value was absent**.

```csharp
    [Fact]
    public void AdvertisingId_Absent_IsAFinding_AndUndoLeavesItAbsent()
    {
        var registry = new FakeRegistry();
        var ctx = Ctx(registry);
        var rule = new AdvertisingIdRule();

        Assert.NotNull(rule.Detect(ctx));
        var prior = rule.Fix(ctx);
        Assert.Null(rule.Detect(ctx));
        rule.Undo(ctx, prior);

        Assert.Null(registry.GetInt(AdvertisingIdRule.KeyPath, AdvertisingIdRule.ValueName));
    }
```

- [ ] **Step 2: Run them and watch every one fail**

Expected: compile failure first (the rules do not exist), then real assertion failures once the classes exist but `Detect` returns `null`. Both are acceptable RED; record which you saw.

- [ ] **Step 3: Implement `TelemetrySwitchRule` and the four subclasses**

Every finding carries `Kind: FindingKind.Notice`, `Category: RuleCategory.Auto`, `EvidenceKey: $"rule.{Id}.evidence"`, and a `FixDescription`. No `Headline` on these four — they have no number, and the spec forbids inventing one.

- [ ] **Step 4: Add the resx strings to BOTH files**

Per rule: `rule.<id>.title`, `rule.<id>.evidence`, `rule.<id>.advice`. Turkish and English. **No string may claim anything about what Microsoft receives.** Write "şu an açık okunuyor", never "Microsoft görüyor".

- [ ] **Step 5: Register the rules and run the whole suite**

Add all four to `DiagnosticRuleRegistry.All`. Run the full solution suite: existing counts must not move except by your new tests.

- [ ] **Step 6: Commit**

---

### Task 3: The two switches that cost the user something

**Files:**
- Create: `LocationRule.cs`, `ActivityHistoryRule.cs` in `src/BriskEngine/Diagnostics/Rules/Privacy/`
- Modify: `DiagnosticRuleRegistry.cs`, both resx files
- Test: beside Task 2's tests

**Interfaces:**
- Consumes: `TelemetrySwitchRule` from Task 2. `location` stores a **string**, not an int, so it overrides `Detect`/`Fix`/`Undo` rather than using the base's int list — do not bend the base to fit it.

| rule id | key | value | on | fix writes | what the user loses |
|---|---|---|---|---|---|
| `location` | `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location` | `Value` (string) | `"Allow"` | `"Deny"` | Find my device |
| `activity-history` | `HKLM\SOFTWARE\Policies\Microsoft\Windows\System` | `PublishUserActivities` **and** `UploadUserActivities` | `1` or absent | `0` (both) | Timeline |

Both are `RuleCategory.Confirm`, not `Auto` — that is what the consent level is for, and it is what keeps them out of any one-click batch. Verify against `FixAllService` that `Confirm` rules are excluded from fix-all before relying on this; if they are not, say so in the report rather than changing `FixAllService` here.

- [ ] **Step 1: Write the failing tests**

Same three-state shape as Task 2, plus one assertion that neither rule is `RuleCategory.Auto`.

- [ ] **Step 2: Watch them fail**

- [ ] **Step 3: Implement both rules**

- [ ] **Step 4: The resx strings, and the loss named in the copy**

`rule.location.advice` must name what stops working, in both languages. English: "Find my device stops working." Turkish: "'Cihazımı bul' çalışmaz." Same for `activity-history` and Timeline. A switch that costs something and does not say so is the failure this task exists to prevent.

- [ ] **Step 5: Register, run the suite, commit**

---

### Task 4: The three report-only disclosures

**Files:**
- Create: `RecallStatusRule.cs`, `UsbHistoryRule.cs`, `RunHistoryRule.cs`
- Modify: `DiagnosticRuleRegistry.cs`, both resx files
- Test: beside the others

**Interfaces:**
- Consumes: `IRegistryProbe.GetSubKeyNames`, `GetValueNames`, `GetBytes` — all already on the interface. No new probe.
- Produces: findings that carry a `Headline`, which is what puts them in Task 8's lottery.

All three are `RuleCategory.Advise`, `CanFix: false`, `FindingKind.Notice`.

**`usb-history`** — count the instance subkeys under `HKLM\SYSTEM\CurrentControlSet\Enum\USBSTOR`: `GetSubKeyNames` on the root gives device models, and `GetSubKeyNames` on each model gives instances. Count instances, never names. For "how far back", read each instance's `Properties\{83da6326-97a6-4088-9453-a1923f573b29}\0064` as bytes — a Windows FILETIME (`DateTime.FromFileTimeUtc(BitConverter.ToInt64(bytes, 0))`) — and take the earliest. **If that property cannot be read, report the count alone and put the date in unreadable.** Never guess a date.

**`run-history`** — count value names under both `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count` and `{F4E57C4B-2036-45F0-A9AB-443BCFE33D9F}\Count`. The names are ROT13-encoded paths; **do not decode them**. The count is the finding; decoding would produce exactly the contents the spec forbids, and code that decodes is code a reviewer must then verify never leaks.

**`recall-status`** — read `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI\DisableAIDataAnalysis`. Three outcomes: `1` → off by policy; `0` → on; absent → **not determinable on this build**, which is a real answer and goes to unreadable, not to "off".

- [ ] **Step 1: Write the failing tests**

Including, for each rule, the unreadable path: a `FakeRegistry` that returns nothing must produce either no finding or an explicitly-unreadable finding — never a finding claiming zero. Assert on that distinction directly:

```csharp
    [Fact]
    public void UsbHistory_WhenTheKeyCannotBeRead_DoesNotClaimZeroDevices()
    {
        var finding = new UsbHistoryRule().Detect(Ctx(new FakeRegistry()));
        Assert.True(finding is null || !finding.Evidence.Contains("0 "),
            "an unreadable USB record must not be reported as zero devices");
    }
```

- [ ] **Step 2: Watch them fail**

- [ ] **Step 3: Implement the three rules**

Each carries a `Headline` whose `Value` is the count and whose `Caption` names what the count is, with `ValueKey`/`CaptionKey` set — follow `DiskBreakdownRule`'s headline for the exact convention.

- [ ] **Step 4: resx strings in both files**

- [ ] **Step 5: Register, run the suite, commit**

---

### Task 5: Delivery Optimization — the one new probe

**Files:**
- Create: `src/BriskEngine/Diagnostics/IDeliveryOptimizationProbe.cs`
- Create: `src/BriskEngine/Diagnostics/RealProbes/RealDeliveryOptimizationProbe.cs`
- Create: `src/BriskEngine/Diagnostics/Rules/Privacy/DeliveryOptimizationRule.cs`
- Modify: `src/BriskEngine/Diagnostics/DiagnosticContext.cs`, `DiagnosticRuleRegistry.cs`, both resx files, and every construction site of `DiagnosticContext` (search for `new DiagnosticContext(` — the fakes in the test project construct it too)

**Interfaces:**
- Produces:

```csharp
public interface IDeliveryOptimizationProbe
{
    /// Bytes this machine uploaded to other machines this month, or null when
    /// the counter cannot be read. null is not zero: a machine that uploaded
    /// nothing and a machine brisk could not ask are different claims.
    long? BytesUploadedToPeers();
}
```

- [ ] **Step 1: Write the rule's tests against a fake probe first**

Three cases: a real number → a finding with that number in its headline; zero → **no finding** (nothing to disclose); `null` → no finding and the unreadable list gains an entry. Write these before the real probe exists — the rule is the part that must be right, and it must be testable without a machine that uploads anything.

- [ ] **Step 2: Watch them fail, then implement the rule**

`RuleCategory.Advise`, `CanFix: false`, `Notice`, with a `Headline` carrying the byte count formatted through the existing `Fmt.Bytes`.

- [ ] **Step 3: Add the probe to `DiagnosticContext` and fix every construction site**

The record has 12 members today; adding a 13th breaks every positional construction. Compile, find them all, and update them — including test fakes. Do not give it a default value to avoid the work: a defaulted probe is how a real scan silently gets a fake.

- [ ] **Step 4: Implement `RealDeliveryOptimizationProbe`**

Shell out to `powershell.exe -NoProfile -NonInteractive -Command "Get-DeliveryOptimizationPerfSnap | ConvertTo-Json -Compress"` and read `BytesToPeers`. **Follow the process-launching pattern already in this repo** — read `src/BriskEngine/Diagnostics/StartupManager.cs` and whatever runs `schtasks`, and copy its timeout, its redirect handling and its failure behaviour rather than inventing another. Any failure, non-zero exit, timeout, or unparseable output returns `null`. It must never throw into a scan.

- [ ] **Step 5: Run the whole suite, then run `brisk scan` on this machine once**

The probe's real behaviour cannot be unit-tested. Run the CLI once and record in the report what the machine actually said — a number, or the honest null. Either is a pass; a crash is not.

- [ ] **Step 6: Commit**

---

### Task 6: The read-back — the sentence no competitor prints

**Files:**
- Create: `src/BriskEngine/Diagnostics/ReadBack.cs`
- Create: `src/Brisk.Tests/ReadBackTests.cs`
- Modify: both resx files

**Interfaces:**
- Consumes: `FixJournal.ListUndoable()` → `IReadOnlyList<UndoableFix>` where `UndoableFix` is `(string RuleId, DateTime FixedAtUtc)`; and each privacy rule's live `Detect`.
- Produces:

```csharp
public enum ReadBackState { Held, Reverted, WrittenButIgnored }

public sealed record ReadBackResult(
    string RuleId, ReadBackState State, DateTime FixedAtUtc);

public static class ReadBack
{
    public static IReadOnlyList<ReadBackResult> For(
        DiagnosticContext ctx,
        IReadOnlyList<UndoableFix> journal,
        IReadOnlyList<IDiagnosticRule> rules);
}
```

The whole mechanism, stated so nobody invents a scheduler:

- journal has an entry for the rule **and** `Detect` returns `null` → `Held`
- journal has an entry **and** `Detect` returns a finding **and** the value brisk wrote is **gone** → `Reverted`. Something put it back.
- journal has an entry **and** `Detect` returns a finding **and** the value brisk wrote is **still there** → `WrittenButIgnored`. This is the Home-edition case, and it is the most important sentence in the wave: brisk reporting that its own fix did not take.

> **This plan was wrong here, and Task 6 proved it. Corrected 2026-08-26; the
> original is left above because it is what the implementer was handed.**
>
> **The third branch is unreachable.** Every switch's `Detect` reads *exactly*
> the values its `Fix` writes, so "Detect fires while brisk's write is still
> there" is a contradiction for all six rules — and this plan's own worked
> example (policy=1, effective=3) yields `Detect == null`, which under these
> three branches returns `Held`. The mechanism was falsified by the example
> printed beside it.
>
> `WrittenButIgnored` can only come from a **second** read of an effective key.
> `diagnostic-level` has one; `activity-history` deliberately does not, because
> Task 3 refused to invent a registry path brisk could not vouch for. So the
> states are not equally knowable across the six rules, and a three-state design
> would have reported *"still off"* on exactly the Home machine where the policy
> is ignored and Timeline still runs — the read-back lying in the one case it
> exists to catch.
>
> **Shipped: four states.** The fourth, `WrittenButUnverified`, says brisk does
> not know. Also corrected by it: branch 1 as written grants `Held` on
> `Detect`-null alone, even with the effective level at 3.
>
> A second defect in this section: `HealthScore.For` (line 101) does not exist —
> the entry point is `HealthScore.Compute`. Task 1 caught it because its dispatch
> said to use the real signature rather than the plan's.

That third branch is why `TelemetrySwitchRule` exposes its `Values` — the read-back needs to ask "is what I wrote still written?" separately from "is the setting on?".

- [ ] **Step 1: Write all three tests, and watch each fail**

```csharp
    [Fact]
    public void AFixThatWasWrittenButIsStillOn_ReadsAsIgnored()
    {
        var registry = new FakeRegistry();
        registry.SetInt(DiagnosticLevelRule.PolicyKey, "AllowTelemetry", 1); // brisk's write, still there
        registry.SetInt(DiagnosticLevelRule.EffectiveKey, "AllowTelemetry", 3); // the machine ignoring it
        var journal = new[] { new UndoableFix("diagnostic-level", new DateTime(2026, 8, 12)) };

        var results = ReadBack.For(Ctx(registry), journal, DiagnosticRuleRegistry.All);

        Assert.Equal(ReadBackState.WrittenButIgnored,
            results.Single(r => r.RuleId == "diagnostic-level").State);
    }
```

- [ ] **Step 2: Implement `ReadBack`**

- [ ] **Step 3: The three sentences, in both languages**

`readback.held` — *"{0} gün önce kapattın, hâlâ kapalı"*
`readback.reverted` — *"Bunu {0} tarihinde kapatmıştın; şu an yeniden açık."*
`readback.ignored` — *"Ayar kapalı yazıyor ama bu Windows sürümü onu dikkate almıyor."*

None of the three may say anything about what Microsoft receives. `readback.ignored` in particular must describe **Windows ignoring a local policy**, which is what was measured, and nothing about data leaving the machine, which was not.

- [ ] **Step 4: Run the suite and commit**

---

### Task 7: The Privacy page

**Files:**
- Create: `src/Brisk/ViewModels/PrivacyViewModel.cs`, `src/Brisk/Views/PrivacyPage.xaml`, `PrivacyPage.xaml.cs`
- Create: `src/Brisk.Tests/PrivacyViewModelTests.cs`
- Modify: `src/Brisk/Windows/MainWindow.xaml` + `.xaml.cs`, `src/Brisk/App.xaml.cs`, both resx files

**Interfaces:**
- Consumes: `FindingSections.IsPrivacy` (Task 1), the ten rules, `ReadBack.For` (Task 6).
- Produces: `PrivacyViewModel` with `DisclosureRows`, `SafeSwitchRows`, `CostlySwitchRows`, `ReadBackRows`, and `TurnOffSafeCommand`.

**Build it from `HealthViewModel`, not from scratch.** That view model already subscribes to `AppState.Changed`, rebuilds rows in `Refresh()`, and holds `FindingRow` objects with the fix/undo lifecycle. The privacy page needs the same lifecycle; what differs is only the grouping.

Three blocks, in this order: disclosure numbers (largest first), the switches, the read-back lines.

The switch block is the maintainer's two-tier model: **one button** for the four `Auto` rules, whose caption says how many settings and that it is reversible; the two `Confirm` rules each on their own row with the loss named beside them.

- [ ] **Step 1: Write the view model tests first**

Rows land in the right three collections; `TurnOffSafeCommand` fixes exactly the four `Auto` privacy rules and never a `Confirm` one. That last assertion is the guard that keeps a costly switch out of the one-click path — write it before the command exists.

- [ ] **Step 2: Watch them fail, then implement `PrivacyViewModel`**

- [ ] **Step 3: The page and the sixth nav tile**

`MainWindow.xaml` currently holds five `RadioButton`s named `NavOverview`, `NavHealth`, `NavPerf`, `NavClean`, `NavSettings` (lines 177-192), each with a glyph in `Tag` and content bound through `Loc.Instance`. Add `NavPrivacy` between `NavClean` and `NavSettings`, matching the existing shape exactly, with `nav.privacy` in both resx files. Wire the page the same way the others are wired in `MainWindow.xaml.cs`.

- [ ] **Step 4: Check the nav guards still pass**

`ShellSourceTests` and `PanelSourceTests` parse the shell's XAML and assert things about the nav. A sixth tile may break a count or an ordering assertion. If one fails, read what it claims before changing it: it may be right and the new tile wrong.

- [ ] **Step 5: Render the page**

Add a snapshot test beside the existing ones in `src/Brisk.Tests/Snapshots/SnapshotTests.cs` and write `.snapshots/page-privacy.png`. **Look at the image.** A blank frame is a failure. Report what you saw in words — this repo's method is a photograph before every judgment, and its own history says the suite goes green while the window is wrong.

- [ ] **Step 6: Commit**

---

### Task 8: Into the revelation lottery, and onto the report card

**Files:**
- Modify: `src/BriskEngine/Diagnostics/RevelationPicker.cs`
- Modify: `src/Brisk/ViewModels/ReportCardModel.cs`
- Test: `src/Brisk.Tests/ReportCardModelTests.cs`, and wherever `RevelationPicker` is tested

**Interfaces:**
- Consumes: the headlines from Task 4 and Task 5.

`RevelationPicker.Pick` already includes any finding with a `Headline`, ranking unlisted rules after the listed ones. So the disclosure findings enter the lottery with no change at all — the only decision is placement in `Priority`.

Place `usb-history` **third**, after `boot-degradation` and `display-refresh` and before `startup-bloat`. Reasoning to record in the commit: boot time and a wrong refresh rate are things the user can act on today, and brisk leads with actionable measurements; the USB count is the strongest number brisk owns that the user cannot act on, so it leads the moment nothing actionable outranks it. `run-history` and `delivery-optimization` stay unlisted and fall to the tail rank, which is the correct default for them.

- [ ] **Step 1: Write the ordering test and watch it fail**

A scan carrying `boot-degradation` and `usb-history` leads with boot; a scan carrying only `usb-history` and `startup-bloat` leads with USB.

- [ ] **Step 2: Edit `Priority`, watch it pass**

- [ ] **Step 3: The report card carries counts and never contents**

Add the disclosure findings to the card, then write the guard that matters:

```csharp
    [Fact]
    public void TheCard_CarriesCounts_AndNeverADeviceOrProgramName()
    {
        var card = ReportCardModel.Build(SnapshotWithPrivacyFindings(), NoUndoables(), EnglishLoc());
        var text = AllTextOn(card);

        Assert.Contains("47", text);
        Assert.DoesNotContain("Kingston", text);   // planted device name
        Assert.DoesNotContain("chrome.exe", text); // planted program name
    }
```

Plant those names in the fake registry the snapshot is built from, so the test proves they were available and were not printed — a test that never had the name in reach proves nothing.

- [ ] **Step 4: Run the suite and commit**

---

### Task 9: The red lines become tests

**Files:**
- Modify: `src/Brisk.Tests/PrivacyRedLineTests.cs`

This task exists because the spec's four red lines are the product, and a red line that is only a sentence in a document is not enforced by anything.

- [ ] **Step 1: No copy claims anything about what Microsoft can see**

Parse both resx files. For every key beginning `rule.` whose id is in `PrivacyRuleIds.All`, plus every `readback.*` key — **and, corrected 2026-08-26, every `privacy.*` key: the page's own copy, which this parse as written missed, and which is where Task 7's deferred heading decision lived. Widened in Task 9's dispatch before it ran** — assert the value contains none of: `Microsoft` in the same sentence as a seeing/receiving verb, `göremez`, `görmüyor`, `artık göremez`, `no longer see`, `can't see`, `cannot see`, `stops sending`, `veri gitmiyor`. Keep the banned list in the test file with a comment naming the spec section, so the next person adding a string learns the rule from the failure message.

Write the failure message to name the offending key **and** the offending phrase. A guard whose message does not name what it found sends the reader hunting, and this branch has shipped that four times.

- [ ] **Step 2: Watch it fail**

Temporarily add `rule.advertising-id.advice` = "Microsoft artık göremez." to the Turkish resx, run, see it fail naming that key and that phrase, then remove it.

- [ ] **Step 3: Every privacy rule produces `Notice`**

Iterate `DiagnosticRuleRegistry.All`, filter to `PrivacyRuleIds.All`, `Detect` each against a fake context that makes every setting look "on", and assert every returned finding has `Kind == FindingKind.Notice`. Watch it fail by flipping one rule to `Problem`.

- [ ] **Step 4: An unreadable probe never becomes a zero**

Assert across all ten rules against a fake context whose probes return nothing: no finding's `Evidence` or `Headline.Value` is `"0"`. Watch it fail by making one rule return a zero-count finding on an empty registry.

- [ ] **Step 5: Run the whole suite and commit**

---

### Task 10: 0.6.0, and the photograph before the close

**Files:**
- Modify: `src/BriskEngine/EngineInfo.cs`

- [ ] **Step 1: Run the app on this machine and read the Privacy page**

Publish outside the source tree — `dotnet publish src/Brisk/Brisk.csproj -c Release -o <scratchpad>/run` — because a running brisk locks `src/Brisk/bin` and blocks every later build. Launch it, open Gizlilik, and **report the real numbers this machine shows**. If any number is implausible, that is a finding, not a formality.

- [ ] **Step 2: Re-render the Overview**

If the USB count won the lottery, the hero now leads with it. Photograph `window.png` and say what it reads. Remember this repo's own established fact: the hero panel is not byte-reproducible, so no raw before/after pixel diff covering it means anything.

- [ ] **Step 3: `EngineInfo.Version` → `0.6.0`. No tag.**

- [ ] **Step 4: Full suite in Release, 0 warnings, tree clean, and commit**

---

## Self-review notes

- **Spec coverage.** Every spec section maps to a task: red lines → Task 9 (and Task 1 for the score line); probes → Tasks 4 and 5; rules → Tasks 2, 3, 4, 5; read-back → Task 6; UI → Task 7; report card → Task 8; version → Task 10. The spec's "two task groups" is Tasks 1-6 then 7-10.
- **The spec said `RuleCategory.Privacy`; this plan does not.** `RuleCategory` is a consent level and `FindingSections` says so; topic routing is by rule id. The spec was corrected to match before this plan was written. Privacy rules span all three consent levels, which is the point: `Auto` for the four safe ones, `Confirm` for the two costly ones, `Advise` for report-only.
- **The riskiest task is 5**, because its probe cannot be unit-tested and shells out to PowerShell. That is why its rule is tested against a fake first and the real probe is exercised once, by hand, with the result recorded either way.
- **Task 7 will collide with existing shell guards.** `ShellSourceTests` and `PanelSourceTests` parse the shell and assert on the nav. The plan tells its implementer to read the guard before editing it, because on this branch a guard has twice been right where the new code was wrong.
- **Registry paths are the plan's biggest factual risk.** Every key in Tasks 2-4 is written out verbatim so a reviewer can check them against Microsoft's documentation without reading the code, and every rule is tested against a fake registry so a wrong path fails as "no finding" rather than as a crash on a user's machine.
