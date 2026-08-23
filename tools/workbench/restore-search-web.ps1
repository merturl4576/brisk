$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\search-web.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$prior = Get-Content $state -Raw | ConvertFrom-Json
$policyKey = 'HKCU:\Software\Policies\Microsoft\Windows\Explorer'
$legacyKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search'

if ($null -ne $prior.policy) {
    # Never New-Item -Force an existing registry key: on this provider -Force
    # replaces the key, taking every other value and subkey in it with it.
    # Somebody's Explorer policies are not ours to delete.
    if (-not (Test-Path $policyKey)) { New-Item -Path $policyKey -Force | Out-Null }
    Set-ItemProperty $policyKey -Name DisableSearchBoxSuggestions -Value $prior.policy -Type DWord
} else {
    Remove-ItemProperty $policyKey -Name DisableSearchBoxSuggestions -ErrorAction SilentlyContinue
    # The value was absent when we started, so any level of this path that was
    # also absent then should be absent now. Deepest first, and each only while
    # it is still empty - anything that moved in since belongs to somebody else.
    foreach ($k in @($prior.invented | Where-Object { $_ })) {
        if (-not (Test-Path $k)) { continue }
        $item = Get-Item $k
        if ($item.ValueCount -eq 0 -and $item.SubKeyCount -eq 0) { Remove-Item $k }
    }
}

if ($null -ne $prior.legacy) {
    Set-ItemProperty $legacyKey -Name BingSearchEnabled -Value $prior.legacy -Type DWord
} else {
    Remove-ItemProperty $legacyKey -Name BingSearchEnabled -ErrorAction SilentlyContinue
}

# "Put back exactly" is a claim about the registry, not about the script having
# reached its last line. Both values are read back and compared to the record
# before it is announced, and before the record is deleted.
function Show($v) { if ($null -eq $v) { 'absent' } else { "$v" } }
$nowPolicy = (Get-ItemProperty $policyKey -Name DisableSearchBoxSuggestions -ErrorAction SilentlyContinue).DisableSearchBoxSuggestions
$nowLegacy = (Get-ItemProperty $legacyKey -Name BingSearchEnabled -ErrorAction SilentlyContinue).BingSearchEnabled
if ($nowPolicy -ne $prior.policy) {
    throw "read-back failed: DisableSearchBoxSuggestions is $(Show $nowPolicy), the record says $(Show $prior.policy) - state file kept"
}
if ($nowLegacy -ne $prior.legacy) {
    throw "read-back failed: BingSearchEnabled is $(Show $nowLegacy), the record says $(Show $prior.legacy) - state file kept"
}

Remove-Item $state
Write-Host 'restored: search-web values put back exactly'
