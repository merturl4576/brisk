$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\visual-effects.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$prior = Get-Content $state -Raw | ConvertFrom-Json
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects'

# A read that answers $null for "the value is not there" AND for "you may not
# look" makes the check below a rubber stamp: the same ACL that could defeat
# the write would hide its own failure, and the restore would announce a change
# it never verified. OpenSubKey separates the two - $null only when the key
# genuinely does not exist, an exception when it exists and cannot be opened.
#
# The hive is taken from the path, never assumed. An earlier version stripped
# the prefix and then opened HKCU whatever the caller passed, so asking it for
# an HKLM path quietly read the HKCU key of the same name, found nothing, and
# answered "absent" - the very fail-open shape this read exists to remove, one
# level up in the thing removing it. HKLM is listed rather than refused because
# proving this helper works needs a key that exists and cannot be read, and
# HKLM\SECURITY is the one every Windows has.
function Read-Value([string] $path, [string] $name) {
    $root = switch -Wildcard ($path) {
        'HKCU:\*' { [Microsoft.Win32.Registry]::CurrentUser }
        'HKLM:\*' { [Microsoft.Win32.Registry]::LocalMachine }
        default   { throw "Read-Value does not handle this registry root: $path" }
    }
    $sub = $path.Substring($path.IndexOf('\') + 1)
    try { $k = $root.OpenSubKey($sub) }
    catch { throw "could not read $path to verify the restore ($($_.Exception.Message)) - state file kept" }
    if ($null -eq $k) { return $null }
    try { return $k.GetValue($name, $null) } finally { $k.Close() }
}
function Show($v) { if ($null -eq $v) { 'absent' } else { "$v" } }

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
$now = Read-Value $key 'VisualFXSetting'
if ($now -ne $prior.value) {
    throw "read-back failed: VisualFXSetting is $(Show $now), the record says $(Show $prior.value) - state file kept"
}

Remove-Item $state
Write-Host 'restored: VisualFXSetting put back exactly'
