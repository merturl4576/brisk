$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\visual-effects.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$prior = Get-Content $state -Raw | ConvertFrom-Json
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects'
if ($null -ne $prior.value) { Set-ItemProperty $key -Name VisualFXSetting -Value $prior.value -Type DWord }
else {
    Remove-ItemProperty $key -Name VisualFXSetting -ErrorAction SilentlyContinue
    # Keys the plant invented are not part of "byte-identical" either. Deepest
    # first, and each only while it is still as empty as the plant left it.
    foreach ($k in @($prior.invented | Where-Object { $_ })) {
        if (-not (Test-Path $k)) { continue }
        $item = Get-Item $k
        if ($item.ValueCount -eq 0 -and $item.SubKeyCount -eq 0) { Remove-Item $k }
    }
}

# Read the value back before claiming it was put back, and before deleting the
# only copy of what it is supposed to be.
$now = (Get-ItemProperty $key -Name VisualFXSetting -ErrorAction SilentlyContinue).VisualFXSetting
function Show($v) { if ($null -eq $v) { 'absent' } else { "$v" } }
if ($now -ne $prior.value) {
    throw "read-back failed: VisualFXSetting is $(Show $now), the record says $(Show $prior.value) - state file kept"
}

Remove-Item $state
Write-Host 'restored: VisualFXSetting put back exactly'
