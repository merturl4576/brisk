# v0.9 — Security posture on the Privacy page — Design

**Status:** approved in chat 2026-09-05 (maintainer's choice of scope, order
and release timing); written spec pending his read.

**Version:** lands as 0.9.0 on branch `feat/v0.9-security`. main stays at
the released 0.8.0 until the branch passes the whole gate. The r/opensource
post goes out with 0.8.0; 0.9.0 is the follow-up ("added after the comments")
for the r/dotnet step. Decided 2026-09-05.

## Why

On 2026-09-03 the maintainer's own machine was audited by hand next to a
brisk 0.8.0 scan. brisk saw eight things. The audit saw, in addition: UAC set
to elevate silently (`ConsentPromptBehaviorAdmin=0`), five folders excluded
from Defender scanning, three cloud-reporting apps that had used the
microphone, Windows' suggestion and silent-install flags all on, and a
firewall and Remote Desktop configuration that was FINE and that brisk never
said was fine.

None of that is speed. All of it is "what is this machine doing that its
owner does not know about", which is the Privacy page's question already.
The differentiator stays what it was in Faz 3: brisk reads the machine,
says exactly what it read, fixes only what it can undo, and re-reads its own
fix on every later scan.

Nothing here is graded. The health score is speed and hygiene, the Privacy
page moves it by zero, and that stays true with a Security tier on it.

## Scope

**In (five items, in build order):**

1. `uac-prompts` — a switch with a fix and a read-back.
2. `camera-mic-use` — a disclosure: which apps used the microphone and the
   camera in the last 30 days, counted on the finding, named on the page.
3. `defender-blind-spots` — a disclosure: exclusions, real-time and tamper
   protection, scan age; a link to Windows Security.
4. `windows-suggestions` — a switch: six content-delivery values, Auto.
5. Security posture — the positive lines: firewall per profile, Remote
   Desktop, UAC, Defender real-time and tamper, as one block on the page and
   one line in `brisk scan`.

**Out, to v0.10, with reasons:**

