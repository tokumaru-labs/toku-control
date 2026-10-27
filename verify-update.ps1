[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OldZip,
    [Parameter(Mandatory)][string]$CandidateFeed,
    [Parameter(Mandatory)][string]$Destination,
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$tokuOld = (Resolve-Path -LiteralPath $OldZip).Path
$tokuFeed = (Resolve-Path -LiteralPath $CandidateFeed).Path
$tokuSandbox = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $tokuSandbox) { throw 'Use a fresh, disposable test directory.' }
$tokuRoot = Join-Path $tokuSandbox 'portable'
$tokuState = Join-Path $tokuSandbox 'settings'
$tokuCapture = Join-Path $tokuSandbox 'restarted'
New-Item -ItemType Directory -Path $tokuRoot, $tokuState | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($tokuOld, $tokuRoot)
$tokuSettings = '{"CheckUpdatesOnStartup":false,"Crt":false,"Bloom":0.31,"TextSize":13}'
[IO.File]::WriteAllText((Join-Path $tokuState 'settings.json'), $tokuSettings)
$tokuBefore = ([xml](Get-Content -LiteralPath (Join-Path $tokuRoot 'current\sq.version') -Raw)).package.metadata.version
$tokuExpected = (Get-Content -LiteralPath (Join-Path $tokuFeed 'releases.win.json') -Raw | ConvertFrom-Json).Assets | Where-Object Type -eq 'Full' | Select-Object -First 1 -ExpandProperty Version
if ([version]$tokuExpected -le [version]$tokuBefore) { throw 'Candidate must be newer than the old portable fixture.' }
& $Dotnet run --project (Join-Path $PSScriptRoot 'tests\UpdateSmoke\UpdateSmoke.csproj') -c Release -- $tokuRoot $tokuFeed $tokuState $tokuCapture
if ($LASTEXITCODE -ne 0) { throw 'Native update runner failed.' }
$tokuWatch = [Diagnostics.Stopwatch]::StartNew()
while (-not (Test-Path -LiteralPath (Join-Path $tokuCapture 'system.png'))) {
    if ($tokuWatch.Elapsed.TotalSeconds -gt 60) { throw 'Timed out waiting for the updated app to restart and render.' }
    Start-Sleep -Milliseconds 300
}
Start-Sleep -Seconds 2
$tokuAfter = ([xml](Get-Content -LiteralPath (Join-Path $tokuRoot 'current\sq.version') -Raw)).package.metadata.version
$tokuSaved = Get-Content -LiteralPath (Join-Path $tokuState 'settings.json') -Raw | ConvertFrom-Json
$tokuChecks = [ordered]@{
    before = $tokuBefore
    after = $tokuAfter
    versionUpdated = $tokuAfter -eq $tokuExpected
    executableUpdated = (Get-Item -LiteralPath (Join-Path $tokuRoot 'current\TokuControl.dll')).VersionInfo.ProductVersion -eq $tokuExpected
    restartedAndRendered = (Test-Path -LiteralPath (Join-Path $tokuCapture 'overview.png')) -and (Test-Path -LiteralPath (Join-Path $tokuCapture 'system.png'))
    settingsRetained = $tokuSaved.CheckUpdatesOnStartup -eq $false -and $tokuSaved.Crt -eq $false -and $tokuSaved.Bloom -eq 0.31 -and $tokuSaved.TextSize -eq 13
    stableLauncherRetained = Test-Path -LiteralPath (Join-Path $tokuRoot 'TokuControl.exe')
    noCaptureFailure = -not (Test-Path -LiteralPath (Join-Path $tokuCapture 'capture-error.txt'))
}
$tokuChecks.passed = -not ($tokuChecks.Values -contains $false)
$tokuChecks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $tokuSandbox 'native-update-checks.json') -Encoding UTF8
if (-not $tokuChecks.passed) { throw 'Native update checks failed.' }
Write-Output "PASS: native update $tokuBefore -> $tokuAfter, restart/rendering and settings retention. Report: $tokuSandbox"
