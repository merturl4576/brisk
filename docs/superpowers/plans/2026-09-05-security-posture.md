# v0.9 Security Posture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put a Security tier on brisk's Privacy page: one undoable UAC fix with read-back, three report-only disclosures (which apps used the microphone and camera, what Defender skips, Windows' suggestion flags as a switch), and a posture block that also says what is fine — with no name or path on any shared surface and nothing graded.

**Architecture:** Every new rule follows an existing family: `TelemetrySwitchRule` (switches with fix/undo/read-back) or `PrivacyDisclosureRule` (Advise, Notice, report-only). Two foundations come first: a QWORD read on `IRegistryProbe` and a `Severity` hook on the switch family. One new probe (`IDefenderProbe`, WMI) joins `DiagnosticContext`. Names and paths travel only on `ScanSnapshot` lists the Privacy page reads (the `UsbDevices` shape). The posture block is a memoized reader, not a rule.

**Tech Stack:** .NET 8, WPF, xunit, `System.Management` (already referenced by BriskEngine). Solution `brisk.sln` at the repo root. Full suite: `dotnet test brisk.sln -c Release --nologo -v q` — 1391 green on `feat/v0.9-security @ 2b20c46`; that number only goes up. Build must stay at 0 warnings.

**Spec:** `docs/superpowers/specs/2026-09-05-security-posture-design.md`. One deviation, recorded here: the `uac-prompts` evidence is a fixed sentence without the three values as arguments, because `TelemetrySwitchRule.Detect` is non-virtual and passes `EvidenceArgs: null` for the whole family. Task 2 amends the spec sentence.

## Global Constraints

- Branch `feat/v0.9-security`; never commit to `main`. One commit per task; subject in plain conventional form (`feat(rule): ...`, `test: ...`, `docs: ...`), body says what was wrong or missing and what test pins it. Every commit message ends with the two trailer lines the repo uses (see `git log -1 --format=%B`).
- Both resx files (`src/Brisk/Localization/Strings.resx`, `Strings.tr.resx`) must keep identical key sets — `LocTests.ResxFiles_ExposeTheSameKeySet` enforces it. Every new key lands in BOTH. Turkish copy is written in the plain voice of the existing `rule.usb-history.*` and `privacy.*` entries; no machine-translation tone.
- brisk never prints a claim it did not measure. Unreadable is reported as unreadable, never as zero and never as fine.
- Red line 2: no app name, package name, executable path or exclusion path in any `DiagnosticFinding` string (Title, Evidence, EvidenceArgs, Headline value/caption/args) — `brisk scan --json` serializes findings and is a shared surface.
- No new NuGet packages. Windows-only APIs are fine.
- Every new rule id also goes into `PrivacyRuleIds.All` (`src/Brisk/ViewModels/FindingSections.cs`) — that list is what keeps a finding off the graded pages and on the Privacy page.
- Run the owning test project after every task (`dotnet test src/BriskEngine.Tests -c Release --nologo -v q` or `dotnet test src/Brisk.Tests -c Release --nologo -v q`), and the full suite before each commit.
- Read `git log --oneline -12` once before the first commit to match the message voice.

## File map

| Area | Files |
|---|---|
| Foundations | `src/BriskEngine/Diagnostics/Probes.cs` (IRegistryProbe), `src/BriskEngine/Diagnostics/RealProbes/RealRegistryProbe.cs`, `src/BriskEngine/Diagnostics/Rules/Privacy/TelemetrySwitchRule.cs`, five fakes (`src/BriskEngine.Tests/TestContext.cs`, `ReadBackTests.cs`, `Rules/PrivacyDisclosureRuleTests.cs`, `StartupManagerTests.cs`, `src/Brisk.Tests/EngineHostTests.cs`), `src/Brisk.Tests/Fakes.cs` (`NoOtherProbes`) |
| uac-prompts | create `src/BriskEngine/Diagnostics/Rules/Privacy/UacPromptRule.cs`, `src/BriskEngine.Tests/Rules/UacPromptRuleTests.cs`; modify `DiagnosticRuleRegistry.cs`, both resx, `FindingSections.cs`, `src/Brisk.Tests/PrivacyRedLineTests.cs`, `src/BriskEngine.Tests/ReadBackTests.cs`, `src/BriskEngine.Tests/ProgramFixTests.cs` |
| camera-mic-use | create `src/BriskEngine/Diagnostics/Rules/Privacy/CapabilityUseRule.cs`, `src/BriskEngine.Tests/Rules/CapabilityUseRuleTests.cs`; modify `DiagnosticRuleRegistry.cs`, both resx, `FindingSections.cs`, `PrivacyRedLineTests.cs`, `src/Brisk/Services/IEngineHost.cs` (ScanSnapshot), `EngineHost.cs`, `src/Brisk.Tests/Fakes.cs`, `HealthViewModelTests.cs`, `OverviewViewModelTests.cs`, `src/Brisk/ViewModels/PrivacyViewModel.cs`, `src/Brisk/Views/PrivacyPage.xaml`, `src/Brisk.Tests/PrivacyViewModelTests.cs` |
| defender-blind-spots | create `src/BriskEngine/Diagnostics/IDefenderProbe.cs`, `RealProbes/RealDefenderProbe.cs`, `Rules/Privacy/DefenderRule.cs`, `src/BriskEngine.Tests/Rules/DefenderRuleTests.cs`; modify `DiagnosticContext.cs` and its five construction sites (`src/Brisk/Services/AppServices.cs`, `src/Brisk.Cli/Program.cs`, `TestContext.cs`, `EngineHostTests.cs`, `Fakes.cs`, `PrivacyRedLineTests.cs`), registry, resx, `FindingSections.cs`, `HealthViewModel.cs` (WindowsSettingRules), snapshot + page as above |
| windows-suggestions | create `Rules/Privacy/ContentSuggestionsRule.cs`; modify registry, resx, `FindingSections.cs`, `TelemetrySwitchRuleTests.cs`, `ReadBackTests.cs`, `PrivacyRedLineTests.cs` |
| posture | create `src/BriskEngine/Diagnostics/SecurityPosture.cs`, `src/BriskEngine.Tests/SecurityPostureTests.cs`, `src/BriskEngine.Tests/ProgramScanPostureTests.cs`; modify snapshot + `EngineHost.cs` + `Fakes.cs`, `PrivacyViewModel.cs`, `PrivacyPage.xaml`, `src/Brisk.Cli/Program.cs`, resx |
| docs | `README.md`, `README.tr.md`, `.github/release-notes.md`, `src/BriskEngine/EngineInfo.cs` |

---

### Task 1: Foundations — `IRegistryProbe.GetLong` and `TelemetrySwitchRule.Severity`

**Files:**
- Modify: `src/BriskEngine/Diagnostics/Probes.cs` (interface `IRegistryProbe`, lines 17-28)
- Modify: `src/BriskEngine/Diagnostics/RealProbes/RealRegistryProbe.cs`
- Modify: `src/BriskEngine/Diagnostics/Rules/Privacy/TelemetrySwitchRule.cs` (add the hook; `Detect` reads it)
- Modify fakes: `src/BriskEngine.Tests/TestContext.cs` (`FakeRegistry`), `src/BriskEngine.Tests/ReadBackTests.cs`, `src/BriskEngine.Tests/Rules/PrivacyDisclosureRuleTests.cs`, `src/BriskEngine.Tests/StartupManagerTests.cs`, `src/Brisk.Tests/EngineHostTests.cs` (`NullRegistry`), `src/Brisk.Tests/Fakes.cs` (`NoOtherProbes`) — every class that implements `IRegistryProbe` (find them with `grep -rn ": IRegistryProbe" src --include=*.cs`)
- Test: `src/BriskEngine.Tests/RegistryProbeLongTests.cs` (create), `src/BriskEngine.Tests/Rules/TelemetrySwitchRuleTests.cs` (add one test)

**Interfaces:**
- Produces: `long? IRegistryProbe.GetLong(string keyPath, string valueName)` — null when the value is absent or not a QWORD.
- Produces: `FakeRegistry.SetLong(string k, string v, long value)` — fake-only planting helper (not on the interface).
- Produces: `protected virtual Severity TelemetrySwitchRule.Severity { get; }` — `Severity.Info` by default; `Detect` passes it where it passed the literal.

- [ ] **Step 1: Write the failing tests**

`src/BriskEngine.Tests/RegistryProbeLongTests.cs`:

```csharp
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

public class RegistryProbeLongTests
{
    [Fact]
    public void FakeRegistry_ReadsBackAQwordItPlanted_AndNullForAnythingElse()
    {
        var reg = new FakeRegistry();
        reg.SetLong(@"HKCU\X", "Stamp", 134309229721436686L);
        reg.SetInt(@"HKCU\X", "Small", 5);

        Assert.Equal(134309229721436686L, reg.GetLong(@"HKCU\X", "Stamp"));
        Assert.Null(reg.GetLong(@"HKCU\X", "Small"));
        Assert.Null(reg.GetLong(@"HKCU\X", "Missing"));
    }
}
```

Append to `src/BriskEngine.Tests/Rules/TelemetrySwitchRuleTests.cs` (inside the class):

```csharp
    /// The one hook this wave adds to the family. Info stays the default for
    /// every shipped switch; a subclass that says otherwise is heard by
    /// Detect, which is what makes the Warning on uac-prompts possible
    /// without making Kind or the finding's shape virtual.
    private sealed class LouderSwitch : TelemetrySwitchRule
    {
        public override string Id => "test-louder";
        public override IReadOnlyList<RegistryValue> Values { get; } = new[]
        {
            new RegistryValue(@"HKCU\Test", "V", OnValue: 1, OffValue: 0),
        };
        protected override Severity Severity => Severity.Warning;
        protected override string Title => "t";
        protected override string Evidence => "e";
        protected override string FixDescription => "f";
    }

    [Fact]
    public void ASubclass_MayRaiseTheSeverity_AndTheFindingCarriesIt()
    {
        var (ctx, _) = Context();
        var finding = new LouderSwitch().Detect(ctx)!;
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(FindingKind.Notice, finding.Kind);
    }

    [Theory]
    [MemberData(nameof(AllSwitches))]
    public void EveryShippedSwitch_KeepsInfo(string id)
    {
        var (ctx, _) = Context();
        Assert.Equal(Severity.Info, Rule(id).Detect(ctx)!.Severity);
    }
```

