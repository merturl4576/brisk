# Wave 1 — Display Refresh, Start Menu Web Search, Elevation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the two rules a user feels immediately — a display left below its
refresh rate and a Start menu that waits on the network — and make brisk run
elevated so its sensor path stops silently failing.

**Architecture:** A new `IDisplayProbe` joins the existing probe set on
`DiagnosticContext`, so both rules stay pure judgment over fakeable data.
The refresh-rate fix is provisional: because a bad display mode hides the undo
button behind a black screen, applying it starts a 15-second confirmation that
rolls back automatically. Elevation arrives via an application manifest, which
breaks `HKCU\Run` autostart, so `StartupLauncher` moves to a Scheduled Task.

**Tech Stack:** .NET 8 (`net8.0-windows`), C#, WPF, xUnit, Win32 interop
(`user32.dll`), `schtasks.exe`.

**Spec:** `docs/superpowers/specs/2026-08-18-seven-rules-design.md`

## Global Constraints

- `TreatWarningsAsErrors` is `true` in every project. Warnings fail the build.
- `Nullable` is `enable` in every project. Annotate honestly; do not use `!`
  to silence a genuine null.
- `ImplicitUsings` is **enabled** in `BriskEngine`, **disabled** in `Brisk`.
  Files under `src/Brisk` must declare every `using` explicitly.
- Platform is `x64`, target `net8.0-windows`, Windows 10 1809+ / Windows 11.
- Every rule id is lowercase kebab-case and is the localization key stem:
  `rule.<id>.title`, `rule.<id>.evidence`, and `rule.<id>.done` for fixables.
- Every localization key added to `Strings.resx` MUST also be added to
  `Strings.tr.resx`. `LocTests` fails the build when the two drift.
- Fix/Undo round-trip is mandatory for any rule with `CanFix: true`.
- Rules never throw from `Detect`. Missing data means "no finding", never a
  guess.
- Commit messages: lowercase `type: subject`, subject describes the behaviour
  change, not the file touched. Match the existing log style.

---

### Task 1: Display probe

**Files:**
- Create: `src/BriskEngine/Diagnostics/DisplayInfo.cs`
- Create: `src/BriskEngine/Diagnostics/RealProbes/RealDisplayProbe.cs`
- Modify: `src/BriskEngine/Diagnostics/Probes.cs` (append `IDisplayProbe`)
- Modify: `src/BriskEngine/Diagnostics/DiagnosticContext.cs`
- Modify: `src/BriskEngine.Tests/TestContext.cs` (add `FakeDisplays`, extend `Empty`)
- Test: `src/BriskEngine.Tests/DisplayProbeTests.cs`

`DiagnosticContext` is a positional record, so adding a member breaks every
construction site. There are exactly three, and all three must be updated in
this task:
- Modify: `src/Brisk/Services/AppServices.cs:35`
- Modify: `src/Brisk.Cli/Program.cs:30`
- Modify: `src/Brisk.Tests/EngineHostTests.cs:100` (also needs a `NullDisplays`)

**Interfaces:**
- Produces: `DisplayInfo(string DeviceName, string FriendlyName, int CurrentHz, int MaxHz)`;
  `IDisplayProbe` with `IReadOnlyList<DisplayInfo> Displays()` and
  `void SetRefreshRate(string deviceName, int hz)`;
  `FakeDisplays` with a mutable `List<DisplayInfo> Attached` and a
  `List<(string Device, int Hz)> SetCalls`;
  `DiagnosticContext` gains a `Displays` member positioned after `Sensors`.

- [ ] **Step 1: Write the failing test**

Create `src/BriskEngine.Tests/DisplayProbeTests.cs`:

```csharp
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

public class DisplayProbeTests
{
    [Fact]
    public void FakeDisplays_RecordsSetCalls()
    {
        var displays = new FakeDisplays();
        displays.Attached.Add(new DisplayInfo(@"\\.\DISPLAY1", "Dell U2720Q", 60, 144));

        displays.SetRefreshRate(@"\\.\DISPLAY1", 144);

        Assert.Equal(@"\\.\DISPLAY1", displays.SetCalls[0].Device);
        Assert.Equal(144, displays.SetCalls[0].Hz);
    }

    [Fact]
    public void EmptyContext_HasNoDisplays()
    {
        Assert.Empty(TestContext.Empty().Displays.Displays());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/BriskEngine.Tests --filter "FullyQualifiedName~DisplayProbeTests"`
Expected: FAIL — `DisplayInfo`, `FakeDisplays` and `DiagnosticContext.Displays` do not exist.

- [ ] **Step 3: Add the model and interface**

Create `src/BriskEngine/Diagnostics/DisplayInfo.cs`:

```csharp
namespace BriskEngine.Diagnostics;

/// One attached display, and the refresh rates its driver reports for the
/// resolution it is running at right now. MaxHz is computed by the probe
/// because "which modes count" is a Win32 enumeration detail; the rule above
/// only decides whether the gap is worth reporting.
public sealed record DisplayInfo(
    string DeviceName,     // @"\\.\DISPLAY1" — the name ChangeDisplaySettingsEx wants
    string FriendlyName,   // what the finding shows the user
    int CurrentHz,
    int MaxHz);
```

Append to `src/BriskEngine/Diagnostics/Probes.cs`:

```csharp
public interface IDisplayProbe
{
    IReadOnlyList<DisplayInfo> Displays();
    void SetRefreshRate(string deviceName, int hz);
}
```

- [ ] **Step 4: Add the fake**

Append to `src/BriskEngine.Tests/TestContext.cs`, before the static `TestContext` class:

```csharp
public sealed class FakeDisplays : IDisplayProbe
{
    public List<DisplayInfo> Attached = new();
    public List<(string Device, int Hz)> SetCalls = new();

    public IReadOnlyList<DisplayInfo> Displays() => Attached;

    public void SetRefreshRate(string deviceName, int hz)
    {
        SetCalls.Add((deviceName, hz));
        var i = Attached.FindIndex(d => d.DeviceName == deviceName);
        if (i >= 0) Attached[i] = Attached[i] with { CurrentHz = hz };
    }
}
```

- [ ] **Step 5: Extend the context**

In `src/BriskEngine/Diagnostics/DiagnosticContext.cs`, add `IDisplayProbe Displays,`
immediately after `ISensorProbe Sensors,`.

