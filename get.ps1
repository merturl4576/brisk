# brisk'i indirir ve baslatir — kurulum yok, tek klasor, silmek = klasoru silmek.
#
# Kullanim (duyuruda verilecek tek satir):
#   irm https://raw.githubusercontent.com/<KULLANICI>/brisk/main/get.ps1 | iex
#
# Bu dosya BILEREK kisa ve okunur: "irm | iex" tarzina gelen hakli elestiri,
# insanlarin ne calistirdigini gormeden calistirmasidir. Goren gozler icin
# butun is asagida; gormek istemeyenler icin de indirilen arsivin SHA256'si,
# release'in yaninda yayimlanan ozetle KARSILASTIRILIR — tutmazsa calismaz.
$ErrorActionPreference = 'Stop'

$repo = '<KULLANICI>/brisk'   # duyurudan once doldurulacak
$dir  = Join-Path $env:LOCALAPPDATA 'brisk'

$rel   = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest"
$asset = $rel.assets | Where-Object name -like 'brisk-win-x64*.zip' | Select-Object -First 1
if (-not $asset) { throw 'brisk: son release icinde brisk-win-x64*.zip bulunamadi' }

$zip = Join-Path $env:TEMP $asset.name
Invoke-WebRequest $asset.browser_download_url -OutFile $zip

# Ozet dogrulamasi: release her zip'in yaninda "<ad>.zip.sha256" yayimlar.
$shaAsset = $rel.assets | Where-Object name -eq ($asset.name + '.sha256')
if (-not $shaAsset) { throw 'brisk: release ozet dosyasini tasimiyor; bu betik dogrulamadan kurmaz' }
$expected = (Invoke-RestMethod $shaAsset.browser_download_url).Trim().Split(' ')[0].ToUpperInvariant()
$actual   = (Get-FileHash $zip -Algorithm SHA256).Hash.ToUpperInvariant()
if ($actual -ne $expected) {
    throw "brisk: indirilen dosyanin ozeti release'in yayimladigiyla tutmadi (beklenen $expected, gelen $actual)"
}

Expand-Archive $zip -DestinationPath $dir -Force
Remove-Item $zip
Start-Process (Join-Path $dir 'brisk-app.exe')   # UAC istemini brisk'in kendisi acar
Write-Host "brisk kuruldu: $dir  (kaldirmak = bu klasoru silmek)"
