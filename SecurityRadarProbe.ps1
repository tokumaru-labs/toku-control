# Embedded read-only probes. $source is supplied only from the compiled RadarSource enum.
# No profiles, third-party modules, elevation, network lookups, settings writes or dynamic execution.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$rows = [Collections.Generic.List[object]]::new()
$state = 'Complete'
$note = ''
$limit = 2000
$rowChars = 0
function Row([string]$key, [string]$label, [string]$value) {
    if ($rows.Count -ge $limit + 1 -or $script:rowChars + $key.Length + $label.Length + $value.Length -gt 1048576) { Partial 'Result size limit reached; incomplete collection.'; return }
    $script:rowChars += $key.Length + $label.Length + $value.Length
    $rows.Add([pscustomobject]@{ Key = $key; Label = $label; Value = $value })
}
function Partial([string]$reason) { $script:state = 'Partial'; $script:note = $reason }
function Digest([string]$text) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-', '') }
    finally { $hash.Dispose() }
}
function ProcessLabel([int]$processId) {
    try {
        $p = [Diagnostics.Process]::GetProcessById($processId)
        try { return $p.ProcessName } finally { $p.Dispose() }
    } catch { return 'unavailable / exited or access denied' }
}
function Read-LocalAttributes([string]$path) { return [IO.File]::GetAttributes($path) }
function Test-LocalStartupPath([string]$path) {
    if (-not $path -or $path.StartsWith('\\') -or ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($path))).DriveType -ne [IO.DriveType]::Fixed) { throw 'Non-local startup folder' }
    $parent = [IO.DirectoryInfo]::new($path)
    $ancestors = [Collections.Generic.List[string]]::new()
    while ($null -ne $parent) { $ancestors.Add($parent.FullName); $parent = $parent.Parent }
    $ancestors.Reverse()
    foreach ($ancestor in $ancestors) {
        try { $attributes = Read-LocalAttributes $ancestor }
        catch [IO.FileNotFoundException] { return $false }
        catch [IO.DirectoryNotFoundException] { return $false }
        if ($attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse point' }
    }
    return $true
}
try {
    switch ($source) {
        'Defender' {
            $status = Defender\Get-MpComputerStatus
            foreach ($name in @('AMRunningMode', 'AMServiceEnabled', 'AntivirusEnabled', 'RealTimeProtectionEnabled', 'BehaviorMonitorEnabled', 'IsTamperProtected', 'AntivirusSignatureLastUpdated')) {
                $property = $status.PSObject.Properties[$name]
                $value = if ($null -eq $property -or $null -eq $property.Value) { 'Unknown' } else { [string]$property.Value }
                Row $name $name $value
            }
            $note = 'Reported Defender state; third-party antivirus and bypass resistance are not evaluated.'
        }
        'Firewall' {
            foreach ($profile in @(NetSecurity\Get-NetFirewallProfile -PolicyStore ActiveStore)) {
                Row ([string]$profile.Name) ([string]$profile.Name) ([string]$profile.Enabled)
            }
            $note = 'All three profile settings in ActiveStore; active interfaces and individual rules are not evaluated.'
        }
        'RunKeys' {
            foreach ($hive in @('CurrentUser', 'LocalMachine')) {
                foreach ($view in @('Registry64', 'Registry32')) {
                    $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]$hive, [Microsoft.Win32.RegistryView]$view)
                    try {
                        foreach ($leaf in @('Run', 'RunOnce')) {
                            $key = $null
                            try {
                                $key = $root.OpenSubKey(('Software\Microsoft\Windows\CurrentVersion\' + $leaf), $false)
                                if ($null -ne $key) {
                                    foreach ($name in $key.GetValueNames()) {
                                        $value = $key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                                        if ($null -eq $value) { throw 'Value disappeared during enumeration' }
                                        Row ($hive + '/' + $view + '/' + $leaf + '/' + $name) ($hive + ' ' + $view + ' ' + $leaf + ' / ' + $name) ($key.GetValueKind($name).ToString() + ' / ' + (ConvertTo-Json -InputObject $value -Compress))
                                    }
                                }
                            } catch { Partial 'Some Run keys or values could not be read; comparisons are disabled.' }
                            finally { if ($null -ne $key) { $key.Dispose() } }
                        }
                    } finally { $root.Dispose() }
                }
            }
            if ($state -eq 'Complete') { $note = 'Current user and machine, 32/64-bit Run and RunOnce only. Other user hives and other autorun locations are not covered.' }
        }
        'StartupFolders' {
            foreach ($folder in @('Startup', 'CommonStartup')) {
                $path = [Environment]::GetFolderPath([Environment+SpecialFolder]$folder, [Environment+SpecialFolderOption]::DoNotVerify)
                try {
                    # Do not touch redirected network folders, reparse points, or their targets.
                    # Check root-to-leaf BEFORE touching the leaf: a parent may redirect to a share.
                    # Directory.Exists suppresses access errors, so use attributes and typed errors.
                    if (-not (Test-LocalStartupPath $path)) { continue }
                    foreach ($file in [IO.DirectoryInfo]::new($path).GetFiles()) {
                        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint -or $file.Length -gt 8388608) { Partial 'A startup file was skipped (link/reparse point or size over 8 MiB).'; continue }
                        # Hash the shortcut file itself, never resolve or execute its target.
                        $stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
                        $hash = [Security.Cryptography.SHA256]::Create()
                        try { $digest = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') }
                        finally { $hash.Dispose(); $stream.Dispose() }
                        Row ($folder + '/' + $file.Name) ($folder + ' / ' + $file.Name) ($file.FullName + ' / SHA256 ' + $digest)
                    }
                } catch { Partial 'A startup folder/file could not be read or is redirected; comparisons are disabled.' }
            }
            if ($state -eq 'Complete') { $note = 'Current-user and common Startup files, SHA256 of file content. Shortcut targets are not opened or verified.' }
        }
        'Services' {
            foreach ($service in @(CimCmdlets\Get-CimInstance -ClassName Win32_Service -OperationTimeoutSec 8 | Select-Object -First 2001)) {
                Row ([string]$service.Name) ([string]$service.DisplayName) ('Path: ' + $service.PathName + ' | Start: ' + $service.StartMode + ' | Account: ' + $service.StartName + ' | Delayed: ' + $service.DelayedAutoStart)
            }
            $note = 'Service path/start/account/delayed-start configuration; running/stopped transitions, DLL contents and drivers are not compared.'
        }
        'ScheduledTasks' {
            foreach ($task in @(ScheduledTasks\Get-ScheduledTask | Select-Object -First 2001)) {
                try {
                    $xml = ScheduledTasks\Export-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath
                    $definition = [xml]$xml
                    $actions = $definition.Task.Actions.OuterXml
                    Row ($task.TaskPath + $task.TaskName) ($task.TaskPath + $task.TaskName) ($actions + ' | Definition SHA256: ' + (Digest $xml))
                } catch { Partial 'A visible task definition could not be read; comparisons are disabled.' }
            }
            if ($state -eq 'Complete') { $note = 'Visible task definitions only; Windows may hide tasks from this account. Transient runtime state is excluded.' }
        }
        'Authentication' {
            $limit = 500
            $end = [DateTime]::Now
            $start = $end.AddHours(-24)
            $events = @()
            try {
                $events = @(Microsoft.PowerShell.Diagnostics\Get-WinEvent -FilterHashtable @{ LogName = 'Security'; Id = @(4624, 4625, 1102); StartTime = $start; EndTime = $end } -MaxEvents 501)
            } catch {
                if ($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*') { throw }
            }
            foreach ($event in $events) {
                try {
                    $xml = [xml]$event.ToXml()
                    $data = @{}
                    foreach ($field in $xml.Event.EventData.Data) { $data[[string]$field.Name] = [string]$field.'#text' }
                    $type = if ($data.ContainsKey('LogonType')) { $data['LogonType'] } else { 'n/a' }
                    Row ([string]$event.RecordId) ([string]$event.Id + ' / logon type ' + $type) ($event.TimeCreated.ToString('o') + ' | Account: ' + $data['TargetUserName'] + ' | IP: ' + $data['IpAddress'] + ' | Status: ' + $data['Status'] + ' | Substatus: ' + $data['SubStatus'])
                } finally { $event.Dispose() }
            }
            $note = 'Window: ' + $start.ToString('o') + ' to ' + $end.ToString('o') + '. Latest 500 events. Audit policy and retention are not verified; zero results do not imply no attacks.'
        }
        'Tcp' {
            $limit = 1000
            $index = 0
            foreach ($connection in @(NetTCPIP\Get-NetTCPConnection | Select-Object -First 1001)) {
                $index++
                $processId = [int]$connection.OwningProcess
                Row ([string]$index) ((ProcessLabel $processId) + ' / PID ' + $processId + ' / ' + $connection.State) ('[' + $connection.LocalAddress + ']:' + $connection.LocalPort + ' -> [' + $connection.RemoteAddress + ']:' + $connection.RemotePort)
            }
            $note = 'TCP IPv4/IPv6 point-in-time snapshot. No DNS, geolocation, packet capture, reputation lookup or signature validation. PID lookup can race process exit/reuse.'
        }
        'Udp' {
            $limit = 1000
            $index = 0
            foreach ($endpoint in @(NetTCPIP\Get-NetUDPEndpoint | Select-Object -First 1001)) {
                $index++
                $processId = [int]$endpoint.OwningProcess
                Row ([string]$index) ((ProcessLabel $processId) + ' / PID ' + $processId) ('[' + $endpoint.LocalAddress + ']:' + $endpoint.LocalPort + ' / remote peer unavailable for UDP endpoint snapshot')
            }
            $note = 'Local UDP endpoints only. A bound socket does not prove remote reachability or data transfer.'
        }
        default { throw 'Unknown probe' }
    }
    if ($rows.Count -gt $limit) { Partial ($note + ' Result limit reached; incomplete collection.'); }
} catch {
    $state = 'Unavailable'
    $note = 'Read failed: ' + $_.CategoryInfo.Category + ' / ' + $_.Exception.GetType().Name + '. No elevation or policy change attempted.'
    $rows.Clear()
}
[pscustomobject]@{ State = $state; CapturedAt = [DateTimeOffset]::Now.ToString('o'); Note = $note; Rows = @($rows | Select-Object -First $limit) } | ConvertTo-Json -Depth 5 -Compress
