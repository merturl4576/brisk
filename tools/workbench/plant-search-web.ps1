# search-web-results fires when the policy value is ABSENT and the legacy
# Windows 10 switch is not 0 - so the plant removes the policy and, if the
# legacy switch is the thing holding web search off, flips it back on.
$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\search-web.json'
if (Test-Path $state) { throw 'state file exists - restore first (double plant would lose the true original)' }
New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
$policyKey = 'HKCU:\Software\Policies\Microsoft\Windows\Explorer'
$legacyKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search'
$policy = (Get-ItemProperty $policyKey -Name DisableSearchBoxSuggestions -ErrorAction SilentlyContinue).DisableSearchBoxSuggestions
$legacy = (Get-ItemProperty $legacyKey -Name BingSearchEnabled -ErrorAction SilentlyContinue).BingSearchEnabled

# Which levels of the policy path do not exist yet, deepest first. The plant
# creates none of them, but the restore has to create the leaf to put a
# recorded value back - and on a machine that never had an Explorer policy key
# this is the record that lets it clean up after itself. Same idiom as the
# storage-sense and visual-effects plants.
$invented = @()
$probe = $policyKey
while ($probe -and -not (Test-Path $probe)) { $invented += $probe; $probe = Split-Path $probe -Parent }

@{ policy = $policy; legacy = $legacy; invented = $invented } | ConvertTo-Json | Set-Content $state -Encoding ascii
if ($null -ne $policy) { Remove-ItemProperty $policyKey -Name DisableSearchBoxSuggestions }
if ($legacy -eq 0)     { Set-ItemProperty $legacyKey -Name BingSearchEnabled -Value 1 -Type DWord }
Write-Host 'planted: Start-menu web search re-enabled'