In `TestContext.Empty`, add `new FakeDisplays(),` in the same position — after
`new FakeSensors(),` and before `new FakeDisk(),`.

- [ ] **Step 6: Write the real probe**

Create `src/BriskEngine/Diagnostics/RealProbes/RealDisplayProbe.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BriskEngine.Diagnostics.RealProbes;

public sealed class RealDisplayProbe : IDisplayProbe
{
    private const int EnumCurrentSettings = -1;
    private const uint AttachedToDesktop = 0x1;
    private const uint CdsUpdateRegistry = 0x1;
    private const uint DmDisplayFrequency = 0x400000;

    public IReadOnlyList<DisplayInfo> Displays()
    {
        var found = new List<DisplayInfo>();
        var adapter = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref adapter, 0); i++)
        {
            if ((adapter.StateFlags & AttachedToDesktop) == 0)
            {
                adapter.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
                continue;
            }

            var device = adapter.DeviceName;
            var current = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(device, EnumCurrentSettings, ref current))
            {
                var max = current.dmDisplayFrequency;
                var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
                for (int m = 0; EnumDisplaySettings(device, m, ref mode); m++)
                {
                    // Only modes the display is actually running: a higher rate
                    // at a lower resolution is not an improvement.
                    if (mode.dmPelsWidth == current.dmPelsWidth &&
                        mode.dmPelsHeight == current.dmPelsHeight &&
                        mode.dmBitsPerPel == current.dmBitsPerPel &&
                        mode.dmDisplayFrequency > max)
                        max = mode.dmDisplayFrequency;
                    mode.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
                }
                found.Add(new DisplayInfo(device, FriendlyName(device, adapter.DeviceString),
                    (int)current.dmDisplayFrequency, (int)max));
            }
            adapter.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
        }
        return found;
    }

    public void SetRefreshRate(string deviceName, int hz)
    {
        var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode)) return;
        mode.dmDisplayFrequency = (uint)hz;
        mode.dmFields = DmDisplayFrequency;
        ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
    }

    /// The monitor attached to an adapter carries the name a user recognises;
    /// when it has none, the adapter's own description is the honest fallback.
    private static string FriendlyName(string device, string adapterName)
    {
        var monitor = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        if (EnumDisplayDevices(device, 0, ref monitor, 0) &&
            !string.IsNullOrWhiteSpace(monitor.DeviceString))
            return monitor.DeviceString;
        return adapterName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(
        string? device, uint devNum, ref DISPLAY_DEVICE info, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(
        string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(
        string deviceName, ref DEVMODE devMode, IntPtr wnd, uint flags, IntPtr param);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }
}
```

- [ ] **Step 7: Wire all three construction sites**

In `src/Brisk/Services/AppServices.cs:35`, inside the `new DiagnosticContext(...)`
call, add `new RealDisplayProbe(),` immediately after `sensors,`.

In `src/Brisk.Cli/Program.cs:30`, add `new RealDisplayProbe(),` in the same
position — after the sensor probe argument.

In `src/Brisk.Tests/EngineHostTests.cs:100`, add `new NullDisplays(),` after
`new NullSensors(),`, and add the fake beside the other `file sealed class Null*`
declarations near the top of that file:

```csharp
file sealed class NullDisplays : IDisplayProbe
{
    public IReadOnlyList<DisplayInfo> Displays() => System.Array.Empty<DisplayInfo>();
    public void SetRefreshRate(string deviceName, int hz) { }
}
```