- Browser extensions with all-sites/debugger powers: manifest parsing across
  profiles and locale files; a day on its own. Root certificates not from
  known CAs: needs EKU honesty (a code-signing root cannot read traffic —
  the maintainer's own dev certificate is exactly that) and a trusted list to
  compare against. Unsigned startup programs: needs WinVerifyTrust, not just
  "has a certificate". Programs listening on all interfaces: needs the
  firewall rule set read beside the socket table or it overstates. Local
  accounts with no password required: the SAM flag reads wrongly on
  Microsoft-account machines. Each of these is a wave, not a rule.
- The parked branch `feat/fable-gap-rules` (scheduled-task bloat, Game Bar):
  reviewed separately, not merged blind.
- Any change to the report card. Security rows do not go on the shareable
  PNG in this wave; the card keeps saying what it said.

## The red lines (each becomes a test)

1. **Numbers, never contents, on every surface built to be shared.** "5
   paths Defender skips" on the finding; the paths on the Privacy page only.
   "6 apps used the microphone" on the finding; the app names and paths on
   the Privacy page only. `brisk scan --json` is a shared surface (bug
   reports carry it): no path and no app name in any finding string,
   evidence argument, headline, or JSON field. Tests plant recognisable names
   and read every string a finding can carry back out again, the
   `PrivacyDisclosureRuleTests` way.
2. **Unreadable reports unreadable, never zero, never fine.** A Defender read
   that failed, an unelevated process that cannot see exclusions, a
   capability key that is not there: the finding says brisk could not read
   it, and the posture row shows "not read". No row goes green on a null.
3. **Absence is not the bad state for UAC.** The privacy family reads an
   unwritten switch as ON because it cannot read it as off. UAC inverts that:
   Windows' default for the three values is prompting, so an absent value
   cannot be read as "never prompt" and raises no finding. Written down here
   because it is the opposite of the family's standing rule, on purpose.
4. **brisk never says the machine is secure.** Posture rows say "firewall on
   for all three profiles", "Remote Desktop off", "Defender real-time on".
   They never sum to a verdict, a badge, or a number. The section header is
   "Security", the sentence under it says what brisk read and that it reads
   nothing past that.
5. **Nothing graded.** Kind is Notice on every finding in this wave; the
   score does not move. The page's hero note already says so and stays true.

## Components

### 0. Two foundations

**`IRegistryProbe.GetLong(keyPath, valueName)`** — a QWORD read, `long?`,
null when absent or not a QWORD. `RealRegistryProbe` implements it with the
same `OpenSubKey` shape as `GetInt`. The five fakes that implement the
interface (`TestContext`, `ReadBackTests`, `PrivacyDisclosureRuleTests`,
`StartupManagerTests`, `EngineHostTests`) gain the member. Needed by
camera-mic-use: `LastUsedTimeStop` is a FILETIME stored as QWORD.

**`TelemetrySwitchRule.Severity`** — `protected virtual Severity Severity =>
Severity.Info;`, read by the family's `Detect` in place of the literal.
`uac-prompts` overrides it to `Warning`. A test pins that it is the only
override in the registry, so the tiering cannot drift by accident. Kind
stays `Notice` for every member; that is not made virtual.

### 1. `uac-prompts` (UacPromptRule : TelemetrySwitchRule)

**Reads** `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System`:
`EnableLUA`, `ConsentPromptBehaviorAdmin`, `PromptOnSecureDesktop`. This is
the key UAC itself reads, not a Group Policy key, so the write IS the
setting: `EffectOfTheWrite` stays `NotAPolicy`.

**IsOn (the bad state) is overridden:** `EnableLUA == 0` or
`ConsentPromptBehaviorAdmin == 0`. Nothing else fires it. `PromptOnSecureDesktop`
is not part of the test: the third position of Windows' own UAC slider
("notify me, don't dim the desktop") sets it to 0 with
`ConsentPromptBehaviorAdmin=5`, and that is a user's choice, not a hole.
`ConsentPromptBehaviorAdmin` values 1–5 all prompt in some form; only 0 is
"elevate without prompting". Absent values raise nothing (red line 3).

**Values** (for Fix/Undo): the three above with `OffValue` = Windows' default
(`EnableLUA=1`, `ConsentPromptBehaviorAdmin=5`, `PromptOnSecureDesktop=1`)
and `OnValue=0`. The family's Fix writes all three and records every prior
value by path; Undo restores them, deleting what was absent. Restoring
`EnableLUA` from 0 takes effect at the next restart; the done copy says so
("prompts return after a restart") when and only when `EnableLUA` was the
value that read 0.

**Category: Confirm.** The user will see consent prompts again; that is a
change in their day and it is shown before it happens. Confirm also keeps it
out of `brisk fix --all`, the same reason location and activity-history are
Confirm. **Severity: Warning** (the override above). **Kind: Notice.**

**Copy.** Title: "Windows does not ask before a program takes control".
Evidence, with the three values as arguments: "brisk read Windows' three UAC
values: EnableLUA {0}, ConsentPromptBehaviorAdmin {1}, PromptOnSecureDesktop
{2}. A 0 in the second means any program that asks for administrator rights
gets them with no prompt on screen. Windows' default is to ask, on a dimmed
screen. Restoring the default writes up to three values and can be undone.
Programs will ask again; that is the point." Fix description: "Ask before a
program takes control again (undoable)". Done: "Windows asks again before a
program takes control". An absent value renders as "not set" in the argument.

**Read-back:** free from the family: Held or Reverted. An optimizer that
turns prompting off again after brisk turned it on shows as Reverted on the
next scan, which is the sentence this rule exists for.

**Page:** the new Security tier on the Privacy page, as a finding row with
the Confirm flow the costly switches use. **CLI:** prints as any finding.

**Tests:** a state table — (0,0,0) fires; (1,0,1) fires; (0,5,1) fires;
(1,5,1), (1,2,1), (1,5,0), (absent,absent,absent), (1,absent,absent) do not.
Fix from (1,0,0) writes (1,5,1) and Undo puts (1,0,0) back; Fix from
(absent,0,absent) and Undo deletes the two that were absent. Read-back Held
after fix, Reverted after a planted 0. `WhichSwitchesReadAsOff_AtAStateTheirOwnFixDidNotWrite`
gets the row this rule needs. Category Confirm pinned; `brisk fix --all`
leaves it alone (ProgramFixTests).

### 2. `camera-mic-use` (CapabilityUseRule : PrivacyDisclosureRule)

**Reads** `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{microphone,webcam}`.
Direct subkeys are packaged apps (a package family name such as
`5319275A.WhatsAppDesktop_cv1g1gvanyjgm`); the `NonPackaged` subkey holds
one subkey per desktop executable, its path with `#` in place of `\`
(`C:#Users#MERT#Desktop#system-helper.exe`). Each carries `LastUsedTimeStop`
(QWORD FILETIME; 0 or absent = never used), read with `GetLong`.

**Window:** 30 days back from now. "Now" is injected (`Func<DateTime>` on the
constructor, default `DateTime.UtcNow`) so the table tests plant dates.
FILETIME converts with `DateTime.FromFileTimeUtc`; a value that does not
convert is skipped, not guessed.

**Identity:** one app is counted once. Packaged: the name part before the
`_` of the family name (`5319275A.WhatsAppDesktop`). Desktop: the file name
(`Discord.exe`), because Discord and Wispr Flow keep one subkey per installed
version and the maintainer's machine has 40 Discord entries for one app.
The record carries the newest `LastUsedTimeStop` of the group.

**Finding:** fires when at least one app used either capability in the
window. Title: "Apps have used the microphone or camera recently". Evidence,
counts as arguments: "By Windows' own record of when each app last used
them, {0} apps used the microphone and {1} used the camera in the last 30
days. Which ones, and when, is on this page and on nothing built to be
shared." Headline: the larger count — "{n} apps" / "used the microphone in
the last 30 days" (or camera). Zero in both: no finding; the posture block
does not mention it either (a "nothing used the mic" line is a claim about
a record brisk only partly reads, the packaged half of which is per-user).
Both keys unreadable (no key, or a throw): the unreadable disclosure
sentence, no headline, the `UnreadableRows` band. One key readable and one
not: the readable half is reported and the other is named as not read in
the evidence.

**The names travel one way:** `ScanSnapshot.CapabilityUses` — a required
`IReadOnlyList<CapabilityUseRecord>` (`Capability`, `AppName`, `Path?`,
`LastUsedUtc`) built by `EngineHost.ScanAsync` from the same rule's reader
in the same pass, the `UsbDevices` shape. `Path` is set for desktop apps
only and is shown beneath the name in the page's fold, which is the owner's
own screen (red line 2 as amended 2026-08-26). Nothing on `DiagnosticFinding`
carries a name.

**Page:** the disclosure section, with a fold "Which apps, and when" listing
name, capability, and date, newest first. **RevelationPicker:** tail rank
(off the declared list), like run-history. **CLI:** the finding with its two
counts; `--json` carries counts only.

**Tests:** window arithmetic at the boundary (29 days counts, 31 does not);
grouping (three Discord versions count once, newest date wins); packaged
name split; FILETIME 0 and absent are "never"; red line 2 with planted names
`ZZZ-Recorder.exe` and `ZZZ.PackagedApp_abc` read back from every finding
string and from the JSON; unreadable both / unreadable one; snapshot carries
the records and the finding does not.

### 3. `defender-blind-spots` (DefenderRule : PrivacyDisclosureRule)

**Probe:** `IDefenderProbe.Read()` → `DefenderStatus?`, a new interface on
`DiagnosticContext`. Real implementation reads WMI namespace
`root\Microsoft\Windows\Defender`, classes `MSFT_MpComputerStatus`
(`RealTimeProtectionEnabled`, `IsTamperProtected`, `AMRunningMode`,
`QuickScanEndTime`, `FullScanEndTime`, `AntivirusSignatureAge`) and
`MSFT_MpPreference` (`ExclusionPath`, `ExclusionExtension`,
`ExclusionProcess`), through `System.Management`, which BriskEngine already
references. Whole read fails (no Defender, namespace missing, throw) → null.

`DefenderStatus`: `bool? RealTimeOn`, `bool? TamperOn`, `string?
RunningMode`, `int? ExclusionCount`, `IReadOnlyList<string> ExclusionPaths`,
`DateTime? LastQuickScanUtc`, `DateTime? LastFullScanUtc`, `int?
SignatureAgeDays`. **Exclusions are hidden from non-administrators by
Windows itself** (an empty list, not an error), so the real probe checks its
own token and reports `ExclusionCount = null` and an empty list when not
elevated. brisk-app always runs elevated; the CLI may not, and then it says
it could not read them. Verified 2026-09-05 on the maintainer's machine: the
elevated read returns 5 paths, WMI and registry agree.

**Finding:** fires when any of: `ExclusionCount > 0`; `RealTimeOn == false`;
`TamperOn == false`; `RunningMode` is not "Normal"; the newer of the two scan
times is more than 14 days old, or both are null while the rest was read;
or `ExclusionCount` is null (unelevated) while the rest was read. Whole read
null → the unreadable sentence ("brisk could not read Windows Defender's
state on this machine"), `UnreadableRows`, no headline.

Title: "Windows Defender has blind spots". Evidence is assembled from
parts, each a short clause with its own resx key so the Turkish reads as a
sentence and not a list: "{n} paths are excluded from scanning" / "brisk
could not read the exclusion list without administrator rights";
"real-time protection is off" / "on"; "tamper protection is off" / "on";
"Defender is not running in normal mode ({mode}); another product may be
active, brisk cannot see which"; "last scan {d} days ago" / "no completed
scan on record". The clauses that are fine are included, so the sentence is
the full reading and not only the bad half. Headline, only when
`ExclusionCount > 0`: "{n} paths" / "Defender skips". The paths themselves
never leave the snapshot: `ScanSnapshot.DefenderExclusions`
(`IReadOnlyList<string>`, required) → a fold on the page, "What Defender
skips".

**Action:** the `WindowsSettingRules` map in `HealthViewModel` gains
`["defender-blind-spots"] = ("windowsdefender://threatsettings",
"finding.action.windowssecurity")`; `WindowsSettingsLink.Open` already
hands any URI to the shell. New caption key in both resx files: "Open
Windows Security".

**Page:** Security tier. **CLI:** finding with the count and the clauses;
`--json` carries the count and the booleans, never a path.

**Tests:** the fake probe returns each shape; every trigger fires alone and
the all-fine shape does not; unelevated shape says "could not read", never
"0 paths"; null shape → unreadable row; red line 2 with planted path
`C:\ZZZ-excluded` read back from every finding string and JSON; 14-day
boundary; the settings map entry pinned with its caption key present in
both resx files.

### 4. `windows-suggestions` (ContentSuggestionsRule : TelemetrySwitchRule)

**Values**, all under `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager`,
`OnValue=1`, `OffValue=0`: `SilentInstalledAppsEnabled`,
`SystemPaneSuggestionsEnabled`, `SoftLandingEnabled`,
`RotatingLockScreenOverlayEnabled`, `SubscribedContent-338388Enabled`,
`SubscribedContent-338389Enabled`. Absence reads as on (the family's rule,
and Windows' default for these). `ContentDeliveryAllowed`, the master
switch, is deliberately not touched: it also turns off things people use
(Spotlight pictures), and this rule's promise is "takes nothing away".

**Category: Auto** (safe tier; joins "Switch all N off"). **NotAPolicy.**

**Copy.** Title: "Windows shows suggested apps and tips, and can install
promoted apps on its own". Evidence: "Six values under Windows' content
delivery settings read as on or not set: suggested apps in Start, tips and
suggestions, lock-screen promotions, and silent installation of promoted
apps. brisk reads the six values and nothing past them; what Windows has
already installed this way is not something those reads can tell you.
Switching them off writes six values and can be undone." Fix: "Switch
suggestions and silent installs off (undoable)". Done: "Suggestions and
silent installs switched off". Advice key in the family's shape.

**Tests:** the family's table tests pick it up from the registry; add the
explicit six-path pin and the `ContentDeliveryAllowed`-untouched pin.

### 5. Security posture (SecurityPosture, no rule)

**Engine:** `SecurityPosture.Read(ctx)` → `SecurityPostureReport`, memoized
in `ctx.Memo` under `"posture"` so the two rules and the snapshot share one
read. Rows, each `bool?` with null = not read:

| Row | Read | Fine when |
|---|---|---|
| Firewall, domain / private / public | `HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{DomainProfile,StandardProfile,PublicProfile}\EnableFirewall` | each reads 1; absent = not read |
| Remote Desktop | `HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server\fDenyTSConnections` | reads 1 (connections denied); 0 = on; absent = not read |
| UAC prompts | the uac-prompts rule's own `IsOn` | not on |
| Defender real-time | `DefenderStatus.RealTimeOn` | true |
| Defender tamper protection | `DefenderStatus.TamperOn` | true |

**Snapshot:** `ScanSnapshot.Posture` (required, like `Sensors`: there is no
default that adds no claim).

**Page:** the Security tier opens with the posture block: one row per line
above, three visual states — fine, needs attention (the finding row sits
right below), not read (grey, "brisk could not read this"). Header
"Security"; sub-note: "brisk reads these values and grades none of them.
Fine means the value reads as Windows' safer setting; it says nothing about
what else is on this machine." The page title becomes "Privacy and security
on this machine"; the sidebar tile keeps its label.

**CLI:** after the findings, one line: "Checked and fine: firewall on for
all 3 profiles, Remote Desktop off, Defender real-time on, tamper protection
on." Rows that are not fine are already findings and are left out of the
line; rows not read are listed after: "Not read: Defender tamper
protection." `--json` carries the report as booleans and nulls.

**Report card:** untouched (scope).

**Tests:** each row's three states from planted registry values and fake
Defender shapes; memo hit (one read per scan); the CLI line for a
fixture of all-fine, one-bad, one-null; the snapshot requires it (a
constructor without it does not compile — no test needed, the type does it).

### Routing, strings, docs

- `PrivacyRuleIds.All` gains the four ids; the pin test moves from ten to
  fourteen. `FindingSections` is unchanged.
- `PrivacyViewModel` routes: `uac-prompts` and `defender-blind-spots` →
  `SecurityRows` (new); `camera-mic-use` → `DisclosureRows` or
  `UnreadableRows`; `windows-suggestions` → the safe switches. Read-back
  rows come from the family for the two switches.
- Every new key lands in both resx files; the parity test holds. Turkish
  copy is written in the same plain voice as the existing privacy strings.
- README (EN and TR): rule count 27 → 31; "ten privacy rules" → "fourteen
  privacy and security rules"; one bullet under "What it does": "Security
  posture, report first: whether Windows asks before a program takes
  control (one undoable fix), what Defender skips, which apps used the
  microphone and camera, firewall and Remote Desktop. Nothing here is
  graded, and no path or app name reaches anything you would share."
- `EngineInfo.Version` → 0.9.0 in the last commit of the wave; release notes
  lead with the five items and the one fix.

## Error handling, in one paragraph

Every read is guarded at the probe. A throw in a registry read costs that
value and not the scan (the rules above treat it as absent or not read, per
row). The Defender probe catches everything and returns null. `EngineHost`'s
catch-all still drops a finding whose rule throws, so no rule throws: the
tests plant a fake that throws on one key and assert the finding survives
with that key reported as not read.

## Fixture: the maintainer's machine, 2026-09-05

Read elevated; used as the field-test fixture the way
`HealthScoreTests.FieldTestMachine_ReadsEightyFive_NotFortyEight` uses a
real machine.

- UAC: `EnableLUA=1`, `ConsentPromptBehaviorAdmin=0`, `PromptOnSecureDesktop=0`
  → fires. After the fix: (1,5,1), read-back Held.
- Defender: 5 excluded paths (WMI and registry agree), real-time on, tamper
  on, mode Normal, quick scan 2 days old, full scan 21 days old, signature
  age 0 → fires on exclusions alone, headline "5 paths / Defender skips".
- Content delivery: all six values present and 1 → fires; after fix all 0.
- Microphone, last 30 days (window from 2026-09-05, so from 08-06): Chrome
  (08-11) and Wispr Flow (08-11, newest of 17 version entries) → 2 apps.
  WhatsApp (07-24), Sound Recorder (07-11), Edge WebView (07-24) and Unigram
  (03-26) fall outside the window and do not count. Camera: WhatsApp (09-01)
  → 1 app; the Camera app (07-14) and Teams are outside. The record also
  holds `Desktop\system-helper.exe` (mic, 2026-03-07) and
  `Desktop\SystemUIHelper.exe` (mic, 2026-02-22): outside the window, so
  brisk shows neither; they are noted here for the owner, not for the rule.
- Firewall 1/1/1, `fDenyTSConnections=1` → posture all fine except UAC.

## Order, cut line, gate

Build order is the numbered scope: foundations, uac-prompts, camera-mic-use,
defender-blind-spots, windows-suggestions, posture, then docs and version.
Each item is one commit on the branch with its tests, plain conventional
message. If Tuesday's post window arrives first, the branch stays where it
is; nothing merges before the whole-branch review (the same gate every wave
has had), a green full suite (1391 + the new tests, 0 warnings), and a live
`brisk scan` on the maintainer's machine read against the fixture above.
Merge and tag on his word.
