# startup-bloat fires at six or more startup entries; six inert values make
# the trigger self-sufficient on any machine.
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\startup-bloat.json'
if (Test-Path $state) { throw 'state file exists - restore first (double plant would lose the true original)' }
New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$names = 1..6 | ForEach-Object { "brisk-workbench-$_" }
foreach ($n in $names) {
    if ($null -ne (Get-ItemProperty $runKey -Name $n -ErrorAction SilentlyContinue).$n) {
        throw "value $n already exists - refusing to overwrite"
    }
}
@{ names = $names } | ConvertTo-Json | Set-Content $state -Encoding ascii
foreach ($n in $names) {
    Set-ItemProperty $runKey -Name $n -Value "$env:WINDIR\System32\cmd.exe /c rem brisk workbench" -Type String
}
Write-Host 'planted: six inert startup entries'
