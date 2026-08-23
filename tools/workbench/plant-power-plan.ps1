# power-plan fires when the active scheme is Balanced or Power saver.
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\power-plan.json'
if (Test-Path $state) { throw 'state file exists - restore first (double plant would lose the true original)' }
New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
$active = (powercfg /getactivescheme) -replace '^.*GUID:\s*([0-9a-f-]+).*$', '$1'
@{ scheme = $active.Trim() } | ConvertTo-Json | Set-Content $state -Encoding ascii
powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e
Write-Host "planted: active scheme -> Balanced (was $($active.Trim()))"
