# Wave 2 — Boot Attribution, Store Startup, Memory Speed

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tell the user what their boot actually costs them, using Windows' own
measurement, and hand them the switch for the parts that can be switched — which
means seeing Store apps for the first time. Plus an honest memory-speed rule that
refuses to guess at a cause it cannot verify.

**Architecture:** Two new probes join `DiagnosticContext` (`IEventLogProbe`,
`IHardwareProbe`), following the pattern wave 1 established with `IDisplayProbe`.
`StartupManager` grows a second source — Store-app startup tasks — behind its existing
`List`/`SetEnabled` surface, so the GUI needs no change to show them. The boot rule
joins the two: it reads what Windows blamed, and marks an offender fixable only when
it maps to something `StartupManager` can actually toggle.

**Tech Stack:** .NET 8 (`net8.0-windows`), C#, WPF, xUnit, WMI via `System.Management`,
Windows event log via the `System.Diagnostics.EventLog` package.

**Spec:** `docs/superpowers/specs/2026-08-18-seven-rules-design.md`

## Global Constraints

- `TreatWarningsAsErrors` is `true` in every project. Warnings fail the build,
  including an unused private field or `using`.
- `Nullable` is `enable` in every project. Do not use `!` to silence a genuine null.
- `ImplicitUsings` is **disabled in `src/Brisk` only**. Enabled everywhere else,
  including `Brisk.Tests`.
- Platform `x64`, target `net8.0-windows`, Windows 10 1809+ / Windows 11.
- Every rule id is lowercase kebab-case and is the localization key stem:
  `rule.<id>.title`, `rule.<id>.evidence`, `rule.<id>.done` for fixables.
- Every key added to `Strings.resx` MUST also be added to `Strings.tr.resx`.
  `LocTests` fails the build when the two drift.
- Rules never throw from `Detect`. Missing data means "no finding", never a guess.
- Probes return empty rather than guessing when a source is unavailable.
- Commit messages: lowercase `type: subject`, subject describes the behaviour change.
  End each with:
  `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`

## A deliberate gap in this plan

Wave 1's plan wrote out every line, including the P/Invoke. Two of those lines were
wrong — a markup extension this codebase never had, and a `ChangeDisplaySettingsEx`
form that reads the registry rather than writing it. Both cost a fix round, and both
were *my* guesses about an API, not the implementer's mistakes.

So the probe steps below give the **contract and the measured field names** — the part
that cannot be re-derived without a machine — and leave the API mechanics to the
implementer, with an explicit instruction to verify against the real API before
relying on it. Where a value was measured on hardware it is stated as measured. Where
it is an assumption, it says so.

## Measured facts this plan is built on

Everything below was read off the maintainer's machine before the plan was written.
Do not re-derive it; do check it still holds if something surprises you.

- Boot times over the last eight boots: 51, 112, 45, 57, 60, 94, 51, 74 seconds.
  Median ≈ 57 s. `MainPathBootTime` sits around 21-26 s, so most of the cost is
  after the main path.
- ID 100's payload carries `BootTime` and `MainPathBootTime`. ~~`PostBootTime` and
  `BootDegradationTime` come back empty on Windows 11 26100.~~ **Corrected while Task 2 was
  built:** those names do not exist. The payload spells them `BootPostBootTime` and
  `BootDegradationDelta`, both populated, and the build is **26200**. `BootTime` sits at
  index 5 of 44 elements with `SystemBootInstance` = 392 at index 3, which is why every
  value must be read by name.
- ID 101's payload: `Name`, `FriendlyName`, `Version`, `TotalTime`,
  `DegradationTime`, `Path`, `ProductName`, `CompanyName`. The log also carries
  103 (service) and 108, 200, 203 which this wave ignores.
- The worst offenders were `MsMpEng.exe` (52 s), `Spotify.exe` (37 s),
  `msedgewebview2.exe` (36 s), `brisk-app.exe` (26 s), `TiWorker.exe` (9 s).
- Those overlap the machine's `Run` entries **almost not at all**. Store-app startup
  tasks live at
  `HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\<PFN>\<TaskId>\State`,
  where `2` means enabled and `0` disabled. Seven were enabled on that machine,
  including both of Spotify's.
- `HKCU\...\Explorer\StartupApproved\Run` still holds an orphaned `brisk` record from
  before wave 1 removed the Run value.

---

### Task 1: Store-app startup tasks

