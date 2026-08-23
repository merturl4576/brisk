$ErrorActionPreference = 'Stop'
$state = Join-Path $PSScriptRoot '.state\visual-effects.json'
if (-not (Test-Path $state)) { throw 'no state file - nothing was planted' }
$prior = Get-Content $state -Raw | ConvertFrom-Json
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects'
if ($null -ne $prior.value) { Set-ItemProperty $key -Name VisualFXSetting -Value $prior.value -Type DWord }
else {
    Remove-ItemProperty $key -Name VisualFXSetting -ErrorAction SilentlyContinue
    if (-not $prior.keyExisted -and (Test-Path $key)) {
        $k = Get-Item $key
        if ($k.ValueCount -eq 0 -and $k.SubKeyCount -eq 0) { Remove-Item $key }
    }
}
Remove-Item $state
Write-Host 'restored: VisualFXSetting put back exactly'
