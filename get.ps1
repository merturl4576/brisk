# Downloads brisk and starts it. No installer, one folder; uninstall = delete the folder.
#
# Usage (the one line the announcement gives):
#   irm https://raw.githubusercontent.com/merturl4576/brisk/main/get.ps1 | iex
#
# This file is SHORT AND READABLE ON PURPOSE. The fair objection to "irm | iex"
# is that people run what they have not read. For those who look, the whole job
# is below; for those who do not, the downloaded archive's SHA-256 is COMPARED
# with the digest published beside the release. No match, no run.
$ErrorActionPreference = 'Stop'
# Older Windows PowerShell installs open with TLS 1.0, which GitHub refuses.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
# In PS 5.1 Invoke-WebRequest draws a progress bar that slows the download
# tens of times over; on a 94 MB zip that is minutes.
$ProgressPreference = 'SilentlyContinue'

$repo = 'merturl4576/brisk'
$dir  = Join-Path $env:LOCALAPPDATA 'brisk'

$rel   = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest"
$asset = $rel.assets | Where-Object name -like 'brisk-win-x64*.zip' | Select-Object -First 1
if (-not $asset) { throw 'brisk: the latest release carries no brisk-win-x64*.zip' }

$zip = Join-Path $env:TEMP $asset.name
Invoke-WebRequest $asset.browser_download_url -OutFile $zip

# Digest check: the release publishes "<name>.zip.sha256" beside every zip.
# Downloaded to a file and read from there: GitHub serves assets as
# octet-stream, and Invoke-RestMethod then returns Byte[] rather than a string.
$shaAsset = $rel.assets | Where-Object name -eq ($asset.name + '.sha256')
if (-not $shaAsset) { throw 'brisk: the release carries no digest file; this script does not install unverified' }
$shaFile = Join-Path $env:TEMP $shaAsset.name
Invoke-WebRequest $shaAsset.browser_download_url -OutFile $shaFile
$expected = (Get-Content $shaFile -Raw).Trim().Split(' ')[0].ToUpperInvariant()
Remove-Item $shaFile
$actual   = (Get-FileHash $zip -Algorithm SHA256).Hash.ToUpperInvariant()
if ($actual -ne $expected) {
    throw "brisk: the download's digest does not match the one the release published (expected $expected, got $actual)"
}

Expand-Archive $zip -DestinationPath $dir -Force
Remove-Item $zip
Write-Host "brisk installed: $dir  (to uninstall, delete this folder)"

# Launching is a separate job and fails separately: "Don't run" on the
# SmartScreen prompt, "No" to UAC, or Smart App Control, which stops an
# unsigned exe outright with no "run anyway". All three arrive as the same
# sentence, "The operation was canceled by the user". The install is done;
# the script must not die here, and the user must see where things stand.
try {
    Start-Process (Join-Path $dir 'brisk-app.exe')   # brisk raises the UAC prompt itself
} catch {
    Write-Host "brisk could not be launched: $($_.Exception.Message)"
    Write-Host "Open it by hand: $dir\brisk-app.exe  (answer Yes to the UAC prompt)"
    # Windows 11 Smart App Control: 1 = on. While it is on, unsigned brisk does
    # not run at all; turning it off is one-way, there is no way back short of
    # reinstalling Windows.
    $ci  = 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy'
    $sac = (Get-ItemProperty $ci -ErrorAction SilentlyContinue).VerifiedAndReputablePolicyState
    if ($sac -eq 1) {
        Write-Host 'Smart App Control is on: Windows stops an unsigned program without asking. It is switched off under Settings > Privacy & security > Windows Security > App & browser control, and once off it cannot be switched back on. Do not turn it off for brisk; wait for the signed build. The README explains.'
    }
}
