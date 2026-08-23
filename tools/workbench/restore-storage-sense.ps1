$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\storage-sense.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$prior = Get-Content $state -Raw | ConvertFrom-Json
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy'
if ($null -ne $prior.value) { Set-ItemProperty $key -Name '01' -Value $prior.value -Type DWord }
else {
    Remove-ItemProperty $key -Name '01' -ErrorAction SilentlyContinue
    # A key the plant invented is not part of "byte-identical" either, so it
    # goes back too - but only while it is still as empty as the plant left it.
    if (-not $prior.keyExisted -and (Test-Path $key)) {
        $k = Get-Item $key
        if ($k.ValueCount -eq 0 -and $k.SubKeyCount -eq 0) { Remove-Item $key }
    }
}
Remove-Item $state
Write-Host 'restored: Storage Sense value put back exactly'
