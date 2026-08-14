# brisk Plan A: Engine + CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `BriskEngine` (diagnostics + allowlist cleaner, fully unit-tested) and the `brisk` CLI on top of it, producing a working command-line tool.

**Architecture:** UI-free class library `BriskEngine` holds all rules, targets, scanning, fixing and safety logic; every mutation passes through `SafetyValidator`. A thin console app `Brisk.Cli` wires real system probes into the engine. All system access (registry, powercfg, processes, sensors, filesystem specials) goes through injected probe interfaces so tests run against fakes.

**Tech Stack:** .NET 8 (`net8.0-windows`, x64), xUnit, LibreHardwareMonitorLib (NuGet), `Microsoft.VisualBasic` FileIO for Recycle Bin. No other dependencies.

**Spec:** `docs/superpowers/specs/2026-08-14-brisk-design.md` (read it first; this plan implements its Engine + CLI parts. WPF app and distribution assets are separate plans.)

## Global Constraints

- TargetFramework `net8.0-windows` on every project; `<Platforms>x64</Platforms>`.
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in every csproj.
- The app NEVER runs wholesale as admin; no manifest requesting elevation. Elevation-needing actions must set `RequiresElevation=true` and are skipped with a clear message when unelevated.
- No mutation without explicit consent: CLI mutates only with `--yes`.
- Every mutation appends to the JSONL action log; every fix records prior state in the fix journal.
- Never touch: registry "cleaning", drivers, Documents/Desktop/Pictures/Music/Videos contents, `System32`, `WinSxS`, `Program Files`.
- Engine strings: English, each finding also carries a stable string key (`title_key`) so the UI plan can localize later.
- Commit after every task (Conventional Commits style, English). Work in `C:\Users\MERT\Desktop\brisk`; ALWAYS `git -C C:\Users\MERT\Desktop\brisk ...` (the Desktop folder itself belongs to an unrelated outer repo — never commit there).
- All engine file/registry/process access via probe interfaces defined in Task 7 (filesystem sizing in Task 5 may use the real filesystem against temp dirs in tests).

## File Structure

```
brisk.sln
src/BriskEngine/
  EngineInfo.cs                    version constant
  Models/CleanupLevel.cs           enum Safe/Developer/Deep
  Models/Severity.cs               enum Info/Warning/Critical
  Models/RuleCategory.cs           enum Auto/Confirm/Advise
  Models/CleanupTarget.cs          immutable target record
  Models/ResolvedItem.cs           scan result item (path, bytes, lastWrite)
  Models/DiagnosticFinding.cs      finding record
  Paths/PathExpander.cs            %VAR% + ~ expansion
  Safety/ProtectedPaths.cs         protected root list
  Safety/SafetyValidator.cs        single authorization point
  Cleaning/CleanupTargetRegistry.cs  ~30 targets as data
  Cleaning/SizeCalculator.cs       tolerant recursive sizing
  Cleaning/Scanner.cs              targets -> ResolvedItems
  Cleaning/IRecycler.cs + WindowsRecycler.cs
  Cleaning/CleanRunner.cs          validate -> recycle -> log
  Logging/ActionLog.cs             JSONL append
  Diagnostics/Probes.cs            probe interfaces (IPowercfgProbe, IRegistryProbe, ...)
  Diagnostics/DiagnosticContext.cs probe bundle
  Diagnostics/IDiagnosticRule.cs   rule contract
  Diagnostics/FixJournal.cs        prior-state journal + undo source
  Diagnostics/FixRunner.cs         run/undo fixes
  Diagnostics/Rules/*.cs           one file per rule (12 rules)
  Diagnostics/RealProbes/*.cs      real probe implementations (used by CLI)
src/BriskEngine.Tests/             xUnit; one test file per source unit
src/Brisk.Cli/
  CliParser.cs                     testable parser -> CliCommand
  Program.cs                       wire real probes, dispatch commands
```

---

### Task 1: Solution scaffold

**Files:**
- Create: `brisk.sln`, `src/BriskEngine/BriskEngine.csproj`, `src/BriskEngine/EngineInfo.cs`, `src/BriskEngine.Tests/BriskEngine.Tests.csproj`, `src/BriskEngine.Tests/EngineInfoTests.cs`, `src/Brisk.Cli/Brisk.Cli.csproj`, `src/Brisk.Cli/Program.cs`, `.gitignore`

**Interfaces:**
- Produces: `BriskEngine.EngineInfo.Version` (string const `"0.1.0"`); solution builds with `dotnet build`, tests run with `dotnet test`.

- [ ] **Step 1: Scaffold projects**

```powershell
cd C:\Users\MERT\Desktop\brisk
dotnet new sln -n brisk
dotnet new classlib -o src/BriskEngine -n BriskEngine -f net8.0
dotnet new xunit -o src/BriskEngine.Tests -n BriskEngine.Tests -f net8.0
dotnet new console -o src/Brisk.Cli -n Brisk.Cli -f net8.0
dotnet sln add src/BriskEngine src/BriskEngine.Tests src/Brisk.Cli
dotnet add src/BriskEngine.Tests reference src/BriskEngine
dotnet add src/Brisk.Cli reference src/BriskEngine
```

Then edit ALL THREE csproj files: change `<TargetFramework>` to `net8.0-windows`, and inside the first `<PropertyGroup>` add:

```xml
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<Platforms>x64</Platforms>
<PlatformTarget>x64</PlatformTarget>
```

Delete the template files `src/BriskEngine/Class1.cs` and `src/BriskEngine.Tests/UnitTest1.cs`.

- [ ] **Step 2: Write the failing test**

`src/BriskEngine.Tests/EngineInfoTests.cs`:

```csharp
using BriskEngine;
using Xunit;

namespace BriskEngine.Tests;

public class EngineInfoTests
{
    [Fact]
    public void Version_IsSemver()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", EngineInfo.Version);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test src/BriskEngine.Tests`
Expected: build FAILS — `EngineInfo` does not exist.

- [ ] **Step 4: Minimal implementation**

`src/BriskEngine/EngineInfo.cs`:

```csharp
namespace BriskEngine;

public static class EngineInfo
{
    public const string Version = "0.1.0";
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test src/BriskEngine.Tests`
Expected: PASS (1 test).

- [ ] **Step 6: .gitignore + commit**

`.gitignore`:

```
bin/
obj/
*.user
.vs/
```

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "chore: solution scaffold (engine, tests, cli)"
```

---

### Task 2: Core models + PathExpander

**Files:**
- Create: `src/BriskEngine/Models/CleanupLevel.cs`, `Models/Severity.cs`, `Models/RuleCategory.cs`, `Models/CleanupTarget.cs`, `Models/ResolvedItem.cs`, `src/BriskEngine/Paths/PathExpander.cs`
- Test: `src/BriskEngine.Tests/PathExpanderTests.cs`

**Interfaces:**
- Produces:
  - `enum CleanupLevel { Safe, Developer, Deep }`
  - `enum Severity { Info, Warning, Critical }`
  - `enum RuleCategory { Auto, Confirm, Advise }`
  - `record CleanupTarget(string Id, string DisplayName, CleanupLevel Level, IReadOnlyList<string> PathTemplates, string Category, bool DeletesContentsNotDirectory = false, bool Regenerates = false, string? RequiresAppClosedProcess = null, bool RequiresIndividualSelection = false, bool RequiresExplicitOptIn = false, bool BypassesRecycleBin = false, bool RequiresElevation = false)`
  - `record ResolvedItem(string TargetId, string Path, long Bytes, DateTime? LastWriteUtc)`
  - `static string? PathExpander.Expand(string template)` — expands `%VAR%` env vars and leading `~` to the user profile; returns `null` if any referenced env var is undefined.

- [ ] **Step 1: Write the failing tests**

`src/BriskEngine.Tests/PathExpanderTests.cs`:

```csharp
using System;
using BriskEngine.Paths;
using Xunit;

namespace BriskEngine.Tests;