- [ ] **Step 8: Run the full suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/BriskEngine src/BriskEngine.Tests src/Brisk/Services/AppServices.cs
git commit -m "feat: the engine can see what refresh rate each display is running"
```

---

### Task 2: The display-refresh rule

**Files:**
- Create: `src/BriskEngine/Diagnostics/Rules/DisplayRefreshRule.cs`
- Modify: `src/BriskEngine/Diagnostics/DiagnosticRuleRegistry.cs`
- Modify: `src/Brisk/ViewModels/FindingSections.cs:12-15`
- Modify: `src/Brisk/Localization/Strings.resx`, `src/Brisk/Localization/Strings.tr.resx`
- Test: `src/BriskEngine.Tests/Rules/DisplayRefreshRuleTests.cs`

**Interfaces:**
- Consumes: `IDisplayProbe`, `DisplayInfo`, `FakeDisplays` from Task 1.
- Produces: `DisplayRefreshRule` with `public const int MinimumGapHz = 10`
  and rule id `"display-refresh"`.

- [ ] **Step 1: Write the failing test**

Create `src/BriskEngine.Tests/Rules/DisplayRefreshRuleTests.cs`:

```csharp
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class DisplayRefreshRuleTests
{
    private static (DiagnosticContext ctx, FakeDisplays displays) Context(
        params DisplayInfo[] attached)
    {
        var displays = new FakeDisplays();
        displays.Attached.AddRange(attached);
        return (TestContext.Empty() with { Displays = displays }, displays);
    }

    [Fact]
    public void SixtyOnA144HzPanel_IsAFinding()
    {
        var (ctx, _) = Context(new DisplayInfo(@"\\.\DISPLAY1", "Dell U2720Q", 60, 144));
        var finding = new DisplayRefreshRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Equal(RuleCategory.Auto, finding!.Category);
        Assert.True(finding.CanFix);
        Assert.Contains("144", finding.Evidence);
    }

    [Fact]
    public void AlreadyAtMaximum_NoFinding()
    {
        var (ctx, _) = Context(new DisplayInfo(@"\\.\DISPLAY1", "Dell U2720Q", 144, 144));
        Assert.Null(new DisplayRefreshRule().Detect(ctx));
    }

    // 59.94 Hz is reported as 59 next to a nominal 60. That is a unit-rounding
    // artefact, not a display left on the wrong mode, and reporting it would
    // make brisk look like it is inventing problems.
    [Fact]
    public void OneHzOfRounding_IsNotAFinding()
    {
        var (ctx, _) = Context(new DisplayInfo(@"\\.\DISPLAY1", "Generic PnP Monitor", 59, 60));
        Assert.Null(new DisplayRefreshRule().Detect(ctx));
    }

    [Fact]
    public void OnlyDisplaysBehind_AreFixed()
    {
        var (ctx, displays) = Context(
            new DisplayInfo(@"\\.\DISPLAY1", "Dell U2720Q", 60, 144),
            new DisplayInfo(@"\\.\DISPLAY2", "Laptop panel", 120, 120));
        new DisplayRefreshRule().Fix(ctx);
        Assert.Single(displays.SetCalls);
        Assert.Equal((@"\\.\DISPLAY1", 144), displays.SetCalls[0]);
    }

    [Fact]
    public void FixThenUndo_RestoresEachPriorRate()
    {
        var (ctx, displays) = Context(
            new DisplayInfo(@"\\.\DISPLAY1", "Dell U2720Q", 60, 144),
            new DisplayInfo(@"\\.\DISPLAY2", "BenQ XL2411", 75, 165));
        var rule = new DisplayRefreshRule();
        var prior = rule.Fix(ctx);
        rule.Undo(ctx, prior);
        Assert.Equal(60, displays.Attached.Find(d => d.DeviceName == @"\\.\DISPLAY1")!.CurrentHz);
        Assert.Equal(75, displays.Attached.Find(d => d.DeviceName == @"\\.\DISPLAY2")!.CurrentHz);
    }

    [Fact]
    public void NoDisplays_NoFinding()
    {
        Assert.Null(new DisplayRefreshRule().Detect(TestContext.Empty()));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/BriskEngine.Tests --filter "FullyQualifiedName~DisplayRefreshRuleTests"`
Expected: FAIL — `DisplayRefreshRule` does not exist.

- [ ] **Step 3: Write the rule**

Create `src/BriskEngine/Diagnostics/Rules/DisplayRefreshRule.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

public sealed class DisplayRefreshRule : IDiagnosticRule
{
    /// Below this a gap is unit rounding — 59.94 Hz surfaces as 59 beside a
    /// nominal 60 — rather than a display parked on the wrong mode.
    public const int MinimumGapHz = 10;

    public string Id => "display-refresh";
    public RuleCategory Category => RuleCategory.Auto;

    private static List<DisplayInfo> Behind(DiagnosticContext ctx) =>
        ctx.Displays.Displays()
            .Where(d => d.MaxHz - d.CurrentHz >= MinimumGapHz)
            .ToList();

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var behind = Behind(ctx);
        if (behind.Count == 0) return null;

        var readings = string.Join(", ",
            behind.Select(d => $"{d.FriendlyName} {d.CurrentHz} Hz / {d.MaxHz} Hz"));
        return new DiagnosticFinding(
            Id, "rule.display-refresh.title",
            "A display is running below its refresh rate",
            $"{readings}. Windows left the display slower than it supports, " +
            "so everything on screen moves at the lower rate.",
            Severity.Critical, Category, ImpactStars: 5, CanFix: true,
            FixDescription: "Raise each display to its highest refresh rate (undoable)",
            EvidenceKey: $"rule.{Id}.evidence", EvidenceArgs: new[] { readings });
    }

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Dictionary<string, int>();
        foreach (var display in Behind(ctx))
        {
            prior[display.DeviceName] = display.CurrentHz;
            ctx.Displays.SetRefreshRate(display.DeviceName, display.MaxHz);
        }
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Dictionary<string, int>>(priorStateJson)!;
        foreach (var (device, hz) in prior) ctx.Displays.SetRefreshRate(device, hz);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/BriskEngine.Tests --filter "FullyQualifiedName~DisplayRefreshRuleTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Register the rule and route it to the Performance page**

In `src/BriskEngine/Diagnostics/DiagnosticRuleRegistry.cs`, add
`new DisplayRefreshRule(),` to the array.

In `src/Brisk/ViewModels/FindingSections.cs`, add `"display-refresh"` to the
`Performance` set — a refresh rate is a speed lever, not machine condition.

- [ ] **Step 6: Add localization strings**

Add to `src/Brisk/Localization/Strings.resx`:

```xml
  <data name="rule.display-refresh.title" xml:space="preserve"><value>A display is running below its refresh rate</value></data>
  <data name="rule.display-refresh.evidence" xml:space="preserve"><value>{0}. Windows left the display slower than it supports, so everything on screen moves at the lower rate.</value></data>
  <data name="rule.display-refresh.done" xml:space="preserve"><value>Displays raised to their highest refresh rate</value></data>
```

Add to `src/Brisk/Localization/Strings.tr.resx`:

```xml
  <data name="rule.display-refresh.title" xml:space="preserve"><value>Ekran, desteklediği yenileme hızının altında çalışıyor</value></data>
  <data name="rule.display-refresh.evidence" xml:space="preserve"><value>{0}. Windows ekranı desteklediğinden yavaş bir hızda bırakmış; bu yüzden ekrandaki her şey daha düşük hızda akıyor.</value></data>
  <data name="rule.display-refresh.done" xml:space="preserve"><value>Ekranlar en yüksek yenileme hızına alındı</value></data>
```

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: PASS. `LocTests` verifies the two resx files carry the same keys.

- [ ] **Step 8: Commit**

```bash
git add src/BriskEngine src/BriskEngine.Tests src/Brisk
git commit -m "feat: a 144 Hz panel stuck at 60 stops going unnoticed"
```

---

### Task 3: The search-web-results rule

**Files:**
- Create: `src/BriskEngine/Diagnostics/Rules/SearchWebResultsRule.cs`
- Modify: `src/BriskEngine/Diagnostics/DiagnosticRuleRegistry.cs`
- Modify: `src/Brisk/ViewModels/FindingSections.cs`
- Modify: `src/Brisk/Localization/Strings.resx`, `src/Brisk/Localization/Strings.tr.resx`
- Test: `src/BriskEngine.Tests/Rules/SearchWebResultsRuleTests.cs`

**Interfaces:**
- Consumes: `IRegistryProbe` and `FakeRegistry` (both already exist).
- Produces: `SearchWebResultsRule` with rule id `"search-web-results"` and
  public constants `PolicyKey`, `PolicyValue`, `LegacyKey`, `LegacyValue`.

- [ ] **Step 1: Write the failing test**

Create `src/BriskEngine.Tests/Rules/SearchWebResultsRuleTests.cs`:

```csharp
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class SearchWebResultsRuleTests
{
    private static (DiagnosticContext ctx, FakeRegistry reg) Context()
    {
        var reg = new FakeRegistry();
        return (TestContext.Empty() with { Registry = reg }, reg);
    }

    [Fact]
    public void UntouchedMachine_IsAFinding()
    {
        var (ctx, _) = Context();
        var finding = new SearchWebResultsRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Equal(RuleCategory.Auto, finding!.Category);
        Assert.True(finding.CanFix);
    }

    [Fact]
    public void AlreadyDisabled_NoFinding()
    {
        var (ctx, reg) = Context();
        reg.SetInt(SearchWebResultsRule.PolicyKey, SearchWebResultsRule.PolicyValue, 1);
        Assert.Null(new SearchWebResultsRule().Detect(ctx));
    }

    // A policy value that exists but says "keep web search on" was written by
    // an administrator. brisk does not fight Group Policy.
    [Fact]
    public void PolicyExplicitlyEnablesWebSearch_NoFinding()
    {
        var (ctx, reg) = Context();
        reg.SetInt(SearchWebResultsRule.PolicyKey, SearchWebResultsRule.PolicyValue, 0);
        Assert.Null(new SearchWebResultsRule().Detect(ctx));
    }

    [Fact]
    public void WindowsTenRouteAlreadyTaken_NoFinding()
    {
        var (ctx, reg) = Context();
        reg.SetInt(SearchWebResultsRule.LegacyKey, SearchWebResultsRule.LegacyValue, 0);
        Assert.Null(new SearchWebResultsRule().Detect(ctx));
    }

    [Fact]
    public void Fix_SetsBothValues()
    {
        var (ctx, reg) = Context();
        new SearchWebResultsRule().Fix(ctx);
        Assert.Equal(1, reg.GetInt(SearchWebResultsRule.PolicyKey, SearchWebResultsRule.PolicyValue));
        Assert.Equal(0, reg.GetInt(SearchWebResultsRule.LegacyKey, SearchWebResultsRule.LegacyValue));
    }

    [Fact]
    public void FixThenUndo_LeavesNoTrace()
    {
        var (ctx, reg) = Context();
        var rule = new SearchWebResultsRule();
        rule.Undo(ctx, rule.Fix(ctx));
        Assert.Null(reg.GetInt(SearchWebResultsRule.PolicyKey, SearchWebResultsRule.PolicyValue));
        Assert.Null(reg.GetInt(SearchWebResultsRule.LegacyKey, SearchWebResultsRule.LegacyValue));
    }

    [Fact]
    public void FixThenUndo_RestoresAPreExistingLegacyValue()
    {
        var (ctx, reg) = Context();
        reg.SetInt(SearchWebResultsRule.LegacyKey, SearchWebResultsRule.LegacyValue, 1);
        var rule = new SearchWebResultsRule();
        rule.Undo(ctx, rule.Fix(ctx));
        Assert.Equal(1, reg.GetInt(SearchWebResultsRule.LegacyKey, SearchWebResultsRule.LegacyValue));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/BriskEngine.Tests --filter "FullyQualifiedName~SearchWebResultsRuleTests"`
Expected: FAIL — `SearchWebResultsRule` does not exist.

- [ ] **Step 3: Write the rule**

Create `src/BriskEngine/Diagnostics/Rules/SearchWebResultsRule.cs`:

```csharp
using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

public sealed class SearchWebResultsRule : IDiagnosticRule
{
    public const string PolicyKey = @"HKCU\Software\Policies\Microsoft\Windows\Explorer";
    public const string PolicyValue = "DisableSearchBoxSuggestions";
    public const string LegacyKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search";
    public const string LegacyValue = "BingSearchEnabled";

    private sealed record Prior(int? Policy, int? Legacy);

    public string Id => "search-web-results";
    public RuleCategory Category => RuleCategory.Auto;

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        // Any existing policy value is somebody's decision: 1 means the fix is
        // already in place, anything else means an administrator wants web
        // results. Either way there is nothing for brisk to do.
        if (ctx.Registry.GetInt(PolicyKey, PolicyValue) is not null) return null;
        // Windows 10's own switch, already thrown.
        if (ctx.Registry.GetInt(LegacyKey, LegacyValue) == 0) return null;

        return new DiagnosticFinding(
            Id, "rule.search-web-results.title",
            "Start menu search waits on the internet",
            "Every keystroke in Start is sent to Bing, and local results for " +
            "your apps and files wait for that round-trip. Turning web results " +
            "off makes Start answer immediately. Takes effect after you sign in again.",
            Severity.Warning, Category, ImpactStars: 4, CanFix: true,
            FixDescription: "Stop Start menu search from querying the web (undoable)",
            EvidenceKey: $"rule.{Id}.evidence", EvidenceArgs: null);
    }

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Prior(ctx.Registry.GetInt(PolicyKey, PolicyValue),
                              ctx.Registry.GetInt(LegacyKey, LegacyValue));
        ctx.Registry.SetInt(PolicyKey, PolicyValue, 1);
        ctx.Registry.SetInt(LegacyKey, LegacyValue, 0);
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Prior>(priorStateJson)!;
        if (prior.Policy is null) ctx.Registry.DeleteValue(PolicyKey, PolicyValue);
        else ctx.Registry.SetInt(PolicyKey, PolicyValue, prior.Policy.Value);
        if (prior.Legacy is null) ctx.Registry.DeleteValue(LegacyKey, LegacyValue);
        else ctx.Registry.SetInt(LegacyKey, LegacyValue, prior.Legacy.Value);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/BriskEngine.Tests --filter "FullyQualifiedName~SearchWebResultsRuleTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Register and route**

Add `new SearchWebResultsRule(),` to `DiagnosticRuleRegistry.All`, and
`"search-web-results"` to the `Performance` set in `FindingSections.cs`.

- [ ] **Step 6: Add localization strings**

Add to `src/Brisk/Localization/Strings.resx`:

```xml
  <data name="rule.search-web-results.title" xml:space="preserve"><value>Start menu search waits on the internet</value></data>
  <data name="rule.search-web-results.evidence" xml:space="preserve"><value>Every keystroke in Start is sent to Bing, and local results for your apps and files wait for that round-trip. Turning web results off makes Start answer immediately. Takes effect after you sign in again.</value></data>
  <data name="rule.search-web-results.done" xml:space="preserve"><value>Start menu search no longer queries the web</value></data>
```

Add to `src/Brisk/Localization/Strings.tr.resx`:

```xml
  <data name="rule.search-web-results.title" xml:space="preserve"><value>Başlat menüsü araması interneti bekliyor</value></data>
  <data name="rule.search-web-results.evidence" xml:space="preserve"><value>Başlat'a yazdığın her harf Bing'e gönderiliyor; uygulamalarına ve dosyalarına ait yerel sonuçlar bu gidiş dönüşü bekliyor. Web sonuçlarını kapatmak Başlat'ın anında cevap vermesini sağlar. Yeniden oturum açtığında etkili olur.</value></data>
  <data name="rule.search-web-results.done" xml:space="preserve"><value>Başlat menüsü araması artık web'e sormuyor</value></data>
```

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/BriskEngine src/BriskEngine.Tests src/Brisk
git commit -m "feat: the start menu stops waiting on bing before it answers"
```

---

### Task 4: Run elevated

**Files:**
- Create: `src/Brisk/app.manifest`
- Modify: `src/Brisk/Brisk.csproj`

**Interfaces:**
- Produces: nothing consumed by other tasks. Task 5 depends on this being
  merged, because a Scheduled Task at highest privileges is only useful once
  the app itself is willing to run elevated.

- [ ] **Step 1: Write the manifest**

Create `src/Brisk/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <!-- brisk reads hardware sensors and writes machine-wide settings. Running
       as a standard user made RealSensorProbe fail silently, so a user with a
       real heat problem was told nothing at all. -->
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 and Windows 11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 2: Reference it from the project**

In `src/Brisk/Brisk.csproj`, inside the existing `<PropertyGroup>`, add:

```xml
    <ApplicationManifest>app.manifest</ApplicationManifest>
```

- [ ] **Step 3: Build and verify the manifest is embedded**

Run: `dotnet build`
Expected: build succeeds.

Then confirm the requested execution level is present in the produced binary:

```bash
grep -c requireAdministrator src/Brisk/bin/Debug/net8.0-windows/brisk-app.exe
```

Expected: a count of at least 1. (The manifest is embedded as text in the PE
resource section, so a plain grep finds it.)

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: PASS. Tests exercise view models, not the elevated process, so no
test should change.

- [ ] **Step 5: Commit**

```bash
git add src/Brisk/app.manifest src/Brisk/Brisk.csproj
git commit -m "fix: the sensor probe stops failing silently for want of elevation"
```

---

### Task 5: Autostart via Scheduled Task

**Files:**
- Modify: `src/Brisk/Services/StartupLauncher.cs` (full rewrite)
- Modify: `src/Brisk/Services/AppServices.cs` (the `Launcher` construction)
- Modify: `src/Brisk.Tests/SettingsTests.cs:84-97`
- Modify: `src/Brisk.Tests/SecondaryViewModelTests.cs:122`
- Modify: `src/Brisk.Tests/Fakes.cs` (add `FakeProcessRunner`)

**Interfaces:**
- Consumes: `IProcessRunner` from `BriskEngine.Cleaning` — `(int ExitCode, string StdOut) Run(string exe, string args)`.
- Produces: `StartupLauncher(IProcessRunner runner, string exePath)` keeping the
  same public surface as before — `bool IsOn()` and `void Apply(bool on)` — so
  `SettingsViewModel` needs no change. Adds `public const string TaskName = "brisk-logon"`.
  `FakeProcessRunner` with `List<(string Exe, string Args)> Calls` and a
  settable `int NextExitCode`.

- [ ] **Step 1: Write the failing test**

Add `FakeProcessRunner` to `src/Brisk.Tests/Fakes.cs`:

```csharp
public sealed class FakeProcessRunner : BriskEngine.Cleaning.IProcessRunner
{
    public System.Collections.Generic.List<(string Exe, string Args)> Calls = new();
    public int NextExitCode;

    public (int ExitCode, string StdOut) Run(string exe, string args)
    {
        Calls.Add((exe, args));
        return (NextExitCode, "");
    }
}
```

Replace the `StartupLauncher_OnWritesQuotedCommand_OffRemoves` test in
`src/Brisk.Tests/SettingsTests.cs` with:

```csharp
    // HKCU\Run cannot start an app that requires elevation — Windows just
    // skips it. A Scheduled Task at highest privileges is the supported way,
    // and it starts without a UAC prompt at logon.
    [Fact]
    public void StartupLauncher_OnCreatesElevatedLogonTask()
    {
        var runner = new FakeProcessRunner();
        var launcher = new StartupLauncher(runner, @"C:\Apps\brisk-app.exe");

        launcher.Apply(true);

        var (exe, args) = runner.Calls[0];
        Assert.Equal("schtasks.exe", exe);
        Assert.Contains("/Create", args);
        Assert.Contains("/TN brisk-logon", args);
        Assert.Contains("/SC ONLOGON", args);
        Assert.Contains("/RL HIGHEST", args);
        Assert.Contains(@"C:\Apps\brisk-app.exe", args);
        Assert.Contains("--tray", args);
    }

    [Fact]
    public void StartupLauncher_OffDeletesTheTask()
    {
        var runner = new FakeProcessRunner();
        new StartupLauncher(runner, @"C:\Apps\brisk-app.exe").Apply(false);

        var (exe, args) = runner.Calls[0];
        Assert.Equal("schtasks.exe", exe);
        Assert.Contains("/Delete", args);
        Assert.Contains("/TN brisk-logon", args);
    }

    [Fact]
    public void StartupLauncher_IsOn_FollowsTheQueryExitCode()
    {
        var runner = new FakeProcessRunner();
        var launcher = new StartupLauncher(runner, @"C:\Apps\brisk-app.exe");

        runner.NextExitCode = 0;
        Assert.True(launcher.IsOn());

        runner.NextExitCode = 1;
        Assert.False(launcher.IsOn());
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Brisk.Tests --filter "FullyQualifiedName~SettingsTests"`
Expected: FAIL — `StartupLauncher` still takes an `IRegistryProbe`.

- [ ] **Step 3: Rewrite the launcher**

Replace `src/Brisk/Services/StartupLauncher.cs` entirely:

```csharp
using BriskEngine.Cleaning;

namespace Brisk.Services;

/// Registers brisk to start at logon. Default is OFF: a tool that criticizes
/// startup bloat earns trust by staying out of startup unless asked — and when
/// it is asked, it lists itself among its own startup findings.
///
/// A Scheduled Task rather than HKCU\Run, because brisk requires elevation and
/// Windows silently refuses to auto-start an elevated app from the Run key.
/// "Run with highest privileges" starts it elevated with no UAC prompt.
public sealed class StartupLauncher
{
    public const string TaskName = "brisk-logon";

    private readonly IProcessRunner _runner;
    private readonly string _exePath;

    public StartupLauncher(IProcessRunner runner, string exePath)
    {
        _runner = runner;
        _exePath = exePath;
    }

    public bool IsOn() =>
        _runner.Run("schtasks.exe", $"/Query /TN {TaskName}").ExitCode == 0;

    public void Apply(bool on)
    {
        if (on)
            _runner.Run("schtasks.exe",
                $"/Create /F /TN {TaskName} /SC ONLOGON /RL HIGHEST " +
                $"/TR \"\\\"{_exePath}\\\" --tray\"");
        else
            _runner.Run("schtasks.exe", $"/Delete /F /TN {TaskName}");
    }
}
```

- [ ] **Step 4: Update the two call sites**

In `src/Brisk/Services/AppServices.cs`, change the `Launcher` construction to
pass the `runner` already created at the top of `Build()`:

```csharp
            Launcher = new StartupLauncher(runner,
                Path.Combine(AppContext.BaseDirectory, "brisk-app.exe")),
```

In `src/Brisk.Tests/SecondaryViewModelTests.cs:122`, replace
`new StartupLauncher(reg, @"C:\x\brisk-app.exe")` with
`new StartupLauncher(new FakeProcessRunner(), @"C:\x\brisk-app.exe")`.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS. If `SettingsTests` no longer uses `RunKey` or `MemRegistry`,
remove the now-unused members — `TreatWarningsAsErrors` will otherwise fail
the build on the unused field.

- [ ] **Step 6: Commit**

```bash
git add src/Brisk src/Brisk.Tests
git commit -m "fix: start with windows survives the app needing elevation"
```

---

### Task 6: The refresh-rate confirmation

**Files:**
- Create: `src/Brisk/Services/RefreshConfirmation.cs`
- Test: `src/Brisk.Tests/RefreshConfirmationTests.cs`

**Interfaces:**
- Produces: `RefreshConfirmation(Action rollback, Func<TimeSpan, CancellationToken, Task>? delay = null)`
  with `TimeSpan Window { get; init; }` defaulting to 15 seconds,
  `bool RolledBack { get; }`, `Task<bool> AwaitConfirmationAsync()` and
  `void Keep()`. Task 7 binds this to the page.

- [ ] **Step 1: Write the failing test**

Create `src/Brisk.Tests/RefreshConfirmationTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Brisk.Services;
using Xunit;

namespace Brisk.Tests;

public class RefreshConfirmationTests
{
    // The window elapsing means the user never answered — which is exactly
    // what a black screen looks like from here.
    [Fact]
    public async Task WindowElapses_RollsBack()
    {
        var rolledBack = false;
        var confirmation = new RefreshConfirmation(
            () => rolledBack = true, (_, _) => Task.CompletedTask);

        Assert.False(await confirmation.AwaitConfirmationAsync());
        Assert.True(rolledBack);
        Assert.True(confirmation.RolledBack);
    }

    [Fact]
    public async Task Kept_DoesNotRollBack()
    {
        var rolledBack = false;
        var confirmation = new RefreshConfirmation(
            () => rolledBack = true,
            (_, ct) => Task.Delay(Timeout.Infinite, ct));

        var pending = confirmation.AwaitConfirmationAsync();
        confirmation.Keep();

        Assert.True(await pending);
        Assert.False(rolledBack);
        Assert.False(confirmation.RolledBack);
    }

    [Fact]
    public void DefaultWindow_IsFifteenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(15),
            new RefreshConfirmation(() => { }).Window);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Brisk.Tests --filter "FullyQualifiedName~RefreshConfirmationTests"`
Expected: FAIL — `RefreshConfirmation` does not exist.

- [ ] **Step 3: Write the service**

Create `src/Brisk/Services/RefreshConfirmation.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Brisk.Services;

/// A display mode change is the one fix whose failure also removes the user's
/// ability to undo it: a driver can advertise a rate the cable or adapter
/// cannot carry, and the screen goes black. So the change is provisional —
/// unless it is confirmed inside the window, it rolls back on its own.
public sealed class RefreshConfirmation
{
    private readonly Action _rollback;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _kept = new();

    public RefreshConfirmation(Action rollback,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _rollback = rollback;
        _delay = delay ?? Task.Delay;
    }

    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(15);

    public bool RolledBack { get; private set; }

    /// True when the user confirmed the picture is back; false when the window
    /// elapsed and the prior mode was restored.
    public async Task<bool> AwaitConfirmationAsync()
    {
        try
        {
            await _delay(Window, _kept.Token);
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        _rollback();
        RolledBack = true;
        return false;
    }

    public void Keep() => _kept.Cancel();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/Brisk.Tests --filter "FullyQualifiedName~RefreshConfirmationTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Brisk/Services/RefreshConfirmation.cs src/Brisk.Tests/RefreshConfirmationTests.cs
git commit -m "feat: a display change that blanks the screen undoes itself"
```

---

### Task 7: Wire the confirmation into the performance page

`HealthViewModel` is the class behind **both** pages: `App.xaml.cs:66` builds it
with `FindingSections.IsHealth` and `App.xaml.cs:70` builds a second instance
with `FindingSections.IsPerformance`. Task 2 routed `display-refresh` to
Performance, so the view-model change lands once and the overlay belongs on
`PerfPage.xaml`.

**Files:**
- Modify: `src/Brisk/Views/LocKeyConverter.cs` (add `NullToVis`)
- Modify: `src/Brisk/ViewModels/HealthViewModel.cs`
- Modify: `src/Brisk/Views/PerfPage.xaml`
- Modify: `src/Brisk/Localization/Strings.resx`, `src/Brisk/Localization/Strings.tr.resx`
- Test: `src/Brisk.Tests/HealthViewModelTests.cs`

**Interfaces:**
- Consumes: `RefreshConfirmation` from Task 6; rule id `"display-refresh"` from
  Task 2; `IEngineHost.Undo(string ruleId)`.
- Produces: `HealthViewModel.PendingConfirmation` (`RefreshConfirmation?`) and
  `HealthViewModel.KeepDisplayCommand` (`RelayCommand`); `NullToVis.Instance`.

- [ ] **Step 1: Write the failing test**

Add to `src/Brisk.Tests/HealthViewModelTests.cs`, following the `Build()` +
`state.ScanAsync()` pattern the neighbouring tests already use:

```csharp
    // A display change can blank the screen, so it is applied provisionally.
    [Fact]
    public async Task FixingDisplayRefresh_RaisesAConfirmation()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("display-refresh", Severity.Critical, RuleCategory.Auto,
                stars: 5, canFix: true),
        });
        await state.ScanAsync();

        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "display-refresh"));

        Assert.NotNull(vm.PendingConfirmation);
    }

    [Fact]
    public async Task FixingAnotherRule_RaisesNoConfirmation()
    {
        var (vm, _, state) = Build();
        await state.ScanAsync();

        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "power-plan"));

        Assert.Null(vm.PendingConfirmation);
    }

    [Fact]
    public async Task ConfirmationWindowElapsing_UndoesTheDisplayFix()
    {
        var (vm, host, state) = Build();
        host.NextSnapshot = TestData.Snapshot(new[]
        {
            TestData.Finding("display-refresh", Severity.Critical, RuleCategory.Auto,
                stars: 5, canFix: true),
        });
        await state.ScanAsync();

        // Zero-length window: the same path a user takes by not answering.
        vm.ConfirmationWindow = TimeSpan.Zero;
        await vm.FixAsync(vm.Rows.First(r => r.RuleId == "display-refresh"));

        Assert.Equal(new[] { "display-refresh" }, host.Undone);
        Assert.Null(vm.PendingConfirmation);
    }
