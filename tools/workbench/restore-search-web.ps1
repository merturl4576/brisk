$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\search-web.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$prior = Get-Content $state -Raw | ConvertFrom-Json
$policyKey = 'HKCU:\Software\Policies\Microsoft\Windows\Explorer'
$legacyKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search'

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

if ($null -ne $prior.policy) {
    # Never New-Item -Force an existing registry key: on this provider -Force
    # replaces the key, taking every other value and subkey in it with it.
    # Somebody's Explorer policies are not ours to delete.
    if (-not (Test-Path $policyKey)) { New-Item -Path $policyKey -Force | Out-Null }
    Set-ItemProperty $policyKey -Name DisableSearchBoxSuggestions -Value $prior.policy -Type DWord
} else {
    Remove-ItemProperty $policyKey -Name DisableSearchBoxSuggestions -ErrorAction SilentlyContinue
}

if ($null -ne $prior.legacy) {
    Set-ItemProperty $legacyKey -Name BingSearchEnabled -Value $prior.legacy -Type DWord
} else {
    Remove-ItemProperty $legacyKey -Name BingSearchEnabled -ErrorAction SilentlyContinue
}

# "Put back exactly" is a claim about the registry, not about the script having
# reached its last line. Both values are read back and compared to the record
# before it is announced, and before the record is deleted.
$nowPolicy = Read-Value $policyKey 'DisableSearchBoxSuggestions'
$nowLegacy = Read-Value $legacyKey 'BingSearchEnabled'
if ($nowPolicy -ne $prior.policy) {
    throw "read-back failed: DisableSearchBoxSuggestions is $(Show $nowPolicy), the record says $(Show $prior.policy) - state file kept"
}
if ($nowLegacy -ne $prior.legacy) {
    throw "read-back failed: BingSearchEnabled is $(Show $nowLegacy), the record says $(Show $prior.legacy) - state file kept"
}

Remove-Item $state
Write-Host 'restored: search-web values put back exactly'
