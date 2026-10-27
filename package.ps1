[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [string]$Vpk = 'vpk')
$ErrorActionPreference = 'Stop'
$tokuVersion = ([xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'TokuControl.csproj') -Raw)).Project.PropertyGroup.Version
$tokuPublish = Join-Path $PSScriptRoot "artifacts\publish-v$tokuVersion"
$tokuRelease = Join-Path $PSScriptRoot "artifacts\release-v$tokuVersion"
if (Test-Path -LiteralPath $tokuRelease) { throw "Release directory already exists: $tokuRelease" }
if (Test-Path -LiteralPath $tokuPublish) { throw "Use a fresh publish directory: $tokuPublish" }
$tokuToolVersion = & $Vpk --help
if ($LASTEXITCODE -ne 0 -or ($tokuToolVersion -join "`n") -notmatch 'Velopack CLI 1\.2\.161,') { throw 'Use vpk 1.2.161, matching the pinned application dependency.' }
& (Join-Path $PSScriptRoot 'build.ps1') -Dotnet $Dotnet -Destination $tokuPublish
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
foreach ($tokuRequired in @('TokuControl.exe', 'TokuControl.dll', 'Velopack.dll', 'LICENSE', 'LICENSE-NET-EXCEPTION', 'LICENSE.txt', 'ThirdPartyNotices.txt', 'THIRD-PARTY.md', 'START-HERE.txt', 'runtime-license\VELOPACK-LICENSE.txt', 'docs\overview.png', 'docs\system.png')) {
    if (-not (Test-Path -LiteralPath (Join-Path $tokuPublish $tokuRequired) -PathType Leaf)) { throw "Missing release file: $tokuRequired" }
}
$tokuUnexpected = @(Get-ChildItem -LiteralPath $tokuPublish -Recurse -File | Where-Object { $_.Extension -in @('.pdb', '.log') -or $_.Name -eq 'settings.json' })
if ($tokuUnexpected.Count -ne 0) { throw 'Private state or debug files in publish output.' }
& $Vpk pack --packId TokuControl --packTitle TokuControl --packAuthors 'Tokumaru Labs' --packVersion $tokuVersion --packDir $tokuPublish --mainExe TokuControl.exe --icon (Join-Path $PSScriptRoot 'Assets\TokuControl.ico') --channel win --runtime win-x64 --delta None --noInst --skip-updates --outputDir $tokuRelease
if ($LASTEXITCODE -ne 0) { throw 'Velopack packaging failed.' }
$tokuPortable = Join-Path $tokuRelease 'TokuControl-win-Portable.zip'
if (-not (Test-Path -LiteralPath $tokuPortable)) { throw 'Portable bundle missing.' }
Copy-Item -LiteralPath $tokuPortable -Destination (Join-Path $tokuRelease "TOKU-CONTROL-v$tokuVersion-win-x64.zip")
$tokuSums = Get-ChildItem -LiteralPath $tokuRelease -File | Where-Object { ($_.Extension -in @('.zip', '.nupkg', '.json') -and $_.Name -ne 'assets.win.json') -or $_.Name -eq 'RELEASES' } | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
[IO.File]::WriteAllText((Join-Path $tokuRelease 'SHA256SUMS.txt'), (($tokuSums -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
Write-Output "Created update-enabled portable release in $tokuRelease"
Write-Output 'Publish releases.win.json and the full .nupkg alongside the ZIP on the same GitHub release.'