```

`FakeEngineHost` already records undo calls — `Undone` at
`src/Brisk.Tests/Fakes.cs:50`, appended by its `Undo` at line 73. Nothing to add.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Brisk.Tests --filter "FullyQualifiedName~HealthViewModelTests"`
Expected: FAIL — `PendingConfirmation` and `ConfirmationWindow` do not exist.

- [ ] **Step 3: Add the view-model state**

In `src/Brisk/ViewModels/HealthViewModel.cs`, add `using System;` and
`using Brisk.Services;` if absent (this project has `ImplicitUsings` disabled).

Add the backing field next to the other private fields:

```csharp
    private RefreshConfirmation? _pendingConfirmation;
```

Add the properties next to the other public members (`ViewModelBase` supplies
`Set`, which raises `PropertyChanged` — there is no `OnPropertyChanged` here):

```csharp
    /// Non-null while a display mode change is still provisional. The view
    /// shows a countdown over the page; a user staring at a black screen
    /// cannot answer it, which is exactly what the timeout means.
    public RefreshConfirmation? PendingConfirmation
    {
        get => _pendingConfirmation;
        private set => Set(ref _pendingConfirmation, value);
    }

    /// Overridable so tests can elapse the window without waiting.
    public TimeSpan ConfirmationWindow { get; set; } = TimeSpan.FromSeconds(15);
```

