# brisk'i indirir ve baslatir — kurulum yok, tek klasor, silmek = klasoru silmek.
#
# Kullanim (duyuruda verilecek tek satir):
#   irm https://raw.githubusercontent.com/merturl4576/brisk/main/get.ps1 | iex
#
# Bu dosya BILEREK kisa ve okunur: "irm | iex" tarzina gelen hakli elestiri,
# insanlarin ne calistirdigini gormeden calistirmasidir. Goren gozler icin
# butun is asagida; gormek istemeyenler icin de indirilen arsivin SHA256'si,
# release'in yaninda yayimlanan ozetle KARSILASTIRILIR — tutmazsa calismaz.
$ErrorActionPreference = 'Stop'
# Eski Windows PowerShell kurulumlari TLS 1.0 ile acilir; GitHub reddeder.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
# PS 5.1'de Invoke-WebRequest, ilerleme cubugunu cizerken indirmeyi
# onlarca kat yavaslatir; 94 MB'lik zip icin fark dakikalardir.
$ProgressPreference = 'SilentlyContinue'

$repo = 'merturl4576/brisk'
$dir  = Join-Path $env:LOCALAPPDATA 'brisk'

$rel   = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest"
$asset = $rel.assets | Where-Object name -like 'brisk-win-x64*.zip' | Select-Object -First 1
if (-not $asset) { throw 'brisk: son release icinde brisk-win-x64*.zip bulunamadi' }

$zip = Join-Path $env:TEMP $asset.name
Invoke-WebRequest $asset.browser_download_url -OutFile $zip

# Ozet dogrulamasi: release her zip'in yaninda "<ad>.zip.sha256" yayimlar.
# Dosyaya indirilip oyle okunur: GitHub varliklari octet-stream olarak servis
# eder ve Invoke-RestMethod o zaman string degil Byte[] dondurur.
$shaAsset = $rel.assets | Where-Object name -eq ($asset.name + '.sha256')
if (-not $shaAsset) { throw 'brisk: release ozet dosyasini tasimiyor; bu betik dogrulamadan kurmaz' }
$shaFile = Join-Path $env:TEMP $shaAsset.name
Invoke-WebRequest $shaAsset.browser_download_url -OutFile $shaFile
$expected = (Get-Content $shaFile -Raw).Trim().Split(' ')[0].ToUpperInvariant()
Remove-Item $shaFile
$actual   = (Get-FileHash $zip -Algorithm SHA256).Hash.ToUpperInvariant()
if ($actual -ne $expected) {
    throw "brisk: indirilen dosyanin ozeti release'in yayimladigiyla tutmadi (beklenen $expected, gelen $actual)"
}

Expand-Archive $zip -DestinationPath $dir -Force
Remove-Item $zip
Start-Process (Join-Path $dir 'brisk-app.exe')   # UAC istemini brisk'in kendisi acar
Write-Host "brisk kuruldu: $dir  (kaldirmak = bu klasoru silmek)"