public class PathExpanderTests
{
    [Fact]
    public void Expand_LocalAppData()
    {
        var result = PathExpander.Expand(@"%LOCALAPPDATA%\Temp");
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Temp",
            result);
    }

    [Fact]
    public void Expand_TildeIsUserProfile()
    {
        var result = PathExpander.Expand(@"~\.cargo");
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\.cargo",
            result);
    }

    [Fact]
    public void Expand_UndefinedVariable_ReturnsNull()
    {
        Assert.Null(PathExpander.Expand(@"%BRISK_DOES_NOT_EXIST_XYZ%\x"));
    }

    [Fact]
    public void Expand_PlainAbsolutePath_Unchanged()
    {
        Assert.Equal(@"C:\Windows\Temp", PathExpander.Expand(@"C:\Windows\Temp"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests --filter PathExpanderTests`
Expected: build FAILS — `PathExpander` missing.

- [ ] **Step 3: Implement models and expander**

Each enum in its own file under `src/BriskEngine/Models/`, e.g. `CleanupLevel.cs`:

```csharp
namespace BriskEngine.Models;

public enum CleanupLevel { Safe, Developer, Deep }
```

(`Severity.cs` and `RuleCategory.cs` identical pattern with members from Interfaces above.)

`Models/CleanupTarget.cs`:

```csharp
using System.Collections.Generic;

namespace BriskEngine.Models;

public sealed record CleanupTarget(
    string Id,
    string DisplayName,
    CleanupLevel Level,
    IReadOnlyList<string> PathTemplates,
    string Category,
    bool DeletesContentsNotDirectory = false,
    bool Regenerates = false,
    string? RequiresAppClosedProcess = null,
    bool RequiresIndividualSelection = false,
    bool RequiresExplicitOptIn = false,
    bool BypassesRecycleBin = false,
    bool RequiresElevation = false);
```

`Models/ResolvedItem.cs`:

```csharp
using System;

namespace BriskEngine.Models;

public sealed record ResolvedItem(string TargetId, string Path, long Bytes, DateTime? LastWriteUtc);
```

`Paths/PathExpander.cs`:

```csharp
using System;

namespace BriskEngine.Paths;

public static class PathExpander
{
    /// Expands %VAR% and a leading "~" to the user profile.
    /// Returns null when a referenced environment variable is undefined,
    /// so callers can skip templates that do not apply on this machine.
    public static string? Expand(string template)
    {
        var work = template.StartsWith('~')
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + template[1..]
            : template;
        var expanded = Environment.ExpandEnvironmentVariables(work);
        return expanded.Contains('%') ? null : expanded;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/BriskEngine.Tests --filter PathExpanderTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: core models and path expander"
```

---

### Task 3: SafetyValidator (single authorization point)

**Files:**
- Create: `src/BriskEngine/Safety/ProtectedPaths.cs`, `src/BriskEngine/Safety/RealPath.cs`, `src/BriskEngine/Safety/SafetyValidator.cs`
- Test: `src/BriskEngine.Tests/SafetyValidatorTests.cs`

**Interfaces:**
- Consumes: `CleanupTarget`, `PathExpander.Expand` (Task 2).
- Produces:
  - `record AuthorizationResult(bool Allowed, string Reason)` with statics `Ok()` / `Deny(string)`
  - `AuthorizationResult SafetyValidator.Authorize(string path, CleanupTarget target)` — the ONLY gate; CleanRunner (Task 6) must call it per path.
  - `static string RealPath.Resolve(string path)` — final path after junction/symlink resolution (falls back to `Path.GetFullPath` when the path does not exist).
  - `static bool ProtectedPaths.IsProtected(string realPath)`

- [ ] **Step 1: Write the failing tests**

`src/BriskEngine.Tests/SafetyValidatorTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BriskEngine.Models;
using BriskEngine.Safety;
using Xunit;

namespace BriskEngine.Tests;

public sealed class SafetyValidatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-test-").FullName;
    private readonly SafetyValidator _validator = new();

    private CleanupTarget TargetOver(string template, bool contentsOnly = false) => new(
        Id: "test-target", DisplayName: "Test", Level: CleanupLevel.Safe,
        PathTemplates: new List<string> { template }, Category: "Test",
        DeletesContentsNotDirectory: contentsOnly);

    [Fact]
    public void PathInsideTemplate_Allowed()
    {
        var inside = Path.Combine(_root, "cache", "a.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(inside)!);
        File.WriteAllText(inside, "x");
        var result = _validator.Authorize(inside, TargetOver(Path.Combine(_root, "cache")));
        Assert.True(result.Allowed, result.Reason);
    }

    [Fact]
    public void PathOutsideTemplate_Denied()
    {
        var outside = Path.Combine(_root, "elsewhere", "a.tmp");
        var result = _validator.Authorize(outside, TargetOver(Path.Combine(_root, "cache")));
        Assert.False(result.Allowed);
    }

    [Fact]
    public void JunctionEscapingTemplate_Denied()
    {
        var template = Path.Combine(_root, "cache");
        var outside = Path.Combine(_root, "victim");
        Directory.CreateDirectory(template);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "doc.txt"), "x");
        var junction = Path.Combine(template, "jump");
        // Junctions need no admin rights; mklink is a cmd builtin.
        var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{outside}\"")
        { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);

        var result = _validator.Authorize(Path.Combine(junction, "doc.txt"), TargetOver(template));
        Assert.False(result.Allowed);
        Assert.Contains("allowlist", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProtectedFolder_DeniedEvenWhenTemplateCoversIt()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var result = _validator.Authorize(
            Path.Combine(documents, "novel.docx"), TargetOver(documents));
        Assert.False(result.Allowed);
        Assert.Contains("protected", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContentsOnlyTarget_TemplateItselfDenied_ChildAllowed()
    {
        var template = Path.Combine(_root, "cache2");
        Directory.CreateDirectory(template);
        File.WriteAllText(Path.Combine(template, "a.tmp"), "x");
        var target = TargetOver(template, contentsOnly: true);
        Assert.False(_validator.Authorize(template, target).Allowed);
        Assert.True(_validator.Authorize(Path.Combine(template, "a.tmp"), target).Allowed);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests --filter SafetyValidatorTests`
Expected: build FAILS — `SafetyValidator` missing.

- [ ] **Step 3: Implement**

`src/BriskEngine/Safety/RealPath.cs` — resolves the FINAL path (all junctions/symlinks in the chain) via `GetFinalPathNameByHandleW`:

```csharp
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BriskEngine.Safety;

public static class RealPath
{
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000; // required to open directories

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess,
        uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(IntPtr hFile,
        StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// Final filesystem path with every link in the chain resolved.
    /// A path that cannot be opened (does not exist) falls back to GetFullPath —
    /// it cannot be deleted anyway, and the validator still gets a canonical string.
    public static string Resolve(string path)
    {
        var full = Path.GetFullPath(path);
        var handle = CreateFileW(full, 0 /* query attributes only */, 7 /* rwd share */,
            IntPtr.Zero, 3 /* OPEN_EXISTING */, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle == new IntPtr(-1)) return full;
        try
        {
            var sb = new StringBuilder(1024);
            var len = GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, 0);
            if (len == 0 || len > sb.Capacity) return full;
            var final = sb.ToString();
            return final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
        }
        finally { CloseHandle(handle); }
    }
}
```

`src/BriskEngine/Safety/ProtectedPaths.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BriskEngine.Safety;

public static class ProtectedPaths
{
    /// Folders brisk must never delete from, not even via a covering template.
    public static IReadOnlyList<string> Roots()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var roots = new List<string?>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetEnvironmentVariable("OneDrive"),
            Path.Combine(windows, "System32"),
            Path.Combine(windows, "WinSxS"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };
        return roots.Where(r => !string.IsNullOrEmpty(r)).Select(r => r!).ToList();
    }

    public static bool IsProtected(string realPath)
    {
        foreach (var root in Roots())
        {
            var rootReal = RealPath.Resolve(root);
            if (string.Equals(realPath, rootReal, StringComparison.OrdinalIgnoreCase)) return true;
            if (realPath.StartsWith(rootReal + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
```

`src/BriskEngine/Safety/SafetyValidator.cs`:

```csharp
using System;
using System.IO;
using BriskEngine.Models;
using BriskEngine.Paths;

namespace BriskEngine.Safety;

public sealed record AuthorizationResult(bool Allowed, string Reason)
{
    public static AuthorizationResult Ok() => new(true, "ok");
    public static AuthorizationResult Deny(string reason) => new(false, reason);
}

/// The only component allowed to authorize a mutation. Allowlist-only:
/// a path is deletable only when its REAL path (junctions resolved) stays
/// inside a registered template's real path, and protected folders win
/// over any template as defense in depth.
public sealed class SafetyValidator
{
    public AuthorizationResult Authorize(string path, CleanupTarget target)
    {
        var pathReal = RealPath.Resolve(path);
        if (ProtectedPaths.IsProtected(pathReal))
            return AuthorizationResult.Deny($"'{pathReal}' is inside a protected folder");

        foreach (var template in target.PathTemplates)
        {
            var expanded = PathExpander.Expand(template);
            if (expanded is null) continue;
            var templateReal = RealPath.Resolve(expanded);

            var isTemplateItself = string.Equals(pathReal, templateReal,
                StringComparison.OrdinalIgnoreCase);
            var isUnder = pathReal.StartsWith(templateReal + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

            if (isTemplateItself && !target.DeletesContentsNotDirectory)
                return AuthorizationResult.Ok();
            if (isUnder)
                return AuthorizationResult.Ok();
        }
        return AuthorizationResult.Deny(
            $"'{pathReal}' is outside the allowlist of target '{target.Id}'");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/BriskEngine.Tests --filter SafetyValidatorTests`
Expected: PASS (5 tests). The junction test passes because real-path resolution makes `cache\jump\doc.txt` resolve into `victim\doc.txt`, which is outside the template — denied by the allowlist check itself, not by string tricks.

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: safety validator with junction-escape protection"
```

---

### Task 4: TemplateResolver + CleanupTargetRegistry (targets are data)

**Files:**
- Create: `src/BriskEngine/Paths/TemplateResolver.cs`, `src/BriskEngine/Cleaning/CleanupTargetRegistry.cs`
- Modify: `src/BriskEngine/Safety/SafetyValidator.cs` (template loop resolves wildcards)
- Test: `src/BriskEngine.Tests/TemplateResolverTests.cs`, `src/BriskEngine.Tests/CleanupTargetRegistryTests.cs`

**Interfaces:**
- Consumes: `PathExpander`, `CleanupTarget`, `ProtectedPaths`, `SafetyValidator` (Tasks 2-3).
- Produces:
  - `static IReadOnlyList<string> TemplateResolver.Resolve(string template)` — expands env vars, then `*` wildcards against the real filesystem; returns only existing paths; no-wildcard template returns itself iff it exists.
  - `static IReadOnlyList<CleanupTarget> CleanupTargetRegistry.All` — the entire allowlist.
  - Special target ids with EMPTY `PathTemplates` handled by CleanRunner (Task 6), never by path deletion: `"docker-prune"`, `"empty-recycle-bin"`.

- [ ] **Step 1: Write the failing tests**

`src/BriskEngine.Tests/TemplateResolverTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using BriskEngine.Paths;
using Xunit;

namespace BriskEngine.Tests;

public sealed class TemplateResolverTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-tr-").FullName;

    [Fact]
    public void NoWildcard_ExistingPath_ReturnsItself()
    {
        var result = TemplateResolver.Resolve(_root);
        Assert.Equal(new[] { _root }, result);
    }

    [Fact]
    public void NoWildcard_MissingPath_ReturnsEmpty()
    {
        Assert.Empty(TemplateResolver.Resolve(Path.Combine(_root, "nope")));
    }

    [Fact]
    public void MidSegmentWildcard_EnumeratesDirectories()
    {
        Directory.CreateDirectory(Path.Combine(_root, "p1.default", "cache2"));
        Directory.CreateDirectory(Path.Combine(_root, "p2.default", "cache2"));
        Directory.CreateDirectory(Path.Combine(_root, "p3.other")); // no cache2 inside
        var result = TemplateResolver.Resolve(Path.Combine(_root, "*", "cache2"));
        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.EndsWith("cache2", p));
    }

    [Fact]
    public void FinalSegmentWildcard_MatchesFiles()
    {
        File.WriteAllText(Path.Combine(_root, "thumbcache_32.db"), "x");
        File.WriteAllText(Path.Combine(_root, "thumbcache_96.db"), "x");
        File.WriteAllText(Path.Combine(_root, "other.txt"), "x");
        var result = TemplateResolver.Resolve(Path.Combine(_root, "thumbcache_*.db"));
        Assert.Equal(2, result.Count);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
```

`src/BriskEngine.Tests/CleanupTargetRegistryTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Models;
using BriskEngine.Paths;
using BriskEngine.Safety;
using Xunit;

namespace BriskEngine.Tests;

public class CleanupTargetRegistryTests
{
    private static readonly HashSet<string> PathlessIds = new() { "docker-prune", "empty-recycle-bin" };

    [Fact]
    public void Ids_AreUnique()
    {
        var ids = CleanupTargetRegistry.All.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void EveryTarget_HasTemplates_OrIsKnownPathless()
    {
        foreach (var t in CleanupTargetRegistry.All)
        {
            if (PathlessIds.Contains(t.Id)) { Assert.Empty(t.PathTemplates); continue; }
            Assert.NotEmpty(t.PathTemplates);
        }
    }

    [Fact]
    public void NoTemplate_PointsInsideAProtectedRoot()
    {
        // old-installers points at Downloads, which is NOT a protected root; everything
        // must stay out of Documents/Desktop/Pictures/... and system roots.
        foreach (var t in CleanupTargetRegistry.All)
        foreach (var template in t.PathTemplates)
        {
            var expanded = PathExpander.Expand(template);
            if (expanded is null) continue; // env var absent on this machine
            var probe = expanded.Split('*')[0].TrimEnd('\\');
            Assert.False(ProtectedPaths.IsProtected(Path.GetFullPath(probe)),
                $"{t.Id}: {template} resolves into a protected root");
        }
    }

    [Fact]
    public void SafeLevel_NeverRequiresElevation()
    {
        foreach (var t in CleanupTargetRegistry.All.Where(t => t.Level == CleanupLevel.Safe))
            Assert.False(t.RequiresElevation, $"{t.Id} is Safe but requires elevation");
    }

    [Fact]
    public void OldInstallers_IsIndividualSelectionOnly()
    {
        var t = CleanupTargetRegistry.All.Single(t => t.Id == "old-installers");
        Assert.True(t.RequiresIndividualSelection);
        Assert.Equal(CleanupLevel.Deep, t.Level);
    }

    [Fact]
    public void DockerPrune_IsExplicitOptIn()
    {
        var t = CleanupTargetRegistry.All.Single(t => t.Id == "docker-prune");
        Assert.True(t.RequiresExplicitOptIn);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests --filter "TemplateResolverTests|CleanupTargetRegistryTests"`
Expected: build FAILS — `TemplateResolver`, `CleanupTargetRegistry` missing.

- [ ] **Step 3: Implement TemplateResolver**

`src/BriskEngine/Paths/TemplateResolver.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace BriskEngine.Paths;

public static class TemplateResolver
{
    /// Expands env vars, then any '*' wildcards against the real filesystem.
    /// Returns only paths that exist right now. Used by BOTH the scanner and
    /// the validator so the two can never disagree about what a template means.
    public static IReadOnlyList<string> Resolve(string template)
    {
        var expanded = PathExpander.Expand(template);
        if (expanded is null) return Array.Empty<string>();
        return Glob(Path.GetFullPath(expanded));
    }

    private static IReadOnlyList<string> Glob(string path)
    {
        var star = path.IndexOf('*');
        if (star < 0)
            return File.Exists(path) || Directory.Exists(path)
                ? new[] { path }
                : Array.Empty<string>();

        var sepBefore = path.LastIndexOf('\\', star);
        var sepAfter = path.IndexOf('\\', star);
        var parent = path[..sepBefore];
        var pattern = sepAfter < 0 ? path[(sepBefore + 1)..] : path[(sepBefore + 1)..sepAfter];
        var rest = sepAfter < 0 ? null : path[(sepAfter + 1)..];
        if (!Directory.Exists(parent)) return Array.Empty<string>();

        var results = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(parent, pattern))
        {
            if (rest is null) results.Add(entry);
            else results.AddRange(Glob(entry + '\\' + rest));
        }
        return results;
    }
}
```

- [ ] **Step 4: Point SafetyValidator at TemplateResolver**

In `SafetyValidator.Authorize`, replace the body of the `foreach (var template in target.PathTemplates)` loop: instead of expanding once, iterate every concrete path:

```csharp
foreach (var template in target.PathTemplates)
foreach (var concrete in TemplateResolver.Resolve(template))
{
    var templateReal = RealPath.Resolve(concrete);

    var isTemplateItself = string.Equals(pathReal, templateReal,
        StringComparison.OrdinalIgnoreCase);
    var isUnder = pathReal.StartsWith(templateReal + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase);

    if (isTemplateItself && !target.DeletesContentsNotDirectory)
        return AuthorizationResult.Ok();
    if (isUnder)
        return AuthorizationResult.Ok();
}
```

(Change `using BriskEngine.Paths;` stays; drop the now-unused direct `PathExpander` call.)

- [ ] **Step 5: Implement the registry**

`src/BriskEngine/Cleaning/CleanupTargetRegistry.cs` — the entire allowlist. Targets are data; a new target is one entry here plus nothing else:

```csharp
using System.Collections.Generic;
using BriskEngine.Models;

namespace BriskEngine.Cleaning;

public static class CleanupTargetRegistry
{
    private static CleanupTarget T(string id, string name, CleanupLevel level,
        string category, string[] paths, bool contents = false, bool regen = false,
        string? app = null, bool pick = false, bool optIn = false,
        bool noBin = false, bool admin = false) =>
        new(id, name, level, paths, category, contents, regen, app, pick, optIn, noBin, admin);

    public static readonly IReadOnlyList<CleanupTarget> All = new List<CleanupTarget>
    {
        // ---- Safe: regenerates on its own, no elevation, zero functional impact
        T("user-temp", "User temp files", CleanupLevel.Safe, "System",
            new[] { @"%TEMP%" }, contents: true, regen: true),
        T("chrome-cache", "Chrome cache", CleanupLevel.Safe, "Browser",
            new[] { @"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Cache",
                    @"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Code Cache",
                    @"%LOCALAPPDATA%\Google\Chrome\User Data\Default\GPUCache" },
            contents: true, regen: true, app: "chrome"),
        T("edge-cache", "Edge cache", CleanupLevel.Safe, "Browser",
            new[] { @"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Cache",
                    @"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Code Cache",
                    @"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\GPUCache" },
            contents: true, regen: true, app: "msedge"),
        T("firefox-cache", "Firefox cache", CleanupLevel.Safe, "Browser",
            new[] { @"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\cache2" },
            contents: true, regen: true, app: "firefox"),
        T("brave-cache", "Brave cache", CleanupLevel.Safe, "Browser",
            new[] { @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\Default\Cache",
                    @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\Default\Code Cache",
                    @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\Default\GPUCache" },
            contents: true, regen: true, app: "brave"),
        T("opera-cache", "Opera cache", CleanupLevel.Safe, "Browser",
            new[] { @"%LOCALAPPDATA%\Opera Software\Opera Stable\Cache" },
            contents: true, regen: true, app: "opera"),
        T("thumbnail-cache", "Explorer thumbnail cache", CleanupLevel.Safe, "System",
            new[] { @"%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db" },
            regen: true),
        T("discord-cache", "Discord cache", CleanupLevel.Safe, "App",
            new[] { @"%APPDATA%\discord\Cache", @"%APPDATA%\discord\Code Cache" },
            contents: true, regen: true, app: "Discord"),
        T("spotify-storage", "Spotify cache", CleanupLevel.Safe, "App",
            new[] { @"%LOCALAPPDATA%\Spotify\Storage" },
            contents: true, regen: true, app: "Spotify"),
        T("teams-cache", "Microsoft Teams cache", CleanupLevel.Safe, "App",
            new[] { @"%APPDATA%\Microsoft\Teams\Cache",
                    @"%LOCALAPPDATA%\Packages\MSTeams_8wekyb3d8bbwe\LocalCache" },
            contents: true, regen: true, app: "ms-teams"),
        T("slack-cache", "Slack cache", CleanupLevel.Safe, "App",
            new[] { @"%APPDATA%\Slack\Cache", @"%APPDATA%\Slack\Code Cache",
                    @"%APPDATA%\Slack\GPUCache" },
            contents: true, regen: true, app: "slack"),
        T("vscode-cache", "VS Code cache", CleanupLevel.Safe, "App",
            new[] { @"%APPDATA%\Code\Cache", @"%APPDATA%\Code\CachedData",
                    @"%APPDATA%\Code\Code Cache" },
            contents: true, regen: true, app: "Code"),
        T("whatsapp-cache", "WhatsApp cache", CleanupLevel.Safe, "App",
            new[] { @"%LOCALAPPDATA%\Packages\5319275A.WhatsAppDesktop_cv1g1gvanyjgm\LocalCache" },
            contents: true, regen: true, app: "WhatsApp"),
        T("telegram-media-cache", "Telegram media cache", CleanupLevel.Safe, "App",
            new[] { @"%APPDATA%\Telegram Desktop\tdata\user_data\media_cache" },
            contents: true, regen: true, app: "Telegram"),
        T("crash-dumps", "Crash dumps", CleanupLevel.Safe, "System",
            new[] { @"%LOCALAPPDATA%\CrashDumps" }, contents: true, regen: true),
        T("wer-reports", "Windows Error Reporting queues", CleanupLevel.Safe, "System",
            new[] { @"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportQueue",
                    @"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive" },
            contents: true, regen: true),

        // ---- Developer: re-downloads or rebuilds on demand
        T("npm-cache", "npm cache", CleanupLevel.Developer, "Package Manager",
            new[] { @"%LOCALAPPDATA%\npm-cache" }, contents: true, regen: true),
        T("pip-cache", "pip cache", CleanupLevel.Developer, "Package Manager",
            new[] { @"%LOCALAPPDATA%\pip\cache" }, contents: true, regen: true),
        T("yarn-cache", "Yarn cache", CleanupLevel.Developer, "Package Manager",
            new[] { @"%LOCALAPPDATA%\Yarn\Cache" }, contents: true, regen: true),
        T("pnpm-store", "pnpm store", CleanupLevel.Developer, "Package Manager",
            new[] { @"%LOCALAPPDATA%\pnpm\store" }, contents: true, regen: true),
        T("nuget-http-cache", "NuGet HTTP cache", CleanupLevel.Developer, "Package Manager",
            new[] { @"%LOCALAPPDATA%\NuGet\v3-cache" }, contents: true, regen: true),
        T("cargo-registry-cache", "Cargo registry cache", CleanupLevel.Developer, "Package Manager",
            new[] { @"~\.cargo\registry\cache" }, contents: true, regen: true),
        T("gradle-caches", "Gradle caches", CleanupLevel.Developer, "Package Manager",
            new[] { @"~\.gradle\caches" }, contents: true, regen: true),
        T("docker-prune", "Docker unused data (docker system prune)", CleanupLevel.Developer,
            "Container", System.Array.Empty<string>(), optIn: true),

        // ---- Deep: look before you leap
        T("windows-temp", "Windows temp", CleanupLevel.Deep, "System",
            new[] { @"%SystemRoot%\Temp" }, contents: true, regen: true, admin: true),
        T("windows-update-cache", "Windows Update download cache", CleanupLevel.Deep, "System",
            new[] { @"%SystemRoot%\SoftwareDistribution\Download" },
            contents: true, regen: true, admin: true),
        T("delivery-optimization", "Delivery Optimization cache", CleanupLevel.Deep, "System",
            new[] { @"%SystemRoot%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache" },
            contents: true, regen: true, admin: true),
        T("old-installers", "Old installers in Downloads", CleanupLevel.Deep, "Downloads",
            new[] { @"%USERPROFILE%\Downloads\*.exe", @"%USERPROFILE%\Downloads\*.msi",
                    @"%USERPROFILE%\Downloads\*.iso" },
            pick: true),
        T("empty-recycle-bin", "Empty Recycle Bin", CleanupLevel.Deep, "System",
            System.Array.Empty<string>(), noBin: true, pick: true),
    };
}
```

Note on `old-installers`: the allowlist technically covers every installer file in Downloads; the 30-day age filter is applied by the Scanner (Task 5), `RequiresIndividualSelection` keeps it out of any automatic clean, and CleanRunner (Task 6) only ever deletes scan-resolved items.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test src/BriskEngine.Tests`
Expected: PASS — all tests including Tasks 1-3 suites (validator behavior unchanged for wildcard-free templates).

- [ ] **Step 7: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: cleanup target registry and wildcard template resolver"
```

---

### Task 5: SizeCalculator + Scanner

**Files:**
- Create: `src/BriskEngine/Cleaning/SizeCalculator.cs`, `src/BriskEngine/Cleaning/Scanner.cs`, `src/BriskEngine/Cleaning/IProcessLister.cs`, `src/BriskEngine/Models/ScanModels.cs`
- Test: `src/BriskEngine.Tests/SizeCalculatorTests.cs`, `src/BriskEngine.Tests/ScannerTests.cs`

**Interfaces:**
- Consumes: `CleanupTarget`, `ResolvedItem`, `TemplateResolver` (Tasks 2-4).
- Produces:
  - `interface IProcessLister { bool IsRunning(string processName); }` + `sealed class RealProcessLister : IProcessLister` (uses `Process.GetProcessesByName`).
  - `static long SizeCalculator.SizeOf(string path, CancellationToken ct = default)` — file length or tolerant recursive directory sum; never traverses reparse points; unreadable entries are skipped, not thrown.
  - `record TargetScanResult(CleanupTarget Target, IReadOnlyList<ResolvedItem> Items, string? SkippedReason)` with computed `long TotalBytes => Items.Sum(i => i.Bytes)`.
  - `record ScanResult(IReadOnlyList<TargetScanResult> Targets)` with `long TotalBytes`.
  - `class Scanner { Scanner(IReadOnlyList<CleanupTarget> targets, IProcessLister processes); ScanResult Scan(CancellationToken ct = default); }`
  - Scanner special cases BY TARGET ID: `"old-installers"` keeps only files with `LastWriteTimeUtc <= now - 30 days`; `"docker-prune"` and `"empty-recycle-bin"` yield zero items with `SkippedReason = null` (CleanRunner measures/handles them).

- [ ] **Step 1: Write the failing tests**

`src/BriskEngine.Tests/SizeCalculatorTests.cs`:

```csharp
using System;
using System.Diagnostics;
using System.IO;
using BriskEngine.Cleaning;
using Xunit;

namespace BriskEngine.Tests;

public sealed class SizeCalculatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-sz-").FullName;

    [Fact]
    public void SizesNestedFiles()
    {
        File.WriteAllBytes(Path.Combine(_root, "a.bin"), new byte[100]);
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllBytes(Path.Combine(_root, "sub", "b.bin"), new byte[50]);
        Assert.Equal(150, SizeCalculator.SizeOf(_root));
    }

    [Fact]
    public void MissingPath_IsZero()
    {
        Assert.Equal(0, SizeCalculator.SizeOf(Path.Combine(_root, "nope")));
    }

    [Fact]
    public void DoesNotTraverseJunctions()
    {
        var big = Path.Combine(_root, "big");
        Directory.CreateDirectory(big);
        File.WriteAllBytes(Path.Combine(big, "big.bin"), new byte[1000]);
        var scanned = Path.Combine(_root, "scanned");
        Directory.CreateDirectory(scanned);
        File.WriteAllBytes(Path.Combine(scanned, "own.bin"), new byte[10]);
        var p = Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c mklink /J \"{Path.Combine(scanned, "jump")}\" \"{big}\"")
        { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        Assert.Equal(10, SizeCalculator.SizeOf(scanned));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
```

`src/BriskEngine.Tests/ScannerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

file sealed class FakeProcesses : IProcessLister
{
    public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsRunning(string processName) => Running.Contains(processName);
}

public sealed class ScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-scan-").FullName;
    private readonly FakeProcesses _processes = new();

    private CleanupTarget Target(string id, string template, string? app = null,
        bool pick = false) => new(
        id, id, CleanupLevel.Safe, new List<string> { template }, "Test",
        RequiresAppClosedProcess: app, RequiresIndividualSelection: pick);

    [Fact]
    public void ResolvesItemsWithSizes()
    {
        var dir = Path.Combine(_root, "cache");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "x.bin"), new byte[42]);
        var result = new Scanner(new[] { Target("t1", dir) }, _processes).Scan();
        var t = result.Targets.Single();
        Assert.Null(t.SkippedReason);
        Assert.Equal(42, t.TotalBytes);
    }

    [Fact]
    public void RunningApp_SkipsTarget()
    {
        var dir = Path.Combine(_root, "appcache");
        Directory.CreateDirectory(dir);
        _processes.Running.Add("chrome");
        var result = new Scanner(new[] { Target("t2", dir, app: "chrome") }, _processes).Scan();
        var t = result.Targets.Single();
        Assert.NotNull(t.SkippedReason);
        Assert.Empty(t.Items);
    }

    [Fact]
    public void OldInstallers_FiltersByAge()
    {
        var downloads = Path.Combine(_root, "Downloads");
        Directory.CreateDirectory(downloads);
        var old = Path.Combine(downloads, "old-setup.exe");
        var fresh = Path.Combine(downloads, "fresh-setup.exe");
        File.WriteAllBytes(old, new byte[10]);
        File.WriteAllBytes(fresh, new byte[10]);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-45));
        var target = new CleanupTarget("old-installers", "Old installers", CleanupLevel.Deep,
            new List<string> { Path.Combine(downloads, "*.exe") }, "Downloads",
            RequiresIndividualSelection: true);
        var result = new Scanner(new[] { target }, _processes).Scan();
        var item = result.Targets.Single().Items.Single();
        Assert.EndsWith("old-setup.exe", item.Path);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests --filter "SizeCalculatorTests|ScannerTests"`
Expected: build FAILS — types missing.

- [ ] **Step 3: Implement**

`src/BriskEngine/Cleaning/IProcessLister.cs`:

```csharp
using System.Diagnostics;

namespace BriskEngine.Cleaning;

public interface IProcessLister
{
    bool IsRunning(string processName);
}

public sealed class RealProcessLister : IProcessLister
{
    public bool IsRunning(string processName) =>
        Process.GetProcessesByName(processName).Length > 0;
}
```

`src/BriskEngine/Models/ScanModels.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace BriskEngine.Models;

public sealed record TargetScanResult(
    CleanupTarget Target,
    IReadOnlyList<ResolvedItem> Items,
    string? SkippedReason)
{
    public long TotalBytes => Items.Sum(i => i.Bytes);
}

public sealed record ScanResult(IReadOnlyList<TargetScanResult> Targets)
{
    public long TotalBytes => Targets.Sum(t => t.TotalBytes);
}
```

`src/BriskEngine/Cleaning/SizeCalculator.cs`:

```csharp
using System;
using System.IO;
using System.Threading;

namespace BriskEngine.Cleaning;

public static class SizeCalculator
{
    /// Tolerant recursive size. Skips unreadable entries and never traverses
    /// reparse points (a junction inside a cache must not count — or delete —
    /// what it points to).
    public static long SizeOf(string path, CancellationToken ct = default)
    {
        if (File.Exists(path)) return new FileInfo(path).Length;
        if (!Directory.Exists(path)) return 0;
        return SizeOfDirectory(new DirectoryInfo(path), ct);
    }

    private static long SizeOfDirectory(DirectoryInfo dir, CancellationToken ct)
    {
        long total = 0;
        FileSystemInfo[] entries;
        try { entries = dir.GetFileSystemInfos(); }
        catch (UnauthorizedAccessException) { return 0; }
        catch (IOException) { return 0; }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            total += entry switch
            {
                FileInfo f => f.Length,
                DirectoryInfo d => SizeOfDirectory(d, ct),
                _ => 0
            };
        }
        return total;
    }
}
```

`src/BriskEngine/Cleaning/Scanner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BriskEngine.Models;
using BriskEngine.Paths;

namespace BriskEngine.Cleaning;

public sealed class Scanner
{
    public const int OldInstallerMinAgeDays = 30;

    private readonly IReadOnlyList<CleanupTarget> _targets;
    private readonly IProcessLister _processes;

    public Scanner(IReadOnlyList<CleanupTarget> targets, IProcessLister processes)
    {
        _targets = targets;
        _processes = processes;
    }

    public ScanResult Scan(CancellationToken ct = default)
    {
        var results = new TargetScanResult[_targets.Count];
        Parallel.For(0, _targets.Count,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            i => results[i] = ScanTarget(_targets[i], ct));
        return new ScanResult(results);
    }

    private TargetScanResult ScanTarget(CleanupTarget target, CancellationToken ct)
    {
        if (target.RequiresAppClosedProcess is { } app && _processes.IsRunning(app))
            return new TargetScanResult(target, Array.Empty<ResolvedItem>(),
                $"{app} is running — close it to include this target");

        var items = new List<ResolvedItem>();
        foreach (var template in target.PathTemplates)
        foreach (var path in TemplateResolver.Resolve(template))
        {
            ct.ThrowIfCancellationRequested();
            DateTime? lastWrite = null;
            try { lastWrite = File.GetLastWriteTimeUtc(path); } catch { }

            if (target.Id == "old-installers" &&
                (lastWrite is null ||
                 lastWrite > DateTime.UtcNow.AddDays(-OldInstallerMinAgeDays)))
                continue;

            items.Add(new ResolvedItem(target.Id, path, SizeCalculator.SizeOf(path, ct), lastWrite));
        }
        return new TargetScanResult(target, items, null);
    }
}
```

Note: `Parallel.For` needs `using System.Threading.Tasks;` — add it.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/BriskEngine.Tests`
Expected: PASS (all suites).

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: tolerant size calculator and target scanner"
```

---

### Task 6: ActionLog + Recycler + CleanRunner

**Files:**
- Create: `src/BriskEngine/Logging/ActionLog.cs`, `src/BriskEngine/Cleaning/IRecycler.cs`, `src/BriskEngine/Cleaning/WindowsRecycler.cs`, `src/BriskEngine/Cleaning/IProcessRunner.cs`, `src/BriskEngine/Cleaning/CleanRunner.cs`
- Test: `src/BriskEngine.Tests/ActionLogTests.cs`, `src/BriskEngine.Tests/CleanRunnerTests.cs`

**Interfaces:**
- Consumes: `SafetyValidator.Authorize` (Task 3), `TargetScanResult` (Task 5).
- Produces:
  - `class ActionLog { ActionLog(string filePath); void Append(object entry); }` — one JSON object per line, UTF-8, creates parent dir. Default CLI path: `%LOCALAPPDATA%\brisk\action-log.jsonl`.
  - `interface IRecycler { void Recycle(string path); }` + `sealed class WindowsRecycler : IRecycler` (SHFileOperationW with FOF_ALLOWUNDO).
  - `interface IProcessRunner { (int ExitCode, string StdOut) Run(string exe, string args); }` + `sealed class RealProcessRunner` — reused by diagnostics probes in Task 7.
  - `record CleanEntry(string TargetId, string Path, long Bytes, string Action)` — Action is one of `"recycled" | "refused" | "dry-run" | "error" | "external"`.
  - `record CleanReport(IReadOnlyList<CleanEntry> Entries)` with `long RecycledBytes`.
  - `class CleanRunner { CleanRunner(SafetyValidator validator, IRecycler recycler, ActionLog log, IProcessRunner processRunner, Func<bool> isElevated); CleanReport Clean(TargetScanResult scan, bool dryRun); }`

**CleanRunner contract (the safety-critical piece):**
1. `docker-prune`: only acts when `dryRun == false`; runs `docker system prune -af` via IProcessRunner; entry Action `"external"`. Never touches the filesystem itself.
2. `empty-recycle-bin`: calls `SHEmptyRecycleBinW` (P/Invoke, flags `SHERB_NOCONFIRMATION|SHERB_NOPROGRESSUI|SHERB_NOSOUND` = 0x7); Action `"external"`.
3. `RequiresElevation` target while `isElevated() == false`: every item becomes Action `"refused"` with reason logged; nothing deleted.
4. Every other item MUST pass `validator.Authorize(item.Path, scan.Target)`; denial → Action `"refused"`. Approved + `dryRun` → `"dry-run"`. Approved + real → `recycler.Recycle(path)` → `"recycled"`. Exception on one item → Action `"error"`, run continues.
5. EVERY entry (including refusals and dry-runs) is appended to the ActionLog as `{ts, targetId, path, bytes, action}`.

- [ ] **Step 1: Write the failing tests**

`src/BriskEngine.Tests/ActionLogTests.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using BriskEngine.Logging;
using Xunit;

namespace BriskEngine.Tests;

public sealed class ActionLogTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-log-").FullName;

    [Fact]
    public void AppendsOneJsonObjectPerLine()
    {
        var path = Path.Combine(_root, "sub", "log.jsonl");
        var log = new ActionLog(path);
        log.Append(new { action = "recycled", bytes = 42 });
        log.Append(new { action = "refused", bytes = 0 });
        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Equal("recycled",
            JsonDocument.Parse(lines[0]).RootElement.GetProperty("action").GetString());
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
```

`src/BriskEngine.Tests/CleanRunnerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Cleaning;
using BriskEngine.Logging;
using BriskEngine.Models;
using BriskEngine.Safety;
using Xunit;

namespace BriskEngine.Tests;

file sealed class FakeRecycler : IRecycler
{
    public List<string> Recycled { get; } = new();
    public void Recycle(string path) => Recycled.Add(path);
}

file sealed class FakeRunner : IProcessRunner
{
    public List<string> Commands { get; } = new();
    public (int ExitCode, string StdOut) Run(string exe, string args)
    { Commands.Add($"{exe} {args}"); return (0, ""); }
}

public sealed class CleanRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-clean-").FullName;
    private readonly FakeRecycler _recycler = new();
    private readonly FakeRunner _runner = new();
    private readonly ActionLog _log;
    private readonly string _logPath;

    public CleanRunnerTests()
    {
        _logPath = Path.Combine(_root, "log.jsonl");
        _log = new ActionLog(_logPath);
    }

    private CleanRunner Runner(bool elevated = false) =>
        new(new SafetyValidator(), _recycler, _log, _runner, () => elevated);

    private (CleanupTarget, TargetScanResult) ScanOver(string dir, params string[] files)
    {
        Directory.CreateDirectory(dir);
        var items = new List<ResolvedItem>();
        foreach (var f in files)
        {
            var p = Path.Combine(dir, f);
            File.WriteAllBytes(p, new byte[10]);
            items.Add(new ResolvedItem("t", p, 10, DateTime.UtcNow));
        }
        var target = new CleanupTarget("t", "T", CleanupLevel.Safe,
            new List<string> { dir }, "Test", DeletesContentsNotDirectory: true);
        return (target, new TargetScanResult(target, items, null));
    }

    [Fact]
    public void RecyclesAuthorizedItems_AndLogs()
    {
        var (_, scan) = ScanOver(Path.Combine(_root, "cache"), "a.tmp", "b.tmp");
        var report = Runner().Clean(scan, dryRun: false);
        Assert.Equal(2, _recycler.Recycled.Count);
        Assert.Equal(20, report.RecycledBytes);
        Assert.Equal(2, File.ReadAllLines(_logPath).Length);
    }

    [Fact]
    public void DryRun_TouchesNothing_ButLogs()
    {
        var (_, scan) = ScanOver(Path.Combine(_root, "cache2"), "a.tmp");
        var report = Runner().Clean(scan, dryRun: true);
        Assert.Empty(_recycler.Recycled);
        Assert.Equal("dry-run", report.Entries.Single().Action);
        Assert.Single(File.ReadAllLines(_logPath));
    }

    [Fact]
    public void UnauthorizedItem_IsRefused()
    {
        var (target, _) = ScanOver(Path.Combine(_root, "cache3"), "a.tmp");
        var outside = Path.Combine(_root, "outside.txt");
        File.WriteAllText(outside, "x");
        var scan = new TargetScanResult(target,
            new[] { new ResolvedItem("t", outside, 1, DateTime.UtcNow) }, null);
        var report = Runner().Clean(scan, dryRun: false);
        Assert.Equal("refused", report.Entries.Single().Action);
        Assert.Empty(_recycler.Recycled);
    }

    [Fact]
    public void ElevationRequired_WithoutElevation_RefusesAll()
    {
        var dir = Path.Combine(_root, "admin");
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, "a.tmp");
        File.WriteAllBytes(p, new byte[10]);
        var target = new CleanupTarget("adm", "Adm", CleanupLevel.Deep,
            new List<string> { dir }, "Test", RequiresElevation: true);
        var scan = new TargetScanResult(target,
            new[] { new ResolvedItem("adm", p, 10, DateTime.UtcNow) }, null);
        var report = Runner(elevated: false).Clean(scan, dryRun: false);
        Assert.Equal("refused", report.Entries.Single().Action);
    }

    [Fact]
    public void DockerPrune_RunsExternalCommand()
    {
        var target = new CleanupTarget("docker-prune", "Docker", CleanupLevel.Developer,
            new List<string>(), "Container", RequiresExplicitOptIn: true);
        var scan = new TargetScanResult(target, Array.Empty<ResolvedItem>(), null);
        Runner().Clean(scan, dryRun: false);
        Assert.Contains("docker system prune -af", _runner.Commands);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests --filter "ActionLogTests|CleanRunnerTests"`
Expected: build FAILS — types missing.

- [ ] **Step 3: Implement**

`src/BriskEngine/Logging/ActionLog.cs`:

```csharp
using System.IO;
using System.Text.Json;

namespace BriskEngine.Logging;

public sealed class ActionLog
{
    private readonly string _path;
    private readonly object _gate = new();

    public ActionLog(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void Append(object entry)
    {
        var line = JsonSerializer.Serialize(entry);
        lock (_gate) File.AppendAllText(_path, line + "\n");
    }
}
```

`src/BriskEngine/Cleaning/IRecycler.cs`:

```csharp
namespace BriskEngine.Cleaning;

public interface IRecycler
{
    /// Sends a file or directory to the Recycle Bin (never permanent).
    void Recycle(string path);
}
```

`src/BriskEngine/Cleaning/WindowsRecycler.cs`:

```csharp
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BriskEngine.Cleaning;

public sealed class WindowsRecycler : IRecycler
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW op);

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;      // -> Recycle Bin
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    public void Recycle(string path)
    {
        var op = new SHFILEOPSTRUCTW
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0\0", // double-null-terminated list
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };
        var code = SHFileOperationW(ref op);
        if (code != 0) throw new IOException($"SHFileOperation failed ({code}) for '{path}'");
    }
}
```

`src/BriskEngine/Cleaning/IProcessRunner.cs`:

```csharp
using System.Diagnostics;

namespace BriskEngine.Cleaning;

public interface IProcessRunner
{
    (int ExitCode, string StdOut) Run(string exe, string args);
}

public sealed class RealProcessRunner : IProcessRunner
{
    public (int ExitCode, string StdOut) Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout);
    }
}
```

`src/BriskEngine/Cleaning/CleanRunner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using BriskEngine.Logging;
using BriskEngine.Models;
using BriskEngine.Safety;

namespace BriskEngine.Cleaning;

public sealed record CleanEntry(string TargetId, string Path, long Bytes, string Action);

public sealed record CleanReport(IReadOnlyList<CleanEntry> Entries)
{
    public long RecycledBytes =>
        Entries.Where(e => e.Action == "recycled").Sum(e => e.Bytes);
}

public sealed class CleanRunner
{
    [DllImport("shell32.dll")]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? root, uint flags);
    private const uint SHERB_SILENT = 0x7; // no confirm, no progress UI, no sound

    private readonly SafetyValidator _validator;
    private readonly IRecycler _recycler;
    private readonly ActionLog _log;
    private readonly IProcessRunner _processRunner;
    private readonly Func<bool> _isElevated;

    public CleanRunner(SafetyValidator validator, IRecycler recycler, ActionLog log,
        IProcessRunner processRunner, Func<bool> isElevated)
    {
        _validator = validator;
        _recycler = recycler;
        _log = log;
        _processRunner = processRunner;
        _isElevated = isElevated;
    }

    public CleanReport Clean(TargetScanResult scan, bool dryRun)
    {
        var entries = new List<CleanEntry>();
        void Record(string path, long bytes, string action)
        {
            var entry = new CleanEntry(scan.Target.Id, path, bytes, action);
            entries.Add(entry);
            _log.Append(new { ts = DateTime.UtcNow, targetId = entry.TargetId,
                path = entry.Path, bytes = entry.Bytes, action = entry.Action });
        }

        switch (scan.Target.Id)
        {
            case "docker-prune":
                if (!dryRun)
                {
                    _processRunner.Run("docker", "system prune -af");
                    Record("(docker)", 0, "external");
                }
                return new CleanReport(entries);
            case "empty-recycle-bin":
                if (!dryRun)
                {
                    SHEmptyRecycleBinW(IntPtr.Zero, null, SHERB_SILENT);
                    Record("(recycle bin)", 0, "external");
                }
                return new CleanReport(entries);
        }

        var blockedByElevation = scan.Target.RequiresElevation && !_isElevated();
        foreach (var item in scan.Items)
        {
            if (blockedByElevation) { Record(item.Path, 0, "refused"); continue; }

            var auth = _validator.Authorize(item.Path, scan.Target);
            if (!auth.Allowed) { Record(item.Path, 0, "refused"); continue; }
            if (dryRun) { Record(item.Path, item.Bytes, "dry-run"); continue; }

            try
            {
                _recycler.Recycle(item.Path);
                Record(item.Path, item.Bytes, "recycled");
            }
            catch (Exception)
            {
                Record(item.Path, 0, "error"); // one bad file never stops the run
            }
        }
        return new CleanReport(entries);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/BriskEngine.Tests`
Expected: PASS (all suites).

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: clean runner with recycle bin, action log, external commands"
```

---

### Task 7: Diagnostics infrastructure (probes, rule contract, fix journal, fix runner)

**Files:**
- Create: `src/BriskEngine/Models/DiagnosticFinding.cs`, `src/BriskEngine/Diagnostics/Probes.cs`, `src/BriskEngine/Diagnostics/DiagnosticContext.cs`, `src/BriskEngine/Diagnostics/IDiagnosticRule.cs`, `src/BriskEngine/Diagnostics/FixJournal.cs`, `src/BriskEngine/Diagnostics/FixRunner.cs`, `src/BriskEngine/Diagnostics/RealProbes/RealRegistryProbe.cs`
- Test: `src/BriskEngine.Tests/FixJournalTests.cs`, `src/BriskEngine.Tests/FixRunnerTests.cs`

**Interfaces:**
- Consumes: `Severity`, `RuleCategory` (Task 2), `IProcessRunner` (Task 6).
- Produces (every rule task 8-12 builds on EXACTLY these):

```csharp
// Models/DiagnosticFinding.cs
public sealed record DiagnosticFinding(
    string RuleId,
    string TitleKey,        // stable localization key, e.g. "rule.power-plan.title"
    string Title,           // English
    string Evidence,        // English, concrete: "Active plan: Balanced"
    Severity Severity,
    RuleCategory Category,
    int ImpactStars,        // 1..5 expected impact
    bool CanFix,
    string? FixDescription);

// Diagnostics/Probes.cs — ALL system access for rules goes through these
public interface IPowercfgProbe
{
    (Guid Id, string Name) GetActiveScheme();
    IReadOnlyList<(Guid Id, string Name)> ListSchemes();
    void SetActive(Guid id);
}
public interface IRegistryProbe
{
    string? GetString(string keyPath, string valueName);       // keyPath like @"HKCU\Software\X"
    void SetString(string keyPath, string valueName, string value);
    void DeleteValue(string keyPath, string valueName);
    byte[]? GetBytes(string keyPath, string valueName);
    void SetBytes(string keyPath, string valueName, byte[] value);
    int? GetInt(string keyPath, string valueName);
    void SetInt(string keyPath, string valueName, int value);
    IReadOnlyList<string> GetValueNames(string keyPath);
    IReadOnlyList<string> GetSubKeyNames(string keyPath);
}
public interface IProcessInfoProbe
{
    IReadOnlyList<(string Name, long WorkingSetBytes)> TopByMemory(int count);
    double MemoryLoadPercent();
}
public interface ISensorProbe
{
    double? CpuTempC();   // null = sensors unavailable (no admin / unsupported)
    double? GpuTempC();
    int GpuCount();
}
public interface IDiskInfoProbe
{
    long FreeBytes(string driveRoot);   // driveRoot like @"C:\"
    long TotalBytes(string driveRoot);
}

// Diagnostics/DiagnosticContext.cs
public sealed record DiagnosticContext(
    IPowercfgProbe Powercfg,
    IRegistryProbe Registry,
    IProcessInfoProbe Processes,
    ISensorProbe Sensors,
    IDiskInfoProbe Disk,
    string DataDirectory);   // %LOCALAPPDATA%\brisk — history store, journals

// Diagnostics/IDiagnosticRule.cs
public interface IDiagnosticRule
{
    string Id { get; }
    RuleCategory Category { get; }
    DiagnosticFinding? Detect(DiagnosticContext ctx);   // null = no finding
    string Fix(DiagnosticContext ctx);                  // returns prior-state JSON
    void Undo(DiagnosticContext ctx, string priorStateJson);
}
```

  - `class FixJournal { FixJournal(string path); void RecordFix(string ruleId, string priorStateJson); void RecordUndo(string ruleId); string? LastUndoablePriorState(string ruleId); }` — JSONL; `LastUndoablePriorState` returns the prior state of the most recent fix for that rule that has no later undo entry, else null.
  - `class FixRunner { FixRunner(FixJournal journal, Logging.ActionLog log); FixOutcome Apply(IDiagnosticRule rule, DiagnosticContext ctx); FixOutcome Undo(IDiagnosticRule rule, DiagnosticContext ctx); }` with `record FixOutcome(bool Ok, string Message)`. Advise-category rules are refused by `Apply` (`"rule has no fix"`). Every Apply/Undo appends to the ActionLog.

- [ ] **Step 1: Write the failing tests**

`src/BriskEngine.Tests/FixJournalTests.cs`:

```csharp
using System;
using System.IO;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

public sealed class FixJournalTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-fj-").FullName;
    private FixJournal Journal() => new(Path.Combine(_root, "fix-journal.jsonl"));

    [Fact]
    public void FixThenUndo_ThenNothingUndoable()
    {
        var j = Journal();
        j.RecordFix("power-plan", "{\"guid\":\"abc\"}");
        Assert.Equal("{\"guid\":\"abc\"}", j.LastUndoablePriorState("power-plan"));
        j.RecordUndo("power-plan");
        Assert.Null(j.LastUndoablePriorState("power-plan"));
    }

    [Fact]
    public void SecondFix_IsTheUndoableOne()
    {
        var j = Journal();
        j.RecordFix("r", "one");
        j.RecordFix("r", "two");
        Assert.Equal("two", j.LastUndoablePriorState("r"));
    }

    [Fact]
    public void UnknownRule_HasNothingUndoable()
    {
        Assert.Null(Journal().LastUndoablePriorState("nope"));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
```

`src/BriskEngine.Tests/FixRunnerTests.cs`:

```csharp
using System;
using System.IO;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

file sealed class ToggleRule : IDiagnosticRule
{
    public string State = "bad";
    public string Id => "toggle";
    public RuleCategory Category => RuleCategory.Auto;
    public DiagnosticFinding? Detect(DiagnosticContext ctx) => State == "bad"
        ? new DiagnosticFinding(Id, "rule.toggle.title", "Toggle is bad", $"State: {State}",
            Severity.Warning, Category, 3, true, "Set state to good")
        : null;
    public string Fix(DiagnosticContext ctx) { var prior = State; State = "good"; return prior; }
    public void Undo(DiagnosticContext ctx, string prior) => State = prior;
}

file sealed class AdviseRule : IDiagnosticRule
{
    public string Id => "advise-only";
    public RuleCategory Category => RuleCategory.Advise;
    public DiagnosticFinding? Detect(DiagnosticContext ctx) => null;
    public string Fix(DiagnosticContext ctx) => throw new InvalidOperationException();
    public void Undo(DiagnosticContext ctx, string prior) => throw new InvalidOperationException();
}

public sealed class FixRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-fr-").FullName;
    private readonly DiagnosticContext _ctx = TestContext.Empty();
    private FixRunner Runner() => new(
        new FixJournal(Path.Combine(_root, "j.jsonl")),
        new ActionLog(Path.Combine(_root, "log.jsonl")));

    [Fact]
    public void ApplyThenUndo_RestoresState()
    {
        var rule = new ToggleRule();
        var runner = Runner();
        Assert.True(runner.Apply(rule, _ctx).Ok);
        Assert.Equal("good", rule.State);
        Assert.True(runner.Undo(rule, _ctx).Ok);
        Assert.Equal("bad", rule.State);
    }

    [Fact]
    public void UndoWithoutFix_Fails()
    {
        Assert.False(Runner().Undo(new ToggleRule(), _ctx).Ok);
    }

    [Fact]
    public void AdviseRule_IsNeverApplied()
    {
        Assert.False(Runner().Apply(new AdviseRule(), _ctx).Ok);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
```

Also create the shared test helper `src/BriskEngine.Tests/TestContext.cs` (used by every rule test from here on):

```csharp
using System;
using System.Collections.Generic;
using BriskEngine.Diagnostics;

namespace BriskEngine.Tests;

public sealed class FakePowercfg : IPowercfgProbe
{
    public (Guid Id, string Name) Active;
    public List<(Guid Id, string Name)> Schemes = new();
    public List<Guid> SetCalls = new();
    public (Guid Id, string Name) GetActiveScheme() => Active;
    public IReadOnlyList<(Guid Id, string Name)> ListSchemes() => Schemes;
    public void SetActive(Guid id)
    {
        SetCalls.Add(id);
        Active = Schemes.Find(s => s.Id == id);
    }
}

public sealed class FakeRegistry : IRegistryProbe
{
    // key = $"{keyPath}::{valueName}"
    public Dictionary<string, object> Values = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> SubKeys = new(StringComparer.OrdinalIgnoreCase);
    private static string K(string k, string v) => $"{k}::{v}";
    public string? GetString(string k, string v) => Values.TryGetValue(K(k, v), out var o) ? o as string : null;
    public void SetString(string k, string v, string value) => Values[K(k, v)] = value;
    public void DeleteValue(string k, string v) => Values.Remove(K(k, v));
    public byte[]? GetBytes(string k, string v) => Values.TryGetValue(K(k, v), out var o) ? o as byte[] : null;
    public void SetBytes(string k, string v, byte[] value) => Values[K(k, v)] = value;
    public int? GetInt(string k, string v) => Values.TryGetValue(K(k, v), out var o) ? o as int? : null;
    public void SetInt(string k, string v, int value) => Values[K(k, v)] = value;
    public IReadOnlyList<string> GetValueNames(string keyPath)
    {
        var names = new List<string>();
        foreach (var key in Values.Keys)
            if (key.StartsWith(keyPath + "::", StringComparison.OrdinalIgnoreCase))
                names.Add(key[(keyPath.Length + 2)..]);
        return names;
    }
    public IReadOnlyList<string> GetSubKeyNames(string keyPath) =>
        SubKeys.TryGetValue(keyPath, out var s) ? s : new List<string>();
}

public sealed class FakeProcessInfo : IProcessInfoProbe
{
    public List<(string Name, long WorkingSetBytes)> Top = new();
    public double MemoryLoad = 40;
    // Tuple element names MUST match the interface exactly (CS8141).
    public IReadOnlyList<(string Name, long WorkingSetBytes)> TopByMemory(int count) =>
        Top.GetRange(0, Math.Min(count, Top.Count));
    public double MemoryLoadPercent() => MemoryLoad;
}

public sealed class FakeSensors : ISensorProbe
{
    public double? CpuTemp; public double? GpuTemp; public int Gpus = 1;
    public double? CpuTempC() => CpuTemp;
    public double? GpuTempC() => GpuTemp;
    public int GpuCount() => Gpus;
}

public sealed class FakeDisk : IDiskInfoProbe
{
    public long Free = 500L << 30; public long Total = 1000L << 30;
    public long FreeBytes(string driveRoot) => Free;
    public long TotalBytes(string driveRoot) => Total;
}

public static class TestContext
{
    public static DiagnosticContext Empty(string? dataDir = null) => new(
        new FakePowercfg(), new FakeRegistry(), new FakeProcessInfo(),
        new FakeSensors(), new FakeDisk(),
        dataDir ?? System.IO.Directory.CreateTempSubdirectory("brisk-ctx-").FullName);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests --filter "FixJournalTests|FixRunnerTests"`
Expected: build FAILS — types missing.

- [ ] **Step 3: Implement**

Create the interface/record files exactly as shown in the Interfaces block above (`Models/DiagnosticFinding.cs`, `Diagnostics/Probes.cs` — all five interfaces in one file, namespace `BriskEngine.Diagnostics`, with `using BriskEngine.Models;` where needed — `Diagnostics/DiagnosticContext.cs`, `Diagnostics/IDiagnosticRule.cs`).

`src/BriskEngine/Diagnostics/FixJournal.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BriskEngine.Diagnostics;

public sealed class FixJournal
{
    private sealed record Entry(string RuleId, string Action, string? PriorState, System.DateTime Ts);

    private readonly string _path;
    private readonly object _gate = new();

    public FixJournal(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void RecordFix(string ruleId, string priorStateJson) =>
        Append(new Entry(ruleId, "fix", priorStateJson, System.DateTime.UtcNow));

    public void RecordUndo(string ruleId) =>
        Append(new Entry(ruleId, "undo", null, System.DateTime.UtcNow));

    public string? LastUndoablePriorState(string ruleId)
    {
        string? candidate = null;
        foreach (var entry in ReadAll())
        {
            if (entry.RuleId != ruleId) continue;
            candidate = entry.Action == "fix" ? entry.PriorState : null;
        }
        return candidate;
    }

    private void Append(Entry entry)
    {
        lock (_gate) File.AppendAllText(_path, JsonSerializer.Serialize(entry) + "\n");
    }

    private IEnumerable<Entry> ReadAll()
    {
        if (!File.Exists(_path)) yield break;
        foreach (var line in File.ReadAllLines(_path))
            if (JsonSerializer.Deserialize<Entry>(line) is { } e) yield return e;
    }
}
```

`src/BriskEngine/Diagnostics/FixRunner.cs`:

```csharp
using System;
using BriskEngine.Logging;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics;

public sealed record FixOutcome(bool Ok, string Message);

public sealed class FixRunner
{
    private readonly FixJournal _journal;
    private readonly ActionLog _log;

    public FixRunner(FixJournal journal, ActionLog log)
    {
        _journal = journal;
        _log = log;
    }

    public FixOutcome Apply(IDiagnosticRule rule, DiagnosticContext ctx)
    {
        if (rule.Category == RuleCategory.Advise)
            return new FixOutcome(false, $"{rule.Id}: rule has no fix (advise-only)");
        try
        {
            var prior = rule.Fix(ctx);
            _journal.RecordFix(rule.Id, prior);
            _log.Append(new { ts = DateTime.UtcNow, ruleId = rule.Id, action = "fix" });
            return new FixOutcome(true, $"{rule.Id}: fixed");
        }
        catch (Exception ex)
        {
            return new FixOutcome(false, $"{rule.Id}: fix failed — {ex.Message}");
        }
    }

    public FixOutcome Undo(IDiagnosticRule rule, DiagnosticContext ctx)
    {
        var prior = _journal.LastUndoablePriorState(rule.Id);
        if (prior is null)
            return new FixOutcome(false, $"{rule.Id}: nothing to undo");
        try
        {
            rule.Undo(ctx, prior);
            _journal.RecordUndo(rule.Id);
            _log.Append(new { ts = DateTime.UtcNow, ruleId = rule.Id, action = "undo" });
            return new FixOutcome(true, $"{rule.Id}: undone");
        }
        catch (Exception ex)
        {
            return new FixOutcome(false, $"{rule.Id}: undo failed — {ex.Message}");
        }
    }
}
```

`src/BriskEngine/Diagnostics/RealProbes/RealRegistryProbe.cs` — first add the package:

```powershell
dotnet add src/BriskEngine package Microsoft.Win32.Registry
```

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace BriskEngine.Diagnostics.RealProbes;

public sealed class RealRegistryProbe : IRegistryProbe
{
    private static (RegistryKey Root, string SubPath) Split(string keyPath)
    {
        var sep = keyPath.IndexOf('\\');
        var root = keyPath[..sep] switch
        {
            "HKCU" => Registry.CurrentUser,
            "HKLM" => Registry.LocalMachine,
            _ => throw new ArgumentException($"Unsupported hive in '{keyPath}'"),
        };
        return (root, keyPath[(sep + 1)..]);
    }

    private static T? Read<T>(string keyPath, string valueName) where T : class
    {
        var (root, sub) = Split(keyPath);
        using var key = root.OpenSubKey(sub);
        return key?.GetValue(valueName) as T;
    }

    private static void Write(string keyPath, string valueName, object value, RegistryValueKind kind)
    {
        var (root, sub) = Split(keyPath);
        using var key = root.CreateSubKey(sub, writable: true);
        key.SetValue(valueName, value, kind);
    }

    public string? GetString(string k, string v) => Read<string>(k, v);
    public void SetString(string k, string v, string value) => Write(k, v, value, RegistryValueKind.String);
    public byte[]? GetBytes(string k, string v) => Read<byte[]>(k, v);
    public void SetBytes(string k, string v, byte[] value) => Write(k, v, value, RegistryValueKind.Binary);
    public int? GetInt(string k, string v)
    {
        var (root, sub) = Split(k);
        using var key = root.OpenSubKey(sub);
        return key?.GetValue(v) as int?;
    }
    public void SetInt(string k, string v, int value) => Write(k, v, value, RegistryValueKind.DWord);

    public void DeleteValue(string keyPath, string valueName)
    {
        var (root, sub) = Split(keyPath);
        using var key = root.OpenSubKey(sub, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    public IReadOnlyList<string> GetValueNames(string keyPath)
    {
        var (root, sub) = Split(keyPath);
        using var key = root.OpenSubKey(sub);
        return key?.GetValueNames() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> GetSubKeyNames(string keyPath)
    {
        var (root, sub) = Split(keyPath);
        using var key = root.OpenSubKey(sub);
        return key?.GetSubKeyNames() ?? Array.Empty<string>();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/BriskEngine.Tests`
Expected: PASS (all suites).

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: diagnostics infrastructure (probes, fix journal, fix runner)"
```

---

### Task 8: power-plan rule (the fully-worked exemplar every other rule copies)

**Files:**
- Create: `src/BriskEngine/Diagnostics/Rules/PowerPlanRule.cs`, `src/BriskEngine/Diagnostics/RealProbes/RealPowercfgProbe.cs`
- Test: `src/BriskEngine.Tests/Rules/PowerPlanRuleTests.cs`, `src/BriskEngine.Tests/RealPowercfgParsingTests.cs`

**Interfaces:**
- Consumes: `IDiagnosticRule`, `DiagnosticContext`, `IPowercfgProbe`, `FakePowercfg`/`TestContext` (Task 7), `IProcessRunner` (Task 6).
- Produces: `sealed class PowerPlanRule : IDiagnosticRule` (`Id = "power-plan"`, `Category = Auto`); `sealed class RealPowercfgProbe : IPowercfgProbe` with internal static parser `ParseSchemes(string powercfgOutput)`; static GUIDs `PowerPlanRule.Balanced/PowerSaver/HighPerformance/Ultimate`.

- [ ] **Step 1: Write the failing tests**

`src/BriskEngine.Tests/Rules/PowerPlanRuleTests.cs`:

```csharp
using System;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class PowerPlanRuleTests
{
    private static (DiagnosticContext ctx, FakePowercfg power) Context(
        Guid active, string name, params (Guid, string)[] extra)
    {
        var power = new FakePowercfg { Active = (active, name) };
        power.Schemes.Add((active, name));
        foreach (var s in extra) power.Schemes.Add(s);
        var baseCtx = TestContext.Empty();
        return (baseCtx with { Powercfg = power }, power);
    }

    [Fact]
    public void BalancedPlan_IsAFinding()
    {
        var (ctx, _) = Context(PowerPlanRule.Balanced, "Balanced",
            (PowerPlanRule.HighPerformance, "High performance"));
        var finding = new PowerPlanRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Equal(RuleCategory.Auto, finding!.Category);
        Assert.Contains("Balanced", finding.Evidence);
        Assert.True(finding.CanFix);
    }

    [Fact]
    public void HighPerformancePlan_NoFinding()
    {
        var (ctx, _) = Context(PowerPlanRule.HighPerformance, "High performance");
        Assert.Null(new PowerPlanRule().Detect(ctx));
    }

    [Fact]
    public void Fix_PrefersUltimate_WhenAvailable()
    {
        var (ctx, power) = Context(PowerPlanRule.Balanced, "Balanced",
            (PowerPlanRule.HighPerformance, "High performance"),
            (PowerPlanRule.Ultimate, "Ultimate Performance"));
        new PowerPlanRule().Fix(ctx);
        Assert.Equal(PowerPlanRule.Ultimate, power.Active.Id);
    }

    [Fact]
    public void FixThenUndo_RestoresBalanced()
    {
        var (ctx, power) = Context(PowerPlanRule.Balanced, "Balanced",
            (PowerPlanRule.HighPerformance, "High performance"));
        var rule = new PowerPlanRule();
        var prior = rule.Fix(ctx);
        Assert.Equal(PowerPlanRule.HighPerformance, power.Active.Id);
        rule.Undo(ctx, prior);
        Assert.Equal(PowerPlanRule.Balanced, power.Active.Id);
    }
}
```

`src/BriskEngine.Tests/RealPowercfgParsingTests.cs` — the parser must be locale-proof (Turkish Windows says "Güç Düzeni GUID'i:"), so it keys on the GUID pattern and parentheses, never on English labels:

```csharp
using System;
using System.Linq;
using BriskEngine.Diagnostics.RealProbes;
using Xunit;

namespace BriskEngine.Tests;

public class RealPowercfgParsingTests
{
    private const string EnglishList = """
        Existing Power Schemes (* Active)
        -----------------------------------
        Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *
        Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)
        """;

    private const string TurkishList = """
        Var Olan Güç Düzenleri (* Etkin)
        -----------------------------------
        Güç Düzeni GUID'i: 381b4222-f694-41f0-9685-ff5bb260df2e  (Dengeli) *
        Güç Düzeni GUID'i: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (Yüksek performans)
        """;

    [Theory]
    [InlineData(EnglishList, "Balanced")]
    [InlineData(TurkishList, "Dengeli")]
    public void ParsesSchemes_AndActiveMarker(string output, string activeName)
    {
        var schemes = RealPowercfgProbe.ParseSchemes(output);
        Assert.Equal(2, schemes.Count);
        var active = schemes.Single(s => s.IsActive);
        Assert.Equal(Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"), active.Id);
        Assert.Equal(activeName, active.Name);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests --filter "PowerPlanRuleTests|RealPowercfgParsingTests"`
Expected: build FAILS — types missing.

- [ ] **Step 3: Implement**

`src/BriskEngine/Diagnostics/Rules/PowerPlanRule.cs`:

```csharp
using System;
using System.Linq;
using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

public sealed class PowerPlanRule : IDiagnosticRule
{
    public static readonly Guid Balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid PowerSaver = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid HighPerformance = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid Ultimate = Guid.Parse("e9a42b02-d5df-448d-aa66-1f0e7d5efb5a");

    private sealed record Prior(Guid PreviousScheme);

    public string Id => "power-plan";
    public RuleCategory Category => RuleCategory.Auto;

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var (id, name) = ctx.Powercfg.GetActiveScheme();
        if (id != Balanced && id != PowerSaver) return null;
        return new DiagnosticFinding(
            Id, "rule.power-plan.title",
            "Power plan is throttling your CPU",
            $"Active plan: {name}. This plan deliberately limits CPU boost clocks; " +
            "a performance plan lets the CPU reach its full turbo frequency.",
            Severity.Critical, Category, ImpactStars: 5, CanFix: true,
            FixDescription: "Switch to the High performance power plan (undoable)");
    }

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Prior(ctx.Powercfg.GetActiveScheme().Id);
        var schemes = ctx.Powercfg.ListSchemes();
        var best = schemes.Any(s => s.Id == Ultimate) ? Ultimate : HighPerformance;
        ctx.Powercfg.SetActive(best);
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Prior>(priorStateJson)!;
        ctx.Powercfg.SetActive(prior.PreviousScheme);
    }
}
```

`src/BriskEngine/Diagnostics/RealProbes/RealPowercfgProbe.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BriskEngine.Cleaning;

namespace BriskEngine.Diagnostics.RealProbes;

public sealed class RealPowercfgProbe : IPowercfgProbe
{
    public sealed record Scheme(Guid Id, string Name, bool IsActive);

    private readonly IProcessRunner _runner;
    public RealPowercfgProbe(IProcessRunner runner) => _runner = runner;

    // Locale-proof: matches the GUID and the parenthesised name, never English labels.
    private static readonly Regex SchemeLine = new(
        @"(?<guid>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s+\((?<name>[^)]+)\)\s*(?<active>\*)?",
        RegexOptions.Compiled);

    public static IReadOnlyList<Scheme> ParseSchemes(string powercfgOutput) =>
        SchemeLine.Matches(powercfgOutput)
            .Select(m => new Scheme(
                Guid.Parse(m.Groups["guid"].Value),
                m.Groups["name"].Value.Trim(),
                m.Groups["active"].Success))
            .ToList();

    public (Guid Id, string Name) GetActiveScheme()
    {
        var (_, stdout) = _runner.Run("powercfg", "/getactivescheme");
        var scheme = ParseSchemes(stdout).FirstOrDefault()
            ?? throw new InvalidOperationException("Could not parse powercfg output");
        return (scheme.Id, scheme.Name);
    }

    public IReadOnlyList<(Guid Id, string Name)> ListSchemes()
    {
        var (_, stdout) = _runner.Run("powercfg", "/list");
        return ParseSchemes(stdout).Select(s => (s.Id, s.Name)).ToList();
    }

    public void SetActive(Guid id)
    {
        var (code, _) = _runner.Run("powercfg", $"/setactive {id}");
        if (code != 0) throw new InvalidOperationException($"powercfg /setactive failed ({code})");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/BriskEngine.Tests`
Expected: PASS (all suites).

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: power-plan diagnostic rule with locale-proof powercfg parsing"
```

---

### Task 9: Context extension + browser-gpu + hw-acceleration rules

**Files:**
- Create: `src/BriskEngine/Diagnostics/IFileProbe.cs`, `src/BriskEngine/Diagnostics/RealProbes/RealFileProbe.cs`, `src/BriskEngine/Diagnostics/Rules/BrowserGpuRule.cs`, `src/BriskEngine/Diagnostics/Rules/HardwareAccelerationRule.cs`
- Modify: `src/BriskEngine/Diagnostics/DiagnosticContext.cs` (add two members), `src/BriskEngine.Tests/TestContext.cs` (fakes for them)
- Test: `src/BriskEngine.Tests/Rules/BrowserGpuRuleTests.cs`, `src/BriskEngine.Tests/Rules/HardwareAccelerationRuleTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 7-8; `IProcessLister` (Task 5).
- Produces:

```csharp
// Diagnostics/IFileProbe.cs — file access for rules (never for deletion; that is the cleaner's job)
public interface IFileProbe
{
    bool FileExists(string path);
    string? ReadAllText(string path);                  // null when missing/unreadable
    void WriteAllText(string path, string content);
    IReadOnlyList<string> ListFiles(string directory); // empty when missing
    long DirectorySizeBytes(string path);              // delegates to SizeCalculator
    DateTime? NewestWriteUtc(string path, int limit = 1500); // bounded deep walk
}
```

- `DiagnosticContext` gains two members (append to the record, keep order): `IFileProbe Files, Cleaning.IProcessLister Processes2` — NAME IT `RunningApps` instead of Processes2: full new record signature:

```csharp
public sealed record DiagnosticContext(
    IPowercfgProbe Powercfg,
    IRegistryProbe Registry,
    IProcessInfoProbe Processes,
    ISensorProbe Sensors,
    IDiskInfoProbe Disk,
    IFileProbe Files,
    Cleaning.IProcessLister RunningApps,
    string DataDirectory);
```

Update `TestContext.Empty()` accordingly with `FakeFiles` (in-memory dict path→content, dict dir→file list, dict path→size, dict path→newest-write) and the existing `FakeProcesses` pattern from ScannerTests (move that class into `TestContext.cs` as `public sealed class FakeRunningApps : Cleaning.IProcessLister` and reuse it in ScannerTests). `RealFileProbe` implements the interface with `File`/`Directory` + `SizeCalculator.SizeOf` + an enumerator capped at `limit` entries for `NewestWriteUtc` that skips reparse points.

- `sealed class BrowserGpuRule : IDiagnosticRule` — `Id = "browser-gpu"`, `Category = Auto`.
- `sealed class HardwareAccelerationRule : IDiagnosticRule` — `Id = "hw-acceleration"`, `Category = Confirm`.

**Rule contracts:**

*browser-gpu* — only on hybrid machines (`ctx.Sensors.GpuCount() >= 2`). Installed browsers found via App Paths default values: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\<exe>` (value name `""`) for `chrome.exe, msedge.exe, firefox.exe, brave.exe, opera.exe`. For each installed browser, the value named `<full exe path>` under `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` must contain `GpuPreference=2`; browsers missing that produce the finding. Fix: `SetString(prefsKey, exePath, "GpuPreference=2;")` per offender; prior state = JSON map exePath → previous value or null. Undo: restore previous / `DeleteValue` where null.

*hw-acceleration* — reads Chrome/Edge `Local State` JSON (`%LOCALAPPDATA%\Google\Chrome\User Data\Local State`, `%LOCALAPPDATA%\Microsoft\Edge\User Data\Local State`) via `ctx.Files`; a browser with `"hardware_acceleration_mode":{"enabled":false}` produces the finding. Fix: refuses (`throw InvalidOperationException("close <browser> first")`) while the browser process runs (`ctx.RunningApps.IsRunning("chrome"/"msedge")`); otherwise rewrites the JSON with `enabled` removed→true using `System.Text.Json.Nodes.JsonNode`; prior = JSON map file → original `enabled` bool. Undo: write the original value back.

- [ ] **Step 1: Write the failing tests**

`src/BriskEngine.Tests/Rules/BrowserGpuRuleTests.cs`:

```csharp
using BriskEngine.Diagnostics.Rules;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class BrowserGpuRuleTests
{
    private const string AppPaths = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private const string Prefs = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    private const string ChromeExe = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

    private static (BriskEngine.Diagnostics.DiagnosticContext, FakeRegistry, FakeSensors) Ctx()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        var sensors = (FakeSensors)ctx.Sensors;
        sensors.Gpus = 2;
        reg.SetString($@"{AppPaths}\chrome.exe", "", ChromeExe);
        return (ctx, reg, sensors);
    }

    [Fact]
    public void HybridGpu_BrowserWithoutPreference_IsAFinding()
    {
        var (ctx, _, _) = Ctx();
        var finding = new BrowserGpuRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Contains("chrome", finding!.Evidence, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleGpu_NoFinding()
    {
        var (ctx, _, sensors) = Ctx();
        sensors.Gpus = 1;
        Assert.Null(new BrowserGpuRule().Detect(ctx));
    }

    [Fact]
    public void PreferenceAlreadySet_NoFinding()
    {
        var (ctx, reg, _) = Ctx();
        reg.SetString(Prefs, ChromeExe, "GpuPreference=2;");
        Assert.Null(new BrowserGpuRule().Detect(ctx));
    }

    [Fact]
    public void FixThenUndo_RoundTrips()
    {
        var (ctx, reg, _) = Ctx();
        var rule = new BrowserGpuRule();
        var prior = rule.Fix(ctx);
        Assert.Equal("GpuPreference=2;", reg.GetString(Prefs, ChromeExe));
        rule.Undo(ctx, prior);
        Assert.Null(reg.GetString(Prefs, ChromeExe)); // was absent before the fix
    }
}
```

`src/BriskEngine.Tests/Rules/HardwareAccelerationRuleTests.cs`:

```csharp
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Paths;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class HardwareAccelerationRuleTests
{
    private static readonly string ChromeLocalState =
        PathExpander.Expand(@"%LOCALAPPDATA%\Google\Chrome\User Data\Local State")!;

    private static (BriskEngine.Diagnostics.DiagnosticContext, FakeFiles, FakeRunningApps) Ctx(string json)
    {
        var ctx = TestContext.Empty();
        var files = (FakeFiles)ctx.Files;
        var apps = (FakeRunningApps)ctx.RunningApps;
        files.Texts[ChromeLocalState] = json;
        return (ctx, files, apps);
    }

    [Fact]
    public void DisabledAcceleration_IsAFinding()
    {
        var (ctx, _, _) = Ctx("""{"hardware_acceleration_mode":{"enabled":false}}""");
        Assert.NotNull(new HardwareAccelerationRule().Detect(ctx));
    }

    [Fact]
    public void EnabledOrAbsent_NoFinding()
    {
        var (ctx, _, _) = Ctx("""{"browser":{}}""");
        Assert.Null(new HardwareAccelerationRule().Detect(ctx));
    }

    [Fact]
    public void Fix_WhileBrowserRunning_Throws()
    {
        var (ctx, _, apps) = Ctx("""{"hardware_acceleration_mode":{"enabled":false}}""");
        apps.Running.Add("chrome");
        Assert.Throws<System.InvalidOperationException>(() => new HardwareAccelerationRule().Fix(ctx));
    }

    [Fact]
    public void FixThenUndo_RoundTrips()
    {
        var (ctx, files, _) = Ctx("""{"hardware_acceleration_mode":{"enabled":false}}""");
        var rule = new HardwareAccelerationRule();
        var prior = rule.Fix(ctx);
        Assert.Contains("\"enabled\":true", files.Texts[ChromeLocalState].Replace(" ", ""));
        rule.Undo(ctx, prior);
        Assert.Contains("\"enabled\":false", files.Texts[ChromeLocalState].Replace(" ", ""));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/BriskEngine.Tests --filter "BrowserGpuRuleTests|HardwareAccelerationRuleTests"`
Expected: build FAILS.

- [ ] **Step 3: Implement**

1. `IFileProbe` + `RealFileProbe` exactly per the Interfaces block (`RealFileProbe.NewestWriteUtc`: `new DirectoryInfo(path).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)` wrapped in try/catch, stop after `limit` entries, skip `ReparsePoint` attributes, return max `LastWriteTimeUtc`).
2. Extend `DiagnosticContext` + `TestContext` (add `FakeFiles` with public dictionaries `Texts`, `FileLists`, `Sizes`, `NewestWrites`; move `FakeProcesses` from ScannerTests into TestContext.cs renamed `FakeRunningApps`; fix ScannerTests to use it).
3. `BrowserGpuRule`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

public sealed class BrowserGpuRule : IDiagnosticRule
{
    private const string AppPaths = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private const string Prefs = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    private static readonly string[] BrowserExes =
        { "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe" };

    public string Id => "browser-gpu";
    public RuleCategory Category => RuleCategory.Auto;

    private static List<string> Offenders(DiagnosticContext ctx)
    {
        var offenders = new List<string>();
        if (ctx.Sensors.GpuCount() < 2) return offenders;
        foreach (var exe in BrowserExes)
        {
            var path = ctx.Registry.GetString($@"{AppPaths}\{exe}", "");
            if (path is null) continue;
            var pref = ctx.Registry.GetString(Prefs, path);
            if (pref is null || !pref.Contains("GpuPreference=2")) offenders.Add(path);
        }
        return offenders;
    }

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var offenders = Offenders(ctx);
        if (offenders.Count == 0) return null;
        var names = string.Join(", ", offenders.Select(System.IO.Path.GetFileName));
        return new DiagnosticFinding(Id, "rule.browser-gpu.title",
            "Browser is not pinned to the fast GPU",
            $"This machine has two GPUs, but {names} has no high-performance GPU " +
            "preference, so Windows may run it on the slow integrated GPU.",
            Severity.Warning, Category, ImpactStars: 4, CanFix: true,
            FixDescription: "Set the high-performance GPU preference for each browser (undoable)");
    }

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Dictionary<string, string?>();
        foreach (var path in Offenders(ctx))
        {
            prior[path] = ctx.Registry.GetString(Prefs, path);
            ctx.Registry.SetString(Prefs, path, "GpuPreference=2;");
        }
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Dictionary<string, string?>>(priorStateJson)!;
        foreach (var (path, value) in prior)
        {
            if (value is null) ctx.Registry.DeleteValue(Prefs, path);
            else ctx.Registry.SetString(Prefs, path, value);
        }
    }
}
```

4. `HardwareAccelerationRule`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using BriskEngine.Models;
using BriskEngine.Paths;

namespace BriskEngine.Diagnostics.Rules;

public sealed class HardwareAccelerationRule : IDiagnosticRule
{
    private static readonly (string Process, string LocalStateTemplate)[] Browsers =
    {
        ("chrome", @"%LOCALAPPDATA%\Google\Chrome\User Data\Local State"),
        ("msedge", @"%LOCALAPPDATA%\Microsoft\Edge\User Data\Local State"),
    };

    public string Id => "hw-acceleration";
    public RuleCategory Category => RuleCategory.Confirm;

    private static List<(string Process, string Path)> Offenders(DiagnosticContext ctx)
    {
        var offenders = new List<(string, string)>();
        foreach (var (process, template) in Browsers)
        {
            var path = PathExpander.Expand(template);
            if (path is null) continue;
            var text = ctx.Files.ReadAllText(path);
            if (text is null) continue;
            try
            {
                var enabled = JsonNode.Parse(text)?["hardware_acceleration_mode"]?["enabled"];
                if (enabled is not null && enabled.GetValue<bool>() == false)
                    offenders.Add((process, path));
            }
            catch (Exception) { /* unreadable Local State — not our problem */ }
        }
        return offenders;
    }

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var offenders = Offenders(ctx);
        if (offenders.Count == 0) return null;
        return new DiagnosticFinding(Id, "rule.hw-acceleration.title",
            "Browser hardware acceleration is turned off",
            $"Hardware acceleration is disabled in: {string.Join(", ", offenders.Select(o => o.Process))}. " +
            "Video decoding falls back to the CPU, which stutters on YouTube.",
            Severity.Warning, Category, ImpactStars: 4, CanFix: true,
            FixDescription: "Re-enable hardware acceleration (browser must be closed)");
    }

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Dictionary<string, bool>();
        foreach (var (process, path) in Offenders(ctx))
        {
            if (ctx.RunningApps.IsRunning(process))
                throw new InvalidOperationException($"Close {process} first, then retry the fix.");
            var node = JsonNode.Parse(ctx.Files.ReadAllText(path)!)!;
            prior[path] = false;
            node["hardware_acceleration_mode"]!["enabled"] = true;
            ctx.Files.WriteAllText(path, node.ToJsonString());
        }
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Dictionary<string, bool>>(priorStateJson)!;
        foreach (var (path, original) in prior)
        {
            var text = ctx.Files.ReadAllText(path);
            if (text is null) continue;
            var node = JsonNode.Parse(text)!;
            node["hardware_acceleration_mode"] ??= new JsonObject();
            node["hardware_acceleration_mode"]!["enabled"] = original;
            ctx.Files.WriteAllText(path, node.ToJsonString());
        }
    }
}
```

- [ ] **Step 4: Run ALL tests to verify they pass** (`dotnet test src/BriskEngine.Tests`) — ScannerTests must still pass after the FakeRunningApps move.

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: browser GPU preference and hardware acceleration rules"
```

---

### Task 10: startup-bloat rule

**Files:**
- Create: `src/BriskEngine/Diagnostics/Rules/StartupBloatRule.cs`
- Test: `src/BriskEngine.Tests/Rules/StartupBloatRuleTests.cs`

**Interfaces:**
- Consumes: Tasks 7-9 (`IRegistryProbe`, `IFileProbe`, `TestContext`).
- Produces: `sealed class StartupBloatRule : IDiagnosticRule` — `Id = "startup-bloat"`, `Category = Confirm`; `static readonly IReadOnlySet<string> KnownHeavy`.

**Rule contract:** Startup items = value names under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` and `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`, plus `.lnk` files in the user Startup folder (`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`, via `ctx.Files.ListFiles`). An HKCU/HKLM item is DISABLED when `StartupApproved\Run` (same hive) has bytes for it whose first byte is odd (0x03 = disabled; 0x02 or absent = enabled). Finding when enabled-count ≥ 6 OR any enabled item matches `KnownHeavy` = { "Steam", "Discord", "Spotify", "Docker Desktop", "EpicGamesLauncher", "WhatsApp", "Teams", "BlueStacks", "WallpaperEngine" } (case-insensitive substring match on the item name). Evidence lists enabled count + matched heavy names. Fix disables ONLY the enabled `KnownHeavy` registry items (Startup-folder links and unknown items are listed, never touched): write 12 bytes `{0x03,0,0,0,0,0,0,0,0,0,0,0}` to the item's name under `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run` (HKCU items) or the HKLM twin (HKLM items — note: writing HKLM needs elevation; catch `UnauthorizedAccessException` per item, keep going, report what was skipped in the outcome message via a `skipped` list inside prior state). Prior state JSON: map `hive|name` → previous bytes as base64 or `null` when absent. Undo restores previous bytes / deletes the value when it was absent.

- [ ] **Step 1: Write the failing tests**

`src/BriskEngine.Tests/Rules/StartupBloatRuleTests.cs`:

```csharp
using System;
using BriskEngine.Diagnostics.Rules;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class StartupBloatRuleTests
{
    private const string RunKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private static (BriskEngine.Diagnostics.DiagnosticContext, FakeRegistry) Ctx(params string[] runItems)
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        foreach (var item in runItems) reg.SetString(RunKey, item, $@"C:\apps\{item}.exe");
        return (ctx, reg);
    }

    [Fact]
    public void HeavyStartupItem_IsAFinding()
    {
        var (ctx, _) = Ctx("Steam");
        var finding = new StartupBloatRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Contains("Steam", finding!.Evidence);
    }

    [Fact]
    public void DisabledHeavyItem_NoFinding()
    {
        var (ctx, reg) = Ctx("Steam");
        reg.SetBytes(ApprovedKey, "Steam", new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        Assert.Null(new StartupBloatRule().Detect(ctx));
    }

    [Fact]
    public void FewLightItems_NoFinding()
    {
        var (ctx, _) = Ctx("MyTool", "OtherTool");
        Assert.Null(new StartupBloatRule().Detect(ctx));
    }

    [Fact]
    public void ManyItems_IsAFinding_EvenWithoutHeavyOnes()
    {
        var (ctx, _) = Ctx("A", "B", "C", "D", "E", "F");
        Assert.NotNull(new StartupBloatRule().Detect(ctx));
    }

    [Fact]
    public void Fix_DisablesOnlyHeavyItems_AndUndoRestores()
    {
        var (ctx, reg) = Ctx("Steam", "MyTool");
        var rule = new StartupBloatRule();
        var prior = rule.Fix(ctx);
        Assert.Equal(0x03, reg.GetBytes(ApprovedKey, "Steam")![0]);
        Assert.Null(reg.GetBytes(ApprovedKey, "MyTool")); // untouched
        rule.Undo(ctx, prior);
        Assert.Null(reg.GetBytes(ApprovedKey, "Steam")); // was absent before
    }
}
```

- [ ] **Step 2: Run tests to verify they fail** (`dotnet test src/BriskEngine.Tests --filter StartupBloatRuleTests`) — build FAILS.

- [ ] **Step 3: Implement** `StartupBloatRule` per the contract above:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BriskEngine.Models;
using BriskEngine.Paths;

namespace BriskEngine.Diagnostics.Rules;

public sealed class StartupBloatRule : IDiagnosticRule
{
    public static readonly IReadOnlySet<string> KnownHeavy = new HashSet<string>(
        new[] { "Steam", "Discord", "Spotify", "Docker Desktop", "EpicGamesLauncher",
                "WhatsApp", "Teams", "BlueStacks", "WallpaperEngine" },
        StringComparer.OrdinalIgnoreCase);

    private const int ManyThreshold = 6;
    private static readonly (string Hive, string Run, string Approved)[] Hives =
    {
        ("HKCU", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                 @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
        ("HKLM", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                 @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
    };

    public string Id => "startup-bloat";
    public RuleCategory Category => RuleCategory.Confirm;

    private sealed record Item(string Hive, string Name, string Approved);

    private static bool IsHeavy(string name) => KnownHeavy.Any(h =>
        name.Contains(h, StringComparison.OrdinalIgnoreCase));

    private static List<Item> EnabledItems(DiagnosticContext ctx)
    {
        var items = new List<Item>();
        foreach (var (hive, run, approved) in Hives)
        foreach (var name in ctx.Registry.GetValueNames(run))
        {
            var bytes = ctx.Registry.GetBytes(approved, name);
            var disabled = bytes is { Length: > 0 } && (bytes[0] & 1) == 1;
            if (!disabled) items.Add(new Item(hive, name, approved));
        }
        return items;
    }

    private static IReadOnlyList<string> StartupFolderLinks(DiagnosticContext ctx)
    {
        var folder = PathExpander.Expand(
            @"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup");
        return folder is null
            ? Array.Empty<string>()
            : ctx.Files.ListFiles(folder).Where(f => f.EndsWith(".lnk")).ToList();
    }

    public DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var enabled = EnabledItems(ctx);
        var links = StartupFolderLinks(ctx);
        var heavy = enabled.Where(i => IsHeavy(i.Name)).Select(i => i.Name).ToList();
        var total = enabled.Count + links.Count;
        if (heavy.Count == 0 && total < ManyThreshold) return null;

        var evidence = $"{total} programs start with Windows.";
        if (heavy.Count > 0)
            evidence += $" Heavy ones that can be started manually instead: {string.Join(", ", heavy)}.";
        return new DiagnosticFinding(Id, "rule.startup-bloat.title",
            "Too many programs start with Windows", evidence,
            Severity.Warning, Category, ImpactStars: 3, CanFix: heavy.Count > 0,
            FixDescription: heavy.Count > 0
                ? $"Disable at startup: {string.Join(", ", heavy)} (undoable; the apps still work when opened manually)"
                : null);
    }

    public string Fix(DiagnosticContext ctx)
    {
        var prior = new Dictionary<string, string?>();
        var disabledBytes = new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        foreach (var item in EnabledItems(ctx).Where(i => IsHeavy(i.Name)))
        {
            try
            {
                var existing = ctx.Registry.GetBytes(item.Approved, item.Name);
                prior[$"{item.Approved}|{item.Name}"] =
                    existing is null ? null : Convert.ToBase64String(existing);
                ctx.Registry.SetBytes(item.Approved, item.Name, disabledBytes);
            }
            catch (UnauthorizedAccessException) { /* HKLM without elevation — skip */ }
        }
        return JsonSerializer.Serialize(prior);
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        var prior = JsonSerializer.Deserialize<Dictionary<string, string?>>(priorStateJson)!;
        foreach (var (key, base64) in prior)
        {
            var sep = key.LastIndexOf('|');
            var (approved, name) = (key[..sep], key[(sep + 1)..]);
            if (base64 is null) ctx.Registry.DeleteValue(approved, name);
            else ctx.Registry.SetBytes(approved, name, Convert.FromBase64String(base64));
        }
    }
}
```

- [ ] **Step 4: Run ALL tests** (`dotnet test src/BriskEngine.Tests`) — PASS.

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\MERT\Desktop\brisk add -A
git -C C:\Users\MERT\Desktop\brisk commit -m "feat: startup bloat rule with per-item StartupApproved toggling"
```

---

### Task 11: Advise rules (ram-pressure, disk-breakdown, orphaned-data, stale-dev-caches)

**Files:**
- Create: `src/BriskEngine/Diagnostics/Rules/AdviseRuleBase.cs`, `Rules/RamPressureRule.cs`, `Rules/DiskBreakdownRule.cs`, `Rules/OrphanedDataRule.cs`, `Rules/StaleDevCachesRule.cs`
- Test: `src/BriskEngine.Tests/Rules/AdviseRulesTests.cs`

**Interfaces:**
- Consumes: Tasks 7-9 context + fakes; `CleanupTargetRegistry` (Task 4).
- Produces:
  - `abstract class AdviseRuleBase : IDiagnosticRule` — `Category => RuleCategory.Advise`; `Fix`/`Undo` throw `InvalidOperationException("advise-only rule")`; subclasses implement `Id` and `Detect`.
  - Rules with ids `"ram-pressure"`, `"disk-breakdown"`, `"orphaned-data"`, `"stale-dev-caches"`. All findings have `CanFix: false, FixDescription: null`.

**Rule contracts:**
- *ram-pressure*: `ctx.Processes.MemoryLoadPercent() >= 80` → Warning finding, 2 stars; evidence = load percent + top 5 `ctx.Processes.TopByMemory(5)` as "name (N MB)".
- *disk-breakdown*: sizes via `ctx.Files.DirectorySizeBytes` of `%LOCALAPPDATA%` (threshold 50 GB), `%APPDATA%` (20 GB), Desktop (10 GB), `%USERPROFILE%\Downloads` (10 GB) — Desktop path via `Environment.GetFolderPath(SpecialFolder.DesktopDirectory)`. Any folder over its threshold → Warning finding, 2 stars, evidence lists every folder with size and marks the offenders. (Pointing only — Desktop/Downloads are user data, the cleaner never touches them.)
- *orphaned-data*: tools = `("Docker Desktop", %LOCALAPPDATA%\Docker)`, `("BlueStacks", %ProgramData%\BlueStacks_nxt)`, `("Unity", %LOCALAPPDATA%\Unity)`, `("JetBrains", %LOCALAPPDATA%\JetBrains)`. Installed check: any subkey under `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` or `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall` whose `DisplayName` contains the tool name (case-insensitive). Not installed AND `DirectorySizeBytes(dataDir) >= 500 MB` → include; one Warning finding, 3 stars, listing each orphan with its size.
- *stale-dev-caches*: for every `CleanupTargetRegistry.All` target with `Level == Developer && Regenerates && PathTemplates.Count > 0`: expand each template with `PathExpander` (these have no wildcards); `size = ctx.Files.DirectorySizeBytes(path)`, `newest = ctx.Files.NewestWriteUtc(path)`; `size >= 500 MB && newest <= UtcNow - 60 days` → include. One Info finding, 2 stars, listing target display names with size and idle days.

- [ ] **Step 1: Write the failing tests** — `src/BriskEngine.Tests/Rules/AdviseRulesTests.cs`:

```csharp
using System;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Paths;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class AdviseRulesTests
{
    [Fact]
    public void RamPressure_HighLoad_Finds_AndFixRefused()
    {
        var ctx = TestContext.Empty();
        var procs = (FakeProcessInfo)ctx.Processes;
        procs.MemoryLoad = 91;
        procs.Top.Add(("chrome", 900L << 20));
        var rule = new RamPressureRule();
        var finding = rule.Detect(ctx);
        Assert.NotNull(finding);
        Assert.False(finding!.CanFix);
        Assert.Throws<InvalidOperationException>(() => rule.Fix(ctx));
    }

    [Fact]
    public void RamPressure_NormalLoad_Null()
    {
        Assert.Null(new RamPressureRule().Detect(TestContext.Empty()));
    }

    [Fact]
    public void DiskBreakdown_BloatedLocalAppData_Finds()
    {
        var ctx = TestContext.Empty();
        var files = (FakeFiles)ctx.Files;
        files.Sizes[PathExpander.Expand("%LOCALAPPDATA%")!] = 71L << 30;
        var finding = new DiskBreakdownRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Contains("AppData", finding!.Evidence);
    }

    [Fact]
    public void OrphanedData_UninstalledDocker_WithBigData_Finds()
    {
        var ctx = TestContext.Empty();
        var files = (FakeFiles)ctx.Files;
        files.Sizes[PathExpander.Expand(@"%LOCALAPPDATA%\Docker")!] = 3L << 30;
        // registry has no uninstall entries at all -> Docker not installed
        var finding = new OrphanedDataRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Contains("Docker", finding!.Evidence);
    }

    [Fact]
    public void OrphanedData_InstalledDocker_Null()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        var files = (FakeFiles)ctx.Files;
        files.Sizes[PathExpander.Expand(@"%LOCALAPPDATA%\Docker")!] = 3L << 30;
        const string uninstall = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        reg.SubKeys[uninstall] = new() { "Docker" };
        reg.SetString($@"{uninstall}\Docker", "DisplayName", "Docker Desktop 4.30");
        Assert.Null(new OrphanedDataRule().Detect(ctx));
    }

    [Fact]
    public void StaleDevCaches_OldBigNpmCache_Finds()
    {
        var ctx = TestContext.Empty();
        var files = (FakeFiles)ctx.Files;
        var npm = PathExpander.Expand(@"%LOCALAPPDATA%\npm-cache")!;
        files.Sizes[npm] = 2L << 30;
        files.NewestWrites[npm] = DateTime.UtcNow.AddDays(-90);
        var finding = new StaleDevCachesRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Contains("npm", finding!.Evidence);
    }
}
```

- [ ] **Step 2: Verify failure** (`--filter AdviseRulesTests`) — build FAILS.

- [ ] **Step 3: Implement.** `AdviseRuleBase`:

```csharp
using System;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

public abstract class AdviseRuleBase : IDiagnosticRule
{
    public abstract string Id { get; }
    public RuleCategory Category => RuleCategory.Advise;
    public abstract DiagnosticFinding? Detect(DiagnosticContext ctx);
    public string Fix(DiagnosticContext ctx) => throw new InvalidOperationException("advise-only rule");
    public void Undo(DiagnosticContext ctx, string priorStateJson) => throw new InvalidOperationException("advise-only rule");
}
```

The four rules follow the contracts verbatim. `RamPressureRule`:

```csharp
using System.Linq;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

public sealed class RamPressureRule : AdviseRuleBase
{
    public override string Id => "ram-pressure";

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var load = ctx.Processes.MemoryLoadPercent();
        if (load < 80) return null;
        var top = string.Join(", ", ctx.Processes.TopByMemory(5)
            .Select(p => $"{p.Name} ({p.WorkingSetBytes >> 20} MB)"));
        return new DiagnosticFinding(Id, "rule.ram-pressure.title",
            "Memory is under pressure",
            $"RAM is {load:F0}% full. Biggest consumers: {top}. " +
            "Closing or un-starting some of these frees memory.",
            Severity.Warning, Category, ImpactStars: 2, CanFix: false, FixDescription: null);
    }
}
```

`DiskBreakdownRule` (folders as `(label, path, thresholdBytes)` tuples, `Environment.GetFolderPath` for Desktop, `PathExpander` for the rest; build evidence with every size, return finding only if any exceeds), `OrphanedDataRule` (helper `bool Installed(ctx, name)` scanning both uninstall hives' subkeys for a `DisplayName` containing the name), `StaleDevCachesRule` (loop registry targets per contract; evidence like `"npm cache: 2.0 GB, idle 90 days"`). Format sizes with a small shared helper `static string Fmt.Bytes(long)` — add `src/BriskEngine/Fmt.cs`:

```csharp
namespace BriskEngine;

public static class Fmt
{
    public static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F1} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):F0} KB",
        _ => $"{bytes} B",
    };
}
```

- [ ] **Step 4: Run ALL tests** — PASS.
- [ ] **Step 5: Commit** — `git -C C:\Users\MERT\Desktop\brisk add -A` + `git -C C:\Users\MERT\Desktop\brisk commit -m "feat: advise rules (ram, disk breakdown, orphaned data, stale caches)"`

---

### Task 12: thermals, visual-effects, storage-sense, disk-forecast + rule registry + real probes

**Files:**
- Create: `Rules/ThermalsRule.cs`, `Rules/VisualEffectsRule.cs`, `Rules/StorageSenseRule.cs`, `Rules/DiskForecastRule.cs`, `src/BriskEngine/Diagnostics/DiagnosticRuleRegistry.cs`, `RealProbes/RealSensorProbe.cs`, `RealProbes/RealProcessInfoProbe.cs`, `RealProbes/RealDiskInfoProbe.cs`
- Test: `src/BriskEngine.Tests/Rules/SystemRulesTests.cs`

**Interfaces:**
- Produces:
  - `ThermalsRule` (`"thermals"`, Advise): finding when `CpuTempC() >= 75` or `GpuTempC() >= 70`; `null` sensors → no finding (never an error). Severity Warning, 2 stars; evidence includes the temps and "clean fans / renew thermal paste" advice.
  - `VisualEffectsRule` (`"visual-effects"`, Confirm): `GetInt(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting") == 1` (best appearance) → finding, 2 stars. Fix: `SetInt(..., 2)` (best performance); prior JSON `{"previous": <int or -1 when absent>}`; Undo: restore, or `DeleteValue` when -1.
  - `StorageSenseRule` (`"storage-sense"`, Confirm): free space below 15% (`ctx.Disk` on `C:\`) AND `GetInt(@"HKCU\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01") != 1` → finding, 2 stars. Fix: `SetInt(..., "01", 1)`; prior `{"previous": <int or -1>}`; Undo mirror of visual-effects.
  - `DiskForecastRule` (`"disk-forecast"`, Advise): keeps `disk-history.jsonl` in `ctx.DataDirectory` (`{"ts":"...","free":123}` per line, direct file IO). `Detect` appends today's sample (once per calendar day), then: with ≥3 samples spanning ≥7 days and a negative least-squares slope (bytes/day), projects days until free hits zero; ≤60 days → Warning finding, 3 stars ("disk full in ~N days at the current rate").
  - `static IReadOnlyList<IDiagnosticRule> DiagnosticRuleRegistry.All` — all 12 rules, instantiated in spec order: power-plan, browser-gpu, hw-acceleration, startup-bloat, ram-pressure, thermals, disk-breakdown, disk-forecast, orphaned-data, stale-dev-caches, visual-effects, storage-sense.
  - Real probes: `RealSensorProbe` (packages `LibreHardwareMonitorLib` + `System.Management`; `Computer { IsCpuEnabled = true, IsGpuEnabled = true }`, temps = max temperature sensor per hardware kind, any exception → null; `GpuCount()` = count of `Win32_VideoController` rows via `ManagementObjectSearcher`, exception → 1), `RealProcessInfoProbe` (`Process.GetProcesses()` working sets, name-grouped, sorted desc; `MemoryLoadPercent` via P/Invoke `GlobalMemoryStatusEx` → `dwMemoryLoad`), `RealDiskInfoProbe` (`new DriveInfo(driveRoot)` free/total).

- [ ] **Step 1: Write the failing tests** — `src/BriskEngine.Tests/Rules/SystemRulesTests.cs`:

```csharp
using System;
using System.IO;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class SystemRulesTests
{
    private const string FxKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string SenseKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy";

    [Fact]
    public void Thermals_Hot_Finds_NullSensors_Silent()
    {
        var ctx = TestContext.Empty();
        var sensors = (FakeSensors)ctx.Sensors;
        Assert.Null(new ThermalsRule().Detect(ctx));       // null temps
        sensors.CpuTemp = 88;
        Assert.NotNull(new ThermalsRule().Detect(ctx));
    }

    [Fact]
    public void VisualEffects_AppearanceMode_FixesToPerformance_AndUndoes()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetInt(FxKey, "VisualFXSetting", 1);
        var rule = new VisualEffectsRule();
        Assert.NotNull(rule.Detect(ctx));
        var prior = rule.Fix(ctx);
        Assert.Equal(2, reg.GetInt(FxKey, "VisualFXSetting"));
        rule.Undo(ctx, prior);
        Assert.Equal(1, reg.GetInt(FxKey, "VisualFXSetting"));
    }

    [Fact]
    public void StorageSense_LowDiskAndOff_Finds()
    {
        var ctx = TestContext.Empty();
        var disk = (FakeDisk)ctx.Disk;
        disk.Free = 50L << 30; disk.Total = 1000L << 30;   // 5% free
        Assert.NotNull(new StorageSenseRule().Detect(ctx));
        ((FakeRegistry)ctx.Registry).SetInt(SenseKey, "01", 1);
        Assert.Null(new StorageSenseRule().Detect(ctx));
    }

    [Fact]
    public void DiskForecast_ShrinkingDisk_Finds()
    {
        var ctx = TestContext.Empty();
        var disk = (FakeDisk)ctx.Disk;
        disk.Free = 40L << 30;
        var history = Path.Combine(ctx.DataDirectory, "disk-history.jsonl");
        File.WriteAllLines(history, new[]
        {
            $"{{\"ts\":\"{DateTime.UtcNow.AddDays(-14):O}\",\"free\":{100L << 30}}}",
            $"{{\"ts\":\"{DateTime.UtcNow.AddDays(-7):O}\",\"free\":{70L << 30}}}",
        });
        var finding = new DiskForecastRule().Detect(ctx);   // appends today's 40 GB sample
        Assert.NotNull(finding);
        Assert.Contains("days", finding!.Evidence);
    }

    [Fact]
    public void DiskForecast_StableDisk_Null()
    {
        var ctx = TestContext.Empty();
        Assert.Null(new DiskForecastRule().Detect(ctx));    // one sample only
    }

    [Fact]
    public void Registry_HasTwelveRules_WithUniqueIds()
    {
        var all = DiagnosticRuleRegistry.All;
        Assert.Equal(12, all.Count);
        Assert.Equal(12, System.Linq.Enumerable.Count(
            System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Select(all, r => r.Id))));
    }
}
```

- [ ] **Step 2: Verify failure** — build FAILS.

- [ ] **Step 3: Implement** the four rules per contract (visual-effects/storage-sense mirror each other; disk-forecast: read lines → `(DateTime ts, long free)`, append today if the last sample's date != today, least squares over `(days-since-first, free)`, `daysToZero = currentFree / -slope`), then:

`src/BriskEngine/Diagnostics/DiagnosticRuleRegistry.cs`:

```csharp
using System.Collections.Generic;
using BriskEngine.Diagnostics.Rules;

namespace BriskEngine.Diagnostics;

public static class DiagnosticRuleRegistry
{
    public static IReadOnlyList<IDiagnosticRule> All { get; } = new IDiagnosticRule[]
    {
        new PowerPlanRule(), new BrowserGpuRule(), new HardwareAccelerationRule(),
        new StartupBloatRule(), new RamPressureRule(), new ThermalsRule(),
        new DiskBreakdownRule(), new DiskForecastRule(), new OrphanedDataRule(),
        new StaleDevCachesRule(), new VisualEffectsRule(), new StorageSenseRule(),
    };
}
```

Add packages before the real probes: `dotnet add src/BriskEngine package LibreHardwareMonitorLib` and `dotnet add src/BriskEngine package System.Management`. Real probes per the Interfaces block; every real probe wraps its body in try/catch and returns the "unavailable" value (null / 1 / empty) on failure — sensors must NEVER crash a scan.

- [ ] **Step 4: Run ALL tests** — PASS.
- [ ] **Step 5: Commit** — `git -C C:\Users\MERT\Desktop\brisk add -A` + `git -C C:\Users\MERT\Desktop\brisk commit -m "feat: system rules, rule registry, real sensor probes"`

---

### Task 13: brisk CLI

**Files:**
- Create: `src/Brisk.Cli/CliParser.cs`, rewrite `src/Brisk.Cli/Program.cs`
- Test: `src/BriskEngine.Tests/CliParserTests.cs` (add `<ProjectReference>` from the test project to `src/Brisk.Cli`)

**Interfaces:**
- Consumes: everything — `Scanner`, `CleanRunner`, `FixRunner`, `DiagnosticRuleRegistry`, `CleanupTargetRegistry`, all real probes.
- Produces:
  - `sealed record CliCommand(string Verb, string? RuleId = null, string? Level = null, bool Json = false, bool Yes = false, bool All = false, bool Undo = false, string? Error = null)`
  - `static CliCommand CliParser.Parse(string[] args)` — verbs: `scan`, `fix`, `clean`, `targets`, `rules`, `version`. Flags: `--json` (scan), `--all`/`--rule <id>`/`--undo` (fix), `--level <safe|developer|deep>` (clean), `--yes` (fix/clean). Unknown verb/flag or missing flag argument → `Verb = "error"` with `Error` set. No args → `Verb = "help"`.
  - `Program.Main` behavior (exit codes: 0 ok, 1 runtime failure, 2 bad usage):
    - `scan`: run all 12 rules' `Detect` + a full cleaner `Scanner.Scan`; print findings (severity, title, evidence, impact stars, fixability) then per-level reclaimable totals; `--json` prints one JSON object `{findings:[...], cleaner:{targets:[...], totalBytes}}` via `JsonSerializer`.
    - `fix --all --yes`: apply every **Auto** rule whose `Detect` returned a finding, via `FixRunner.Apply`; print each outcome. Without `--yes`: print what WOULD be fixed, mutate nothing.
    - `fix --rule <id> --yes`: apply that one rule (Auto or Confirm — naming the rule IS the consent for Confirm). `fix --rule <id> --undo --yes`: `FixRunner.Undo`.
    - `clean --level safe --yes`: scan that level's targets, then `CleanRunner.Clean` each target scan, auto-selecting only targets with `!RequiresIndividualSelection && !RequiresExplicitOptIn`; print recycled totals. Without `--yes`: print the full deletion plan (every path + size), mutate nothing (pass `dryRun: true` and do not log dry-run entries to the console as deletions).
    - `targets` / `rules`: print the registries (id, level/category, display name).
  - Data directory: `%LOCALAPPDATA%\brisk` (ActionLog `action-log.jsonl`, FixJournal `fix-journal.jsonl`, disk history).
  - Elevation detection for CleanRunner: `new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)`.

- [ ] **Step 1: Write the failing tests** — `src/BriskEngine.Tests/CliParserTests.cs`:

```csharp
using Brisk.Cli;
using Xunit;

namespace BriskEngine.Tests;

public class CliParserTests
{
    [Fact]
    public void NoArgs_IsHelp() => Assert.Equal("help", CliParser.Parse(new string[0]).Verb);

    [Fact]
    public void ScanJson()
    {
        var cmd = CliParser.Parse(new[] { "scan", "--json" });
        Assert.Equal("scan", cmd.Verb);
        Assert.True(cmd.Json);
    }

    [Fact]
    public void FixRuleWithYes()
    {
        var cmd = CliParser.Parse(new[] { "fix", "--rule", "power-plan", "--yes" });
        Assert.Equal(("fix", "power-plan", true), (cmd.Verb, cmd.RuleId, cmd.Yes));
    }

    [Fact]
    public void FixUndo()
    {
        var cmd = CliParser.Parse(new[] { "fix", "--rule", "power-plan", "--undo", "--yes" });
        Assert.True(cmd.Undo);
    }

    [Fact]
    public void CleanLevel()
    {
        var cmd = CliParser.Parse(new[] { "clean", "--level", "developer" });
        Assert.Equal("developer", cmd.Level);
        Assert.False(cmd.Yes);
    }

    [Fact]
    public void BadLevel_IsError()
    {
        Assert.Equal("error", CliParser.Parse(new[] { "clean", "--level", "mega" }).Verb);
    }

    [Fact]
    public void MissingRuleArgument_IsError()
    {
        Assert.Equal("error", CliParser.Parse(new[] { "fix", "--rule" }).Verb);
    }

    [Fact]
    public void UnknownVerb_IsError()
    {
        Assert.Equal("error", CliParser.Parse(new[] { "explode" }).Verb);
    }
}
```

- [ ] **Step 2: Verify failure** — add the project reference (`dotnet add src/BriskEngine.Tests reference src/Brisk.Cli`), run `dotnet test src/BriskEngine.Tests --filter CliParserTests`. Build FAILS (no `CliParser`).

- [ ] **Step 3: Implement** `src/Brisk.Cli/CliParser.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Brisk.Cli;

public sealed record CliCommand(string Verb, string? RuleId = null, string? Level = null,
    bool Json = false, bool Yes = false, bool All = false, bool Undo = false, string? Error = null);

public static class CliParser
{
    private static readonly HashSet<string> Verbs =
        new() { "scan", "fix", "clean", "targets", "rules", "version" };
    private static readonly HashSet<string> Levels = new() { "safe", "developer", "deep" };

    public static CliCommand Parse(string[] args)
    {
        if (args.Length == 0) return new CliCommand("help");
        var verb = args[0];
        if (!Verbs.Contains(verb))
            return new CliCommand("error", Error: $"unknown command '{verb}'");

        var cmd = new CliCommand(verb);
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json": cmd = cmd with { Json = true }; break;
                case "--yes": cmd = cmd with { Yes = true }; break;
                case "--all": cmd = cmd with { All = true }; break;
                case "--undo": cmd = cmd with { Undo = true }; break;
                case "--rule" when i + 1 < args.Length:
                    cmd = cmd with { RuleId = args[++i] }; break;
                case "--level" when i + 1 < args.Length && Levels.Contains(args[i + 1]):
                    cmd = cmd with { Level = args[++i] }; break;
                default:
                    return new CliCommand("error", Error: $"bad argument '{args[i]}'");
            }
        }
        return cmd;
    }
}
```

`src/Brisk.Cli/Program.cs` — wire everything (structure; keep printing plain and greppable):

```csharp
using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using BriskEngine;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.RealProbes;
using BriskEngine.Logging;
using BriskEngine.Models;
using BriskEngine.Safety;

namespace Brisk.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        var cmd = CliParser.Parse(args);
        if (cmd.Verb == "error") { Console.Error.WriteLine($"brisk: {cmd.Error}"); return 2; }
        if (cmd.Verb is "help") { PrintHelp(); return 0; }
        if (cmd.Verb is "version") { Console.WriteLine(EngineInfo.Version); return 0; }

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "brisk");
        var runner = new RealProcessRunner();
        var ctx = new DiagnosticContext(
            new RealPowercfgProbe(runner), new RealRegistryProbe(),
            new RealProcessInfoProbe(), new RealSensorProbe(),
            new RealDiskInfoProbe(), new RealFileProbe(),
            new RealProcessLister(), dataDir);
        var log = new ActionLog(Path.Combine(dataDir, "action-log.jsonl"));
        var fixRunner = new FixRunner(new FixJournal(Path.Combine(dataDir, "fix-journal.jsonl")), log);
        var scanner = new Scanner(CleanupTargetRegistry.All, new RealProcessLister());
        bool IsElevated() => new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);
        var cleanRunner = new CleanRunner(new SafetyValidator(), new WindowsRecycler(),
            log, runner, IsElevated);

        try
        {
            return cmd.Verb switch
            {
                "scan" => Scan(cmd, ctx, scanner),
                "fix" => Fix(cmd, ctx, fixRunner),
                "clean" => Clean(cmd, scanner, cleanRunner),
                "targets" => PrintTargets(),
                "rules" => PrintRules(),
                _ => 2,
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"brisk: {ex.Message}");
            return 1;
        }
    }
    // ... Scan/Fix/Clean/PrintTargets/PrintRules/PrintHelp below
}
```

The subcommands (same file):
- `Scan`: `var findings = DiagnosticRuleRegistry.All.Select(r => Safe(() => r.Detect(ctx))).Where(f => f != null).ToList();` where `Safe` catches per-rule exceptions and returns null (one broken probe never kills the report). Cleaner: `scanner.Scan()`. Text output: `[!!] Power plan is throttling your CPU (impact *****)` + indented evidence, then `Reclaimable — Safe: 4.2 GB, Developer: 9.1 GB, Deep: 11.0 GB (run 'brisk clean')`, sizes via `Fmt.Bytes`. `--json`: serialize `new { findings, cleaner = new { targets = scan.Targets.Select(t => new { id = t.Target.Id, bytes = t.TotalBytes, skipped = t.SkippedReason }), totalBytes = scan.TotalBytes } }`.
- `Fix`: `--undo` requires `RuleId` → `fixRunner.Undo`. `--all`: for each Auto rule with a live finding → without `--yes` print `would fix: <id> — <title>`; with `--yes` → `Apply` + print outcome. `--rule <id>`: find rule (error 2 if unknown); Confirm rules allowed here; without `--yes` print the finding + `add --yes to apply`.
- `Clean`: level filter (`cmd.Level` null → safe). For each target of that level from the scan: auto-select only `!RequiresIndividualSelection && !RequiresExplicitOptIn`; without `--yes` list every item path + size under a `PLAN (nothing deleted)` header; with `--yes` run `cleanRunner.Clean(t, dryRun: false)` and print `recycled: N items, X GB` + refusals/skips.
- `PrintTargets` / `PrintRules`: aligned columns `id  level/category  name`.

- [ ] **Step 4: Run ALL tests** — PASS. Also `dotnet build` the full solution — zero warnings.

- [ ] **Step 5: Commit** — `git -C C:\Users\MERT\Desktop\brisk add -A` + `git -C C:\Users\MERT\Desktop\brisk commit -m "feat: brisk CLI (scan, fix, clean, targets, rules)"`

---

### Task 14: End-to-end verification on the real machine

**Files:**
- Create: `README.md` (stub — the full growth README is Plan C)

No unit-test cycle here; this is the live smoke test of everything. Run each command, READ the output, and confirm the listed expectation before moving on. Machine context: Mert's laptop is hybrid-GPU (GTX 1650 Ti + Intel UHD), so browser-gpu SHOULD fire if preferences are unset.

- [ ] **Step 1: Full scan** — `dotnet run --project src/Brisk.Cli -- scan`
  Expected: no crash; findings print with evidence (power-plan appears IF the current plan is Balanced); cleaner section lists Safe/Developer/Deep totals > 0; sensors line either shows temps or says unavailable — never an exception.
- [ ] **Step 2: JSON scan** — `dotnet run --project src/Brisk.Cli -- scan --json | python -m json.tool` (or pipe to a file and open) — valid JSON.
- [ ] **Step 3: Clean plan (no --yes)** — `dotnet run --project src/Brisk.Cli -- clean --level safe`
  Expected: full path+size list; NOTHING deleted (spot-check a listed temp file still exists); `%LOCALAPPDATA%\brisk\action-log.jsonl` gained no `recycled` entries.
- [ ] **Step 4: Real safe clean** — `dotnet run --project src/Brisk.Cli -- clean --level safe --yes`
  Expected: recycled totals printed; Recycle Bin now contains the items; action log has `recycled` lines.
- [ ] **Step 5: Fix round-trip** — `dotnet run --project src/Brisk.Cli -- fix --rule power-plan --yes` then `powercfg /getactivescheme` (should be High/Ultimate performance), then `... fix --rule power-plan --undo --yes` and confirm the original plan is back. Journal `fix-journal.jsonl` shows the fix + undo pair.
- [ ] **Step 6: README stub** — title, one-paragraph description (from the spec one-liner), `Status: pre-release, CLI only` note, build instructions (`dotnet build`, `dotnet run --project src/Brisk.Cli -- scan`), MIT license file.
- [ ] **Step 7: Commit** — `git -C C:\Users\MERT\Desktop\brisk add -A` + `git -C C:\Users\MERT\Desktop\brisk commit -m "docs: readme stub after live smoke test"`

---

## Out of scope for this plan (deliberately)

- WPF app + design system + tray icon → **Plan B** (`docs/superpowers/plans/` next).
- Growth README, demo GIF, llms.txt, comparison table, winget/Scoop manifests, GitHub Actions CI + release pipeline → **Plan C**.
- System Restore point before "fix all" (needs elevation path design — revisit in Plan B where the UI owns consent).
- Per-action UAC elevation prompts (spec's model). The CLI reports "requires administrator" and refuses; running the console elevated is the CLI-era workaround. The UAC re-launch helper lands in Plan B.
- Localization resources (engine already ships stable `TitleKey`s; resx wiring lands with the UI).