Create the command in the constructor, alongside `FixAllCommand`, and declare
it as a get-only property so the binding holds one instance:

```csharp
        KeepDisplayCommand = new RelayCommand(() => PendingConfirmation?.Keep());
```

```csharp
    public RelayCommand KeepDisplayCommand { get; }
```

Add the confirmation helper:

```csharp
    /// Undo goes through the host by rule id rather than through a row: the
    /// fix triggers a rescan, so by now the row may be gone.
    private async Task ConfirmDisplayAsync(string ruleId)
    {
        if (ruleId != "display-refresh") return;
        var confirmation = new RefreshConfirmation(() => _host.Undo(ruleId))
        {
            Window = ConfirmationWindow,
        };
        PendingConfirmation = confirmation;
        try
        {
            await confirmation.AwaitConfirmationAsync();
        }
        finally
        {
            PendingConfirmation = null;
        }
    }
```

In `FixAsync`, after the `outcome.Ok` branch completes, add:

```csharp
            if (outcome.Ok) await ConfirmDisplayAsync(row.RuleId);
```

In `FixAllAsync`, after the batch completes, add the same call for each rule id
that was fixed in that batch.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/Brisk.Tests --filter "FullyQualifiedName~HealthViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Add the null-to-visibility converter**

Append to `src/Brisk/Views/LocKeyConverter.cs`, mirroring `BoolToVis` at line 17:

