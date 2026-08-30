<#
.SYNOPSIS
  Builds the two files brisk is released as.

.DESCRIPTION
  One script, run identically by a person and by CI, because an artifact built
  by a command nobody can reproduce is an artifact nobody can check.

  Two executables, each self-contained and each complete on its own:

    brisk-app.exe   the window. Requires administrator, because a standard-user
                    run reads no hardware sensors and tells the user nothing
                    about a real heat problem. It also answers console verbs,
                    which is how it re-launches itself elevated for work that
                    needs its own process.

    brisk.exe       the console tool, running as whoever typed it. No elevation
                    prompt for "brisk scan", and no prompt for reading what can
                    be read without one.

  Neither file needs the other, and neither needs .NET installed.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    # Left empty on purpose: $PSScriptRoot is not yet bound while parameter
    # defaults are evaluated, so the default is computed below instead.
    [string] $OutDir = '',
    [string] $Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $PSCommandPath
$root = (Resolve-Path (Join-Path $here '..')).Path
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts' }

# EnableCompressionInSingleFile roughly halves the download. The cost is a
# one-time decompression into a temp folder on first run, which is the trade
# every comparable single-file tool makes.
#
# DebugType=embedded keeps line numbers inside the bundle. A tool whose whole
# claim is "brisk tells you what it saw" should not answer its own crash
# reports with a bare stack of hex.
$common = @(
    '-c', $Configuration
    '-r', $Runtime
    '--self-contained', 'true'
    '-p:PublishSingleFile=true'
    '-p:IncludeNativeLibrariesForSelfExtract=true'
    '-p:EnableCompressionInSingleFile=true'
    '-p:DebugType=embedded'
    # Without this, .NET's own Turkish, Japanese, Russian ... resources for WPF
    # and WinForms ride along. brisk speaks two languages.
    # %3B is a semicolon MSBuild will not read as the end of the argument.
    '-p:SatelliteResourceLanguages=en%3Btr'
)

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

function Publish-One {
    param([string] $Project, [string] $FinalName)

    $staging = Join-Path ([System.IO.Path]::GetTempPath()) ("brisk-publish-" + [guid]::NewGuid().ToString('N'))
    try {
        & dotnet publish (Join-Path $root $Project) @common -o $staging
        if ($LASTEXITCODE -ne 0) { throw "publish failed: $Project" }

        $built = Get-ChildItem $staging -Filter *.exe
        if ($built.Count -ne 1) {
            throw "expected exactly one exe from $Project, found $($built.Count): $($built.Name -join ', ')"
        }
        Copy-Item $built.FullName (Join-Path $OutDir $FinalName) -Force
    }
    finally {
        if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    }
}

Publish-One -Project 'src\Brisk\Brisk.csproj'         -FinalName 'brisk-app.exe'
Publish-One -Project 'src\Brisk.Cli\Brisk.Cli.csproj' -FinalName 'brisk.exe'

# get.ps1 installs from exactly one asset pair: brisk-win-x64.zip and its
# .sha256, and refuses to run without the digest. The zip carries both exes.
$zipPath = Join-Path $OutDir 'brisk-win-x64.zip'
Compress-Archive -Path (Join-Path $OutDir 'brisk-app.exe'), (Join-Path $OutDir 'brisk.exe') -DestinationPath $zipPath
$zipHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()
"$zipHash  brisk-win-x64.zip" | Set-Content ($zipPath + '.sha256') -Encoding ascii

# A checksum file is what lets someone verify the download is the file this
# script produced. Same format as sha256sum, so it can be checked on any OS.
$lines = Get-ChildItem $OutDir -Filter *.exe | Sort-Object Name | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()
    "$hash  $($_.Name)"
}
$lines | Set-Content (Join-Path $OutDir 'SHA256SUMS.txt') -Encoding ascii

Get-ChildItem $OutDir | ForEach-Object {
    '{0,-18} {1,10:N1} MB' -f $_.Name, ($_.Length / 1MB)
}
