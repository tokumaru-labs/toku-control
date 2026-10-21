[CmdletBinding()]
param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1') -Dotnet $Dotnet
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$tokuVersion = ([xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'TokuControl.csproj') -Raw)).Project.PropertyGroup.Version
$tokuName = "TOKU-CONTROL-v$tokuVersion-win-x64"
$tokuPortable = Join-Path $PSScriptRoot 'artifacts\portable'
$tokuRelease = Join-Path $PSScriptRoot 'artifacts\release'
$tokuArchive = Join-Path $tokuRelease ($tokuName + '.zip')
New-Item -ItemType Directory -Path $tokuRelease -Force | Out-Null
if (Test-Path -LiteralPath $tokuArchive) { throw "Archive already exists: $tokuArchive" }
foreach ($tokuRequired in @('TokuControl.exe', 'TokuControl.dll', 'LICENSE', 'LICENSE-NET-EXCEPTION', 'LICENSE.txt', 'ThirdPartyNotices.txt', 'THIRD-PARTY.md', 'START-HERE.txt', 'docs\overview.png', 'docs\system.png')) {
    if (-not (Test-Path -LiteralPath (Join-Path $tokuPortable $tokuRequired) -PathType Leaf)) { throw "Missing release file: $tokuRequired" }
}
$tokuUnexpected = @(Get-ChildItem -LiteralPath $tokuPortable -Recurse -File | Where-Object { $_.Extension -in @('.pdb', '.log') -or $_.Name -eq 'settings.json' })
if ($tokuUnexpected.Count -ne 0) { throw 'Private state or debug files found in portable output; use a clean build output.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($tokuPortable, $tokuArchive, [IO.Compression.CompressionLevel]::Optimal, $false)
$tokuHash = (Get-FileHash -LiteralPath $tokuArchive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $tokuRelease 'SHA256SUMS.txt'), "$tokuHash  $tokuName.zip`n", [Text.UTF8Encoding]::new($false))
Write-Output "Created $tokuArchive"
Write-Output "SHA256 $tokuHash"
