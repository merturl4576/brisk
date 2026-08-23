# startup-bloat fires at six or more startup entries, OR at a single entry
# whose name StartupManager.IsHeavy recognises - so most real machines are
# already firing before the plant runs. Six added values make the count half
# of the trigger self-sufficient on a machine that is not.
#
# The values are harmless but not inert: they are real Run entries and they
# really do execute at the next logon. Reboot while planted and six console
# windows flash past running `cmd /c rem`. Restore before you reboot.
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
Write-Host 'planted: six startup entries (they run at next logon - restore before you reboot)'