**Files:**
- Modify: `src/BriskEngine/Diagnostics/StartupManager.cs`
- Modify: `src/Brisk/Services/StartupLauncher.cs` (orphan cleanup)
- Test: `src/BriskEngine.Tests/StartupManagerTests.cs`
- Test: `src/Brisk.Tests/SettingsTests.cs`

**Interfaces:**
- Consumes: `IRegistryProbe.GetSubKeyNames`, `GetInt`, `SetInt` (all exist).
- Produces: `StartupManager` lists and toggles Store entries under the hive string
  `"Store"`, through the unchanged `List()` / `SetEnabled(hive, name, enabled)`
  surface. `StartupManager.StoreRoot` is public so tests can address it.

The GUI needs no change: `StartupItemRow` already renders `entry.Hive` as its label
and `IEngineHost.SetStartupEnabled` already forwards the hive string.

- [ ] **Step 1: Write the failing test**

Add to `src/BriskEngine.Tests/StartupManagerTests.cs`:

```csharp
    private static void StoreTask(FakeRegistry reg, string pfn, string task, int state)
    {
        var apps = StartupManager.StoreRoot;
        if (!reg.SubKeys.TryGetValue(apps, out var pfns)) reg.SubKeys[apps] = pfns = new List<string>();
        if (!pfns.Contains(pfn)) pfns.Add(pfn);
        var appKey = $@"{apps}\{pfn}";
        if (!reg.SubKeys.TryGetValue(appKey, out var tasks)) reg.SubKeys[appKey] = tasks = new List<string>();
        if (!tasks.Contains(task)) tasks.Add(task);
        reg.SetInt($@"{appKey}\{task}", "State", state);
    }

    [Fact]
    public void StoreApps_AreListed_WithTheirEnabledState()
    {
        var reg = new FakeRegistry();
        StoreTask(reg, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "Spotify", 2);
        StoreTask(reg, "Microsoft.Copilot_8wekyb3d8bbwe", "Copilot.StartupTaskId", 0);

        var items = new StartupManager(reg, null).List();

        var spotify = Assert.Single(items, i => i.Name.Contains("Spotify", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Store", spotify.Hive);
        Assert.True(spotify.Enabled);
        Assert.True(spotify.KnownHeavy);          // the heavy table already lists Spotify

        var copilot = Assert.Single(items, i => i.Name.Contains("Copilot", StringComparison.OrdinalIgnoreCase));
        Assert.False(copilot.Enabled);
    }

    // The package family name is publisher-qualified and hash-suffixed. A user
    // recognises "SpotifyMusic", not "SpotifyAB.SpotifyMusic_zpdnekdrzrea0".
    [Fact]
    public void StoreAppName_DropsThePublisherAndTheHash()
    {
        var reg = new FakeRegistry();
        StoreTask(reg, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "Spotify", 2);
        StoreTask(reg, "MSTeams_8wekyb3d8bbwe", "TeamsTfwStartupTask", 2);

        var names = new StartupManager(reg, null).List().Select(i => i.Name).ToArray();

        Assert.Contains("SpotifyMusic", names);
        Assert.Contains("MSTeams", names);
    }

    [Fact]
    public void DisablingAStoreApp_WritesStateZero_AndEnablingWritesTwo()
    {
        var reg = new FakeRegistry();
        StoreTask(reg, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "Spotify", 2);
        var mgr = new StartupManager(reg, null);

        Assert.True(mgr.SetEnabled("Store", "SpotifyMusic", enabled: false));
        Assert.Equal(0, reg.GetInt($@"{StartupManager.StoreRoot}\SpotifyAB.SpotifyMusic_zpdnekdrzrea0\Spotify", "State"));

        Assert.True(mgr.SetEnabled("Store", "SpotifyMusic", enabled: true));
        Assert.Equal(2, reg.GetInt($@"{StartupManager.StoreRoot}\SpotifyAB.SpotifyMusic_zpdnekdrzrea0\Spotify", "State"));
    }

    // One package can register several startup tasks — Spotify registers two.
    // Toggling the app must move all of them, or the app still starts.
    [Fact]
    public void APackageWithSeveralTasks_TogglesAllOfThem()
    {
        var reg = new FakeRegistry();
        StoreTask(reg, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "Spotify", 2);
        StoreTask(reg, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "SpotifyLauncher", 2);

        var items = new StartupManager(reg, null).List();
        Assert.Single(items, i => i.Hive == "Store");        // one row, not two

        new StartupManager(reg, null).SetEnabled("Store", "SpotifyMusic", enabled: false);
        Assert.Equal(0, reg.GetInt($@"{StartupManager.StoreRoot}\SpotifyAB.SpotifyMusic_zpdnekdrzrea0\Spotify", "State"));
        Assert.Equal(0, reg.GetInt($@"{StartupManager.StoreRoot}\SpotifyAB.SpotifyMusic_zpdnekdrzrea0\SpotifyLauncher", "State"));
    }

    [Fact]
    public void NoStoreApps_ChangesNothingAboutTheRunEntries()
    {
        var reg = new FakeRegistry();
        reg.SetString(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "OneDrive", "x");

        var items = new StartupManager(reg, null).List();

        Assert.Single(items);
        Assert.Equal("HKCU", items[0].Hive);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/BriskEngine.Tests --filter "FullyQualifiedName~StartupManagerTests"`
