$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\startup-bloat.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$names = (Get-Content $state -Raw | ConvertFrom-Json).names
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

# This is the only restore in the suite that deletes rather than writes back,
# so it is the only one that can destroy something the plant never created. An
# edited state file naming "OneDrive" would take a real startup entry out, and
# the SilentlyContinue below guarantees it would do so without a word. Only the
# shape the plant writes is accepted.
foreach ($n in $names) {
    if ($n -notmatch '^brisk-workbench-\d+$') {
        throw "state file names a value the workbench never planted: $n"
    }
}

foreach ($n in $names) { Remove-ItemProperty $runKey -Name $n -ErrorAction SilentlyContinue }
Remove-Item $state
Write-Host 'restored: workbench startup entries removed'
