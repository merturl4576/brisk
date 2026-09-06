### What changed in 0.9.0

- **Windows asks before a program takes control.** A new rule reads the three UAC values and reports when one of them has been written to elevate silently — the change an "optimizer" makes and never mentions. The fix restores Windows' own defaults, asks before it acts because you will see prompts again, and is undoable; every later scan re-reads it and says whether it held.
- **Which apps used the microphone and camera.** A report-only finding counts the apps that used each in the last 30 days, from the record Windows already keeps. The names and dates are on the Privacy page and the paths sit under them; nothing is graded and nothing is changed.
- **What Defender is told to skip.** A report-only finding reads Defender's exclusion list, real-time protection, tamper protection, running mode and scan age, with a link to Windows Security. Unelevated it cannot read the exclusion list, and it says so rather than reporting a zero.
- **Windows' suggestion and silent-install flags.** Six content-delivery values — the suggested apps, the tips, the silently installed ones — read as one switch, with a one-click fix that costs nothing visible.
- **The lines that are fine, said once.** A posture block leads the Privacy page and `brisk scan` prints one line: firewall per profile, Remote Desktop, UAC, Defender real-time and tamper protection. A value brisk could not read is never shown as fine.

Nothing on the Privacy page moves the score, and no path or app name reaches the report card, the JSON, or anything built to be shared.

### What changed in 0.8.0

- **The largest files in your profile, named.** A new report-only finding lists the ten biggest files (500 MB and up) under your profile, with size and date, and leads the Overview when the largest is 1 GB or more. brisk names them and touches none of them.
- **The health score stops rewarding switch flips.** Only findings brisk can measure the other side of (boot timings, refresh rate, days until the disk is full) move the gauge far. A power plan or visual-effects flip now costs 2 points, whatever its stars. Settings flips alone once moved the score 43 points while nothing brisk could measure had changed; that is the reason.
- **The power-plan rule stops overclaiming.** It fires only on a desktop that has a performance plan to switch to, never on a machine with a battery, as a Warning that asks first instead of a Critical one-click, and its text no longer says "throttling": brisk has no measurement that anyone feels a power plan.
- **Delivery Optimization cache actually clears.** 0.7.0 promised gigabytes and delivered 0 B, because that cache lives under a service profile the Recycle Bin cannot take. brisk now uses the past-the-bin path Windows provides and waits for the service to finish before it counts.
- **The component-store step reports what it measured, or nothing.** No more "0 B" after a long DISM run that freed space brisk did not observe.
- **Errors reach you as words.** Shell result codes and refused rights are explained instead of printed raw.
- **Small fixes:** the orphan detector no longer reads "PyCharm Community" as Unity; the Clean page stops warning an administrator about administrators; the report card stops listing fixes under the problems they cured; the read-back says "today" and "yesterday"; the lifetime counter moves with the banner.

### Install with one line

```powershell
irm https://raw.githubusercontent.com/merturl4576/brisk/main/get.ps1 | iex
```

The script downloads `brisk-win-x64.zip`, refuses to run it unless its SHA-256 matches the digest published below, and unpacks into one folder. Uninstall = delete the folder.

### Downloads

| File | What it is |
|---|---|
| `brisk-win-x64.zip` | Both executables in one archive, what the one-line installer uses. |
| `brisk-win-x64.zip.sha256` | The digest the installer verifies before anything runs. |
| `brisk-app.exe` | The app. Asks for administrator, because reading hardware sensors needs it. |
| `brisk.exe` | The same diagnostics from a terminal, running as you. No elevation prompt. |
| `SHA256SUMS.txt` | Checksums for both executables. |

Neither file needs .NET installed and neither needs the other. Nothing is written to your machine until you tell brisk to fix something, and every fix is recorded so it can be undone.

**Not code-signed yet**, so SmartScreen will warn on first run. That warning is honest. Verify the checksum rather than trusting this sentence.
