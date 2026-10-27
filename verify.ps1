[CmdletBinding()]
param([string]$Executable = '')
$ErrorActionPreference = 'Stop'
if (-not $Executable) { $Executable = Join-Path $PSScriptRoot 'artifacts\portable\TokuControl.exe' }
$tokuResults = Join-Path $PSScriptRoot 'artifacts\verification'
New-Item -ItemType Directory -Path $tokuResults -Force | Out-Null
function Run-Check([string[]]$Arguments) {
    $tokuProcess = Start-Process -FilePath $Executable -ArgumentList $Arguments -PassThru -Wait -WindowStyle Hidden
    if ($tokuProcess.ExitCode -ne 0) { throw "Verification failed: $($tokuProcess.ExitCode)" }
}
# Native mode reads audio/system data only. Simulated mode tests writes against its own backend.
Run-Check @('--self-test', '--result', ('"' + (Join-Path $tokuResults 'service-checks.json') + '"'))
Run-Check @('--data-dir', ('"' + (Join-Path $tokuResults 'native-state') + '"'), '--capture-dir', ('"' + (Join-Path $tokuResults 'native') + '"'))
Run-Check @('--preview', '--data-dir', ('"' + (Join-Path $tokuResults 'preview-state') + '"'), '--capture-dir', ('"' + (Join-Path $tokuResults 'preview') + '"'))
Run-Check @('--preview', '--data-dir', ('"' + (Join-Path $tokuResults 'module-state') + '"'), '--module-check-dir', ('"' + (Join-Path $tokuResults 'modules') + '"'))
Run-Check @('--preview', '--data-dir', ('"' + (Join-Path $tokuResults 'crt-state') + '"'), '--crt-check-dir', ('"' + (Join-Path $tokuResults 'crt') + '"'))
Run-Check @('--preview', '--data-dir', ('"' + (Join-Path $tokuResults 'layout-state') + '"'), '--layout-check-dir', ('"' + (Join-Path $tokuResults 'layout') + '"'))
Run-Check @('--preview', '--data-dir', ('"' + (Join-Path $tokuResults 'update-state') + '"'), '--update-check-dir', ('"' + (Join-Path $tokuResults 'updates') + '"'))
foreach ($tokuReport in @('service-checks.json', 'native\ui-checks.json', 'preview\ui-checks.json', 'modules\module-checks.json', 'crt\crt-checks.json', 'layout\layout-checks.json', 'updates\update-ui-checks.json')) {
    $tokuResult = Get-Content -LiteralPath (Join-Path $tokuResults $tokuReport) -Raw | ConvertFrom-Json
    if ($tokuResult.passed -ne $true) { throw "Failed report: $tokuReport" }
}
Write-Output 'PASS: service, native UI read-only, simulated UI, module buttons, CRT controls/rendering, module layout, updater consent/cancellation/feed/checksum and UI checks.'
