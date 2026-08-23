# Runs brisk and answers one question: did the expected rule fire?
param(
    [Parameter(Mandatory)] [string] $RuleId,
    [switch] $ExpectClean,
    # Left empty on purpose: $PSScriptRoot is not yet bound while parameter
    # defaults are evaluated, so the default is computed below instead.
    # (scripts/publish.ps1 carries the same note for the same reason.)
    [string] $BriskExe = ''
)
$ErrorActionPreference = 'Stop'
if (-not $BriskExe) {
    $BriskExe = Join-Path (Split-Path -Parent $PSCommandPath) '..\..\artifacts\brisk.exe'
}
if (-not (Test-Path $BriskExe)) { throw "brisk exe not found: $BriskExe (pass -BriskExe)" }

$raw = & $BriskExe scan --json | Out-String
# -ExpectClean is the step that proves the restore worked, and it passes on an
# ABSENCE. Every way of failing to produce findings - a non-zero exit, an error
# payload, a renamed member - looks exactly like "the rule is silent", so the
# absence is worth nothing until the shape around it has been checked.
if ($LASTEXITCODE -ne 0) { throw "brisk scan exited $LASTEXITCODE - not a verdict" }
$json = $raw | ConvertFrom-Json
if ($null -eq $json.PSObject.Properties['findings']) {
    throw 'scan --json produced no findings member - not a verdict'
}

$fired = @($json.findings | Where-Object { $_.RuleId -eq $RuleId }).Count -gt 0
if ($fired -and -not $ExpectClean) { Write-Host "OK: $RuleId fired."; exit 0 }
if (-not $fired -and $ExpectClean) { Write-Host "OK: $RuleId is clean."; exit 0 }
if ($fired) { Write-Host "FAIL: $RuleId still fires."; exit 1 }
Write-Host "FAIL: $RuleId did not fire."; exit 1
