[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [string]$Destination = '')
$ErrorActionPreference = 'Stop'
if (-not $Destination) { $Destination = Join-Path $PSScriptRoot 'artifacts\portable' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Push-Location $PSScriptRoot
try {
    & $Dotnet publish -c Release -r win-x64 --self-contained true -o $Destination
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
} finally { Pop-Location }