Expected: FAIL — `StartupManager.StoreRoot` does not exist.

- [ ] **Step 3: Add the Store source to `StartupManager`**

Add the constant and a small record beside the existing `Hives` table:

```csharp
    /// Store apps register startup tasks here rather than under Run — the same
    /// records Task Manager writes. State 2 is enabled (by the user), 1 enabled
    /// by default, 0 disabled. Missing this table made brisk blind to the second
    /// largest boot cost on the maintainer's machine.
    public const string StoreRoot =
        @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";

    public const string StoreHive = "Store";
```

Add a reader that groups a package's tasks into one entry:

```csharp
    /// "SpotifyAB.SpotifyMusic_zpdnekdrzrea0" -> "SpotifyMusic".
    /// "MSTeams_8wekyb3d8bbwe" -> "MSTeams".
    internal static string FriendlyPackageName(string packageFamilyName)
    {
        var withoutHash = packageFamilyName.Split('_')[0];
        var dot = withoutHash.LastIndexOf('.');
        return dot >= 0 && dot < withoutHash.Length - 1 ? withoutHash[(dot + 1)..] : withoutHash;
    }

    private IEnumerable<(string Package, string Task, int State)> StoreTasks()
    {
        foreach (var package in _registry.GetSubKeyNames(StoreRoot))
        foreach (var task in _registry.GetSubKeyNames($@"{StoreRoot}\{package}"))
        {
            var state = _registry.GetInt($@"{StoreRoot}\{package}\{task}", "State");
            // No State value means this subkey is not a startup task — the same
            // parent holds Schemas and PersistedStorageItemTable.
            if (state is not null) yield return (package, task, state.Value);
        }
    }
```

In `List()`, after the existing hive loop, append one row per package:

```csharp
        foreach (var group in StoreTasks().GroupBy(t => t.Package))
        {
            var name = FriendlyPackageName(group.Key);
            // A package can register several tasks; the row speaks for the app,
            // so it reads as enabled when any of them is.
            var enabled = group.Any(t => t.State != 0);
            items.Add(new StartupEntry(StoreHive, name, enabled, IsHeavy(name)));
        }
```

In `SetEnabled`, dispatch before the existing hive lookup:

```csharp
        if (string.Equals(hive, StoreHive, StringComparison.OrdinalIgnoreCase))
            return SetStoreEnabled(name, enabled);
```

```csharp
    private bool SetStoreEnabled(string name, bool enabled)
    {
        var matched = false;
        foreach (var (package, task, _) in StoreTasks())
        {
            if (!string.Equals(FriendlyPackageName(package), name, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                _registry.SetInt($@"{StoreRoot}\{package}\{task}", "State", enabled ? 2 : 0);
            }
            catch (UnauthorizedAccessException) { return false; }
            matched = true;
        }
        if (!matched) return false;
        _log?.Append(new { ts = DateTime.UtcNow, startup = name, hive = StoreHive,
            action = enabled ? "enable" : "disable" });
        return true;
    }
```

Add `using System.Linq;` if the file does not already have it (it does).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/BriskEngine.Tests --filter "FullyQualifiedName~StartupManagerTests"`
Expected: PASS.

- [ ] **Step 5: Clear the orphaned approval record**

`StartupLauncher.RemoveLegacyValue` deletes the Run value but leaves the matching
`StartupApproved\Run` record, which is dead data describing an entry that no longer
exists. Delete that too, in the same method, guarded the same way. Add a test in
`src/Brisk.Tests/SettingsTests.cs` asserting both are gone after `Apply`.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/BriskEngine src/BriskEngine.Tests src/Brisk src/Brisk.Tests
git commit -m "feat: the startup list stops being blind to store apps"
```