```csharp
public sealed class NullToVis : IValueConverter
{
    public static readonly NullToVis Instance = new();
    public object Convert(object? value, Type targetType, object? parameter,
        CultureInfo culture) =>
        value is null ? System.Windows.Visibility.Collapsed
                      : System.Windows.Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter,
        CultureInfo culture) => throw new NotSupportedException();
}
```

- [ ] **Step 6: Add the overlay**

`src/Brisk/Views/PerfPage.xaml` already declares both
`xmlns:loc="clr-namespace:Brisk.Localization"` and
`xmlns:Brisk="clr-namespace:Brisk.Views"` (lines 4-5). Its root element is
`<DockPanel Margin="18,16">` on line 6, closing on line 189.

Wrap that `DockPanel` in a `<Grid>` and add this as the Grid's last child, so it
paints over the page.

This file has no `{loc:Str …}` markup extension — every string binds through the
`Loc` indexer, as at line 24. The overlay uses the same form:

```xml
        <Border Background="#CC000000" Panel.ZIndex="10"
                Visibility="{Binding PendingConfirmation,
                    Converter={x:Static Brisk:NullToVis.Instance}}">
            <Border Style="{StaticResource HeroStrip}" MaxWidth="420" Padding="24"
                    VerticalAlignment="Center" HorizontalAlignment="Center">
                <StackPanel>
                    <TextBlock FontSize="16" FontWeight="SemiBold"
                               Margin="0,0,0,8" TextWrapping="Wrap"
                               Text="{Binding [display-confirm.title],
                                   Source={x:Static loc:Loc.Instance}}" />
                    <TextBlock Opacity="0.8" TextWrapping="Wrap" Margin="0,0,0,16"
                               Text="{Binding [display-confirm.body],
                                   Source={x:Static loc:Loc.Instance}}" />
                    <Button HorizontalAlignment="Right"
                            Command="{Binding KeepDisplayCommand}"
                            Content="{Binding [display-confirm.keep],
                                Source={x:Static loc:Loc.Instance}}" />
                </StackPanel>
            </Border>
        </Border>
```