(`Context()` and `Rule(id)` already exist in that file. Add `using BriskEngine.Models;` and `using System.Collections.Generic;` if missing.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests -c Release --nologo -v q`
Expected: compile errors — `SetLong`/`GetLong` do not exist, `Severity` is not a member to override.

- [ ] **Step 3: Add the interface member and the real read**

In `Probes.cs`, inside `IRegistryProbe`, after `GetInt`/`SetInt`:

```csharp
    /// A QWORD, or null when the value is absent or not one. Windows keeps
    /// FILETIME stamps as QWORDs (CapabilityAccessManager's LastUsedTimeStop
    /// is one), and GetInt reads those as null. No SetLong: nothing in brisk
    /// writes one.
    long? GetLong(string keyPath, string valueName);
```

In `RealRegistryProbe.cs`, after `GetInt`:

```csharp
    public long? GetLong(string k, string v)
    {
        var (root, sub) = Split(k);
        using var key = root.OpenSubKey(sub);
        return key?.GetValue(v) as long?;
    }
```

- [ ] **Step 4: Teach every fake the new member**

`FakeRegistry` in `TestContext.cs`, after `GetInt`:

```csharp
    public long? GetLong(string k, string v) => Values.TryGetValue(K(k, v), out var o) ? o as long? : null;
    public void SetLong(string k, string v, long value) => Values[K(k, v)] = value;
```

Every other `IRegistryProbe` implementation gets a `GetLong` that mirrors what its `GetInt` does: a null-returning fake returns `null`; a throwing fake throws the same exception; a dictionary-backed fake reads its dictionary. Run `grep -rn "GetInt" src/BriskEngine.Tests/ReadBackTests.cs src/BriskEngine.Tests/Rules/PrivacyDisclosureRuleTests.cs src/BriskEngine.Tests/StartupManagerTests.cs src/Brisk.Tests/EngineHostTests.cs src/Brisk.Tests/Fakes.cs` to find each one and add the sibling line directly beneath it.

- [ ] **Step 5: Add the Severity hook to the family**

In `TelemetrySwitchRule.cs`, directly above `public abstract IReadOnlyList<RegistryValue> Values { get; }`:

```csharp
    /// Info for every switch in the family but one. Severity says how loud
    /// the row is; Kind says whether brisk may call it a problem, and Kind
    /// stays Notice for all of them, so raising this moves no score. Only
    /// uac-prompts overrides it (to Warning), and a test walks the shipped
    /// switches to keep it that way.
    protected virtual Severity Severity => Severity.Info;
```

In `Detect`, replace `Severity.Info, Category, ImpactStars: 1, CanFix: true,` with `Severity, Category, ImpactStars: 1, CanFix: true,` and update the comment above it: the impact-stars sentence stays; the "Info, one star" opening becomes "One star, and the severity the rule declares (Info for all but one)".

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test brisk.sln -c Release --nologo -v q`
Expected: all green, 1391 + 3 (the two theories add one row per switch: 6 for `EveryShippedSwitch_KeepsInfo`), 0 warnings. Count reported: 1399.

- [ ] **Step 7: Commit**

```bash
git add -A src
git commit -m "feat(engine): QWORD registry read and a severity hook on the switch family"
```

---

### Task 2: `uac-prompts`

**Files:**
- Create: `src/BriskEngine/Diagnostics/Rules/Privacy/UacPromptRule.cs`
- Create: `src/BriskEngine.Tests/Rules/UacPromptRuleTests.cs`
- Modify: `src/BriskEngine/Diagnostics/DiagnosticRuleRegistry.cs` (after `new LocationRule(), new ActivityHistoryRule(),`)
- Modify: `src/Brisk/Localization/Strings.resx`, `Strings.tr.resx`
- Modify: `src/Brisk/ViewModels/FindingSections.cs` (`PrivacyRuleIds.All`)
- Modify: `src/Brisk.Tests/PrivacyRedLineTests.cs` (`Assert.Equal(6, switches.Count)` → 7)
- Modify: `src/BriskEngine.Tests/ReadBackTests.cs` (`DivergesFromItsOwnWrite` gains `["uac-prompts"] = true`)
- Modify: `src/BriskEngine.Tests/ProgramFixTests.cs` (`CliFixAll_NeverReachesASwitchThatCostsTheUserSomething` gains `[InlineData("uac-prompts")]`)
- Modify: `docs/superpowers/specs/2026-09-05-security-posture-design.md` (the evidence sentence, see Step 6)

**Interfaces:**
- Consumes: `TelemetrySwitchRule` (Task 1's `Severity` hook), `ReadBack.For`, `UndoableFix(string RuleId, DateTime FixedAtUtc)`.
- Produces: `UacPromptRule` with `public const string RuleId = "uac-prompts"`, `KeyPath`, `EnableLua`, `ConsentPromptBehaviorAdmin`, `PromptOnSecureDesktop` constants, and `public override bool IsOn(DiagnosticContext ctx)` — Task 6's posture reader calls `IsOn`.

- [ ] **Step 1: Write the failing tests**

`src/BriskEngine.Tests/Rules/UacPromptRuleTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

/// UAC inverts the switch family's standing rule: absence is Windows' DEFAULT
/// (prompting), so an unwritten value cannot be read as "never prompt" and
/// raises nothing. Only a written 0 in EnableLUA or ConsentPromptBehaviorAdmin
/// is the bad state. PromptOnSecureDesktop is restored by the fix but never
/// decides the finding: the third position of Windows' own UAC slider sets
/// it to 0 on purpose.
public class UacPromptRuleTests
{
    private static (DiagnosticContext ctx, FakeRegistry reg) Context()
    {
        var reg = new FakeRegistry();
        return (TestContext.Empty() with { Registry = reg }, reg);
    }

    private static void Plant(FakeRegistry reg, int? lua, int? admin, int? secure)
    {
        if (lua is { } l) reg.SetInt(UacPromptRule.KeyPath, UacPromptRule.EnableLua, l);
        if (admin is { } a) reg.SetInt(UacPromptRule.KeyPath, UacPromptRule.ConsentPromptBehaviorAdmin, a);
        if (secure is { } s) reg.SetInt(UacPromptRule.KeyPath, UacPromptRule.PromptOnSecureDesktop, s);
    }

    private static int? Read(FakeRegistry reg, string name) =>
        reg.GetInt(UacPromptRule.KeyPath, name);

    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(1, 0, 1, true)]
    [InlineData(0, 5, 1, true)]
    [InlineData(null, 0, null, true)]
    [InlineData(1, 5, 1, false)]
    [InlineData(1, 2, 1, false)]
    [InlineData(1, 5, 0, false)]
    [InlineData(null, null, null, false)]
    [InlineData(1, null, null, false)]
    public void FiresOnlyOnAWrittenZero_ThatMeansNeverPrompt(int? lua, int? admin, int? secure, bool fires)
    {
        var (ctx, reg) = Context();
        Plant(reg, lua, admin, secure);
        var finding = new UacPromptRule().Detect(ctx);
        Assert.Equal(fires, finding is not null);
    }

    [Fact]
    public void Fix_WritesWindowsDefaults_AndUndo_PutsTheZerosBack()
    {
        var (ctx, reg) = Context();
        Plant(reg, 1, 0, 0);
        var rule = new UacPromptRule();

        var prior = rule.Fix(ctx);
        Assert.Equal(1, Read(reg, UacPromptRule.EnableLua));
        Assert.Equal(5, Read(reg, UacPromptRule.ConsentPromptBehaviorAdmin));
        Assert.Equal(1, Read(reg, UacPromptRule.PromptOnSecureDesktop));
        Assert.Null(rule.Detect(ctx));

        rule.Undo(ctx, prior);
        Assert.Equal(1, Read(reg, UacPromptRule.EnableLua));
        Assert.Equal(0, Read(reg, UacPromptRule.ConsentPromptBehaviorAdmin));
        Assert.Equal(0, Read(reg, UacPromptRule.PromptOnSecureDesktop));
    }

    [Fact]
    public void Undo_DeletesWhatTheFixCreated()
    {
        var (ctx, reg) = Context();
        Plant(reg, null, 0, null);
        var rule = new UacPromptRule();

        var prior = rule.Fix(ctx);
        rule.Undo(ctx, prior);

        Assert.Null(Read(reg, UacPromptRule.EnableLua));
        Assert.Equal(0, Read(reg, UacPromptRule.ConsentPromptBehaviorAdmin));
        Assert.Null(Read(reg, UacPromptRule.PromptOnSecureDesktop));
    }

    [Fact]
    public void ReadBack_SaysHeldAfterTheFix_AndRevertedWhenSomethingWritesZeroAgain()
    {
        var (ctx, reg) = Context();
        Plant(reg, 1, 0, 0);
        var rule = new UacPromptRule();
        rule.Fix(ctx);
        var journal = new[] { new UndoableFix(UacPromptRule.RuleId,
            new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc)) };

        var held = ReadBack.For(ctx, journal, DiagnosticRuleRegistry.All).Single();
        Assert.Equal(ReadBackState.Held, held.State);

        reg.SetInt(UacPromptRule.KeyPath, UacPromptRule.ConsentPromptBehaviorAdmin, 0);
        var reverted = ReadBack.For(ctx, journal, DiagnosticRuleRegistry.All).Single();
        Assert.Equal(ReadBackState.Reverted, reverted.State);
    }

    [Fact]
    public void TheFinding_IsConfirm_Warning_Notice_AndMovesNoScore()
    {
        var (ctx, reg) = Context();
        Plant(reg, 1, 0, 0);
        var rule = new UacPromptRule();
        var finding = rule.Detect(ctx)!;

        Assert.Equal(RuleCategory.Confirm, rule.Category);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(FindingKind.Notice, finding.Kind);
        Assert.True(finding.CanFix);
        Assert.Null(finding.Headline);
        Assert.Equal(100, HealthScore.Compute(new[] { finding }));
        Assert.Equal(1, DiagnosticRuleRegistry.All.Count(r => r.Id == UacPromptRule.RuleId));
    }

    [Fact]
    public void EveryOtherShippedSwitch_StaysInfo()
    {
        var (ctx, _) = Context();
        foreach (var rule in DiagnosticRuleRegistry.All.OfType<TelemetrySwitchRule>()
                     .Where(r => r.Id != UacPromptRule.RuleId))
            Assert.True(rule.Detect(ctx)!.Severity == Severity.Info,
                $"{rule.Id} raised its severity; only uac-prompts does that");
    }

    [Theory]
    [InlineData("Strings.resx")]
    [InlineData("Strings.tr.resx")]
    public void TheCopy_ExistsInBothLanguages_AndNamesTheCost(string file)
    {
        var resx = Resx(file);
        foreach (var suffix in new[] { "title", "evidence", "advice", "cost", "done" })
            Assert.True(resx.ContainsKey($"rule.uac-prompts.{suffix}"),
                $"rule.uac-prompts.{suffix} is missing from {file}");
        Assert.False(string.IsNullOrWhiteSpace(resx["rule.uac-prompts.cost"]));
    }

    [Fact]
    public void TheEnglishTitle_MatchesTheResxEntry()
    {
        var (ctx, reg) = Context();
        Plant(reg, 1, 0, 0);
        var finding = new UacPromptRule().Detect(ctx)!;
        Assert.Equal(Resx("Strings.resx")["rule.uac-prompts.title"], finding.Title);
        Assert.Equal(Resx("Strings.resx")["rule.uac-prompts.evidence"], finding.Evidence);
    }

    private static Dictionary<string, string> Resx(string fileName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null;
             dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return XDocument
                    .Load(Path.Combine(dir.FullName, "src", "Brisk", "Localization", fileName))
                    .Root!.Elements("data")
                    .ToDictionary(e => (string)e.Attribute("name")!,
                        e => (string)e.Element("value")!);
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests -c Release --nologo -v q`
Expected: compile error, `UacPromptRule` does not exist.

- [ ] **Step 3: Write the rule**

`src/BriskEngine/Diagnostics/Rules/Privacy/UacPromptRule.cs`:

```csharp
using System.Collections.Generic;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// Whether Windows asks before a program takes administrator rights. The
/// three values live under the key UAC itself reads — not a Group Policy
/// key — so the write IS the setting and EffectOfTheWrite stays NotAPolicy.
///
/// THIS RULE INVERTS THE FAMILY'S ABSENCE RULE, ON PURPOSE. The privacy
/// switches read an unwritten value as on because they cannot read it as
/// off. Here Windows' default for all three values is prompting, so an
/// absent value cannot be read as "never prompt" and raises nothing; only a
/// WRITTEN 0 in EnableLUA or ConsentPromptBehaviorAdmin does. That is why
/// IsOn is overridden rather than left to walk Values.
///
/// PromptOnSecureDesktop is restored by the fix (to Windows' default) but
/// never decides the finding: the third position of Windows' own UAC slider
/// ("notify me, don't dim the desktop") sets it to 0 with
/// ConsentPromptBehaviorAdmin=5, and that is a choice, not a hole.
/// ConsentPromptBehaviorAdmin values 1..5 all prompt in some form; 0 alone
/// means "elevate without prompting".
///
/// Confirm, because the user will see prompts again and is told so first;
/// Confirm is also what keeps it out of `brisk fix --all`. Warning is the
/// family's one severity override; Kind stays Notice and the score does not
/// move (spec red line 5).
public sealed class UacPromptRule : TelemetrySwitchRule
{
    public const string RuleId = "uac-prompts";
    public const string KeyPath =
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    public const string EnableLua = "EnableLUA";
    public const string ConsentPromptBehaviorAdmin = "ConsentPromptBehaviorAdmin";
    public const string PromptOnSecureDesktop = "PromptOnSecureDesktop";

    public override string Id => RuleId;

    public override RuleCategory Category => RuleCategory.Confirm;

    protected override Severity Severity => Severity.Warning;

    /// OffValue is Windows' default for each; OnValue is the written 0 that
    /// means "never prompt". The family's Fix writes all three and records
    /// every prior value by path; its Undo restores them and deletes what
    /// was absent.
    public override IReadOnlyList<RegistryValue> Values { get; } = new[]
    {
        new RegistryValue(KeyPath, EnableLua, OnValue: 0, OffValue: 1),
        new RegistryValue(KeyPath, ConsentPromptBehaviorAdmin, OnValue: 0, OffValue: 5),
        new RegistryValue(KeyPath, PromptOnSecureDesktop, OnValue: 0, OffValue: 1),
    };

    /// The bad state, and nothing else: a written 0 in either of the two
    /// values that decide whether a prompt appears at all.
    public override bool IsOn(DiagnosticContext ctx) =>
        ctx.Registry.GetInt(KeyPath, EnableLua) == 0
        || ctx.Registry.GetInt(KeyPath, ConsentPromptBehaviorAdmin) == 0;

    protected override string Title =>
        "Windows does not ask before a program takes control";

    protected override string Evidence =>
        "brisk read Windows' three UAC values, and the one that decides " +
        "whether a program gets administrator rights with no prompt on screen " +
        "reads as \"never prompt\". Any program that asks for those rights " +
        "gets them, and nothing appears. Windows' default is to ask, on a " +
        "dimmed screen. Restoring the default writes up to three values and " +
        "can be undone. Programs will ask again; that is the point.";

    protected override string FixDescription =>
        "Ask before a program takes control again (undoable)";
}
```

- [ ] **Step 4: Register, route, and write the copy**

`DiagnosticRuleRegistry.cs`: after `new LocationRule(), new ActivityHistoryRule(),` add:

```csharp
        // The security switch. Confirm like the two above it, for the same
        // reason: the user will see consent prompts again and is told so
        // before it happens. Absence raises nothing here — see the rule.
        new UacPromptRule(),
```

`FindingSections.cs`: add `"uac-prompts",` to `PrivacyRuleIds.All` after `"delivery-optimization",`.

`Strings.resx` (next to the `rule.location.*` entries) — five keys:

```xml
  <data name="rule.uac-prompts.title" xml:space="preserve"><value>Windows does not ask before a program takes control</value></data>
  <data name="rule.uac-prompts.evidence" xml:space="preserve"><value>brisk read Windows' three UAC values, and the one that decides whether a program gets administrator rights with no prompt on screen reads as "never prompt". Any program that asks for those rights gets them, and nothing appears. Windows' default is to ask, on a dimmed screen. Restoring the default writes up to three values and can be undone. Programs will ask again; that is the point.</value></data>
  <data name="rule.uac-prompts.advice" xml:space="preserve"><value>brisk can put Windows' default back and can undo that. What changes for you: a dimmed screen and a question every time a program wants administrator rights. If EnableLUA was the value that read 0, the prompts return after a restart.</value></data>
  <data name="rule.uac-prompts.cost" xml:space="preserve"><value>Programs ask for permission again, on a dimmed screen</value></data>
  <data name="rule.uac-prompts.done" xml:space="preserve"><value>Windows asks again before a program takes control</value></data>
```

`Strings.tr.resx` — the same five keys:

```xml
  <data name="rule.uac-prompts.title" xml:space="preserve"><value>Windows, bir program kontrolü almadan önce sormuyor</value></data>
  <data name="rule.uac-prompts.evidence" xml:space="preserve"><value>brisk Windows'un üç UAC değerini okudu; bir programın ekranda hiçbir soru çıkmadan yönetici hakkı alıp alamayacağına karar veren değer "hiç sorma" olarak okunuyor. O hakkı isteyen her program alıyor ve ekranda hiçbir şey görünmüyor. Windows'un varsayılanı sormaktır, kararan bir ekranda. Varsayılanı geri getirmek en fazla üç değer yazar ve geri alınabilir. Programlar yeniden soracak; amaç zaten bu.</value></data>
  <data name="rule.uac-prompts.advice" xml:space="preserve"><value>brisk Windows'un varsayılanını geri koyabilir ve bunu geri alabilir. Senin için değişen şu: bir program yönetici hakkı istediğinde kararan bir ekran ve bir soru. 0 okunan değer EnableLUA idiyse sorular yeniden başlatmadan sonra döner.</value></data>
  <data name="rule.uac-prompts.cost" xml:space="preserve"><value>Programlar yeniden izin ister, kararan bir ekranda</value></data>
  <data name="rule.uac-prompts.done" xml:space="preserve"><value>Windows bir program kontrolü almadan önce yeniden soruyor</value></data>
```

- [ ] **Step 5: Extend the three pins that count switches**

`PrivacyRedLineTests.cs`, `EveryTelemetrySwitchRule_ShipsAnIdThePrivacyListCarries`: `Assert.Equal(6, switches.Count)` → `Assert.Equal(7, switches.Count)`.

`ReadBackTests.cs`, `DivergesFromItsOwnWrite`: add

```csharp
            // Overrides IsOn: absence and every non-zero number read as off,
            // and none of those is the 1/5/1 the fix writes.
            ["uac-prompts"] = true,
```

`ProgramFixTests.cs`, `CliFixAll_NeverReachesASwitchThatCostsTheUserSomething`: add `[InlineData("uac-prompts")]` under the two existing rows.

- [ ] **Step 6: Amend the spec sentence**

In the spec's `### 1. uac-prompts` section replace the paragraph starting `**Copy.** Title:` up to `An absent value renders as "not set" in the argument.` with:

```
**Copy.** Title: "Windows does not ask before a program takes control".
Evidence (fixed sentence, no arguments — the family's Detect passes none):
"brisk read Windows' three UAC values, and the one that decides whether a
program gets administrator rights with no prompt on screen reads as
"never prompt". Any program that asks for those rights gets them, and
nothing appears. Windows' default is to ask, on a dimmed screen. Restoring
the default writes up to three values and can be undone. Programs will ask
again; that is the point." Fix description: "Ask before a program takes
control again (undoable)". Done: "Windows asks again before a program takes
control". Cost line (Privacy page): "Programs ask for permission again, on
a dimmed screen".
```

- [ ] **Step 7: Run the full suite**

Run: `dotnet test brisk.sln -c Release --nologo -v q`
Expected: green, 0 warnings. New count = previous + 9 (UacPromptRuleTests) + 1 (the extra InlineData in ProgramFixTests) + 1 (the extra row in `EverySwitchThisBuildShips`) + 1 (`EveryShippedSwitch_KeepsInfo` row? no — that theory walks `AllSwitches` from the test file's own `Ids`, which does not list uac; leave it). If `LocTests` or a HealthViewModel test fails on a key or a count, read its message: it names the key or the number to update.

- [ ] **Step 8: Commit**

```bash
git add -A src docs/superpowers/specs
git commit -m "feat(rule): uac-prompts, a Confirm switch that restores Windows' consent prompt"
```

---

### Task 3: `camera-mic-use`

**Files:**
- Create: `src/BriskEngine/Diagnostics/Rules/Privacy/CapabilityUseRule.cs`
- Create: `src/BriskEngine.Tests/Rules/CapabilityUseRuleTests.cs`
- Modify: `DiagnosticRuleRegistry.cs`, both resx, `FindingSections.cs`, `PrivacyRedLineTests.cs` (`Assert.Equal(4, disclosures.Count)` → 5)
- Modify: `src/Brisk/Services/IEngineHost.cs` (`ScanSnapshot` gains `CapabilityUses`), `src/Brisk/Services/EngineHost.cs` (fills it), `src/Brisk.Tests/Fakes.cs` (`Snapshot` helpers), `src/Brisk.Tests/HealthViewModelTests.cs` and `OverviewViewModelTests.cs` (their one `new ScanSnapshot(` each)
- Modify: `src/Brisk/ViewModels/PrivacyViewModel.cs` (`CapabilityUseRows`), `src/Brisk/Views/PrivacyPage.xaml` (a second fold under the disclosure block), `src/Brisk.Tests/PrivacyViewModelTests.cs`

**Interfaces:**
- Consumes: `IRegistryProbe.GetLong` (Task 1), `PrivacyDisclosureRule.Disclosure(...)`, `Headline`.
- Produces: `enum Capability { Microphone, Camera }`; `record CapabilityUseRecord(Capability Capability, string AppName, string? Path, DateTime LastUsedUtc)`; `CapabilityUseRule.RuleId = "camera-mic-use"`; `static IReadOnlyList<CapabilityUseRecord>? CapabilityUseRule.ReadRecent(DiagnosticContext ctx, Capability capability, DateTime nowUtc)` (null = that record could not be read); `static IReadOnlyList<CapabilityUseRecord> CapabilityUseRule.ReadAllRecent(DiagnosticContext ctx, DateTime nowUtc)` (both capabilities, unreadable halves contribute nothing); `ScanSnapshot.CapabilityUses` (positional, after `BootTrend`).

- [ ] **Step 1: Write the failing engine tests**

`src/BriskEngine.Tests/Rules/CapabilityUseRuleTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

/// Which apps used the microphone and the camera in the last 30 days, by
/// Windows' own LastUsedTimeStop stamps. Numbers on the finding, names on
/// the snapshot list the Privacy page reads, and nowhere else.
public class CapabilityUseRuleTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private static (DiagnosticContext ctx, FakeRegistry reg) Context()
    {
        var reg = new FakeRegistry();
        return (TestContext.Empty() with { Registry = reg }, reg);
    }

    private static CapabilityUseRule Rule() => new(() => Now);

    /// The root Value is what a real machine always carries; planting it is
    /// how a test says "the record exists" without planting an app.
    private static void Store(FakeRegistry reg, string key)
    {
        reg.SetString(key, "Value", "Allow");
        if (!reg.SubKeys.ContainsKey(key)) reg.SubKeys[key] = new List<string>();
    }

    private static void Sub(FakeRegistry reg, string parent, string child)
    {
        if (!reg.SubKeys.TryGetValue(parent, out var kids)) reg.SubKeys[parent] = kids = new List<string>();
        if (!kids.Contains(child)) kids.Add(child);
    }

    private static void Packaged(FakeRegistry reg, string key, string family, DateTime? lastUsedUtc)
    {
        Store(reg, key);
        Sub(reg, key, family);
        if (lastUsedUtc is { } t)
            reg.SetLong($@"{key}\{family}", CapabilityUseRule.LastUsedValueName, t.ToFileTimeUtc());
    }

    private static void Desktop(FakeRegistry reg, string key, string exePath, DateTime? lastUsedUtc)
    {
        Store(reg, key);
        Sub(reg, key, CapabilityUseRule.NonPackaged);
        var hashed = exePath.Replace('\\', '#');
        Sub(reg, $@"{key}\{CapabilityUseRule.NonPackaged}", hashed);
        if (lastUsedUtc is { } t)
            reg.SetLong($@"{key}\{CapabilityUseRule.NonPackaged}\{hashed}",
                CapabilityUseRule.LastUsedValueName, t.ToFileTimeUtc());
    }

    private static IEnumerable<string> EverythingAReaderWouldSee(DiagnosticFinding f)
    {
        yield return f.Title;
        yield return f.Evidence;
        foreach (var arg in f.EvidenceArgs ?? Array.Empty<string>()) yield return arg;
        if (f.Headline is not { } h) yield break;
        yield return h.Value;
        yield return h.Caption;
        foreach (var arg in h.ValueArgs) yield return arg;
        foreach (var arg in h.CaptionArgs) yield return arg;
    }

    [Fact]
    public void CountsAppsInsideTheWindow_AndLeadsWithTheLargerCount()
    {
        var (ctx, reg) = Context();
        Desktop(reg, CapabilityUseRule.MicrophoneKey, @"C:\Program Files\Google\Chrome\Application\chrome.exe", Now.AddDays(-25));
        Desktop(reg, CapabilityUseRule.MicrophoneKey, @"C:\Users\U\AppData\Local\WisprFlow\app-1.6.447\Wispr Flow.exe", Now.AddDays(-25));
        Packaged(reg, CapabilityUseRule.WebcamKey, "5319275A.WhatsAppDesktop_cv1g1gvanyjgm", Now.AddDays(-4));

        var finding = Rule().Detect(ctx)!;

        Assert.Equal(CapabilityUseRule.RuleId, finding.RuleId);
        Assert.Equal(new[] { "2", "1" }, finding.EvidenceArgs);
        Assert.Equal("2", finding.Headline!.Value);
        Assert.Equal($"rule.{CapabilityUseRule.RuleId}.headline.caption.mic", finding.Headline.CaptionKey);
    }

    [Theory]
    [InlineData(29, true)]
    [InlineData(31, false)]
    public void TheWindowIsThirtyDays(int daysAgo, bool counted)
    {
        var (ctx, reg) = Context();
        Packaged(reg, CapabilityUseRule.MicrophoneKey, "A.App_x", Now.AddDays(-daysAgo));
        Store(reg, CapabilityUseRule.WebcamKey);

        var finding = Rule().Detect(ctx);
        Assert.Equal(counted, finding is not null);
    }

    [Fact]
    public void OneAppInstalledInManyVersions_CountsOnce_WithItsNewestDate()
    {
        var (ctx, reg) = Context();
        Desktop(reg, CapabilityUseRule.MicrophoneKey, @"C:\U\Discord\app-1.0.9241\Discord.exe", Now.AddDays(-20));
        Desktop(reg, CapabilityUseRule.MicrophoneKey, @"C:\U\Discord\app-1.0.9244\Discord.exe", Now.AddDays(-3));
        Desktop(reg, CapabilityUseRule.MicrophoneKey, @"C:\U\Discord\app-1.0.9200\Discord.exe", Now.AddDays(-29));
        Store(reg, CapabilityUseRule.WebcamKey);

        var records = CapabilityUseRule.ReadRecent(ctx, Capability.Microphone, Now)!;
        var one = Assert.Single(records);
        Assert.Equal("Discord.exe", one.AppName);
        Assert.Equal(Now.AddDays(-3), one.LastUsedUtc);
        Assert.Equal(@"C:\U\Discord\app-1.0.9244\Discord.exe", one.Path);
    }

    [Fact]
    public void APackagedApp_IsNamedByItsFamilyName_WithoutThePublisherHash()
    {
        var (ctx, reg) = Context();
        Packaged(reg, CapabilityUseRule.WebcamKey, "5319275A.WhatsAppDesktop_cv1g1gvanyjgm", Now.AddDays(-1));
        var one = Assert.Single(CapabilityUseRule.ReadRecent(ctx, Capability.Camera, Now)!);
        Assert.Equal("5319275A.WhatsAppDesktop", one.AppName);
        Assert.Null(one.Path);
    }

    [Fact]
    public void AZeroOrAbsentStamp_IsNeverUsed()
    {
        var (ctx, reg) = Context();
        Packaged(reg, CapabilityUseRule.MicrophoneKey, "Never.Used_x", null);
        Packaged(reg, CapabilityUseRule.MicrophoneKey, "Zero.Stamp_x", null);
        reg.SetLong($@"{CapabilityUseRule.MicrophoneKey}\Zero.Stamp_x", CapabilityUseRule.LastUsedValueName, 0);
        Store(reg, CapabilityUseRule.WebcamKey);

        Assert.Empty(CapabilityUseRule.ReadRecent(ctx, Capability.Microphone, Now)!);
        Assert.Null(Rule().Detect(ctx));
    }

    [Fact]
    public void BothRecordsUnreadable_ReportsUnreadable_WithNoDigits()
    {
        var (ctx, _) = Context();
        var finding = Rule().Detect(ctx)!;

        Assert.Equal($"rule.{CapabilityUseRule.RuleId}.title.unread", finding.TitleKey);
        Assert.Null(finding.Headline);
        var digits = string.Concat(EverythingAReaderWouldSee(finding).SelectMany(s => s.Where(char.IsDigit)));
        Assert.True(digits.Length == 0, $"unreadable finding carries digits: {digits}");
    }

    [Fact]
    public void OneRecordUnreadable_ReportsTheOther_AndSaysWhichWasNotRead()
    {
        var (ctx, reg) = Context();
        Packaged(reg, CapabilityUseRule.MicrophoneKey, "A.App_x", Now.AddDays(-1));
        // no webcam key at all

        var finding = Rule().Detect(ctx)!;
        Assert.Equal($"rule.{CapabilityUseRule.RuleId}.evidence.cam-unread", finding.EvidenceKey);
        Assert.Equal(new[] { "1" }, finding.EvidenceArgs);
        Assert.Equal("1", finding.Headline!.Value);
    }

    [Fact]
    public void ARegistryThatThrows_CostsThatRecordAndNotTheScan()
    {
        var reg = new FakeRegistry();
        var ctx = TestContext.Empty() with { Registry = new ThrowsOnWebcam(reg) };
        Packaged(reg, CapabilityUseRule.MicrophoneKey, "A.App_x", Now.AddDays(-1));

        var finding = Rule().Detect(ctx)!;
        Assert.Equal($"rule.{CapabilityUseRule.RuleId}.evidence.cam-unread", finding.EvidenceKey);
    }

    [Fact]
    public void NoNameOrPath_ReachesAnyFindingString_OrTheJson()
    {
        var (ctx, reg) = Context();
        Desktop(reg, CapabilityUseRule.MicrophoneKey, @"C:\Users\ZZZ-User\Desktop\ZZZ-Recorder.exe", Now.AddDays(-1));
        Packaged(reg, CapabilityUseRule.WebcamKey, "ZZZ.PackagedApp_abc123", Now.AddDays(-1));

        var finding = Rule().Detect(ctx)!;
        var everything = string.Join(" | ", EverythingAReaderWouldSee(finding))
            + " | " + JsonSerializer.Serialize(finding);
        foreach (var fragment in new[] { "ZZZ", "Recorder", "PackagedApp", "abc123", "Desktop" })
            Assert.DoesNotContain(fragment, everything, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadAllRecent_CarriesBothCapabilities_NewestFirst()
    {
        var (ctx, reg) = Context();
        Packaged(reg, CapabilityUseRule.MicrophoneKey, "Old.App_x", Now.AddDays(-10));
        Packaged(reg, CapabilityUseRule.WebcamKey, "New.App_x", Now.AddDays(-1));

        var all = CapabilityUseRule.ReadAllRecent(ctx, Now);
        Assert.Equal(new[] { "New.App", "Old.App" }, all.Select(r => r.AppName));
        Assert.Equal(Capability.Camera, all[0].Capability);
    }

    [Fact]
    public void TheFinding_IsAdvise_Notice_Info_AndRegisteredOnce()
    {
        var (ctx, reg) = Context();
        Packaged(reg, CapabilityUseRule.MicrophoneKey, "A.App_x", Now.AddDays(-1));
        Store(reg, CapabilityUseRule.WebcamKey);
        var rule = Rule();
        var finding = rule.Detect(ctx)!;

        Assert.Equal(RuleCategory.Advise, rule.Category);
        Assert.Equal(FindingKind.Notice, finding.Kind);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.False(finding.CanFix);
        Assert.Equal(100, HealthScore.Compute(new[] { finding }));
        Assert.Equal(1, DiagnosticRuleRegistry.All.Count(r => r.Id == CapabilityUseRule.RuleId));
    }

    /// A registry that refuses one capability's root, the way a locked key
    /// refuses OpenSubKey.
    private sealed class ThrowsOnWebcam : IRegistryProbe
    {
        private readonly FakeRegistry _inner;
        public ThrowsOnWebcam(FakeRegistry inner) => _inner = inner;
        private static void Guard(string k)
        {
            if (k.StartsWith(CapabilityUseRule.WebcamKey, StringComparison.OrdinalIgnoreCase))
                throw new System.Security.SecurityException("refused");
        }
        public string? GetString(string k, string v) { Guard(k); return _inner.GetString(k, v); }
        public void SetString(string k, string v, string value) => _inner.SetString(k, v, value);
        public void DeleteValue(string k, string v) => _inner.DeleteValue(k, v);
        public byte[]? GetBytes(string k, string v) { Guard(k); return _inner.GetBytes(k, v); }
        public void SetBytes(string k, string v, byte[] value) => _inner.SetBytes(k, v, value);
        public int? GetInt(string k, string v) { Guard(k); return _inner.GetInt(k, v); }
        public void SetInt(string k, string v, int value) => _inner.SetInt(k, v, value);
        public long? GetLong(string k, string v) { Guard(k); return _inner.GetLong(k, v); }
        public IReadOnlyList<string> GetValueNames(string k) { Guard(k); return _inner.GetValueNames(k); }
        public IReadOnlyList<string> GetSubKeyNames(string k) { Guard(k); return _inner.GetSubKeyNames(k); }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests -c Release --nologo -v q`
Expected: compile error, `CapabilityUseRule` / `Capability` do not exist.

- [ ] **Step 3: Write the rule**

`src/BriskEngine/Diagnostics/Rules/Privacy/CapabilityUseRule.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

public enum Capability { Microphone, Camera }

/// One app that used the microphone or the camera inside the window, as
/// Windows recorded it. THE ONLY SHAPE AN APP NAME OR PATH TRAVELS IN, and
/// it travels one way: EngineHost puts these on ScanSnapshot.CapabilityUses
/// and the Privacy page renders them behind a fold. Nothing here is on a
/// DiagnosticFinding, so no report card, headline or JSON can carry a name
/// however the rest of the pipeline is wired — spec red line 2, held by
/// construction. Path is set for desktop apps only; packaged apps have none.
public sealed record CapabilityUseRecord(
    Capability Capability, string AppName, string? Path, DateTime LastUsedUtc);

/// Which apps used the microphone and the camera in the last 30 days, from
/// CapabilityAccessManager's consent store: one subkey per packaged app (a
/// package family name) and, under NonPackaged, one per desktop executable
/// with '#' where its path has '\'. Each carries LastUsedTimeStop as a
/// FILETIME QWORD; 0 or absent means never used.
///
/// ONE APP COUNTS ONCE. Discord and Wispr Flow keep one subkey per installed
/// version, and the machine this was written on holds 40 Discord entries for
/// one program. Desktop apps group by file name, packaged apps by the part
/// of the family name before the underscore; the newest stamp in a group is
/// the record's date.
///
/// UNREADABLE IS NOT ZERO. An empty subkey list is what the probe answers
/// for a missing key and for an empty one alike, so the root's own Value
/// (Allow/Deny — present on every real machine) is the second read that
/// tells them apart: no subkeys AND no Value is a record brisk could not
/// read. A throw anywhere under one capability costs that capability and
/// nothing else.
///
/// The clock is injected so the window can be tested at its boundary;
/// nothing else in the rule reads one.
public sealed class CapabilityUseRule : PrivacyDisclosureRule
{
    public const string RuleId = "camera-mic-use";
    public const string StoreRoot =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    public const string MicrophoneKey = StoreRoot + @"\microphone";
    public const string WebcamKey = StoreRoot + @"\webcam";
    public const string NonPackaged = "NonPackaged";
    public const string LastUsedValueName = "LastUsedTimeStop";
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    private readonly Func<DateTime> _utcNow;

    public CapabilityUseRule() : this(() => DateTime.UtcNow) { }

    public CapabilityUseRule(Func<DateTime> utcNow) => _utcNow = utcNow;

    public override string Id => RuleId;

    public static string KeyFor(Capability capability) =>
        capability == Capability.Microphone ? MicrophoneKey : WebcamKey;

    /// The apps that used one capability inside the window, newest first,
    /// or null when that record could not be read at all.
    public static IReadOnlyList<CapabilityUseRecord>? ReadRecent(
        DiagnosticContext ctx, Capability capability, DateTime nowUtc)
    {
        try
        {
            var key = KeyFor(capability);
            var names = ctx.Registry.GetSubKeyNames(key);
            var rootValue = ctx.Registry.GetString(key, "Value");
            if (names.Count == 0 && rootValue is null) return null;

            var found = new Dictionary<string, CapabilityUseRecord>(StringComparer.OrdinalIgnoreCase);
            void Consider(string subKey, string appName, string? path)
            {
                var stamp = ctx.Registry.GetLong(subKey, LastUsedValueName);
                if (stamp is null or <= 0) return;
                DateTime used;
                try { used = DateTime.FromFileTimeUtc(stamp.Value); }
                catch (ArgumentOutOfRangeException) { return; }
                if (used < nowUtc - Window) return;
                if (!found.TryGetValue(appName, out var prior) || prior.LastUsedUtc < used)
                    found[appName] = new CapabilityUseRecord(capability, appName, path, used);
            }

            foreach (var name in names)
            {
                if (string.Equals(name, NonPackaged, StringComparison.OrdinalIgnoreCase))
                {
                    var branch = $@"{key}\{NonPackaged}";
                    foreach (var exe in ctx.Registry.GetSubKeyNames(branch))
                    {
                        var path = exe.Replace('#', '\\');
                        Consider($@"{branch}\{exe}", System.IO.Path.GetFileName(path), path);
                    }
                }
                else
                {
                    var family = name.Split('_')[0];
                    Consider($@"{key}\{name}", family, null);
                }
            }
            return found.Values.OrderByDescending(r => r.LastUsedUtc).ToList();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// Both capabilities in one list, newest first. An unreadable half adds
    /// nothing; the finding is where "could not read" is said.
    public static IReadOnlyList<CapabilityUseRecord> ReadAllRecent(
        DiagnosticContext ctx, DateTime nowUtc) =>
        (ReadRecent(ctx, Capability.Microphone, nowUtc) ?? Array.Empty<CapabilityUseRecord>())
        .Concat(ReadRecent(ctx, Capability.Camera, nowUtc) ?? Array.Empty<CapabilityUseRecord>())
        .OrderByDescending(r => r.LastUsedUtc)
        .ToList();

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var now = _utcNow();
        var mic = ReadRecent(ctx, Capability.Microphone, now);
        var cam = ReadRecent(ctx, Capability.Camera, now);
        if (mic is null && cam is null) return Unread();

        var micCount = mic?.Count ?? 0;
        var camCount = cam?.Count ?? 0;
        if (micCount == 0 && camCount == 0 && mic is not null && cam is not null) return null;

        var micText = micCount.ToString(CultureInfo.InvariantCulture);
        var camText = camCount.ToString(CultureInfo.InvariantCulture);
        var headline = HeadlineFor(micCount, camCount);

        if (mic is null)
            return Disclosure($"rule.{Id}.title", TitleText,
                $"rule.{Id}.evidence.mic-unread",
                $"By Windows' own record of when each app last used it, {camText} apps " +
                "used the camera in the last 30 days. The microphone record could not " +
                "be read, so brisk says nothing about it. " + Trailer,
                new[] { camText }, headline);
        if (cam is null)
            return Disclosure($"rule.{Id}.title", TitleText,
                $"rule.{Id}.evidence.cam-unread",
                $"By Windows' own record of when each app last used it, {micText} apps " +
                "used the microphone in the last 30 days. The camera record could not " +
                "be read, so brisk says nothing about it. " + Trailer,
                new[] { micText }, headline);
        return Disclosure($"rule.{Id}.title", TitleText,
            $"rule.{Id}.evidence",
            $"By Windows' own record of when each app last used them, {micText} apps " +
            $"used the microphone and {camText} used the camera in the last 30 days. " +
            Trailer,
            new[] { micText, camText }, headline);
    }

    private const string TitleText = "Apps have used the microphone or camera recently";

    private const string Trailer =
        "Which ones, and when, is on this page and on nothing built to be shared. " +
        "brisk reads the record and nothing past it: what an app did with the " +
        "microphone or the camera is not something that read can tell you.";

    /// The larger count leads; a tie goes to the microphone. Null when there
    /// is nothing to lead with, which puts the row in the unreadable band.
    private Headline? HeadlineFor(int micCount, int camCount)
    {
        if (micCount == 0 && camCount == 0) return null;
        var mic = micCount >= camCount;
        var n = (mic ? micCount : camCount).ToString(CultureInfo.InvariantCulture);
        return new Headline(
            n,
            mic ? "apps used the microphone in the last 30 days"
                : "apps used the camera in the last 30 days",
            $"rule.{Id}.headline.value", new[] { n },
            $"rule.{Id}.headline.caption.{(mic ? "mic" : "cam")}", Array.Empty<string>());
    }

    /// No digits anywhere in this one: a reading that never arrived is not
    /// reported with a number, and the window is not mentioned by its length.
    private DiagnosticFinding Unread() => Disclosure(
        $"rule.{Id}.title.unread",
        "Which apps used the microphone or camera could not be established",
        $"rule.{Id}.evidence.unread",
        "brisk looked where Windows records when each app last used the " +
        "microphone and the camera and found nothing there it could read. A " +
        "record with nothing in it and a record brisk could not open read the " +
        "same from here, so brisk does not report that no app used them.");
}
```

- [ ] **Step 4: Register, route, and write the copy**

`DiagnosticRuleRegistry.cs`: after `new DeliveryOptimizationRule(),` add:

```csharp
        // The fifth disclosure: which apps used the microphone and camera
        // in the last 30 days, counted here, named on the Privacy page only.
        new CapabilityUseRule(),
```

`FindingSections.cs`: add `"camera-mic-use",` to `PrivacyRuleIds.All`.

`PrivacyRedLineTests.cs`: `Assert.Equal(4, disclosures.Count)` → `5`.

`Strings.resx` (after the `rule.delivery-optimization.*` entries):

```xml
  <data name="rule.camera-mic-use.title" xml:space="preserve"><value>Apps have used the microphone or camera recently</value></data>
  <data name="rule.camera-mic-use.title.unread" xml:space="preserve"><value>Which apps used the microphone or camera could not be established</value></data>
  <data name="rule.camera-mic-use.evidence" xml:space="preserve"><value>By Windows' own record of when each app last used them, {0} apps used the microphone and {1} used the camera in the last 30 days. Which ones, and when, is on this page and on nothing built to be shared. brisk reads the record and nothing past it: what an app did with the microphone or the camera is not something that read can tell you.</value></data>
  <data name="rule.camera-mic-use.evidence.mic-unread" xml:space="preserve"><value>By Windows' own record of when each app last used it, {0} apps used the camera in the last 30 days. The microphone record could not be read, so brisk says nothing about it. Which ones, and when, is on this page and on nothing built to be shared.</value></data>
  <data name="rule.camera-mic-use.evidence.cam-unread" xml:space="preserve"><value>By Windows' own record of when each app last used it, {0} apps used the microphone in the last 30 days. The camera record could not be read, so brisk says nothing about it. Which ones, and when, is on this page and on nothing built to be shared.</value></data>
  <data name="rule.camera-mic-use.evidence.unread" xml:space="preserve"><value>brisk looked where Windows records when each app last used the microphone and the camera and found nothing there it could read. A record with nothing in it and a record brisk could not open read the same from here, so brisk does not report that no app used them.</value></data>
  <data name="rule.camera-mic-use.advice" xml:space="preserve"><value>brisk only reads this record; it does not change it and offers no fix for it. The list below says which apps and when. If a name there surprises you, that is the thing to look into — brisk cannot tell you what the app did with the microphone or the camera.</value></data>
  <data name="rule.camera-mic-use.headline.value" xml:space="preserve"><value>{0}</value></data>
  <data name="rule.camera-mic-use.headline.caption.mic" xml:space="preserve"><value>apps used the microphone in the last 30 days</value></data>
  <data name="rule.camera-mic-use.headline.caption.cam" xml:space="preserve"><value>apps used the camera in the last 30 days</value></data>
  <data name="privacy.capability.title" xml:space="preserve"><value>Which apps, and when</value></data>
  <data name="privacy.capability.use" xml:space="preserve"><value>{0} — {1} · last used {2}</value></data>
  <data name="privacy.capability.microphone" xml:space="preserve"><value>microphone</value></data>
  <data name="privacy.capability.camera" xml:space="preserve"><value>camera</value></data>
```

`Strings.tr.resx`:

```xml
  <data name="rule.camera-mic-use.title" xml:space="preserve"><value>Uygulamalar yakın zamanda mikrofonu veya kamerayı kullandı</value></data>
  <data name="rule.camera-mic-use.title.unread" xml:space="preserve"><value>Mikrofonu veya kamerayı hangi uygulamaların kullandığı belirlenemedi</value></data>
  <data name="rule.camera-mic-use.evidence" xml:space="preserve"><value>Windows'un her uygulamanın en son ne zaman kullandığını tuttuğu kayda göre son 30 günde {0} uygulama mikrofonu, {1} uygulama kamerayı kullandı. Hangileri ve ne zaman, bu sayfada; paylaşılmak için yapılmış hiçbir yüzeyde değil. brisk kaydı okur, ötesini okumaz: bir uygulamanın mikrofonla veya kamerayla ne yaptığı bu okumadan çıkmaz.</value></data>
  <data name="rule.camera-mic-use.evidence.mic-unread" xml:space="preserve"><value>Windows'un her uygulamanın en son ne zaman kullandığını tuttuğu kayda göre son 30 günde {0} uygulama kamerayı kullandı. Mikrofon kaydı okunamadı, bu yüzden brisk onun hakkında bir şey söylemiyor. Hangileri ve ne zaman, bu sayfada; paylaşılmak için yapılmış hiçbir yüzeyde değil.</value></data>
  <data name="rule.camera-mic-use.evidence.cam-unread" xml:space="preserve"><value>Windows'un her uygulamanın en son ne zaman kullandığını tuttuğu kayda göre son 30 günde {0} uygulama mikrofonu kullandı. Kamera kaydı okunamadı, bu yüzden brisk onun hakkında bir şey söylemiyor. Hangileri ve ne zaman, bu sayfada; paylaşılmak için yapılmış hiçbir yüzeyde değil.</value></data>
  <data name="rule.camera-mic-use.evidence.unread" xml:space="preserve"><value>brisk, Windows'un her uygulamanın mikrofonu ve kamerayı en son ne zaman kullandığını kaydettiği yere baktı ve orada okuyabildiği bir şey bulamadı. İçinde hiçbir şey olmayan bir kayıt ile brisk'in açamadığı bir kayıt buradan aynı okunur; bu yüzden brisk hiçbir uygulamanın kullanmadığını söylemez.</value></data>
  <data name="rule.camera-mic-use.advice" xml:space="preserve"><value>brisk bu kaydı yalnızca okur; değiştirmez ve düzeltme önermez. Aşağıdaki liste hangi uygulamaların ne zaman kullandığını söyler. Orada seni şaşırtan bir isim varsa bakılacak şey odur; uygulamanın mikrofonla veya kamerayla ne yaptığını brisk söyleyemez.</value></data>
  <data name="rule.camera-mic-use.headline.value" xml:space="preserve"><value>{0}</value></data>
  <data name="rule.camera-mic-use.headline.caption.mic" xml:space="preserve"><value>uygulama son 30 günde mikrofonu kullandı</value></data>
  <data name="rule.camera-mic-use.headline.caption.cam" xml:space="preserve"><value>uygulama son 30 günde kamerayı kullandı</value></data>
  <data name="privacy.capability.title" xml:space="preserve"><value>Hangi uygulamalar, ne zaman</value></data>
  <data name="privacy.capability.use" xml:space="preserve"><value>{0} — {1} · son kullanım {2}</value></data>
  <data name="privacy.capability.microphone" xml:space="preserve"><value>mikrofon</value></data>
  <data name="privacy.capability.camera" xml:space="preserve"><value>kamera</value></data>
```

- [ ] **Step 5: Run the engine tests**

Run: `dotnet test src/BriskEngine.Tests -c Release --nologo -v q`
Expected: green. (The GUI project will not compile yet if anything references the snapshot — it does not until Step 6.)

- [ ] **Step 6: Write the failing GUI test — the names ride the snapshot, not the finding**

Append to `src/Brisk.Tests/PrivacyViewModelTests.cs` (read its top 60 lines first for how a `PrivacyViewModel` is built there — reuse that helper, do not build a second one):

```csharp
    [Fact]
    public void CapabilityUses_RenderBehindTheFold_NewestFirst_WithNameCapabilityAndDate()
    {
        var uses = new[]
        {
            new CapabilityUseRecord(Capability.Camera, "5319275A.WhatsAppDesktop", null,
                new DateTime(2026, 9, 1, 14, 59, 0, DateTimeKind.Utc)),
            new CapabilityUseRecord(Capability.Microphone, "chrome.exe",
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                new DateTime(2026, 8, 11, 16, 48, 0, DateTimeKind.Utc)),
        };
        var vm = BuildViewModel(Fakes.Snapshot(findings: null, capabilityUses: uses));

        Assert.Equal(2, vm.CapabilityUseRows.Count);
        Assert.Contains("5319275A.WhatsAppDesktop", vm.CapabilityUseRows[0]);
        Assert.Contains("camera", vm.CapabilityUseRows[0]);
        Assert.Contains("2026-09-01", vm.CapabilityUseRows[0]);
        Assert.Contains("chrome.exe", vm.CapabilityUseRows[1]);
        Assert.Contains(@"C:\Program Files\Google\Chrome\Application\chrome.exe", vm.CapabilityUseRows[1]);
    }
```

`BuildViewModel(snapshot)` is whatever that test file already names its builder; if it takes an `AppState` with a snapshot, set the snapshot the way the USB test (`UsbDeviceRows`) in the same file does — copy that test's setup lines exactly.

- [ ] **Step 7: Extend the snapshot, the host, and the fakes**

`src/Brisk/Services/IEngineHost.cs`, `ScanSnapshot`: append a positional member after `BootTrend? BootTrend`:

```csharp
    BootTrend? BootTrend,
    // The apps that used the microphone or camera in the last 30 days, by
    // name — the ONE channel an app name travels on, for the same reason and
    // on the same terms as UsbDevices above. Required: an empty list says
    // "brisk read no such use", and there is no default that says less.
    IReadOnlyList<CapabilityUseRecord> CapabilityUses);
```

(Add `using BriskEngine.Diagnostics.Rules.Privacy;` at the top if `UsbDeviceRecord` is not already imported that way.)

`EngineHost.cs`, `ScanAsync`: directly after the `usbDevices` block add

```csharp
        IReadOnlyList<CapabilityUseRecord> capabilityUses;
        try
        {
            capabilityUses = CapabilityUseRule.ReadAllRecent(_ctx, DateTime.UtcNow);
        }
        catch (Exception)
        {
            capabilityUses = Array.Empty<CapabilityUseRecord>();
        }
```

and pass `capabilityUses` as the last argument of `new ScanSnapshot(...)`.

`src/Brisk.Tests/Fakes.cs`: add a default `private static readonly CapabilityUseRecord[] NoUsesRead = Array.Empty<CapabilityUseRecord>();`, give the "whole shape" `Snapshot(...)` overload a new parameter `IReadOnlyList<CapabilityUseRecord>? capabilityUses = null` placed BEFORE `params TargetScanResult[] targets`, and pass `capabilityUses ?? NoUsesRead` as the last constructor argument. Add an optional-parameter overload so the test above can call `Fakes.Snapshot(findings: null, capabilityUses: uses)`:

```csharp
    public static ScanSnapshot Snapshot(IReadOnlyList<DiagnosticFinding>? findings,
        IReadOnlyList<CapabilityUseRecord> capabilityUses) =>
        Snapshot(findings, NothingAnswered, NothingReRead, NoDevicesRead, capabilityUses);
```

`HealthViewModelTests.cs` and `OverviewViewModelTests.cs`: their one `new ScanSnapshot(` each gets `Array.Empty<CapabilityUseRecord>()` appended as the last argument.

- [ ] **Step 8: Rows and the fold**

`PrivacyViewModel.cs`: next to `UsbDeviceRows` declare

```csharp
    /// Name, capability, date — one line per app that used the microphone
    /// or camera in the window, newest first, off ScanSnapshot.CapabilityUses
    /// and never off a finding. Desktop apps carry their path on the line:
    /// this page is the owner's own screen, and "Desktop\system-helper.exe"
    /// is the whole point of showing it.
    public ObservableCollection<string> CapabilityUseRows { get; } = new();
```

In `Refresh()`, after the `UsbDeviceRows` loop:

```csharp
        CapabilityUseRows.Clear();
        foreach (var use in snapshot.CapabilityUses)
        {
            var what = _loc[use.Capability == Capability.Microphone
                ? "privacy.capability.microphone" : "privacy.capability.camera"];
            var name = use.Path is null ? use.AppName : $"{use.AppName}  ({use.Path})";
            CapabilityUseRows.Add(_loc.F("privacy.capability.use", name, what,
                use.LastUsedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }
```

`PrivacyPage.xaml`: directly after the USB fold's closing `</StackPanel>` (the one that starts `<StackPanel Margin="0,6,0,16">`), add a second fold with the same markup, substituting: `UsbDeviceRows.Count` → `CapabilityUseRows.Count`, `x:Name="UsbDevicesFold"` → `x:Name="CapabilityUsesFold"`, `[privacy.usb.devices.title]` → `[privacy.capability.title]`, `ItemsSource="{Binding UsbDeviceRows}"` → `ItemsSource="{Binding CapabilityUseRows}"`, `ElementName=UsbDevicesFold` → `ElementName=CapabilityUsesFold`. Keep the same styles (Faint label, Muted rows, TextWrapping).

- [ ] **Step 9: Run the full suite**

Run: `dotnet test brisk.sln -c Release --nologo -v q`
Expected: green, 0 warnings. `ReportCardModelTests` and `PrivacyRedLineTests` must still pass: the card reads no `CapabilityUses`.

- [ ] **Step 10: Commit**

```bash
git add -A src
git commit -m "feat(rule): camera-mic-use, which apps used the microphone and camera in 30 days"
```

---

### Task 4: `defender-blind-spots`

**Files:**
- Create: `src/BriskEngine/Diagnostics/IDefenderProbe.cs`, `src/BriskEngine/Diagnostics/RealProbes/RealDefenderProbe.cs`, `src/BriskEngine/Diagnostics/Rules/Privacy/DefenderRule.cs`, `src/BriskEngine.Tests/Rules/DefenderRuleTests.cs`
- Modify: `src/BriskEngine/Diagnostics/DiagnosticContext.cs` (new positional `IDefenderProbe Defender` before `DataDirectory`) and its construction sites: `src/Brisk/Services/AppServices.cs:35-41`, `src/Brisk.Cli/Program.cs:61-67`, `src/BriskEngine.Tests/TestContext.cs` (`Empty`, plus a `FakeDefender`), `src/Brisk.Tests/EngineHostTests.cs` (`Host`, plus a `file sealed class NullDefender`), `src/Brisk.Tests/Fakes.cs` (`NoOtherProbes` implements `IDefenderProbe`; the `new DiagnosticContext(none, ...)` call gains one more `none`), `src/Brisk.Tests/PrivacyRedLineTests.cs:898` (same)
- Modify: `DiagnosticRuleRegistry.cs`, both resx, `FindingSections.cs`, `PrivacyRedLineTests.cs` (disclosures 5 → 6), `src/Brisk/ViewModels/HealthViewModel.cs` (`WindowsSettingRules`), `IEngineHost.cs` (`ScanSnapshot.DefenderExclusions`), `EngineHost.cs`, `Fakes.cs`, `HealthViewModelTests.cs`, `OverviewViewModelTests.cs`, `PrivacyViewModel.cs` (`DefenderExclusionRows`), `PrivacyPage.xaml` (third fold), `PrivacyViewModelTests.cs`

**Interfaces:**
- Produces: `record DefenderStatus(bool? RealTimeOn, bool? TamperOn, string? RunningMode, int? ExclusionCount, IReadOnlyList<string> ExclusionPaths, DateTime? LastQuickScanUtc, DateTime? LastFullScanUtc, int? SignatureAgeDays)`; `interface IDefenderProbe { DefenderStatus? Read(); }`; `DefenderRule.RuleId = "defender-blind-spots"`; `static DefenderStatus? DefenderRule.Status(DiagnosticContext ctx)` (memoized in `ctx.Memo["defender"]`, one WMI read per scan — Task 6 reads it); `ScanSnapshot.DefenderExclusions` (positional, after `CapabilityUses`); `FakeDefender { DefenderStatus? Status; }` in TestContext.

- [ ] **Step 1: Write the failing engine tests**

`src/BriskEngine.Tests/Rules/DefenderRuleTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

/// What Defender skips and whether it is on, read through a probe. Every
/// trigger fires alone; the all-fine shape stays silent; unreadable is never
/// zero; no excluded path reaches a finding string or the JSON.
public class DefenderRuleTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private static DefenderStatus Fine() => new(
        RealTimeOn: true, TamperOn: true, RunningMode: "Normal", ExclusionCount: 0,
        ExclusionPaths: Array.Empty<string>(),
        LastQuickScanUtc: Now.AddDays(-2), LastFullScanUtc: Now.AddDays(-21), SignatureAgeDays: 0);

    private static (DiagnosticContext ctx, FakeDefender probe) Context(DefenderStatus? status)
    {
        var probe = new FakeDefender { Status = status };
        return (TestContext.Empty() with { Defender = probe }, probe);
    }

    private static DefenderRule Rule() => new(() => Now);

    private static IEnumerable<string> EverythingAReaderWouldSee(DiagnosticFinding f)
    {
        yield return f.Title;
        yield return f.Evidence;
        foreach (var arg in f.EvidenceArgs ?? Array.Empty<string>()) yield return arg;
        if (f.Headline is not { } h) yield break;
        yield return h.Value;
        yield return h.Caption;
        foreach (var arg in h.ValueArgs) yield return arg;
        foreach (var arg in h.CaptionArgs) yield return arg;
    }

    [Fact]
    public void AllFine_IsNoFinding()
    {
        var (ctx, _) = Context(Fine());
        Assert.Null(Rule().Detect(ctx));
    }

    public static TheoryData<string, DefenderStatus> EachTriggerAlone()
    {
        var f = Fine();
        return new TheoryData<string, DefenderStatus>
        {
            { "exclusions", f with { ExclusionCount = 5, ExclusionPaths = new[] { @"C:\a", @"C:\b", @"C:\c", @"C:\d", @"C:\e" } } },
            { "exclusions unread", f with { ExclusionCount = null } },
            { "real-time off", f with { RealTimeOn = false } },
            { "tamper off", f with { TamperOn = false } },
            { "passive mode", f with { RunningMode = "Passive Mode" } },
            { "stale scan", f with { LastQuickScanUtc = Now.AddDays(-15), LastFullScanUtc = Now.AddDays(-40) } },
            { "no scan on record", f with { LastQuickScanUtc = null, LastFullScanUtc = null } },
        };
    }

    [Theory]
    [MemberData(nameof(EachTriggerAlone))]
    public void EachTrigger_FiresAlone(string why, DefenderStatus status)
    {
        var (ctx, _) = Context(status);
        var finding = Rule().Detect(ctx);
        Assert.True(finding is not null, $"{why} did not fire");
        Assert.Equal($"rule.{DefenderRule.RuleId}.title", finding!.TitleKey);
    }

    [Fact]
    public void FourteenDays_IsTheScanAgeBoundary()
    {
        var (fresh, _) = Context(Fine() with { LastQuickScanUtc = Now.AddDays(-14), LastFullScanUtc = null });
        var (stale, _) = Context(Fine() with { LastQuickScanUtc = Now.AddDays(-15), LastFullScanUtc = null });
        Assert.Null(Rule().Detect(fresh));
        Assert.NotNull(Rule().Detect(stale));
    }

    [Fact]
    public void Exclusions_LeadTheHeadline_AndTheEvidenceCarriesTheCount()
    {
        var (ctx, _) = Context(Fine() with { ExclusionCount = 5, ExclusionPaths = new[] { @"C:\ZZZ-excluded" } });
        var finding = Rule().Detect(ctx)!;
        Assert.Equal("5", finding.Headline!.Value);
        Assert.Equal($"rule.{DefenderRule.RuleId}.headline.caption", finding.Headline.CaptionKey);
        Assert.Equal($"rule.{DefenderRule.RuleId}.evidence", finding.EvidenceKey);
        Assert.Equal(new[] { "5", "0", "0", "Normal", "2" }, finding.EvidenceArgs);
    }

    [Fact]
    public void ExclusionsUnread_SaysSo_AndNeverSaysZero()
    {
        var (ctx, _) = Context(Fine() with { ExclusionCount = null });
        var finding = Rule().Detect(ctx)!;
        Assert.Equal($"rule.{DefenderRule.RuleId}.evidence.exclusions-unread", finding.EvidenceKey);
        Assert.Null(finding.Headline);
        Assert.DoesNotContain("0 paths", finding.Evidence, StringComparison.Ordinal);
        Assert.Contains("administrator", finding.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoScanOnRecord_UsesTheNoScanKey()
    {
        var (ctx, _) = Context(Fine() with { LastQuickScanUtc = null, LastFullScanUtc = null });
        var finding = Rule().Detect(ctx)!;
        Assert.Equal($"rule.{DefenderRule.RuleId}.evidence.no-scan", finding.EvidenceKey);
        Assert.Equal(new[] { "0", "0", "0", "Normal" }, finding.EvidenceArgs);
    }

    [Fact]
    public void ProtectionsOff_AreCountedInTheEvidence()
    {
        var (ctx, _) = Context(Fine() with { RealTimeOn = false, TamperOn = null });
        var finding = Rule().Detect(ctx)!;
        // {1} = protections reading off, {2} = protections brisk could not read
        Assert.Equal(new[] { "0", "1", "1", "Normal", "2" }, finding.EvidenceArgs);
    }

    [Fact]
    public void WholeReadFailed_ReportsUnreadable_WithNoDigits()
    {
        var (ctx, _) = Context(null);
        var finding = Rule().Detect(ctx)!;
        Assert.Equal($"rule.{DefenderRule.RuleId}.title.unread", finding.TitleKey);
        Assert.Null(finding.Headline);
        var digits = string.Concat(EverythingAReaderWouldSee(finding).SelectMany(s => s.Where(char.IsDigit)));
        Assert.True(digits.Length == 0, $"unreadable finding carries digits: {digits}");
    }

    [Fact]
    public void NoExcludedPath_ReachesAnyFindingString_OrTheJson()
    {
        var (ctx, _) = Context(Fine() with { ExclusionCount = 2, ExclusionPaths = new[] { @"C:\ZZZ-excluded", @"D:\ZZZ-two" } });
        var finding = Rule().Detect(ctx)!;
        var everything = string.Join(" | ", EverythingAReaderWouldSee(finding))
            + " | " + JsonSerializer.Serialize(finding);
        Assert.DoesNotContain("ZZZ", everything, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Status_IsReadOnce_PerScan()
    {
        var (ctx, probe) = Context(Fine() with { ExclusionCount = 1 });
        Rule().Detect(ctx);
        Rule().Detect(ctx);
        var again = DefenderRule.Status(ctx);
        Assert.Equal(1, probe.Reads);
        Assert.NotNull(again);
    }

    [Fact]
    public void TheFinding_IsAdvise_Notice_Info_AndRegisteredOnce()
    {
        var (ctx, _) = Context(Fine() with { ExclusionCount = 1 });
        var rule = Rule();
        var finding = rule.Detect(ctx)!;
        Assert.Equal(RuleCategory.Advise, rule.Category);
        Assert.Equal(FindingKind.Notice, finding.Kind);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal(100, HealthScore.Compute(new[] { finding }));
        Assert.Equal(1, DiagnosticRuleRegistry.All.Count(r => r.Id == DefenderRule.RuleId));
    }
}
```

Add to `TestContext.cs`:

```csharp
/// Null by default: a test that never speaks to Defender says nothing about
/// it, and null is how the probe says it could not read. Reads counts the
/// calls so the memo can be seen working.
public sealed class FakeDefender : IDefenderProbe
{
    public DefenderStatus? Status;
    public int Reads;
    public DefenderStatus? Read() { Reads++; return Status; }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests -c Release --nologo -v q`
Expected: compile errors (`IDefenderProbe`, `DefenderStatus`, `DefenderRule`, `Defender` on the context).

- [ ] **Step 3: The probe interface, the context member, and the real probe**

`src/BriskEngine/Diagnostics/IDefenderProbe.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace BriskEngine.Diagnostics;

/// What Windows Defender says about itself, through WMI. Every member is
/// nullable on its own: a read that answered for real-time protection and
/// not for the exclusions reports exactly that. The whole record is null
/// when nothing could be read (no Defender, namespace missing, a throw).
///
/// ExclusionCount covers paths, extensions and processes; ExclusionPaths
/// carries the paths alone, for the Privacy page's fold and for nothing
/// else. Windows hides the exclusion list from non-administrators as an
/// EMPTY list, not an error, so the real probe reports ExclusionCount null
/// when it is not elevated rather than a zero it did not read.
public sealed record DefenderStatus(
    bool? RealTimeOn,
    bool? TamperOn,
    string? RunningMode,
    int? ExclusionCount,
    IReadOnlyList<string> ExclusionPaths,
    DateTime? LastQuickScanUtc,
    DateTime? LastFullScanUtc,
    int? SignatureAgeDays);

public interface IDefenderProbe
{
    DefenderStatus? Read();
}
```

`DiagnosticContext.cs`: add `IDefenderProbe Defender,` directly before `string DataDirectory)`.

`src/BriskEngine/Diagnostics/RealProbes/RealDefenderProbe.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Management;
using System.Security.Principal;

namespace BriskEngine.Diagnostics.RealProbes;

/// Two WMI classes under root\Microsoft\Windows\Defender, the same scope
/// Get-MpComputerStatus and Get-MpPreference read. Verified 2026-09-05 on the
/// maintainer's machine: elevated, ExclusionPath came back with five entries
/// and the registry agreed; RealTimeProtectionEnabled and IsTamperProtected
/// both true; AMRunningMode "Normal".
///
/// The exclusion query runs only when the process is elevated. Windows
/// answers a non-administrator with an empty list rather than an error, and
/// an empty list brisk did not earn would be reported as "nothing skipped".
public sealed class RealDefenderProbe : IDefenderProbe
{
    private const string Scope = @"root\Microsoft\Windows\Defender";

    public DefenderStatus? Read()
    {
        try
        {
            bool? realTime = null, tamper = null;
            string? mode = null;
            DateTime? quick = null, full = null;
            int? signatureAge = null;
            var answered = false;
            using (var searcher = new ManagementObjectSearcher(new ManagementScope(Scope),
                       new ObjectQuery("SELECT RealTimeProtectionEnabled, IsTamperProtected, " +
                                       "AMRunningMode, QuickScanEndTime, FullScanEndTime, " +
                                       "AntivirusSignatureAge FROM MSFT_MpComputerStatus")))
            using (var rows = searcher.Get())
            {
                foreach (ManagementBaseObject row in rows)
                {
                    using (row)
                    {
                        answered = true;
                        realTime = row["RealTimeProtectionEnabled"] as bool?;
                        tamper = row["IsTamperProtected"] as bool?;
                        mode = row["AMRunningMode"] as string;
                        quick = WmiDate(row["QuickScanEndTime"]);
                        full = WmiDate(row["FullScanEndTime"]);
                        signatureAge = row["AntivirusSignatureAge"] switch
                        {
                            uint u => (int)Math.Min(u, int.MaxValue),
                            int i => i,
                            _ => null,
                        };
                    }
                    break;
                }
            }
            if (!answered) return null;

            int? count = null;
            var paths = new List<string>();
            if (IsElevated())
            {
                count = 0;
                using var prefs = new ManagementObjectSearcher(new ManagementScope(Scope),
                    new ObjectQuery("SELECT ExclusionPath, ExclusionExtension, ExclusionProcess " +
                                    "FROM MSFT_MpPreference"));
                using var rows = prefs.Get();
                foreach (ManagementBaseObject row in rows)
                {
                    using (row)
                    {
                        foreach (var name in new[] { "ExclusionPath", "ExclusionExtension", "ExclusionProcess" })
                        {
                            if (row[name] is not string[] items) continue;
                            count += items.Length;
                            if (name == "ExclusionPath") paths.AddRange(items);
                        }
                    }
                    break;
                }
            }
            return new DefenderStatus(realTime, tamper, mode, count, paths, quick, full, signatureAge);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// WMI dates are DMTF strings; an unset one is null or empty. A string
    /// the converter refuses is no date, not a date of zero.
    private static DateTime? WmiDate(object? value)
    {
        if (value is not string s || s.Length == 0) return null;
        try { return ManagementDateTimeConverter.ToDateTime(s).ToUniversalTime(); }
        catch (Exception) { return null; }
    }

    private static bool IsElevated() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);
}
```

Construction sites:
- `AppServices.cs`: `new RealDeliveryOptimizationProbe(), dataDir);` → `new RealDeliveryOptimizationProbe(), new RealDefenderProbe(), dataDir);` (add `using BriskEngine.Diagnostics.RealProbes;` if not present).
- `Brisk.Cli/Program.cs`: the same edit at its `new DiagnosticContext(`.
- `TestContext.Empty`: `new FakeMemoryIntegrity(), new FakeDeliveryOptimization(), new FakeDefender(),`.
- `EngineHostTests.cs`: add `file sealed class NullDefender : IDefenderProbe { public DefenderStatus? Read() => null; }` beside `NullDeliveryOptimization`, and pass `new NullDefender(),` after `new NullDeliveryOptimization(),` in `Host`.
- `Fakes.cs`: `NoOtherProbes` implements `IDefenderProbe` too (`public DefenderStatus? Read() => null;`), and the `new DiagnosticContext(none, registry, none, ...)` call gains one more `none` before the path. Same one-more-`none` at `PrivacyRedLineTests.cs:898` (there the DO probe is a real reading; the new `none` goes after it).

- [ ] **Step 4: Write the rule**

`src/BriskEngine/Diagnostics/Rules/Privacy/DefenderRule.cs`:

```csharp
using System;
using System.Globalization;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// Windows Defender's blind spots: what it is told to skip, whether its two
/// protections are on, what mode it runs in, and how old its newest scan is.
/// Report-only; the one thing brisk offers is a link to Windows Security.
///
/// THE EVIDENCE CARRIES NUMBERS AND WINDOWS' OWN MODE WORD, nothing brisk
/// would have to translate: {0} excluded entries, {1} of the two protections
/// reading off, {2} of them unread, {3} the running mode as Windows names
/// it, {4} the age in days of the newest completed scan. Two states have no
/// number to put in a slot and get their own key: no scan on record, and an
/// exclusion list brisk could not read (an unelevated process — Windows
/// hides the list rather than refusing it, so null is the honest count).
///
/// The excluded paths never leave DefenderStatus.ExclusionPaths except onto
/// ScanSnapshot.DefenderExclusions, the Privacy page's own fold. Red line 2.
///
/// One WMI read per scan: Status memoizes on the context, and the posture
/// block reads the same answer.
public sealed class DefenderRule : PrivacyDisclosureRule
{
    public const string RuleId = "defender-blind-spots";
    public static readonly TimeSpan ScanAgeLimit = TimeSpan.FromDays(14);
    private const string MemoKey = "defender";
    private static readonly object Missing = new();

    private readonly Func<DateTime> _utcNow;

    public DefenderRule() : this(() => DateTime.UtcNow) { }

    public DefenderRule(Func<DateTime> utcNow) => _utcNow = utcNow;

    public override string Id => RuleId;

    /// The probe's answer for this scan, read once. A null answer is
    /// memoized too (as a sentinel), so a probe that failed is not asked
    /// again by the next reader.
    public static DefenderStatus? Status(DiagnosticContext ctx)
    {
        var memo = ctx.Memo.GetOrAdd(MemoKey, _ =>
        {
            try { return (object?)ctx.Defender.Read() ?? Missing; }
            catch (Exception) { return Missing; }
        });
        return memo as DefenderStatus;
    }

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var s = Status(ctx);
        if (s is null) return Unread();

        var now = _utcNow();
        DateTime? newest = (s.LastQuickScanUtc, s.LastFullScanUtc) switch
        {
            ({ } q, { } f) => q > f ? q : f,
            ({ } q, null) => q,
            (null, { } f) => f,
            _ => null,
        };
        int? scanAgeDays = newest is { } n ? Math.Max(0, (int)(now - n).TotalDays) : null;

        var off = (s.RealTimeOn == false ? 1 : 0) + (s.TamperOn == false ? 1 : 0);
        var unread = (s.RealTimeOn is null ? 1 : 0) + (s.TamperOn is null ? 1 : 0);
        var mode = s.RunningMode ?? "unknown";
        var notNormal = !string.Equals(mode, "Normal", StringComparison.OrdinalIgnoreCase);
        var exclusions = s.ExclusionCount ?? 0;
        var exclusionsUnread = s.ExclusionCount is null;
        var stale = scanAgeDays is null || now - newest!.Value > ScanAgeLimit;

        if (exclusions == 0 && !exclusionsUnread && off == 0 && unread == 0 && !notNormal && !stale)
            return null;

        var n = exclusions.ToString(CultureInfo.InvariantCulture);
        var offText = off.ToString(CultureInfo.InvariantCulture);
        var unreadText = unread.ToString(CultureInfo.InvariantCulture);
        var protections =
            $"{offText} of the two protections brisk checks, real-time and tamper, " +
            $"read as off, and {unreadText} could not be read. Defender's running mode " +
            $"reads {mode}.";
        var headline = exclusions > 0
            ? new Headline(n, "paths Defender skips",
                $"rule.{Id}.headline.value", new[] { n },
                $"rule.{Id}.headline.caption", Array.Empty<string>())
            : null;

        if (exclusionsUnread)
            return Disclosure($"rule.{Id}.title", TitleText,
                $"rule.{Id}.evidence.exclusions-unread",
                "brisk could not read Defender's exclusion list without administrator " +
                "rights, so it does not say how many entries are skipped. " + protections +
                " " + ScanClause(scanAgeDays) + " " + Trailer,
                scanAgeDays is { } a1
                    ? new[] { offText, unreadText, mode, a1.ToString(CultureInfo.InvariantCulture) }
                    : new[] { offText, unreadText, mode },
                headline: null);

        if (scanAgeDays is null)
            return Disclosure($"rule.{Id}.title", TitleText,
                $"rule.{Id}.evidence.no-scan",
                $"{n} entries are excluded from scanning. " + protections +
                " No completed scan is on record. " + Trailer,
                new[] { n, offText, unreadText, mode }, headline);

        var age = scanAgeDays.Value.ToString(CultureInfo.InvariantCulture);
        return Disclosure($"rule.{Id}.title", TitleText,
            $"rule.{Id}.evidence",
            $"{n} entries are excluded from scanning. " + protections +
            $" The newest completed scan is {age} days old. " + Trailer,
            new[] { n, offText, unreadText, mode, age }, headline);
    }

    private static string ScanClause(int? days) => days is { } d
        ? $"The newest completed scan is {d.ToString(CultureInfo.InvariantCulture)} days old."
        : "No completed scan is on record.";

    private const string TitleText = "Windows Defender has blind spots";

    private const string Trailer =
        "The excluded paths are on this page and on nothing built to be shared. " +
        "brisk reads what Defender reports about itself and nothing past it; " +
        "whether another product protects this machine is not something that " +
        "read can tell you.";

    private DiagnosticFinding Unread() => Disclosure(
        $"rule.{Id}.title.unread",
        "Windows Defender's state could not be established",
        $"rule.{Id}.evidence.unread",
        "brisk asked Windows Defender about itself and got nothing back it can " +
        "read. Defender may be absent, replaced by another product, or refusing " +
        "the question; brisk cannot tell which from here and does not report " +
        "any of them as fine.");
}
```

Check the `exclusions-unread` branch against the test `ExclusionsUnread_SaysSo_AndNeverSaysZero`: its EvidenceArgs are not asserted there, only the key, the null headline and the words. The `ProtectionsOff_AreCountedInTheEvidence` test expects the plain `.evidence` key with `{ "0", "1", "1", "Normal", "2" }` — `off` = 1 (RealTimeOn false), `unread` = 1 (TamperOn null). Correct as written.

- [ ] **Step 5: Register, route, link, and copy**

`DiagnosticRuleRegistry.cs`: after `new CapabilityUseRule(),`:

```csharp
        // The sixth disclosure, and the second that reads through a probe:
        // what Defender skips and whether it is on. Report-only; the Privacy
        // page adds a link to Windows Security.
        new DefenderRule(),
```

`FindingSections.cs`: add `"defender-blind-spots",` to `PrivacyRuleIds.All`. `PrivacyRedLineTests.cs`: disclosures `5` → `6`.

`HealthViewModel.cs`, `WindowsSettingRules`: add

```csharp
            ["defender-blind-spots"] = ("windowsdefender://threatsettings",
                "finding.action.windowssecurity"),
```

`Strings.resx`:

```xml
  <data name="finding.action.windowssecurity" xml:space="preserve"><value>Open Windows Security</value></data>
  <data name="rule.defender-blind-spots.title" xml:space="preserve"><value>Windows Defender has blind spots</value></data>
  <data name="rule.defender-blind-spots.title.unread" xml:space="preserve"><value>Windows Defender's state could not be established</value></data>
  <data name="rule.defender-blind-spots.evidence" xml:space="preserve"><value>{0} entries are excluded from scanning. {1} of the two protections brisk checks, real-time and tamper, read as off, and {2} could not be read. Defender's running mode reads {3}. The newest completed scan is {4} days old. The excluded paths are on this page and on nothing built to be shared. brisk reads what Defender reports about itself and nothing past it; whether another product protects this machine is not something that read can tell you.</value></data>
  <data name="rule.defender-blind-spots.evidence.no-scan" xml:space="preserve"><value>{0} entries are excluded from scanning. {1} of the two protections brisk checks, real-time and tamper, read as off, and {2} could not be read. Defender's running mode reads {3}. No completed scan is on record. The excluded paths are on this page and on nothing built to be shared. brisk reads what Defender reports about itself and nothing past it; whether another product protects this machine is not something that read can tell you.</value></data>
  <data name="rule.defender-blind-spots.evidence.exclusions-unread" xml:space="preserve"><value>brisk could not read Defender's exclusion list without administrator rights, so it does not say how many entries are skipped. {0} of the two protections brisk checks, real-time and tamper, read as off, and {1} could not be read. Defender's running mode reads {2}. brisk reads what Defender reports about itself and nothing past it; whether another product protects this machine is not something that read can tell you.</value></data>
  <data name="rule.defender-blind-spots.evidence.unread" xml:space="preserve"><value>brisk asked Windows Defender about itself and got nothing back it can read. Defender may be absent, replaced by another product, or refusing the question; brisk cannot tell which from here and does not report any of them as fine.</value></data>
  <data name="rule.defender-blind-spots.advice" xml:space="preserve"><value>brisk only reads this; it does not change Defender and offers no fix for it. The list below names what Defender is told to skip. Anything a program drops into an excluded folder is never scanned, and adding itself to that list is a common trick. Windows Security is where you remove an entry.</value></data>
  <data name="rule.defender-blind-spots.headline.value" xml:space="preserve"><value>{0}</value></data>
  <data name="rule.defender-blind-spots.headline.caption" xml:space="preserve"><value>paths Defender skips</value></data>
  <data name="privacy.defender.title" xml:space="preserve"><value>What Defender skips</value></data>
```

`Strings.tr.resx`:

```xml
  <data name="finding.action.windowssecurity" xml:space="preserve"><value>Windows Güvenliği'ni aç</value></data>
  <data name="rule.defender-blind-spots.title" xml:space="preserve"><value>Windows Defender'ın kör noktaları var</value></data>
  <data name="rule.defender-blind-spots.title.unread" xml:space="preserve"><value>Windows Defender'ın durumu belirlenemedi</value></data>
  <data name="rule.defender-blind-spots.evidence" xml:space="preserve"><value>{0} girdi taramanın dışında tutuluyor. brisk'in baktığı iki korumadan, gerçek zamanlı ve kurcalama koruması, {1} tanesi kapalı okunuyor, {2} tanesi okunamadı. Defender'ın çalışma modu {3}. Tamamlanan en yeni tarama {4} gün önce. Dışarıda tutulan yollar bu sayfada; paylaşılmak için yapılmış hiçbir yüzeyde değil. brisk Defender'ın kendisi hakkında bildirdiğini okur, ötesini okumaz; bu makineyi başka bir ürünün koruyup korumadığı bu okumadan çıkmaz.</value></data>
  <data name="rule.defender-blind-spots.evidence.no-scan" xml:space="preserve"><value>{0} girdi taramanın dışında tutuluyor. brisk'in baktığı iki korumadan, gerçek zamanlı ve kurcalama koruması, {1} tanesi kapalı okunuyor, {2} tanesi okunamadı. Defender'ın çalışma modu {3}. Kayıtlı tamamlanmış tarama yok. Dışarıda tutulan yollar bu sayfada; paylaşılmak için yapılmış hiçbir yüzeyde değil. brisk Defender'ın kendisi hakkında bildirdiğini okur, ötesini okumaz; bu makineyi başka bir ürünün koruyup korumadığı bu okumadan çıkmaz.</value></data>
  <data name="rule.defender-blind-spots.evidence.exclusions-unread" xml:space="preserve"><value>brisk, Defender'ın dışlama listesini yönetici hakkı olmadan okuyamadı; bu yüzden kaç girdinin atlandığını söylemiyor. brisk'in baktığı iki korumadan, gerçek zamanlı ve kurcalama koruması, {0} tanesi kapalı okunuyor, {1} tanesi okunamadı. Defender'ın çalışma modu {2}. brisk Defender'ın kendisi hakkında bildirdiğini okur, ötesini okumaz; bu makineyi başka bir ürünün koruyup korumadığı bu okumadan çıkmaz.</value></data>
  <data name="rule.defender-blind-spots.evidence.unread" xml:space="preserve"><value>brisk Windows Defender'a kendisini sordu ve okuyabildiği bir yanıt almadı. Defender yok olabilir, yerini başka bir ürün almış olabilir ya da soruyu reddediyor olabilir; brisk buradan hangisi olduğunu söyleyemez ve hiçbirini iyi diye bildirmez.</value></data>
  <data name="rule.defender-blind-spots.advice" xml:space="preserve"><value>brisk bunu yalnızca okur; Defender'ı değiştirmez ve düzeltme önermez. Aşağıdaki liste Defender'a atlaması söylenenleri adlandırır. Dışarıda tutulan bir klasöre bırakılan hiçbir şey taranmaz ve kendini o listeye eklemek yaygın bir numaradır. Bir girdiyi Windows Güvenliği'nden kaldırırsın.</value></data>
  <data name="rule.defender-blind-spots.headline.value" xml:space="preserve"><value>{0}</value></data>
  <data name="rule.defender-blind-spots.headline.caption" xml:space="preserve"><value>yolu Defender atlıyor</value></data>
  <data name="privacy.defender.title" xml:space="preserve"><value>Defender'ın atladıkları</value></data>
```

- [ ] **Step 6: Snapshot, host, rows, fold**

`IEngineHost.cs`, `ScanSnapshot`: append after `CapabilityUses`:

```csharp
    IReadOnlyList<CapabilityUseRecord> CapabilityUses,
    // The paths Defender is told to skip — on the same terms as the two
    // lists above: the page's fold reads it, no finding carries one.
    IReadOnlyList<string> DefenderExclusions);
```

`EngineHost.cs`, after the `capabilityUses` block:

```csharp
        IReadOnlyList<string> defenderExclusions;
        try
        {
            defenderExclusions = DefenderRule.Status(_ctx)?.ExclusionPaths ?? Array.Empty<string>();
        }
        catch (Exception)
        {
            defenderExclusions = Array.Empty<string>();
        }
```

and pass it last to `new ScanSnapshot(...)`. (Status is memoized: the rule already read it during the loop.)

`Fakes.cs`: `NoExclusionsRead = Array.Empty<string>()`; the whole-shape overload gains `IReadOnlyList<string>? defenderExclusions = null` after `capabilityUses`, passes `defenderExclusions ?? NoExclusionsRead` last; the Task 3 convenience overload passes `NoExclusionsRead`. `HealthViewModelTests.cs` / `OverviewViewModelTests.cs`: append `Array.Empty<string>()`.

`PrivacyViewModel.cs`: `public ObservableCollection<string> DefenderExclusionRows { get; } = new();` and in `Refresh()`:

```csharp
        DefenderExclusionRows.Clear();
        foreach (var path in snapshot.DefenderExclusions) DefenderExclusionRows.Add(path);
```

`PrivacyPage.xaml`: a third fold after the capability fold, same markup: `DefenderExclusionRows.Count`, `x:Name="DefenderFold"`, `[privacy.defender.title]`, `ItemsSource="{Binding DefenderExclusionRows}"`, `ElementName=DefenderFold`.

`PrivacyViewModelTests.cs`: 

```csharp
    [Fact]
    public void DefenderExclusions_RenderBehindTheFold_AndNowhereElse()
    {
        var vm = BuildViewModel(Fakes.Snapshot(findings: null,
            capabilityUses: Array.Empty<CapabilityUseRecord>(),
            defenderExclusions: new[] { @"C:\Users\U\.claude", @"C:\Temp\claude" }));
        Assert.Equal(2, vm.DefenderExclusionRows.Count);
        Assert.Equal(@"C:\Users\U\.claude", vm.DefenderExclusionRows[0]);
    }
```

(Adjust the `Fakes.Snapshot` overload so both named arguments resolve; add `defenderExclusions` to the Task 3 convenience overload as an optional parameter.)

- [ ] **Step 7: Run the full suite**

Run: `dotnet test brisk.sln -c Release --nologo -v q`
Expected: green, 0 warnings. If a HealthViewModel test pins the number of `WindowsSettingRules` entries or their caption keys, extend it by one with the new pair.

- [ ] **Step 8: Commit**

```bash
git add -A src
git commit -m "feat(rule): defender-blind-spots, what Defender skips and whether it is on"
```

---

### Task 5: `windows-suggestions`

**Files:**
- Create: `src/BriskEngine/Diagnostics/Rules/Privacy/ContentSuggestionsRule.cs`
- Modify: `DiagnosticRuleRegistry.cs` (after `new TailoredExperiencesRule(), new SpeechTypingRule(),`), both resx, `FindingSections.cs`, `PrivacyRedLineTests.cs` (switches 7 → 8)
- Modify: `src/BriskEngine.Tests/Rules/TelemetrySwitchRuleTests.cs` (`Ids` gains `"windows-suggestions"`, `Rule()` gains the arm, consent table gains `[InlineData("windows-suggestions", RuleCategory.Auto)]`)
- Modify: `src/BriskEngine.Tests/ReadBackTests.cs` (`Ids` and `SettingIds` gain `"windows-suggestions"`, `Rule()` gains the arm, `DivergesFromItsOwnWrite["windows-suggestions"] = false`)

**Interfaces:**
- Produces: `ContentSuggestionsRule` with `RuleId = "windows-suggestions"`, `KeyPath`, and six `Values`.

- [ ] **Step 1: Write the failing tests**

In `TelemetrySwitchRuleTests.cs`: add `"windows-suggestions"` to `Ids`, the arm `"windows-suggestions" => new ContentSuggestionsRule(),` to `Rule()`, and `[InlineData("windows-suggestions", RuleCategory.Auto)]` to `TheConsentLevel_MatchesWhatTheSwitchCosts`. Append:

```csharp
    [Fact]
    public void WindowsSuggestions_TouchesSixValues_AndNeverTheMasterSwitch()
    {
        var rule = new ContentSuggestionsRule();
        Assert.Equal(6, rule.Values.Count);
        Assert.All(rule.Values, v => Assert.Equal(ContentSuggestionsRule.KeyPath, v.KeyPath));
        Assert.DoesNotContain(rule.Values, v => v.ValueName == "ContentDeliveryAllowed");
        Assert.Contains(rule.Values, v => v.ValueName == "SilentInstalledAppsEnabled");
    }
```

In `ReadBackTests.cs`: add `"windows-suggestions"` to both `Ids` and `SettingIds`, the `Rule()` arm, and `["windows-suggestions"] = false,` to `DivergesFromItsOwnWrite`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests -c Release --nologo -v q`
Expected: compile error, `ContentSuggestionsRule` does not exist.

- [ ] **Step 3: Write the rule**

`src/BriskEngine/Diagnostics/Rules/Privacy/ContentSuggestionsRule.cs`:

```csharp
using System.Collections.Generic;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// Windows' suggestion and promotion flags: suggested apps in Start, tips
/// and suggestions, lock-screen promotions, and silent installation of
/// promoted apps. Six per-user values; absence reads as on, which is also
/// Windows' default for them.
///
/// ContentDeliveryAllowed, the master switch above these, is deliberately
/// not here: it also turns off things people use (Spotlight pictures), and
/// this switch's promise is the safe tier's — it takes nothing away.
public sealed class ContentSuggestionsRule : TelemetrySwitchRule
{
    public const string RuleId = "windows-suggestions";
    public const string KeyPath =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    public override string Id => RuleId;

    public override IReadOnlyList<RegistryValue> Values { get; } = new[]
    {
        new RegistryValue(KeyPath, "SilentInstalledAppsEnabled", OnValue: 1, OffValue: 0),
        new RegistryValue(KeyPath, "SystemPaneSuggestionsEnabled", OnValue: 1, OffValue: 0),
        new RegistryValue(KeyPath, "SoftLandingEnabled", OnValue: 1, OffValue: 0),
        new RegistryValue(KeyPath, "RotatingLockScreenOverlayEnabled", OnValue: 1, OffValue: 0),
        new RegistryValue(KeyPath, "SubscribedContent-338388Enabled", OnValue: 1, OffValue: 0),
        new RegistryValue(KeyPath, "SubscribedContent-338389Enabled", OnValue: 1, OffValue: 0),
    };

    protected override string Title =>
        "Windows shows suggested apps and tips, and can install promoted apps on its own";

    protected override string Evidence =>
        "Six values under Windows' content delivery settings read as on or not " +
        "set: suggested apps in Start, tips and suggestions, lock-screen " +
        "promotions, and silent installation of promoted apps. brisk reads the " +
        "six values and nothing past them; what Windows has already installed " +
        "this way is not something those reads can tell you. Switching them off " +
        "writes six values and can be undone.";

    protected override string FixDescription =>
        "Switch suggestions and silent installs off (undoable)";
}
```

- [ ] **Step 4: Register, route, copy**

`DiagnosticRuleRegistry.cs`: after `new TailoredExperiencesRule(), new SpeechTypingRule(),` add `new ContentSuggestionsRule(),`. `FindingSections.cs`: add `"windows-suggestions",`. `PrivacyRedLineTests.cs`: switches `7` → `8`.

`Strings.resx`:

```xml
  <data name="rule.windows-suggestions.title" xml:space="preserve"><value>Windows shows suggested apps and tips, and can install promoted apps on its own</value></data>
  <data name="rule.windows-suggestions.evidence" xml:space="preserve"><value>Six values under Windows' content delivery settings read as on or not set: suggested apps in Start, tips and suggestions, lock-screen promotions, and silent installation of promoted apps. brisk reads the six values and nothing past them; what Windows has already installed this way is not something those reads can tell you. Switching them off writes six values and can be undone.</value></data>
  <data name="rule.windows-suggestions.advice" xml:space="preserve"><value>brisk can switch these six off and switch them back on again. brisk counts them among the settings whose being off takes nothing away that you use; the master switch above them is left alone, because it also turns off things people keep.</value></data>
  <data name="rule.windows-suggestions.done" xml:space="preserve"><value>Suggestions and silent installs switched off</value></data>
```

`Strings.tr.resx`:

```xml
  <data name="rule.windows-suggestions.title" xml:space="preserve"><value>Windows önerilen uygulamalar ve ipuçları gösteriyor, tanıtılan uygulamaları kendi başına kurabiliyor</value></data>
  <data name="rule.windows-suggestions.evidence" xml:space="preserve"><value>Windows'un içerik teslim ayarları altındaki altı değer açık ya da ayarlanmamış okunuyor: Başlat'ta önerilen uygulamalar, ipuçları ve öneriler, kilit ekranı tanıtımları ve tanıtılan uygulamaların sessiz kurulumu. brisk altı değeri okur, ötesini okumaz; Windows'un bu yolla neyi çoktan kurduğu bu okumalardan çıkmaz. Kapatmak altı değer yazar ve geri alınabilir.</value></data>
  <data name="rule.windows-suggestions.advice" xml:space="preserve"><value>brisk bu altısını kapatabilir ve yeniden açabilir. brisk bunları kapalı olması kullandığın hiçbir şeyi eksiltmeyen ayarlardan sayar; üstlerindeki ana şaltere dokunmaz, çünkü o, insanların sakladığı şeyleri de kapatır.</value></data>
  <data name="rule.windows-suggestions.done" xml:space="preserve"><value>Öneriler ve sessiz kurulumlar kapatıldı</value></data>
```

- [ ] **Step 5: Run the full suite**

Run: `dotnet test brisk.sln -c Release --nologo -v q`
Expected: green, 0 warnings. The family's theories (`UntouchedMachine_...`, `EveryValueAlreadyOff_...`, `EveryValueOn_...`, read-back Held/Reverted) now run over the new id too.

- [ ] **Step 6: Commit**

```bash
git add -A src
git commit -m "feat(rule): windows-suggestions, six content-delivery flags as one Auto switch"
```

---

### Task 6: Security posture — the positive lines, the Security tier, the CLI line

**Files:**
- Create: `src/BriskEngine/Diagnostics/SecurityPosture.cs`, `src/BriskEngine.Tests/SecurityPostureTests.cs`, `src/BriskEngine.Tests/ProgramScanPostureTests.cs`
- Modify: `IEngineHost.cs` (`ScanSnapshot.Posture`), `EngineHost.cs`, `Fakes.cs`, `HealthViewModelTests.cs`, `OverviewViewModelTests.cs`
- Modify: `PrivacyViewModel.cs` (`SecurityRuleIds`, `IsSecurity`, `SecurityRows`, `PostureRows`, `PostureRow`), `PrivacyPage.xaml` (Security tier at the top), both resx (`privacy.title` reworded, `privacy.security.*`, `privacy.posture.*`), `PrivacyViewModelTests.cs`
- Modify: `src/Brisk.Cli/Program.cs` (`Scan`: the posture line and the JSON member; a public static `PostureLines`)

**Interfaces:**
- Consumes: `UacPromptRule.IsOn`, `DefenderRule.Status`.
- Produces: `record SecurityPostureReport(bool? FirewallDomain, bool? FirewallPrivate, bool? FirewallPublic, bool? RemoteDesktopOff, bool? UacPrompts, bool? DefenderRealTime, bool? DefenderTamper)` with `bool? Firewall` (true only when all three are true, null when any is null, else false); `static SecurityPostureReport SecurityPosture.Read(DiagnosticContext ctx)` (memoized under `ctx.Memo["posture"]`); `static (string? Fine, string? Unread) Program.PostureLines(SecurityPostureReport report)`; `ScanSnapshot.Posture` (positional, last); `PrivacyViewModel.SecurityRows`, `PrivacyViewModel.PostureRows` of `PostureRow(string Text, PostureState State)`, `enum PostureState { Fine, Attention, Unread }`.

- [ ] **Step 1: Write the failing engine tests**

`src/BriskEngine.Tests/SecurityPostureTests.cs`:

```csharp
using System;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using Xunit;

namespace BriskEngine.Tests;

public class SecurityPostureTests
{
    private static (DiagnosticContext ctx, FakeRegistry reg, FakeDefender defender) Context()
    {
        var reg = new FakeRegistry();
        var defender = new FakeDefender();
        return (TestContext.Empty() with { Registry = reg, Defender = defender }, reg, defender);
    }

    private static void Firewall(FakeRegistry reg, int? domain, int? standard, int? pub)
    {
        if (domain is { } d) reg.SetInt($@"{SecurityPosture.FirewallRoot}\DomainProfile", "EnableFirewall", d);
        if (standard is { } s) reg.SetInt($@"{SecurityPosture.FirewallRoot}\StandardProfile", "EnableFirewall", s);
        if (pub is { } p) reg.SetInt($@"{SecurityPosture.FirewallRoot}\PublicProfile", "EnableFirewall", p);
    }

    [Fact]
    public void TheMaintainersMachine_ReadsFineEverywhereButUac()
    {
        var (ctx, reg, defender) = Context();
        Firewall(reg, 1, 1, 1);
        reg.SetInt(SecurityPosture.TerminalServerKey, "fDenyTSConnections", 1);
        reg.SetInt(UacPromptRule.KeyPath, UacPromptRule.EnableLua, 1);
        reg.SetInt(UacPromptRule.KeyPath, UacPromptRule.ConsentPromptBehaviorAdmin, 0);
        reg.SetInt(UacPromptRule.KeyPath, UacPromptRule.PromptOnSecureDesktop, 0);
        defender.Status = new DefenderStatus(true, true, "Normal", 5, new[] { "x" }, null, null, 0);

        var p = SecurityPosture.Read(ctx);

        Assert.True(p.Firewall);
        Assert.True(p.RemoteDesktopOff);
        Assert.False(p.UacPrompts);
        Assert.True(p.DefenderRealTime);
        Assert.True(p.DefenderTamper);
    }

    [Fact]
    public void NothingReadable_IsNullEverywhere_NeverFine()
    {
        var (ctx, _, _) = Context();
        var p = SecurityPosture.Read(ctx);
        Assert.Null(p.Firewall);
        Assert.Null(p.RemoteDesktopOff);
        Assert.Null(p.UacPrompts);
        Assert.Null(p.DefenderRealTime);
        Assert.Null(p.DefenderTamper);
    }

    [Fact]
    public void OneFirewallProfileOff_IsFalse_AndOneUnread_IsNull()
    {
        var (off, reg1, _) = Context();
        Firewall(reg1, 1, 0, 1);
        Assert.False(SecurityPosture.Read(off).Firewall);

        var (unread, reg2, _) = Context();
        Firewall(reg2, 1, null, 1);
        Assert.Null(SecurityPosture.Read(unread).Firewall);
    }

    [Fact]
    public void RemoteDesktopAllowed_IsFalse()
    {
        var (ctx, reg, _) = Context();
        reg.SetInt(SecurityPosture.TerminalServerKey, "fDenyTSConnections", 0);
        Assert.False(SecurityPosture.Read(ctx).RemoteDesktopOff);
    }

    [Fact]
    public void UacWithDefaultsWritten_IsFine_AndNothingWritten_IsUnread()
    {
        var (fine, reg, _) = Context();
        reg.SetInt(UacPromptRule.KeyPath, UacPromptRule.ConsentPromptBehaviorAdmin, 5);
        Assert.True(SecurityPosture.Read(fine).UacPrompts);

        var (unread, _, _) = Context();
        Assert.Null(SecurityPosture.Read(unread).UacPrompts);
    }

    [Fact]
    public void ReadTwice_HitsTheMemo()
    {
        var (ctx, _, defender) = Context();
        defender.Status = new DefenderStatus(true, true, "Normal", 0, Array.Empty<string>(), null, null, 0);
        var a = SecurityPosture.Read(ctx);
        var b = SecurityPosture.Read(ctx);
        Assert.Same(a, b);
        Assert.Equal(1, defender.Reads);
    }
}
```

`src/BriskEngine.Tests/ProgramScanPostureTests.cs` (Program is `Brisk.Cli.Program`; `ProgramFixTests` shows the using lines to copy):

```csharp
using Brisk.Cli;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

public class ProgramScanPostureTests
{
    [Fact]
    public void AllFine_IsOneLine_AndNoUnreadLine()
    {
        var (fine, unread) = Program.PostureLines(new SecurityPostureReport(true, true, true, true, true, true, true));
        Assert.Equal("Checked and fine: firewall on for all 3 profiles, Remote Desktop off, " +
                     "Windows asks before a program takes control, Defender real-time protection on, " +
                     "Defender tamper protection on.", fine);
        Assert.Null(unread);
    }

    [Fact]
    public void ABadRow_IsLeftOut_BecauseItIsAlreadyAFinding()
    {
        var (fine, _) = Program.PostureLines(new SecurityPostureReport(true, true, true, true, false, true, true));
        Assert.DoesNotContain("asks before", fine);
        Assert.Contains("firewall on", fine);
    }

    [Fact]
    public void NullRows_GoToTheUnreadLine_NeverToFine()
    {
        var (fine, unread) = Program.PostureLines(new SecurityPostureReport(null, null, null, true, true, null, true));
        Assert.DoesNotContain("firewall", fine);
        Assert.Equal("Not read: firewall, Defender real-time protection.", unread);
    }

    [Fact]
    public void NothingFine_IsNoFineLine()
    {
        var (fine, unread) = Program.PostureLines(new SecurityPostureReport(null, null, null, null, null, null, null));
        Assert.Null(fine);
        Assert.NotNull(unread);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests -c Release --nologo -v q`
Expected: compile errors (`SecurityPosture`, `SecurityPostureReport`, `Program.PostureLines`).

- [ ] **Step 3: The reader**

`src/BriskEngine/Diagnostics/SecurityPosture.cs`:

```csharp
using System;
using BriskEngine.Diagnostics.Rules.Privacy;

namespace BriskEngine.Diagnostics;

/// The positive lines. Each member is true when the value reads as
/// Windows' safer setting, false when it reads as the other, null when
/// brisk did not read it. Null never renders green (spec red line 2), and
/// nothing here sums to a verdict (red line 4).
public sealed record SecurityPostureReport(
    bool? FirewallDomain,
    bool? FirewallPrivate,
    bool? FirewallPublic,
    bool? RemoteDesktopOff,
    bool? UacPrompts,
    bool? DefenderRealTime,
    bool? DefenderTamper)
{
    /// All three profiles on; null when any profile was not read; false
    /// when any read profile is off.
    public bool? Firewall =>
        FirewallDomain is null || FirewallPrivate is null || FirewallPublic is null ? null
        : FirewallDomain == true && FirewallPrivate == true && FirewallPublic == true;
}

/// Reads the posture once per scan (memoized on the context) so the rules,
/// the snapshot and the CLI line share one reading of one machine.
public static class SecurityPosture
{
    public const string FirewallRoot =
        @"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
    public const string TerminalServerKey =
        @"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server";
    private const string MemoKey = "posture";

    public static SecurityPostureReport Read(DiagnosticContext ctx) =>
        (SecurityPostureReport)ctx.Memo.GetOrAdd(MemoKey, _ => ReadUncached(ctx));

    private static SecurityPostureReport ReadUncached(DiagnosticContext ctx)
    {
        bool? Flag(string key, string value, int fineWhen)
        {
            int? read;
            try { read = ctx.Registry.GetInt(key, value); }
            catch (Exception) { return null; }
            return read is null ? null : read == fineWhen;
        }

        bool? uac;
        try
        {
            var lua = ctx.Registry.GetInt(UacPromptRule.KeyPath, UacPromptRule.EnableLua);
            var admin = ctx.Registry.GetInt(UacPromptRule.KeyPath, UacPromptRule.ConsentPromptBehaviorAdmin);
            uac = lua is null && admin is null ? null : !new UacPromptRule().IsOn(ctx);
        }
        catch (Exception)
        {
            uac = null;
        }

        var defender = DefenderRule.Status(ctx);

        return new SecurityPostureReport(
            Flag($@"{FirewallRoot}\DomainProfile", "EnableFirewall", 1),
            Flag($@"{FirewallRoot}\StandardProfile", "EnableFirewall", 1),
            Flag($@"{FirewallRoot}\PublicProfile", "EnableFirewall", 1),
            Flag(TerminalServerKey, "fDenyTSConnections", 1),
            uac,
            defender?.RealTimeOn,
            defender?.TamperOn);
    }
}
```

- [ ] **Step 4: The CLI line and the JSON member**

In `src/Brisk.Cli/Program.cs`, `Scan`: after `var scan = scanner.Scan();` add `var posture = SecurityPosture.Read(ctx);`. In the JSON payload add `posture,` after `sensors = new { ... },`. In the text branch, after the sensor notice line:

```csharp
        var (fineLine, unreadLine) = PostureLines(posture);
        if (fineLine is not null) Console.WriteLine($"[ok] {fineLine}");
        if (unreadLine is not null) Console.WriteLine($"[i ] {unreadLine}");
```

Add the method (public static, next to `SensorNotice`):

```csharp
    /// The positive lines for the console. A row that is not fine is left
    /// out: it is already a finding above. A row brisk did not read is named
    /// on its own line, never folded into "fine".
    public static (string? Fine, string? Unread) PostureLines(SecurityPostureReport p)
    {
        var fine = new List<string>();
        var unread = new List<string>();
        void Row(bool? state, string fineText, string name)
        {
            if (state == true) fine.Add(fineText);
            else if (state is null) unread.Add(name);
        }
        Row(p.Firewall, "firewall on for all 3 profiles", "firewall");
        Row(p.RemoteDesktopOff, "Remote Desktop off", "Remote Desktop");
        Row(p.UacPrompts, "Windows asks before a program takes control", "UAC prompts");
        Row(p.DefenderRealTime, "Defender real-time protection on", "Defender real-time protection");
        Row(p.DefenderTamper, "Defender tamper protection on", "Defender tamper protection");
        return (
            fine.Count > 0 ? "Checked and fine: " + string.Join(", ", fine) + "." : null,
            unread.Count > 0 ? "Not read: " + string.Join(", ", unread) + "." : null);
    }
```

- [ ] **Step 5: Run the engine tests**

Run: `dotnet test src/BriskEngine.Tests -c Release --nologo -v q`
Expected: green.

- [ ] **Step 6: Write the failing GUI tests**

Append to `PrivacyViewModelTests.cs`:

```csharp
    [Fact]
    public void ASecurityFinding_LandsInSecurityRows_NotInTheSwitchBands()
    {
        var uac = new UacPromptRule().Detect(RegistryWithUacOff())!;
        var vm = BuildViewModel(Fakes.Snapshot(new[] { uac }));
        Assert.Single(vm.SecurityRows);
        Assert.Empty(vm.CostlySwitchRows);
        Assert.Empty(vm.SafeSwitchRows);
    }

    [Fact]
    public void PostureRows_RenderThreeStates_AndNullIsNeverFine()
    {
        var posture = new SecurityPostureReport(true, true, true, false, null, true, true);
        var vm = BuildViewModel(Fakes.Snapshot(findings: null, posture: posture));
        Assert.Equal(5, vm.PostureRows.Count);
        Assert.Equal(PostureState.Fine, vm.PostureRows[0].State);       // firewall
        Assert.Equal(PostureState.Attention, vm.PostureRows[1].State);  // remote desktop
        Assert.Equal(PostureState.Unread, vm.PostureRows[2].State);     // uac
        Assert.DoesNotContain(vm.PostureRows, r => r.State == PostureState.Fine && r.Text.Length == 0);
    }
```

`RegistryWithUacOff()` builds a `DiagnosticContext` over a `FakeRegistry` with `ConsentPromptBehaviorAdmin = 0` — the file already has a registry-backed context helper for the switch tests (search it for `AdvertisingIdRule` or `FakeRegistry`); reuse that shape.

- [ ] **Step 7: Snapshot, rows, page, copy**

`IEngineHost.cs`, `ScanSnapshot`: append `SecurityPostureReport Posture);` after `DefenderExclusions` (the comment: "Required like Sensors: there is no default that adds no claim."). `EngineHost.cs`: `var posture = SecurityPosture.Read(_ctx);` (wrap in try/catch returning `new SecurityPostureReport(null, null, null, null, null, null, null)` on exception) and pass it last. `Fakes.cs`: default `NothingRead = new SecurityPostureReport(null, null, null, null, null, null, null)`; the whole-shape overload gains `SecurityPostureReport? posture = null`; add the convenience overload `Snapshot(IReadOnlyList<DiagnosticFinding>? findings, SecurityPostureReport posture)`. The two test files: append `new SecurityPostureReport(null, null, null, null, null, null, null)`.

`PrivacyViewModel.cs`:

```csharp
    /// The two rules whose rows sit under the Security header rather than in
    /// a switch band. A second list beside PrivacyRuleIds, and like it a
    /// copy nothing cross-checks: read it against the registry by hand when
    /// adding a security rule.
    public static readonly ReadOnlyCollection<string> SecurityRuleIds =
        new(new[] { "uac-prompts", "defender-blind-spots" });

    public static bool IsSecurity(DiagnosticFinding finding) =>
        SecurityRuleIds.Contains(finding.RuleId, StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<FindingRow> SecurityRows { get; } = new();

    public ObservableCollection<PostureRow> PostureRows { get; } = new();
```

`Band(...)`: put `IsSecurity(finding) ? SecurityRows :` as the FIRST arm. `Refresh()`: clear `SecurityRows` and `PostureRows` with the others; after the finding loop:

```csharp
        var p = snapshot.Posture;
        PostureRows.Add(Posture("firewall", p.Firewall));
        PostureRows.Add(Posture("rdp", p.RemoteDesktopOff));
        PostureRows.Add(Posture("uac", p.UacPrompts));
        PostureRows.Add(Posture("defender-rtp", p.DefenderRealTime));
        PostureRows.Add(Posture("defender-tamper", p.DefenderTamper));
```

with

```csharp
    private PostureRow Posture(string key, bool? state) => state switch
    {
        true => new PostureRow(_loc[$"privacy.posture.{key}.fine"], PostureState.Fine),
        false => new PostureRow(_loc[$"privacy.posture.{key}.attention"], PostureState.Attention),
        null => new PostureRow(_loc[$"privacy.posture.{key}.unread"], PostureState.Unread),
    };
```

Also include `SecurityRows` in the `foreach (var row in DisclosureRows.Concat(UnreadableRows)...` chain at line ~288 (whatever that loop does to every row, security rows need it too). Declare in the same file (or a new `PostureRow.cs` beside it):

```csharp
public enum PostureState { Fine, Attention, Unread }

public sealed record PostureRow(string Text, PostureState State)
{
    /// Resource keys the page styles by: the health colours already in
    /// Shared.xaml for fine/attention, and Faint for unread.
    public string StyleKey => State switch
    {
        PostureState.Fine => "PostureFine",
        PostureState.Attention => "PostureAttention",
        _ => "PostureUnread",
    };
}
```

`PrivacyPage.xaml`: before "BLOCK 1" insert the Security tier:

```xml
                <!-- BLOCK 0: security. The posture rows say what reads as
                     Windows' safer setting; the finding rows below them are
                     the ones that do not. Nothing here is graded and nothing
                     here sums to a verdict. -->
                <TextBlock Style="{StaticResource PanelHeader}"
                           Text="{Binding [privacy.security.section], Source={x:Static loc:Loc.Instance}}" />
                <TextBlock Style="{StaticResource Muted}" TextWrapping="Wrap" Margin="0,0,0,8"
                           Text="{Binding [privacy.security.note], Source={x:Static loc:Loc.Instance}}" />
                <ItemsControl ItemsSource="{Binding PostureRows}" Margin="2,0,0,8">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <TextBlock TextWrapping="Wrap" Margin="0,0,0,3" Text="{Binding Text}">
                                <TextBlock.Style>
                                    <Style TargetType="TextBlock" BasedOn="{StaticResource Muted}">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding State}" Value="Fine">
                                                <Setter Property="Foreground" Value="{StaticResource HealthGoodBrush}" />
                                            </DataTrigger>
                                            <DataTrigger Binding="{Binding State}" Value="Attention">
                                                <Setter Property="Foreground" Value="{StaticResource HealthWarnBrush}" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </TextBlock.Style>
                            </TextBlock>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <ItemsControl ItemsSource="{Binding SecurityRows}"
                              ItemTemplate="{StaticResource FindingCard}" Margin="0,0,0,16" />
```

`HealthGoodBrush` / `HealthWarnBrush` are placeholders for whatever names `Shared.xaml` gives the health ring's green and amber brushes — `grep -n "Brush" src/Brisk/Views/Shared.xaml | head` and use the two that the ring uses. Unread rows keep `Muted` as is.

`Strings.resx` (change `privacy.title`; add the rest):

```xml
  <data name="privacy.title" xml:space="preserve"><value>Privacy and security on this machine</value></data>
  <data name="privacy.security.section" xml:space="preserve"><value>Security</value></data>
  <data name="privacy.security.note" xml:space="preserve"><value>brisk reads these values and grades none of them. Fine means the value reads as Windows' safer setting; it says nothing about what else is on this machine.</value></data>
  <data name="privacy.posture.firewall.fine" xml:space="preserve"><value>Firewall on for all three network profiles</value></data>
  <data name="privacy.posture.firewall.attention" xml:space="preserve"><value>Firewall off for at least one network profile</value></data>
  <data name="privacy.posture.firewall.unread" xml:space="preserve"><value>Firewall: brisk could not read this</value></data>
  <data name="privacy.posture.rdp.fine" xml:space="preserve"><value>Remote Desktop off</value></data>
  <data name="privacy.posture.rdp.attention" xml:space="preserve"><value>Remote Desktop on: this machine accepts remote sign-ins</value></data>
  <data name="privacy.posture.rdp.unread" xml:space="preserve"><value>Remote Desktop: brisk could not read this</value></data>
  <data name="privacy.posture.uac.fine" xml:space="preserve"><value>Windows asks before a program takes control</value></data>
  <data name="privacy.posture.uac.attention" xml:space="preserve"><value>Windows does not ask before a program takes control (see below)</value></data>
  <data name="privacy.posture.uac.unread" xml:space="preserve"><value>UAC prompts: brisk could not read this</value></data>
  <data name="privacy.posture.defender-rtp.fine" xml:space="preserve"><value>Defender real-time protection on</value></data>
  <data name="privacy.posture.defender-rtp.attention" xml:space="preserve"><value>Defender real-time protection off (see below)</value></data>
  <data name="privacy.posture.defender-rtp.unread" xml:space="preserve"><value>Defender real-time protection: brisk could not read this</value></data>
  <data name="privacy.posture.defender-tamper.fine" xml:space="preserve"><value>Defender tamper protection on</value></data>
  <data name="privacy.posture.defender-tamper.attention" xml:space="preserve"><value>Defender tamper protection off (see below)</value></data>
  <data name="privacy.posture.defender-tamper.unread" xml:space="preserve"><value>Defender tamper protection: brisk could not read this</value></data>
```

`Strings.tr.resx`:

```xml
  <data name="privacy.title" xml:space="preserve"><value>Bu makinede gizlilik ve güvenlik</value></data>
  <data name="privacy.security.section" xml:space="preserve"><value>Güvenlik</value></data>
  <data name="privacy.security.note" xml:space="preserve"><value>brisk bu değerleri okur ve hiçbirine not vermez. "İyi", değerin Windows'un daha güvenli ayarı olarak okunduğu anlamına gelir; bu makinede başka ne olduğu hakkında bir şey söylemez.</value></data>
  <data name="privacy.posture.firewall.fine" xml:space="preserve"><value>Güvenlik duvarı üç ağ profilinde de açık</value></data>
  <data name="privacy.posture.firewall.attention" xml:space="preserve"><value>Güvenlik duvarı en az bir ağ profilinde kapalı</value></data>
  <data name="privacy.posture.firewall.unread" xml:space="preserve"><value>Güvenlik duvarı: brisk bunu okuyamadı</value></data>
  <data name="privacy.posture.rdp.fine" xml:space="preserve"><value>Uzak Masaüstü kapalı</value></data>
  <data name="privacy.posture.rdp.attention" xml:space="preserve"><value>Uzak Masaüstü açık: bu makine uzaktan oturum açmayı kabul ediyor</value></data>
  <data name="privacy.posture.rdp.unread" xml:space="preserve"><value>Uzak Masaüstü: brisk bunu okuyamadı</value></data>
  <data name="privacy.posture.uac.fine" xml:space="preserve"><value>Windows bir program kontrolü almadan önce soruyor</value></data>
  <data name="privacy.posture.uac.attention" xml:space="preserve"><value>Windows bir program kontrolü almadan önce sormuyor (aşağıda)</value></data>
  <data name="privacy.posture.uac.unread" xml:space="preserve"><value>UAC soruları: brisk bunu okuyamadı</value></data>
  <data name="privacy.posture.defender-rtp.fine" xml:space="preserve"><value>Defender gerçek zamanlı koruma açık</value></data>
  <data name="privacy.posture.defender-rtp.attention" xml:space="preserve"><value>Defender gerçek zamanlı koruma kapalı (aşağıda)</value></data>
  <data name="privacy.posture.defender-rtp.unread" xml:space="preserve"><value>Defender gerçek zamanlı koruma: brisk bunu okuyamadı</value></data>
  <data name="privacy.posture.defender-tamper.fine" xml:space="preserve"><value>Defender kurcalama koruması açık</value></data>
  <data name="privacy.posture.defender-tamper.attention" xml:space="preserve"><value>Defender kurcalama koruması kapalı (aşağıda)</value></data>
  <data name="privacy.posture.defender-tamper.unread" xml:space="preserve"><value>Defender kurcalama koruması: brisk bunu okuyamadı</value></data>
```

- [ ] **Step 8: Run the full suite**

Run: `dotnet test brisk.sln -c Release --nologo -v q`
Expected: green, 0 warnings. `ReportCardModelTests` must not have changed: the card reads no `Posture`. If a test pins `privacy.title`'s old wording, update the expectation to the new one.

- [ ] **Step 9: Commit**

```bash
git add -A src
git commit -m "feat(privacy): security tier with posture lines on the page and in brisk scan"
```

---

### Task 7: Docs, release notes, version 0.9.0

**Files:**
- Modify: `README.md:114` and `README.tr.md:114` (the rule-count bullet), plus one new bullet under "What it does" / "Ne yapar" in each
- Modify: `.github/release-notes.md` (read it first: it leads with "What changed in 0.8.0"; write the 0.9.0 block above it in the same shape)
- Modify: `src/BriskEngine/EngineInfo.cs` (`Version = "0.9.0"`)
- Test: `src/BriskEngine.Tests/EngineInfoTests.cs` (read it; if it pins the version string, update the pin in the same commit)

- [ ] **Step 1: README bullets**

`README.md` line 114: `**27 deterministic diagnostic rules.**` → `**31 deterministic diagnostic rules.**`, and `ten privacy rules with the read-back above` → `fourteen privacy and security rules with the read-back above`. Directly after that bullet add:

```markdown
- **Security posture, report first.** Whether Windows asks before a program takes control (one undoable fix, re-checked on every later scan), what Defender is told to skip and whether it is on, which apps used the microphone and camera in the last 30 days, Windows' suggestion and silent-install flags, and the lines that are fine: firewall, Remote Desktop. Nothing here is graded, and no path or app name reaches anything you would share.
```

`README.tr.md` line 114: `**27 deterministik teşhis kuralı.**` → `**31 deterministik teşhis kuralı.**`, `read-back'li on gizlilik kuralı` → `read-back'li on dört gizlilik ve güvenlik kuralı`. After it:

```markdown
- **Güvenlik durumu, önce rapor.** Windows bir program kontrolü almadan önce soruyor mu (geri alınabilir tek düzeltme, sonraki her taramada yeniden okunur), Defender'a neyi atlaması söylenmiş ve açık mı, son 30 günde hangi uygulamalar mikrofonu ve kamerayı kullandı, Windows'un öneri ve sessiz kurulum bayrakları, ve iyi olan satırlar: güvenlik duvarı, Uzak Masaüstü. Burada hiçbir şeye not verilmez ve hiçbir yol ya da uygulama adı paylaşacağın bir şeye ulaşmaz.
```

- [ ] **Step 2: Release notes**

At the top of `.github/release-notes.md`, in the shape the 0.8.0 block uses, add a 0.9.0 block: the five items in the order of this plan, one line each in plain English; the one fix (UAC) named as undoable with read-back; the sentence "Nothing on the Privacy page moves the score, and no path or app name reaches the report card, the JSON, or anything built to be shared." Keep the 0.8.0 block below it untouched.

- [ ] **Step 3: Version**

`src/BriskEngine/EngineInfo.cs`: `"0.8.0"` → `"0.9.0"`. Run `dotnet test src/BriskEngine.Tests -c Release --nologo -v q`; if `EngineInfoTests` pins the string, update it.

- [ ] **Step 4: Full suite and a live scan**

Run: `dotnet test brisk.sln -c Release --nologo -v q` — green, 0 warnings.
Run: `dotnet build brisk.sln -c Release --nologo -v q` then `./src/Brisk.Cli/bin/Release/net8.0-windows/Brisk.Cli.exe scan` (unelevated) — expect the `uac-prompts` finding, the `defender-blind-spots` finding saying the exclusion list could not be read without administrator rights, the `camera-mic-use` counts, the `windows-suggestions` finding, and the `[ok] Checked and fine:` line. Then `... scan --json | grep -c "ZZZ\|\\\\Users\\\\"` must print `0` beyond the large-files rule's own paths (that rule names files by design; check that no `camera-mic-use` or `defender-blind-spots` object carries a path).

- [ ] **Step 5: Commit**

```bash
git add README.md README.tr.md .github/release-notes.md src/BriskEngine/EngineInfo.cs src/BriskEngine.Tests
git commit -m "docs: v0.9.0, the security tier in both READMEs and the release notes"
```

---

## Self-review against the spec

- **Coverage:** foundations (Task 1); uac-prompts with Confirm/Warning/read-back and the three pins (Task 2); camera-mic-use with window, grouping, unreadable halves, the snapshot list and fold (Task 3); defender-blind-spots with the probe, elevation-aware exclusions, the memo, the link, the fold (Task 4); windows-suggestions (Task 5); posture with the page tier, the CLI line and JSON (Task 6); README, release notes, version (Task 7). Report card untouched throughout. RevelationPicker: camera-mic-use and defender-blind-spots are unlisted, which is the tail rank the spec asks for — no change needed.
- **Deviation recorded:** uac-prompts evidence has no arguments (Task 2, Step 6 amends the spec).
- **Type consistency:** `CapabilityUseRecord(Capability, AppName, Path, LastUsedUtc)`, `DefenderStatus(RealTimeOn, TamperOn, RunningMode, ExclusionCount, ExclusionPaths, LastQuickScanUtc, LastFullScanUtc, SignatureAgeDays)`, `SecurityPostureReport(FirewallDomain, FirewallPrivate, FirewallPublic, RemoteDesktopOff, UacPrompts, DefenderRealTime, DefenderTamper)` are used with the same names and order in every task. `ScanSnapshot` grows in the order `CapabilityUses`, `DefenderExclusions`, `Posture`, and `Fakes.Snapshot` grows in the same order.
- **The whole-branch gate** (spec, last section): after Task 7, a fresh reviewer reads the branch end to end before anything merges; the maintainer runs `brisk-app` on his machine and reads the Privacy page against the fixture in the spec. Merge and the 0.9.0 tag on his word, after the post.
