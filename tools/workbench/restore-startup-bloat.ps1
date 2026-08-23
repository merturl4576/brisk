$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\startup-bloat.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$names = (Get-Content $state -Raw | ConvertFrom-Json).names
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
foreach ($n in $names) { Remove-ItemProperty $runKey -Name $n -ErrorAction SilentlyContinue }
Remove-Item $state
Write-Host 'restored: workbench startup entries removed'