- [ ] **Step 7: Add localization strings**

Add to `src/Brisk/Localization/Strings.resx`:

```xml
  <data name="display-confirm.title" xml:space="preserve"><value>Is the picture back?</value></data>
  <data name="display-confirm.body" xml:space="preserve"><value>The display was raised to a higher refresh rate. If you can read this, confirm within 15 seconds — otherwise the previous setting is restored automatically.</value></data>
  <data name="display-confirm.keep" xml:space="preserve"><value>Keep this setting</value></data>
```

Add to `src/Brisk/Localization/Strings.tr.resx`:

```xml
  <data name="display-confirm.title" xml:space="preserve"><value>Görüntü geldi mi?</value></data>
  <data name="display-confirm.body" xml:space="preserve"><value>Ekran daha yüksek bir yenileme hızına alındı. Bunu okuyabiliyorsan 15 saniye içinde onayla — onaylamazsan önceki ayar kendiliğinden geri yüklenir.</value></data>
  <data name="display-confirm.keep" xml:space="preserve"><value>Böyle kalsın</value></data>
```

- [ ] **Step 8: Run the full suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Brisk src/Brisk.Tests
git commit -m "feat: the display change asks whether the picture came back"
```

---

### Task 8: brisk lists itself among startup entries

The spec makes this the condition on brisk joining startup at all: a tool that
criticizes startup bloat may only do so if it holds itself to the same
standard. Without this, turning the setting on hides brisk from the very list
it asks the user to prune.

**Files:**
- Modify: `src/Brisk/ViewModels/StartupViewModel.cs`
- Modify: `src/Brisk/App.xaml.cs` (the `StartupViewModel` construction)
- Modify: `src/Brisk/Localization/Strings.resx`, `src/Brisk/Localization/Strings.tr.resx`
- Test: `src/Brisk.Tests/SecondaryViewModelTests.cs`

**Interfaces:**
- Consumes: `StartupLauncher(IProcessRunner, string)` with `IsOn()`/`Apply(bool)`
  from Task 5; `FakeProcessRunner` from Task 5; `StartupEntry(string Hive,
  string Name, bool Enabled, bool KnownHeavy)`.
- Produces: `StartupViewModel(AppState state, IEngineHost host, Loc loc,
  Func<bool> isDryRun, StartupLauncher launcher)` — one added parameter.

- [ ] **Step 1: Write the failing test**

Add to `src/Brisk.Tests/SecondaryViewModelTests.cs`:

```csharp
    [Fact]
    public async Task StartupList_IncludesBriskItself_WhenAutostartIsOn()
    {
        var host = new FakeEngineHost();
        var state = new AppState(host);
        var runner = new FakeProcessRunner { NextExitCode = 0 };   // task exists
        var vm = new StartupViewModel(state, host, EnglishLoc(), () => false,
            new StartupLauncher(runner, @"C:\x\brisk-app.exe"));

        await state.ScanAsync();

        Assert.Contains(vm.Items, i => i.Name == "brisk");
    }

    [Fact]
    public async Task StartupList_OmitsBrisk_WhenAutostartIsOff()
    {
        var host = new FakeEngineHost();
        var state = new AppState(host);
        var runner = new FakeProcessRunner { NextExitCode = 1 };   // no task
        var vm = new StartupViewModel(state, host, EnglishLoc(), () => false,
            new StartupLauncher(runner, @"C:\x\brisk-app.exe"));

        await state.ScanAsync();

        Assert.DoesNotContain(vm.Items, i => i.Name == "brisk");
    }