---

### Task 2: Event log probe

> **Superseded in implementation — read the code, not this section, for the shapes.**
> The two-call surface below (`RecentBoots` + `RecentOffenders`) was rejected during review
> and replaced: the probe now correlates, so `RecentBoots` returns boots that carry their
> own `Offenders`, `BootOffender` has no timestamp, and `MainPathMs` is `int?`. The reason
> is recorded in the ledger — a flat offender list could be cut mid-boot with no way for the
> consumer to tell a partial list from a complete one. This section is left as written so the
> record shows what was asked before the machine answered back.

**Files:**
- Create: `src/BriskEngine/Diagnostics/BootRecord.cs`
- Create: `src/BriskEngine/Diagnostics/RealProbes/RealEventLogProbe.cs`
- Modify: `src/BriskEngine/BriskEngine.csproj` (package reference)
- Modify: `src/BriskEngine/Diagnostics/Probes.cs`
- Modify: `src/BriskEngine/Diagnostics/DiagnosticContext.cs`
- Modify: `src/BriskEngine.Tests/TestContext.cs`
- Test: `src/BriskEngine.Tests/EventLogProbeTests.cs`

`DiagnosticContext` is a positional record, so all four construction sites must be
updated in this task — the same three wave 1 found plus `TestContext.Empty`:
`src/Brisk/Services/AppServices.cs`, `src/Brisk.Cli/Program.cs`,
`src/Brisk.Tests/EngineHostTests.cs` (which needs a `NullEventLog` beside its other
`file sealed class Null*` fakes), and `src/BriskEngine.Tests/TestContext.cs`.

**Interfaces:**
- Produces: `BootRecord(DateTime When, int BootMs, int MainPathMs)`;
  `BootOffender(DateTime When, string Name, string FriendlyName, string Path, int DegradationMs)`;
  `IEventLogProbe` with `IReadOnlyList<BootRecord> RecentBoots(int count)` and
  `IReadOnlyList<BootOffender> RecentOffenders(int count)`;
  `FakeEventLog` with mutable `Boots` and `Offenders` lists.
  The new member sits on `DiagnosticContext` **after `Displays` and before `Disk`**.

- [ ] **Step 1: Add the package**

In `src/BriskEngine/BriskEngine.csproj`, add to the existing `ItemGroup`:

```xml
    <PackageReference Include="System.Diagnostics.EventLog" Version="8.0.1" />
```

Run `dotnet restore` and confirm it resolves. `System.Diagnostics.Eventing.Reader`
is unavailable on .NET 8 without it.

- [ ] **Step 2: Write the failing test**

Create `src/BriskEngine.Tests/EventLogProbeTests.cs`:

```csharp
using System;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

public class EventLogProbeTests
{
    [Fact]
    public void FakeEventLog_ReturnsWhatItWasGiven()
    {
        var log = new FakeEventLog();
        log.Boots.Add(new BootRecord(new DateTime(2026, 8, 18), 51237, 24437));
        log.Offenders.Add(new BootOffender(new DateTime(2026, 8, 18), "Spotify.exe",
            "Spotify", @"C:\x\Spotify.exe", 37141));

        Assert.Equal(51237, log.RecentBoots(5)[0].BootMs);
        Assert.Equal(37141, log.RecentOffenders(5)[0].DegradationMs);
    }

    [Fact]
    public void EmptyContext_HasNoBootHistory()
    {
        Assert.Empty(TestContext.Empty().EventLog.RecentBoots(5));
        Assert.Empty(TestContext.Empty().EventLog.RecentOffenders(5));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test src/BriskEngine.Tests --filter "FullyQualifiedName~EventLogProbeTests"`
Expected: FAIL — the types do not exist.

- [ ] **Step 4: Add the models and interface**

Create `src/BriskEngine/Diagnostics/BootRecord.cs`:

```csharp
using System;

namespace BriskEngine.Diagnostics;

/// One entry from Windows' own boot performance log (ID 100). Windows measures
/// this itself, which is what makes it stronger than any heuristic brisk could
/// invent. PostBootTime and BootDegradationTime are documented but come back
/// empty on Windows 11 26100, so they are deliberately absent here.
public sealed record BootRecord(DateTime When, int BootMs, int MainPathMs);

/// One thing Windows blamed for slowing a boot (ID 101). DegradationMs is the
/// part Windows attributes to this program beyond what it expected.
public sealed record BootOffender(
    DateTime When,
    string Name,          // "Spotify.exe"
    string FriendlyName,  // "Spotify" — may be empty
    string Path,
    int DegradationMs);
```

