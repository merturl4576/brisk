### What changed in 0.11.0

Until 0.10, brisk only diagnosed. Outside testers installed it, clicked, and felt nothing. 0.11 is the rebuild: one click frees space, takes programs off startup and switches telemetry off, then shows what changed with numbers Windows itself reports.

- **A new window.** Home is a live view of the machine: CPU, graphics and memory gauges, temperature, network, battery on a laptop, the machine's specs and the apps using the most right now. A strip at the bottom says what brisk found, next to **Do it all**.
- **Three pages, one button each.** Free up space, Speed up and Privacy list every row before anything runs. Tap a row to skip it.
- **Free up space in three levels.** MIN is caches and leftovers that rebuild themselves or are never needed again, and it is all that Do it all runs. MID and MAX go further, and every row that cannot come back says so on the row, in amber. New in this release: every browser profile's cache, game shader caches, old kernel dumps, Windows setup logs, extracted driver installers, dependency folders of old projects, archives already extracted beside them, a git repository in your home folder, and old restore points.
- **Speed up.** Startup programs go off the way Task Manager does it, except the ones you would miss (antivirus, audio, sync clients, password managers, VPNs and others, each listed with its reason), plus switches such as full power while plugged in and window animations off.
- **Privacy.** Usage data sent to Microsoft, background data-collection tasks, the advertising ID, Start menu ads, web results in Start search, Recall, Copilot and more. Each switch is read back after the run: **Off ✓**, **Windows ignored it** or **Turned back on**.
- **The receipt.** Free space before and after, enabled startup programs before and after, Windows' own boot time for the first boot after the run, and the RAM the switched-off startup programs were using, counted only once the next boot confirms none of them started.
- **Undo.** Every setting is written to a journal before it is changed, so every setting and startup change of a run can be undone, newest first. Deleted files do not come back, and brisk says so.
- **In a terminal:** `brisk run` prints the plan and changes nothing (`--yes` applies it, `--level mid|max`, `--group space|speed|privacy`), `brisk undo`, `brisk receipt`, and `brisk live`: the apps using the most, which app is connected to which company right now, and Windows' own 30-day per-app record of bytes sent and received.

brisk still opens no network connection, has no account and no AI in the product. 1,788 tests. The notes for earlier releases are on their own release pages.

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
| `brisk-app.exe` | The app. Asks for administrator, because reading hardware sensors and changing system settings need it. |
| `brisk.exe` | The same rows from a terminal (`run`, `undo`, `receipt`, `live`). No elevation prompt of its own. |
| `SHA256SUMS.txt` | Checksums for both executables. |

Neither file needs .NET installed and neither needs the other. Nothing on your machine changes until you press **Do it all**, a page's own button, or run `brisk run --yes`. Every setting change is recorded so it can be undone; deleted files are gone.

**Not code-signed yet**, so SmartScreen will warn on first run. That warning is honest. Verify the checksum rather than trusting this sentence.
