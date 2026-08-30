### Install with one line

```powershell
irm https://raw.githubusercontent.com/merturl4576/brisk/main/get.ps1 | iex
```

The script downloads `brisk-win-x64.zip`, refuses to run it unless its SHA-256 matches the digest published below, and unpacks into one folder. Uninstall = delete the folder.

### Downloads

| File | What it is |
|---|---|
| `brisk-win-x64.zip` | Both executables in one archive — what the one-line installer uses. |
| `brisk-win-x64.zip.sha256` | The digest the installer verifies before anything runs. |
| `brisk-app.exe` | The app. Asks for administrator, because reading hardware sensors needs it. |
| `brisk.exe` | The same diagnostics from a terminal, running as you. No elevation prompt. |
| `SHA256SUMS.txt` | Checksums for both executables. |

Neither file needs .NET installed and neither needs the other. Nothing is written to your machine until you tell brisk to fix something, and every fix is recorded so it can be undone.

**Not code-signed yet**, so SmartScreen will warn on first run. That warning is honest — verify the checksum rather than trusting this sentence.