Append to `src/BriskEngine/Diagnostics/Probes.cs`:

```csharp
public interface IEventLogProbe
{
    IReadOnlyList<BootRecord> RecentBoots(int count);
    IReadOnlyList<BootOffender> RecentOffenders(int count);
}
```

- [ ] **Step 5: Add the fake and wire the context**

Append to `src/BriskEngine.Tests/TestContext.cs`, before the static class:

```csharp
public sealed class FakeEventLog : IEventLogProbe
{
    public List<BootRecord> Boots = new();
    public List<BootOffender> Offenders = new();
    public IReadOnlyList<BootRecord> RecentBoots(int count) =>
        Boots.GetRange(0, Math.Min(count, Boots.Count));
    public IReadOnlyList<BootOffender> RecentOffenders(int count) =>
        Offenders.GetRange(0, Math.Min(count, Offenders.Count));
}
```

Add `IEventLogProbe EventLog,` to `DiagnosticContext` immediately after
`IDisplayProbe Displays,`, then update all four construction sites in the same
position. `EngineHostTests` needs:

```csharp
file sealed class NullEventLog : IEventLogProbe
{
    public IReadOnlyList<BootRecord> RecentBoots(int count) => System.Array.Empty<BootRecord>();
    public IReadOnlyList<BootOffender> RecentOffenders(int count) => System.Array.Empty<BootOffender>();
}
```

- [ ] **Step 6: Write the real probe**

Create `src/BriskEngine/Diagnostics/RealProbes/RealEventLogProbe.cs`. Query
`Microsoft-Windows-Diagnostics-Performance/Operational` with
`EventLogQuery` + `EventLogReader`, newest first. Read ID 100 into `BootRecord` and
ID 101 into `BootOffender`, pulling values by **field name** from the event's XML
payload rather than by index — the index order is not contractual.

Field names, measured on real hardware: ID 100 carries `BootTime` and
`MainPathBootTime`; ID 101 carries `Name`, `FriendlyName`, `TotalTime`,
`DegradationTime`, `Path`.

The whole method body must be wrapped so an unreadable log returns empty rather than
throwing — this log requires elevation, and `brisk scan` from an ordinary prompt will
hit `UnauthorizedAccessException`. Follow `RealSensorProbe`'s shape: catch, return
empty, never let a probe failure reach a rule.

- [ ] **Step 7: Wire the real probe**

Add `new RealEventLogProbe(),` in the matching position at
`src/Brisk/Services/AppServices.cs` and `src/Brisk.Cli/Program.cs`.

