# visual-effects fires when VisualFXSetting is 1 (best appearance).
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\visual-effects.json'
if (Test-Path $state) { throw 'state file exists - restore first (double plant would lose the true original)' }
New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects'
$prior = (Get-ItemProperty $key -Name VisualFXSetting -ErrorAction SilentlyContinue).VisualFXSetting

# Which levels of this path are about to be invented, deepest first. The walk
# stops at the first key that already exists, so on a stock Windows this is
# empty and nothing is created at all.
$invented = @()
$probe = $key
while ($probe -and -not (Test-Path $probe)) { $invented += $probe; $probe = Split-Path $probe -Parent }
@{ value = $prior; invented = $invented } | ConvertTo-Json | Set-Content $state -Encoding ascii

# Never New-Item -Force an existing registry key: on this provider -Force
# replaces the key, and VisualEffects carries a subkey per individual effect -
# nineteen of them on a stock Windows 11. The key is created only when absent.
if ($invented.Count -gt 0) { New-Item -Path $key -Force | Out-Null }
Set-ItemProperty $key -Name VisualFXSetting -Value 1 -Type DWord
Write-Host "planted: visual effects -> best appearance (was: $(if ($null -eq $prior) { 'absent' } else { $prior }))"
