[CmdletBinding()]
param([string]$Result = '')
$ErrorActionPreference = 'Stop'
if (-not $Result) { $Result = Join-Path $PSScriptRoot '..\artifacts\radar-probe-checks.json' }
$sourceFile = Join-Path $PSScriptRoot '..\SecurityRadarProbe.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($sourceFile, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Probe syntax invalid' }
# Load only trusted helper declarations from the source being tested. No real collector runs.
foreach ($name in @('Test-LocalStartupPath', 'Row', 'Partial')) {
    $node = $ast.FindAll({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] }, $true) | Where-Object Name -eq $name
    if (@($node).Count -ne 1) { throw "Missing helper $name" }
    . ([scriptblock]::Create($node.Extent.Text))
}
$calls = [Collections.Generic.List[string]]::new()
$fixture = [IO.Path]::GetPathRoot($env:SystemRoot) + 'RadarFixture\Redirected\Startup'
$redirect = [IO.Path]::GetDirectoryName($fixture)
$mode = 'redirect'
function Read-LocalAttributes([string]$path) {
    $calls.Add($path)
    if ($path -eq $redirect) {
        switch ($mode) {
            'redirect' { return [IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint }
            'denied' { throw [UnauthorizedAccessException]::new('fixture') }
            'missing' { throw [IO.DirectoryNotFoundException]::new('fixture') }
        }
    }
    return [IO.FileAttributes]::Directory
}
$checks = [ordered]@{}
$blocked = $false
try { $null = Test-LocalStartupPath $fixture } catch { $blocked = $_.Exception.Message -eq 'Reparse point' }
$checks.parentRedirectNeverTouchesChild = $blocked -and -not $calls.Contains($fixture) -and $calls[0] -eq [IO.Path]::GetPathRoot($fixture) -and $calls[$calls.Count - 1] -eq $redirect
$calls.Clear(); $mode = 'normal'
$checks.normalPathWalksRootToLeaf = (Test-LocalStartupPath $fixture) -and $calls[0] -eq [IO.Path]::GetPathRoot($fixture) -and $calls[$calls.Count - 1] -eq $fixture
$calls.Clear(); $mode = 'denied'; $denied = $false
try { $null = Test-LocalStartupPath $fixture } catch [UnauthorizedAccessException] { $denied = $true }
$checks.accessDeniedIsNotMissing = $denied -and -not $calls.Contains($fixture)
$calls.Clear(); $mode = 'missing'
$checks.missingDirectoryStopsWalk = -not (Test-LocalStartupPath $fixture) -and -not $calls.Contains($fixture)
$calls.Clear(); $blocked = $false
try { $null = Test-LocalStartupPath '\\example.invalid\share\Startup' } catch { $blocked = $true }
$checks.uncRejectedBeforeAttributes = $blocked -and $calls.Count -eq 0
$rows = [Collections.Generic.List[object]]::new(); $rowChars = 0; $limit = 2; $state = 'Complete'; $note = ''
1..5 | ForEach-Object { Row ([string]$_) 'fixture' 'value' }
$checks.rowLimitRetainsPartial = $state -eq 'Partial' -and $rows.Count -eq 3
$rows.Clear(); $rowChars = 1048576; $state = 'Complete'
Row 'x' 'fixture' 'value'
$checks.textBudgetRetainsPartial = $state -eq 'Partial' -and $rows.Count -eq 0
$checks.passed = -not ($checks.Values -contains $false)
$resultPath = [IO.Path]::GetFullPath($Result)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resultPath)) | Out-Null
$checks | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
if (-not $checks.passed) { throw 'Radar probe regression failed' }
Write-Output 'PASS: startup path guard and probe bounds (7 checks).'
