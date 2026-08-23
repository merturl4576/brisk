# power-plan fires when the active scheme is Balanced or Power saver.
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\power-plan.json'
if (Test-Path $state) { throw 'state file exists - restore first (double plant would lose the true original)' }

# Match, never -replace. -replace hands back the whole input unchanged when the
# pattern misses, so on a Windows whose powercfg label is translated the state
# file would quietly record a sentence instead of a GUID - and the restore
# would feed that sentence back to powercfg. "I could not read where this
# machine started" has exactly one safe answer, and it is not to plant anyway.
if ((powercfg /getactivescheme | Out-String) -match
    '([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})') {
    $active = $Matches[1]
} else {
    throw 'could not read the active scheme GUID - not planting'
}

New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
@{ scheme = $active } | ConvertTo-Json | Set-Content $state -Encoding ascii
powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e
# $ErrorActionPreference does not reach native executables, so powercfg can
# fail with the script none the wiser. A state file for a plant that never
# happened would refuse the next plant for no reason.
if ($LASTEXITCODE -ne 0) {
    Remove-Item $state
    throw "powercfg refused to switch to Balanced (exit $LASTEXITCODE) - nothing planted"
}
Write-Host "planted: active scheme -> Balanced (was $active)"
