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

# "Put back exactly" is a claim about the registry, not about the script having
# reached its last line. Read the value back and compare it to the record
# before saying so, and before deleting the only copy of that record.
$now = (Get-ItemProperty $key -Name '01' -ErrorAction SilentlyContinue).'01'
function Show($v) { if ($null -eq $v) { 'absent' } else { "$v" } }
if ($now -ne $prior.value) {
    throw "read-back failed: '01' is $(Show $now), the record says $(Show $prior.value) - state file kept"
}

Remove-Item $state
Write-Host 'restored: Storage Sense value put back exactly'
