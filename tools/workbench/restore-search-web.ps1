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
}
if ($null -ne $prior.legacy) {
    Set-ItemProperty $legacyKey -Name BingSearchEnabled -Value $prior.legacy -Type DWord
} else {
    Remove-ItemProperty $legacyKey -Name BingSearchEnabled -ErrorAction SilentlyContinue
}
Remove-Item $state
Write-Host 'restored: search-web values put back exactly'
