[CmdletBinding()]
param(
    [string]$Executable = 'Builds/SteamDiagnostics/Monster Supergroup.exe',
    [ValidateSet('D3D12','D3D11','Default')][string]$Graphics = 'D3D12',
    [ValidateSet('host','client','solo')][string]$ExpectedRole = 'client',
    [string]$Scenario = 'R1',
    [string]$ArtifactDirectory,
    [int]$AttachProcessId = 0,
    [ValidateRange(0,60)][int]$ProfileSeconds = 0,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $projectRoot $Executable }
if ($AttachProcessId) {
    $player = Get-Process -Id $AttachProcessId
    $Executable = $player.Path
}
$Executable = (Resolve-Path -LiteralPath $Executable).Path
if (-not $ArtifactDirectory) { $ArtifactDirectory = Join-Path $projectRoot ('Logs/SteamSessions/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + $ExpectedRole) }
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
if (Test-Path -LiteralPath $ArtifactDirectory) { throw 'Use a new artifact directory for each capture.' }
New-Item -ItemType Directory -Path $ArtifactDirectory | Out-Null
function Save-Json($value, $name) { $value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory $name) -Encoding UTF8 }
function Read-DumpProcessId([string]$path) {
    $stream=[IO.File]::OpenRead($path); $reader=[IO.BinaryReader]::new($stream)
    try {
        if($reader.ReadUInt32() -ne 0x504d444d){return $null}
        $stream.Position=8; $count=$reader.ReadUInt32(); $directory=$reader.ReadUInt32()
        for($index=0;$index -lt $count;$index++) {
            $stream.Position=$directory+$index*12; $type=$reader.ReadUInt32(); $size=$reader.ReadUInt32(); $rva=$reader.ReadUInt32()
            if($type -eq 15 -and $size -ge 12) {
                $stream.Position=$rva+4; $flags=$reader.ReadUInt32(); $dumpProcess=$reader.ReadUInt32()
                if($flags -band 1){return $dumpProcess}
            }
        }
        return $null
    } finally { $reader.Dispose(); $stream.Dispose() }
}
$package = Split-Path -Parent $Executable
$data = Join-Path $package ([IO.Path]::GetFileNameWithoutExtension($Executable) + '_Data')
$binaryFiles = @((Get-Item -LiteralPath $Executable), (Get-Item -LiteralPath (Join-Path $package 'UnityPlayer.dll')))
$binaryFiles += @(Get-ChildItem -LiteralPath (Join-Path $data 'Managed') -Filter '*.dll' | Where-Object {$_.Name -like 'MonsterSupergroup*'})
$binaryFiles += @(Get-ChildItem -LiteralPath (Join-Path $package 'D3D12') -Filter '*.dll' -ErrorAction SilentlyContinue)
$manifest = @($binaryFiles | Sort-Object FullName | ForEach-Object { [ordered]@{path=$_.FullName.Substring($package.Length+1); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
Save-Json $manifest 'package-manifest.json'
$hardware=[ordered]@{capturedUtc=[DateTime]::UtcNow.ToString('o');displayAdapters=@();errors=@()}
try {
    $hardware.displayAdapters=@(Get-CimInstance Win32_VideoController | Select-Object Name,PNPDeviceID,DriverVersion,DriverDate,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate)
} catch { $hardware.errors += $_.Exception.Message }
Save-Json $hardware 'hardware.json'
$arguments = @('-timestamps','-logFile',('"' + (Join-Path $ArtifactDirectory 'Player.log') + '"'),'--network-diagnostics',('"--network-diagnostics-output=' + (Join-Path $ArtifactDirectory 'metrics') + '"'))
if ($Graphics -ne 'Default') { $arguments += '-force-' + $Graphics.ToLowerInvariant() }
if ($ProfileSeconds) { $arguments += "--network-profiler-seconds=$ProfileSeconds" }
$record = [ordered]@{schemaVersion=1; scenario=$Scenario; expectedRole=$ExpectedRole; executable=$Executable; arguments=$arguments; requestedGraphics=$Graphics; attached=[bool]$AttachProcessId; startedUtc=$null; processId=$null; complete=$false; exitCode=$null; errors=@(); notes=@(); crashEvents=@(); dumps=@()}
Save-Json $record 'capture.json'
Write-Output "Artifacts: $ArtifactDirectory"
Write-Output 'Use ordinary Steam rooms. Keep both machines on the same package. Close the game normally after the round.'
Write-Output 'Profiler capture, if requested, starts at the first >100 ms frame after 60 seconds. Deep Profiling is not enabled.'
if ($PrepareOnly) { Write-Output ('Prepared arguments: ' + ($arguments -join ' ')); return }
if (-not $AttachProcessId) {
    # This user-invoked launcher deliberately opens an interactive game window.
    $player = Start-Process -FilePath $Executable -WorkingDirectory $package -ArgumentList $arguments -WindowStyle Normal -PassThru
}
$null = $player.Handle
$record.processId=$player.Id
$record.actualCommandLine=(Get-CimInstance Win32_Process -Filter "ProcessId = $($player.Id)").CommandLine
$started = $player.StartTime.ToUniversalTime(); $record.startedUtc=$started.ToString('o')
Save-Json $record 'capture.json'
try {
    $reminded=$false
    while (-not $player.WaitForExit(1000)) {
        if (-not $reminded -and ([DateTime]::UtcNow-$started).TotalMinutes -ge 20) {
            Write-Output '20 minutes since process start. Finish this round and exit when ready; the collector will not terminate the game.'
            $reminded=$true
        }
    }
    $player.WaitForExit(); $record.exitCode=$player.ExitCode; $record.exitedUtc=$player.ExitTime.ToUniversalTime().ToString('o')
    # WER can publish after the game exits. Observe for 15 seconds, without restarting the game.
    Start-Sleep -Seconds 15
    $eventErrors=@()
    $events=@(Get-WinEvent -FilterHashtable @{LogName='Application';Id=1000;StartTime=$started.ToLocalTime();EndTime=(Get-Date)} -ErrorAction SilentlyContinue -ErrorVariable eventErrors)
    foreach($issue in $eventErrors) { if($issue.FullyQualifiedErrorId -notmatch 'NoMatchingEventsFound') { $record.errors += $issue.ToString() } }
    foreach($event in $events) {
        [xml]$xml=$event.ToXml(); $fields=@{}
        $names=@('AppName','AppVersion','AppTimeStamp','ModuleName','ModuleVersion','ModuleTimeStamp','ExceptionCode','FaultingOffset','ProcessId','ProcessCreationTime','AppPath','ModulePath','ReportId','PackageFullName','PackageRelativeAppId')
        $values=@($xml.Event.EventData.Data)
        for($i=0;$i -lt $values.Count;$i++) { $value=$values[$i]; if($value -is [string]){$fields[$names[$i]]=$value}else{$fields[[string]$value.Name]=[string]$value.InnerText} }
        if(-not $fields.ProcessId) {continue}
        $eventPid=[Convert]::ToInt64(($fields.ProcessId -replace '^0x',''),16)
        if($eventPid -eq $player.Id -and [string]::Equals($fields.AppPath,$Executable,[StringComparison]::OrdinalIgnoreCase)) {
            $record.crashEvents += [ordered]@{recordId=$event.RecordId;utc=$event.TimeCreated.ToUniversalTime().ToString('o');fields=$fields;message=$event.Message}
        }
    }
    $dumpRoot=Join-Path $env:LOCALAPPDATA 'CrashDumps'
    $dumpName=[IO.Path]::GetFileName($Executable) + '.' + $player.Id + '.dmp'
    $dump=Get-Item -LiteralPath (Join-Path $dumpRoot $dumpName) -ErrorAction SilentlyContinue
    if($dump -and $dump.LastWriteTimeUtc -ge $started) {
        $dumpOut=Join-Path $ArtifactDirectory $dump.Name
        Copy-Item -LiteralPath $dump.FullName -Destination $dumpOut
        $record.dumps += [ordered]@{source=$dump.FullName;copy=$dumpOut;sha256=(Get-FileHash -LiteralPath $dumpOut -Algorithm SHA256).Hash}
    }
    $appInfo=Join-Path $data 'app.info'
    if(Test-Path -LiteralPath $appInfo) {
        $identity=@(Get-Content -LiteralPath $appInfo)
        if($identity.Count -ge 2) {
            $unityCrashes=Join-Path $env:TEMP ($identity[0] + '/' + $identity[1] + '/Crashes')
            foreach($folder in @(Get-ChildItem -LiteralPath $unityCrashes -Directory -ErrorAction SilentlyContinue | Where-Object {$_.LastWriteTimeUtc -ge $started})) {
                $unityDump=Join-Path $folder.FullName 'crash.dmp'
                if((Test-Path -LiteralPath $unityDump) -and (Read-DumpProcessId $unityDump) -eq $player.Id) {
                    $destination=Join-Path $ArtifactDirectory $folder.Name
                    New-Item -ItemType Directory -Path $destination | Out-Null
                    Get-ChildItem -LiteralPath $folder.FullName -File | Where-Object {$_.Extension -in '.log','.dmp','.txt'} | Copy-Item -Destination $destination
                    $record.dumps += [ordered]@{source=$unityDump;copy=(Join-Path $destination 'crash.dmp');sha256=(Get-FileHash -LiteralPath $unityDump -Algorithm SHA256).Hash}
                }
            }
        }
    }
    if($AttachProcessId) {
        # Attaching cannot retroactively enable diagnostics or isolate the original Player.log.
        $record.notes += 'Attach mode: prior logging flags were not changed. Requested graphics/arguments describe the launcher configuration, not the attached process.'
        if($record.actualCommandLine -match '(?i)-logFile\s+(?:"([^"]+)"|(\S+))') {
            $source=if($Matches[1]){$Matches[1]}else{$Matches[2]}
            if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination (Join-Path $ArtifactDirectory 'attached-Player.log')}
        } else {
            $record.notes += 'No explicit -logFile found; shared Player.log is not attributed to this process automatically.'
        }
    }
    $metrics=Join-Path $ArtifactDirectory 'metrics'
    if($AttachProcessId) {
        $sourceMetrics=$null
        if($record.actualCommandLine -match '(?i)(?:"--network-diagnostics-output=([^"]+)"|--network-diagnostics-output="([^"]+)"|--network-diagnostics-output=([^\s"]+))') {
            $sourceMetrics=@($Matches[1],$Matches[2],$Matches[3]) | Where-Object {$_} | Select-Object -First 1
        } elseif(Test-Path -LiteralPath $appInfo) {
            $identity=@(Get-Content -LiteralPath $appInfo)
            if($identity.Count -ge 2) {
                $sourceMetrics=Join-Path (Split-Path -Parent $env:LOCALAPPDATA) ('LocalLow/'+$identity[0]+'/'+$identity[1]+'/NetworkDiagnostics')
            }
        }
        if($sourceMetrics -and [IO.Path]::GetFullPath($sourceMetrics) -ne [IO.Path]::GetFullPath($metrics)) {
            foreach($sample in @(Get-ChildItem -LiteralPath $sourceMetrics -Filter '*.jsonl' -ErrorAction SilentlyContinue)) {
                try {
                    $header=Get-Content -LiteralPath $sample.FullName -TotalCount 1 | ConvertFrom-Json
                    if($header.processId -ne $player.Id -or -not $header.utcStart){continue}
                    $sampleStart=([DateTime]$header.utcStart).ToUniversalTime()
                    if($sampleStart -lt $started.AddSeconds(-1) -or $sampleStart -gt $player.ExitTime.ToUniversalTime()){continue}
                    New-Item -ItemType Directory -Path $metrics -Force | Out-Null
                    Copy-Item -LiteralPath $sample.FullName -Destination $metrics
                    if(Test-Path -LiteralPath ($sample.FullName+'.status.json')) {Copy-Item -LiteralPath ($sample.FullName+'.status.json') -Destination $metrics}
                } catch { $record.notes += 'Skipped unreadable diagnostics header: '+$sample.Name }
            }
        }
    }
    $metricFiles=@(Get-ChildItem -LiteralPath $metrics -Filter '*.jsonl' -ErrorAction SilentlyContinue)
    $record.metricFiles=@($metricFiles | ForEach-Object {$_.FullName})
    if(-not $metricFiles.Count){$record.errors += 'No diagnostic samples found; cannot infer FPS/GC/queue values from absence.'}
    $record.normalExit=($record.exitCode -eq 0 -and $record.crashEvents.Count -eq 0)
    # Complete means collection finished, not that the game or each metrics stream passed.
    $record.complete=$true
} catch { $record.errors += $_.Exception.ToString() }
finally {
    Save-Json $record 'capture.json'
    Write-Output "Capture saved: $ArtifactDirectory"
    # Deliberately never kill the user's game, including on Ctrl+C or collector failure.
}
