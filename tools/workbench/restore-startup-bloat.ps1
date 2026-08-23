$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\startup-bloat.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$names = (Get-Content $state -Raw | ConvertFrom-Json).names
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

# This is the only restore in the suite that deletes rather than writes back,
# so it is the only one that can destroy something the plant never created. An
# edited state file naming "OneDrive" would take a real startup entry out, and
# the SilentlyContinue below guarantees it would do so without a word. Only the
# shape the plant writes is accepted, and every name is checked before any
# value is touched, so a half-valid file cannot delete its valid half.
foreach ($n in $names) {
    if ($n -notmatch '^brisk-workbench-\d+$') {
        throw "state file names a value the workbench never planted: $n"
    }
}

foreach ($n in $names) { Remove-ItemProperty $runKey -Name $n -ErrorAction SilentlyContinue }

# SilentlyContinue swallows more than "already gone" - an ACL or a locked key
# fails just as quietly. Announcing a removal that did not happen would leave
# six entries that really do run at the next logon, with the only record of
# their names deleted.
#
# The read-back has to be able to fail too. Registry permissions are per-key,
# so whatever could stop the delete can equally stop the read, and a read that
# answers $null for "gone" and for "you may not look" would call those six
# survivors removed. OpenSubKey throws instead of guessing.
try { $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKey.Substring($runKey.IndexOf('\') + 1)) }
catch { throw "could not open $runKey to verify the removal ($($_.Exception.Message)) - state file kept" }
if ($null -eq $key) { throw "could not open $runKey to verify the removal (the key is gone) - state file kept" }
try { $still = @($names | Where-Object { $null -ne $key.GetValue($_, $null) }) }
finally { $key.Close() }
if ($still.Count -gt 0) { throw "could not remove: $($still -join ', ') - state file kept" }

Remove-Item $state
Write-Host 'restored: workbench startup entries removed'
