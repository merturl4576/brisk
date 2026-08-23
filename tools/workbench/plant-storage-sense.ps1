# storage-sense fires when C: is under 15% free AND the master toggle (value
# '01') is not 1. Both halves are the rule: on a machine with room to spare,
# turning the toggle off is not enough to make the finding appear, and that is
# the rule working correctly rather than the plant failing. See README.
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\storage-sense.json'
if (Test-Path $state) { throw 'state file exists - restore first (double plant would lose the true original)' }
New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy'
$prior = (Get-ItemProperty $key -Name '01' -ErrorAction SilentlyContinue).'01'

# Which levels of this path are about to be invented? On a machine that never
# opened Storage Sense the plant creates StorageSense, Parameters AND
# StoragePolicy, and removing only the leaf would leave two keys behind that
# the machine never had. Deepest first; the walk stops at the first key that
# already exists.
$invented = @()
$probe = $key
while ($probe -and -not (Test-Path $probe)) { $invented += $probe; $probe = Split-Path $probe -Parent }
@{ value = $prior; invented = $invented } | ConvertTo-Json | Set-Content $state -Encoding ascii

# Never New-Item -Force an existing registry key: on this provider -Force
# replaces the key, and StoragePolicy holds every Storage Sense schedule
# beside '01'. The key is created only when it is genuinely absent.
if ($invented.Count -gt 0) { New-Item -Path $key -Force | Out-Null }
Set-ItemProperty $key -Name '01' -Value 0 -Type DWord
Write-Host "planted: Storage Sense off (was: $(if ($null -eq $prior) { 'absent' } else { $prior }))"
