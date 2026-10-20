<#
.SYNOPSIS
  Rebuilds src/BriskEngine/Live/asn-owners.tsv from iptoasn.com.

.DESCRIPTION
  Run by a developer, never by brisk: brisk makes no network connection. The
  source is iptoasn.com's ip2asn tables (PDDL 1.0, public domain). Only the
  networks of companies people recognise are kept, so the file stays small.
  Re-run now and then and commit the result.
#>
[CmdletBinding()]
param([string] $Out = '')

$ErrorActionPreference = 'Stop'
if (-not $Out) { $Out = Join-Path $PSScriptRoot '..\src\BriskEngine\Live\asn-owners.tsv' }

# AS description prefix -> the name brisk shows.
$owners = [ordered]@{
    'MICROSOFT' = 'Microsoft'; 'GOOGLE' = 'Google'; 'AMAZON' = 'Amazon'; 'CLOUDFLARENET' = 'Cloudflare'
    'AKAMAI' = 'Akamai'; 'FACEBOOK' = 'Meta'; 'APPLE' = 'Apple'; 'FASTLY' = 'Fastly'
    'RIOT' = 'Riot Games'; 'VALVE' = 'Valve'; 'SPOTIFY' = 'Spotify'; 'NETFLIX' = 'Netflix'
    'TWITTER' = 'X'; 'ZOOM' = 'Zoom'; 'ORACLE' = 'Oracle'; 'GITHUB' = 'GitHub'
    'ANTHROPIC' = 'Anthropic'; 'OPENAI' = 'OpenAI'; 'DISCORD' = 'Discord'; 'NVIDIA' = 'NVIDIA'
    'ADOBE' = 'Adobe'; 'DROPBOX' = 'Dropbox'; 'BYTEDANCE' = 'ByteDance'; 'TENCENT' = 'Tencent'
    'ALIBABA' = 'Alibaba'
}
$prefix = [regex]('^(' + (($owners.Keys | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')')

$tmp = Join-Path ([IO.Path]::GetTempPath()) 'brisk-asn'
New-Item -ItemType Directory -Force $tmp | Out-Null
$rows = New-Object System.Collections.Generic.List[string]

foreach ($name in 'ip2asn-v4.tsv.gz', 'ip2asn-v6.tsv.gz') {
    $gz = Join-Path $tmp $name
    Invoke-WebRequest "https://iptoasn.com/data/$name" -OutFile $gz -UseBasicParsing
    $file = [IO.File]::OpenRead($gz)
    $reader = [IO.StreamReader]::new([IO.Compression.GZipStream]::new($file, [IO.Compression.CompressionMode]::Decompress))
    try {
        while ($null -ne ($line = $reader.ReadLine())) {
            $f = $line.Split("`t")
            if ($f.Count -lt 5 -or $f[2] -eq '0') { continue }
            $m = $prefix.Match($f[4].ToUpperInvariant())
            if (-not $m.Success) { continue }
            $rows.Add("$($f[0])`t$($f[1])`t$($owners[$m.Groups[1].Value])")
        }
    } finally { $reader.Dispose() }
}

$header = "# IP ranges of well-known companies. Source: iptoasn.com (PDDL 1.0, public domain). " +
          "Built by tools/build-asn-table.ps1 on $(Get-Date -Format yyyy-MM-dd)."
[IO.File]::WriteAllLines([IO.Path]::GetFullPath($Out), [string[]](@($header) + $rows))
'{0} ranges written to {1}' -f $rows.Count, $Out
