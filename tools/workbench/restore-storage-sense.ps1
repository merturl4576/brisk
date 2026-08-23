$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\storage-sense.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$prior = Get-Content $state -Raw | ConvertFrom-Json
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy'
if ($null -ne $prior.value) { Set-ItemProperty $key -Name '01' -Value $prior.value -Type DWord }
else {
    Remove-ItemProperty $key -Name '01' -ErrorAction SilentlyContinue
    # Keys the plant invented are not part of "byte-identical" either. Deepest
    # first, and each only while it is still as empty as the plant left it -
    # anything that moved in since belongs to somebody else.
    foreach ($k in @($prior.invented | Where-Object { $_ })) {
        if (-not (Test-Path $k)) { continue }
        $item = Get-Item $k
        if ($item.ValueCount -eq 0 -and $item.SubKeyCount -eq 0) { Remove-Item $k }
    }
}
Remove-Item $state
Write-Host 'restored: Storage Sense value put back exactly'