```

`await state.ScanAsync()` is how the existing `StartupViewModel` tests in this
file fire `AppState.Changed` and refresh the list (see the test at line 44).
`AppState` has no public `Raise`. `EnglishLoc()` already exists in this file at
line 34 — reuse it.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Brisk.Tests --filter "FullyQualifiedName~SecondaryViewModelTests"`
Expected: FAIL — `StartupViewModel` takes four arguments, not five.

- [ ] **Step 3: Add the launcher to the view model**

In `src/Brisk/ViewModels/StartupViewModel.cs`, add `using Brisk.Services;` if
absent, add the field and constructor parameter:

```csharp
    private readonly StartupLauncher _launcher;
```

```csharp
    public StartupViewModel(AppState state, IEngineHost host, Loc loc,
        Func<bool> isDryRun, StartupLauncher launcher)
    {
        _host = host;
        _loc = loc;
        _isDryRun = isDryRun;
        _launcher = launcher;
        state.Changed += Refresh;
    }
```

At the top of `Refresh()`, after `Items.Clear()` and before the engine's
entries, add brisk's own row:

```csharp
        // brisk criticizes startup bloat, so when it joins startup it shows up
        // in the same list, switchable by the same toggle.
        if (_launcher.IsOn())
            Items.Add(new StartupItemRow(
                new StartupEntry("Task", "brisk", true, false), _loc,
                (_, enabled) =>
                {
                    if (_isDryRun()) { ToggleFailed = true; return false; }
                    _launcher.Apply(enabled);
                    return true;
                }));
```

- [ ] **Step 4: Update the construction site**

In `src/Brisk/App.xaml.cs`, pass `composition.Launcher` as the new fifth
argument where `StartupViewModel` is constructed.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test src/Brisk.Tests --filter "FullyQualifiedName~SecondaryViewModelTests"`
Expected: PASS.

- [ ] **Step 6: Add the description string**

`StartupItemRow.DescriptionFor` falls through to an empty description for
unknown names, which is correct — inventing one would be a lie. brisk is not
unknown to itself, so add its line.

In `StartupItemRow.KnownApps`, add `("brisk", "brisk"),` to the array.

Add to `src/Brisk/Localization/Strings.resx`:

```xml
  <data name="startup.app.brisk" xml:space="preserve"><value>brisk itself — turn this off to stop it starting with Windows.</value></data>
```

Add to `src/Brisk/Localization/Strings.tr.resx`:

```xml
  <data name="startup.app.brisk" xml:space="preserve"><value>brisk'in kendisi — Windows ile başlamasını istemiyorsan kapat.</value></data>
```

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Brisk src/Brisk.Tests
git commit -m "feat: brisk holds itself to the startup standard it preaches"
```

---

## Wave 1 exit criteria

Before wave 2 starts:

1. `dotnet test` passes.
2. `dotnet run --project src/Brisk.Cli -- scan --json` on the maintainer's real
   machine lists `display-refresh` and `search-web-results` with correct
   evidence.
3. Findings are reported to the maintainer **before** any fix is applied on
   that machine; fixes go ahead only on explicit approval.
