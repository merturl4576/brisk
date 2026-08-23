$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\power-plan.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$prior = (Get-Content $state -Raw | ConvertFrom-Json).scheme

# A state file is only worth what it holds. Anything that is not a GUID cannot
# be handed to powercfg, and discovering that after the file is deleted is how
# somebody ends up parked on Balanced with no record of where they started.
if ($prior -notmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$') {
    throw "state file does not hold a scheme GUID: '$prior' - state file kept"
}

powercfg /setactive $prior
# $ErrorActionPreference does not reach native executables. Every other restore
# here fails loudly and keeps its state file; this one has to as well. A
# deleted scheme, or a "Select an active power plan" policy, would otherwise
# leave a stranger on Balanced, told they were put back, with the only record
# of their real scheme gone.
if ($LASTEXITCODE -ne 0) { throw "powercfg refused $prior (exit $LASTEXITCODE) - state file kept" }
Remove-Item $state
Write-Host "restored: active scheme -> $prior"
