$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\power-plan.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$prior = (Get-Content $state -Raw | ConvertFrom-Json).scheme
powercfg /setactive $prior
Remove-Item $state
Write-Host "restored: active scheme -> $prior"
