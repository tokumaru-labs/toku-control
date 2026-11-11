[CmdletBinding()]
param([string]$Result = '')
$ErrorActionPreference = 'Stop'
if (-not $Result) { $Result = Join-Path $PSScriptRoot '..\artifacts\radar-probe-checks.json' }
$sourceFile = Join-Path $PSScriptRoot '..\SecurityRadarProbe.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($sourceFile, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Probe syntax invalid' }
# Load only trusted helper declarations from the source being tested. No real collector runs.
foreach ($name in @('Test-LocalStartupPath', 'Row', 'Partial', 'NetworkRow', 'DetectionRow')) {
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
$rows.Clear(); $rowChars = 0; $state = 'Complete'
function ProcessName([int]$processId) { if ($processId -eq 42) { return 'fixture' } }
NetworkRow 1 42 ' / Established' 'local -> remote'
NetworkRow 2 43 '' 'local only'
$checks.networkOwnerFields = $rows[0].Owner.Pid -eq 42 -and $rows[0].Owner.Name -eq 'fixture' -and $rows[0].Label -eq 'fixture / PID 42 / Established'
$checks.unknownOwnerKeepsEndpoint = $rows[1].Owner.Pid -eq 43 -and $null -eq $rows[1].Owner.Name -and $rows[1].Value -eq 'local only'
$rows.Clear(); $rowChars = 1048576 - ('1fixture / PID 42local').Length; $state = 'Complete'
NetworkRow 1 42 '' 'local'
$checks.ownerNameCountsTowardBudget = $rows.Count -eq 0 -and $state -eq 'Partial'
$rows.Clear(); $rowChars = 0; $state = 'Complete'; $note = ''
$record = [pscustomobject]@{ TimeCreated = [datetime]::new(2026,11,10,12,0,5,[DateTimeKind]::Utc); Xml = @'
<Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event"><EventData>
<Data Name="Process Name">C:\Fixture\browser.exe</Data><Data Name="Path">unrelated-private-path</Data>
<Data Name="Detection Time">2026-11-10T12:00:01.123Z</Data><Data Name="Threat Name">Fixture.Detection</Data>
</EventData></Event>
'@
}
$record | Add-Member -MemberType ScriptMethod -Name ToXml -Value { $this.Xml }
DetectionRow $record 1
$checks.detectionFieldsByName = $state -eq 'Complete' -and $rows.Count -eq 1 -and
    $rows[0].Label -eq 'Fixture.Detection' -and $rows[0].Value -eq 'C:\Fixture\browser.exe' -and
    ($rows[0] | ConvertTo-Json -Compress) -notmatch 'unrelated-private-path'
$checks.distinctEventAndDetectionTimes = $rows[0].RecordedAt -eq '2026-11-10T12:00:05.0000000+00:00' -and
    $rows[0].DetectedAt -eq '2026-11-10T12:00:01.1230000+00:00'
foreach ($badTime in @('', 'not a date', '2026-11-10T12:00:01')) {
    $savedXml = $record.Xml
    $record.Xml = $savedXml.Replace('2026-11-10T12:00:01.123Z', $badTime)
    $rows.Clear(); $state = 'Complete'
    DetectionRow $record 1
    $checks["invalidDetectionTime[$badTime]"] = $state -eq 'Partial' -and
        $rows[0].Label -eq 'Fixture.Detection' -and $rows[0].RecordedAt -and -not $rows[0].DetectedAt
    $record.Xml = $savedXml
}
$rows.Clear(); $state = 'Complete'
$record.Xml = '<Event><broken'
DetectionRow $record 1
$checks.unreadableDetectionFieldsKeepKnownRecord = $state -eq 'Partial' -and
    $rows.Count -eq 1 -and $rows[0].RecordedAt -and -not $rows[0].Label -and -not $rows[0].Value
# Run the complete fixed history branch with only its local event reader replaced.
# Dot-source at this script's scope so the collector's script-scoped coverage updates are observable.
function Get-FixtureEvent {
    [CmdletBinding()]
    param([string]$ListLog, [hashtable]$FilterHashtable, [int]$MaxEvents)
    if ($ListLog) {
        if ($ListLog -ne 'Microsoft-Windows-Windows Defender/Operational') { throw 'Unexpected channel' }
        $channelFixture = [pscustomobject]@{IsEnabled = $scenario -ne 'disabled'}
        $channelFixture | Add-Member ScriptMethod Dispose {}
        return $channelFixture
    }
    if ($FilterHashtable.LogName -ne 'Microsoft-Windows-Windows Defender/Operational' -or
        $FilterHashtable.ProviderName -ne 'Microsoft-Windows-Windows Defender' -or
        $FilterHashtable.Id -ne 1116 -or $MaxEvents -ne 51 -or
        $FilterHashtable.EndTime - $FilterHashtable.StartTime -ne [TimeSpan]::FromHours(24)) { throw 'Unexpected event query' }
    if ($scenario -eq 'denied') { throw [UnauthorizedAccessException]::new('fixture') }
    if ($scenario -eq 'empty') {
        $failure = [Management.Automation.ErrorRecord]::new([Exception]::new('fixture'), 'NoMatchingEventsFound', [Management.Automation.ErrorCategory]::ObjectNotFound, $null)
        $PSCmdlet.ThrowTerminatingError($failure)
    }
    foreach ($i in 1..$MaxEvents) {
        $eventFixture = [pscustomobject]@{TimeCreated = [datetime]::UtcNow; Id = $i}
        $eventFixture | Add-Member ScriptMethod ToXml {
            "<Event><EventData><Data Name='Threat Name'>fixture-$($this.Id)</Data><Data Name='Process Name'>browser.exe</Data><Data Name='Detection Time'>2026-11-10T12:00:01Z</Data></EventData></Event>"
        }
        $eventFixture | Add-Member ScriptMethod Dispose {}
        $eventFixture
        if ($scenario -eq 'interrupted') { throw [UnauthorizedAccessException]::new('fixture interrupted') }
    }
}
$collector = [scriptblock]::Create($ast.Extent.Text.Replace('Microsoft.PowerShell.Diagnostics\Get-WinEvent', 'Get-FixtureEvent'))
$source = 'DefenderHistory'
foreach ($scenario in 'empty', 'denied', 'disabled', 'limit', 'interrupted') {
    $history = . $collector | ConvertFrom-Json
    $checks["historyQuery_$scenario"] = switch ($scenario) {
        'empty' { $history.State -eq 'Complete' -and $history.Rows.Count -eq 0 }
        'denied' { $history.State -eq 'Unavailable' -and $history.Rows.Count -eq 0 }
        'disabled' { $history.State -eq 'Unavailable' -and $history.Note -match 'disabled' }
        'limit' { $history.State -eq 'Partial' -and $history.Rows.Count -eq 50 -and $history.Rows[49].Label -eq 'fixture-50' -and $history.Note -match 'limit' }
        'interrupted' { $history.State -eq 'Partial' -and $history.Rows.Count -eq 1 -and $history.Rows[0].Label -eq 'fixture-1' }
    }
}
$checks.passed = -not ($checks.Values -contains $false)
$resultPath = [IO.Path]::GetFullPath($Result)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resultPath)) | Out-Null
$checks | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
if (-not $checks.passed) { throw 'Radar probe regression failed' }
Write-Output 'PASS: startup path guard, probe bounds, network owner fields and Defender event parsing.'