- [ ] **Step 8: Run the full suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/BriskEngine src/BriskEngine.Tests src/Brisk src/Brisk.Cli
git commit -m "feat: the engine can read what windows measured about your last boots"
```

---

### Task 3: The boot-degradation rule

**Files:**
- Create: `src/BriskEngine/Diagnostics/Rules/BootDegradationRule.cs`
- Modify: `src/BriskEngine/Diagnostics/DiagnosticRuleRegistry.cs`
- Modify: `src/Brisk/ViewModels/FindingSections.cs`
- Modify: `src/Brisk/Localization/Strings.resx`, `Strings.tr.resx`
- Modify: `src/BriskEngine.Tests/Rules/SystemRulesTests.cs` (the hardcoded rule count)
- Test: `src/BriskEngine.Tests/Rules/BootDegradationRuleTests.cs`

**Interfaces:**
- Consumes: `IEventLogProbe`, `BootRecord`, `BootOffender`, `FakeEventLog` from
  Task 2; `StartupManager` including the Store entries from Task 1.
- Produces: rule id `"boot-degradation"`, `Advise`, `Warning`, 4 stars.

**The shapes this rule consumes, as they actually exist after Task 2:**

```csharp
IEventLogProbe.RecentBoots(int count)   // newest first, each boot carrying its own offenders
BootRecord(DateTime When, int BootMs, int? MainPathMs, IReadOnlyList<BootOffender> Offenders)
BootOffender(string Name, string FriendlyName, string Path, int DegradationMs)
FakeEventLog { public List<BootRecord> Boots; }
```

Note what changed while Task 2 was built: there is **no** `RecentOffenders` call, offenders
carry no timestamp, and `MainPathMs` is nullable. Write the tests against these.

**Design — every number below came off real hardware, and three of them overturned what
this plan originally said:**

- Require at least **3** boot records; report the **median** `BootMs`. One bad boot after
  an update is normal and must not raise a finding.
- Report only when the median exceeds **40 seconds**. The maintainer's machine sits at 57 s
  and is genuinely slow; a 20 s boot is not a finding.
- Aggregate offenders by `Name` across the sampled boots and take each one's **worst**
  `DegradationMs`, not a sum. Report the top three.
- Fall back to `Name` when `FriendlyName` is empty. It genuinely is for some programs —
  `brisk-app.exe` arrived with none.

**Never join the boot time and the offender list with a sum.** The sentence this plan
originally specified — *"boot takes 57 s and 37 s of it belongs to these three"* — is false.
`DegradationTime` means "this program started slower than Windows expected", not "this
program added that much to your boot". On the maintainer's machine a 51.2 s boot had **no**
blamed programs while a *faster* 45.3 s boot had two. State the two facts side by side and
let the user connect them.

The phrasing rule `BootRecord.Offenders` already carries applies to the copy: **"Windows
blamed these three", never "only these three"**. The list is best effort — an unreadable
record is dropped rather than guessed at — so a completeness claim would be an
overstatement of exactly the kind this product refuses.

**Write copy for the boot Windows blamed nobody for.** Three of the maintainer's ten most
recent boots named no one. A slow boot with an empty list is a normal outcome, not a failure
to explain, and the rule must not read as though it lost the answer.

**Do not compute `BootMs - MainPathMs`.** It equals `BootPostBootTime`, which Windows
already publishes by name, and it means main path versus post-boot — not "Windows versus
your programs". Four of the five programs named on that machine are Microsoft's own.

**Name what brisk cannot act on rather than hiding it.** Defender carried the largest single
degradation measured, 52 s, and brisk must never offer to touch it. Windows components are
usually the largest number on the screen; omitting them would misrepresent where the time
went. Say plainly which entries the Startup page can switch and which it cannot.

This rule stays `Advise`: it names what is actionable and the Startup page carries the
switches. Wiring a fix button through the rule is deliberately out of scope, because mapping
an executable name to a startup entry is fuzzy and a wrong match would disable the wrong
program.

- [ ] **Step 1: Write the failing tests**

Create `src/BriskEngine.Tests/Rules/BootDegradationRuleTests.cs`. Build contexts with
`TestContext.Empty() with { EventLog = log }` and a `FakeEventLog` whose `Boots` you fill
directly — the fake has no offender list of its own any more.

Pin at least these, and say in your report if you added more:

| Case | Expectation |
|---|---|
| three boots at 51237 / 111814 / 57089 ms, one carrying Spotify at 37141 ms | a finding; `Advise`; `CanFix` false; evidence contains `Spotify` and the median in seconds |
| two boots only | no finding — not enough to judge |
| 18000 / 111814 / 19000 ms | no finding — one bad boot among fast ones is not a slow machine |
| no boots at all | no finding |
| the same program blamed on two boots at 7694 and 52661 ms | reported once, at 52661; `7694` must not appear |
| a slow boot whose `Offenders` is empty | a finding that reads as a slow boot nobody was blamed for, not as a missing answer |
| an offender whose `FriendlyName` is `""` | the evidence shows its `Name`, never a blank |
| a boot with `MainPathMs` null | no crash, and no arithmetic on it |

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/BriskEngine.Tests --filter "FullyQualifiedName~BootDegradationRuleTests"`
Expected: FAIL — `BootDegradationRule` does not exist.

- [ ] **Step 3: Write the rule**

Create `src/BriskEngine/Diagnostics/Rules/BootDegradationRule.cs`, deriving from
`AdviseRuleBase` like `ThermalsRule` does. Constants: `MinimumBoots = 3`,
`SlowBootMs = 40_000`, `TopOffenders = 3`. Sample the last 8 boots and the last
25 offenders.

Evidence shape, in English on the record and localized through
`rule.boot-degradation.evidence` with the readings as `{0}`:

> Boot takes about 57 s. Windows blames: Spotify 37 s, Defender 52 s, TiWorker 9 s.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/BriskEngine.Tests --filter "FullyQualifiedName~BootDegradationRuleTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Register, route, and localize**

