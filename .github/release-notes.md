### Downloads

| File | What it is |
|---|---|
| `brisk-app.exe` | The app. Asks for administrator, because reading hardware sensors needs it. |
| `brisk.exe` | The same diagnostics from a terminal, running as you. No elevation prompt. |
| `SHA256SUMS.txt` | Checksums for both files. |

Neither file needs .NET installed and neither needs the other. Nothing is written to your machine until you tell brisk to fix something, and every fix is recorded so it can be undone.

**Not code-signed yet**, so SmartScreen will warn on first run. That warning is honest — verify the checksum rather than trusting this sentence.