Add `new BootDegradationRule(),` to `DiagnosticRuleRegistry.All`, add
`"boot-degradation"` to the `Performance` set in `FindingSections.cs`, and add the
three keys to **both** resx files. Bump the hardcoded count in
`SystemRulesTests.Registry_HasFourteenRules_WithUniqueIds` to fifteen and rename it,
as previous waves did.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/BriskEngine src/BriskEngine.Tests src/Brisk
git commit -m "feat: brisk says what your boot actually costs you, and who is charging"
```

---

### Task 4: Hardware probe and the memory-speed rule

**Files:**
- Create: `src/BriskEngine/Diagnostics/MemoryModule.cs`
- Create: `src/BriskEngine/Diagnostics/RealProbes/RealHardwareProbe.cs`
- Create: `src/BriskEngine/Diagnostics/Rules/MemorySpeedRule.cs`
- Modify: `Probes.cs`, `DiagnosticContext.cs`, `TestContext.cs`, all four
  construction sites, `DiagnosticRuleRegistry.cs`, `FindingSections.cs`, both resx,
  `SystemRulesTests.cs`
- Test: `src/BriskEngine.Tests/Rules/MemorySpeedRuleTests.cs`

**Interfaces:**
- Produces: `MemoryModule(string Slot, int RatedMts, int ConfiguredMts, long CapacityBytes)`;
  `IHardwareProbe` with `IReadOnlyList<MemoryModule> MemoryModules()`;
  `FakeHardware`; rule id `"memory-speed"`, `Advise`, `Warning`, 4 stars.
  `DiagnosticContext` gains `Hardware` **after `EventLog`, before `Disk`**.

**Design, corrected by real hardware — read this before writing the threshold:**

The maintainer's machine runs two modules rated 3200 MT/s at 2933. The original
200 MT/s threshold would have fired and told him to enable a BIOS profile that would
not have helped, because 2933 is that platform's ceiling. WMI exposes neither the
memory controller's maximum nor whether an XMP profile exists.

So: fire only when `ConfiguredMts` is **at or below 80%** of `RatedMts` — the
signature of a profile that was never enabled, where DDR4 falls back to its 2133 or
2400 JEDEC base. And never state the cause: the evidence reports the measurement and
names both possible explanations.

Report in **MT/s, never MHz**. DDR performs two transfers per clock, and the
most-upvoted reply in the source thread was a correction of exactly that confusion.

- [ ] **Step 1: Write the failing test**

Create `src/BriskEngine.Tests/Rules/MemorySpeedRuleTests.cs`:

```csharp
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class MemorySpeedRuleTests
{
    private static DiagnosticContext With(params MemoryModule[] modules)
    {
        var hw = new FakeHardware();
        hw.Modules.AddRange(modules);
        return TestContext.Empty() with { Hardware = hw };
    }

    [Fact]
    public void ProfileNeverEnabled_IsAFinding()
    {
        var ctx = With(new MemoryModule("DIMM0", 3200, 2133, 16L << 30));
        var finding = new MemorySpeedRule().Detect(ctx);

        Assert.NotNull(finding);
        Assert.Equal(RuleCategory.Advise, finding!.Category);
        Assert.False(finding.CanFix);
        Assert.Contains("MT/s", finding.Evidence);
        Assert.DoesNotContain("MHz", finding.Evidence);
    }

    // The maintainer's own machine: 3200-rated modules at 2933. That is this
    // platform's ceiling, not a disabled profile, and WMI cannot tell them
    // apart — so brisk must not send anyone into a BIOS over it.
    [Fact]
    public void PlatformCeiling_IsNotAFinding()
    {
        Assert.Null(new MemorySpeedRule().Detect(
            With(new MemoryModule("DIMM0", 3200, 2933, 16L << 30))));
    }

    [Fact]
    public void RunningAtRatedSpeed_IsNotAFinding()
    {
        Assert.Null(new MemorySpeedRule().Detect(
            With(new MemoryModule("DIMM0", 3200, 3200, 16L << 30))));
    }

    // Soldered laptop memory legitimately reports equal or zero values.
    [Fact]
    public void UnavailableData_IsNotAFinding()
    {
        Assert.Null(new MemorySpeedRule().Detect(
            With(new MemoryModule("DIMM0", 0, 0, 8L << 30))));
        Assert.Null(new MemorySpeedRule().Detect(TestContext.Empty()));
    }

    [Fact]
    public void Evidence_NamesBothExplanations_AndClaimsNeither()
    {
        var ctx = With(new MemoryModule("DIMM0", 3200, 2133, 16L << 30));
        var evidence = new MemorySpeedRule().Detect(ctx)!.Evidence;

        Assert.Contains("3200", evidence);
        Assert.Contains("2133", evidence);
        Assert.Contains("XMP", evidence);        // one explanation
        Assert.Contains("support", evidence);    // the other: the board may not support it
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/BriskEngine.Tests --filter "FullyQualifiedName~MemorySpeedRuleTests"`
Expected: FAIL — the types do not exist.

- [ ] **Step 3: Add the model, interface, fake, and context member**

`MemoryModule(string Slot, int RatedMts, int ConfiguredMts, long CapacityBytes)`,
`IHardwareProbe.MemoryModules()`, `FakeHardware` with a mutable `Modules` list, and
`IHardwareProbe Hardware` on `DiagnosticContext` after `EventLog`. Update all four
construction sites and add `NullHardware` to `EngineHostTests`.

- [ ] **Step 4: Write the real probe**

`RealHardwareProbe` queries `Win32_PhysicalMemory` through `System.Management`,
reading `DeviceLocator`, `Speed` (rated), `ConfiguredClockSpeed`, and `Capacity`.
Wrap the query so an unavailable WMI returns empty. Treat a null or zero
`ConfiguredClockSpeed` as unknown, not as zero.

- [ ] **Step 5: Write the rule**

`MemorySpeedRule : AdviseRuleBase`, id `"memory-speed"`, `Warning`, 4 stars,
`CanFix: false`. Constant `SlowRatio = 0.80`. Skip any module whose rated or
configured value is zero. Evidence names the slot, both speeds in MT/s, and both
explanations without choosing between them.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test src/BriskEngine.Tests --filter "FullyQualifiedName~MemorySpeedRuleTests"`
Expected: PASS (5 tests).

- [ ] **Step 7: Register, route, localize**

Registry, `FindingSections` (Performance), both resx files, and bump the rule-count
test to sixteen.

- [ ] **Step 8: Run the full suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/BriskEngine src/BriskEngine.Tests src/Brisk src/Brisk.Cli
git commit -m "feat: memory running below its rating gets named, without inventing why"
```

---

### Task 5: Thermals tells the truth about what it cannot read

**Files:**
- Modify: `src/BriskEngine/Diagnostics/Rules/ThermalsRule.cs`
- Modify: both resx files
- Test: `src/BriskEngine.Tests/Rules/AdviseRulesTests.cs`

Wave 1's elevation manifest was justified by CPU temperature. Measured afterwards:
GPU reads without elevation, CPU reads with neither, because LibreHardwareMonitor
gets it through the WinRing0 kernel driver and that driver is on Microsoft's
vulnerable-driver blocklist — which this machine enforces, with Memory Integrity
running. On a default Windows 11, thermals are GPU-only whatever brisk asks for.

Today `ThermalsRule` silently reports whichever sensors answered. A user seeing only
a GPU number cannot tell whether their CPU is cool or unread. Fix that: when a sensor
returns nothing, say it was not read and why, rather than omitting it.

- [ ] **Step 1: Write the failing test**

Add to `src/BriskEngine.Tests/Rules/AdviseRulesTests.cs` a test asserting that a
context whose sensors return a GPU temperature but no CPU temperature produces
evidence that mentions the CPU as unread — and one asserting that when both answer,
no such note appears.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/BriskEngine.Tests --filter "FullyQualifiedName~AdviseRulesTests"`
Expected: FAIL.

- [ ] **Step 3: Amend the rule and the strings**

Add the sentence to the evidence when `CpuTempC()` is null, in both resx files. Keep
it short and non-accusatory: the driver is blocked by a Windows security feature
brisk deliberately does not disable.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/BriskEngine src/BriskEngine.Tests src/Brisk
git commit -m "fix: an unread sensor says so instead of quietly reporting half the story"
```

---

## Wave 2 exit criteria

1. `dotnet test` passes with 0 warnings.
2. Run elevated on the maintainer's machine and confirm against the measured facts
   above: boot median near 57 s, Spotify and Defender named, Spotify present and
   toggleable in the startup list under the `Store` hive, and `memory-speed`
   **silent** (3200 rated at 2933 is that platform's ceiling).
3. Findings are reported to the maintainer before any fix is applied on his machine.
