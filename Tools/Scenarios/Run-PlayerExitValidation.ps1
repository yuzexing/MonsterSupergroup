[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [ValidateSet('Manual','UIAutomation')][string]$Mode = 'Manual',
    [ValidateSet('home','settings','singleplayer','client','host','run-end')][string]$Scenario = 'home',
    [ValidateSet('D3D11','Default')][string]$Graphics = 'D3D11',
    [ValidateRange(1,50)][int]$Iterations = 5,
    [ValidateRange(30,3600)][int]$InteractionTimeoutSeconds = 1200,
    [string]$ArtifactDirectory,
    [switch]$AllowValidationBuild
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ProjectTools.psm1')
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $projectRoot $Executable }
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$dataPath = Join-Path (Split-Path -Parent $Executable) ([IO.Path]::GetFileNameWithoutExtension($Executable) + '_Data')
$testAssemblies = @(Get-ChildItem -LiteralPath (Join-Path $dataPath 'Managed') -Filter '*.dll' |
    Where-Object Name -match '(\.Tests\.|TestRunner|nunit\.framework|UnityEngine\.TestRunner)')
if ($testAssemblies.Count -and -not $AllowValidationBuild) {
    throw "Test assemblies found. Use a player-development/player-release build, or explicitly pass -AllowValidationBuild for an old baseline."
}
if (-not $ArtifactDirectory) { $ArtifactDirectory = Join-Path $projectRoot ('Logs/PlayerExit/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + $Mode + '-' + $Graphics) }
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
if (Test-Path -LiteralPath $ArtifactDirectory) { throw "Use a new artifact directory; old control files must not authorize a new run: $ArtifactDirectory" }
New-Item -ItemType Directory -Path $ArtifactDirectory | Out-Null
function Save-ExitJson($value, [string]$path) { $value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $path -Encoding UTF8 }
function Convert-ExitUtc($value) {
    # PowerShell 7 can deserialize ISO JSON timestamps directly to DateTime.
    if ($value -is [datetime]) { return $value.ToUniversalTime() }
    return [datetime]::Parse([string]$value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
}
function Read-QuitSignal([string]$path, [string]$runId, [int]$processId) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    try { $signal = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json } catch { return $null }
    if ($signal.runId -ne $runId -or $signal.processId -ne $processId) { throw 'Quit signal does not belong to this process.' }
    $requested = Convert-ExitUtc $signal.requestedUtc
    if ($requested -gt [DateTime]::UtcNow.AddSeconds(2)) { throw 'Quit signal timestamp is in the future.' }
    return $requested
}
function Read-CrashFields([xml]$xml) {
    $fields = @{}
    $values = @($xml.Event.EventData.Data)
    # Older Application Error event 1000 uses positional, unnamed EventData.
    $names = @('AppName','AppVersion','AppTimeStamp','ModuleName','ModuleVersion','ModuleTimeStamp','ExceptionCode','FaultingOffset','ProcessId','ProcessCreationTime','AppPath','ModulePath','ReportId','PackageFullName','PackageRelativeAppId')
    for ($index=0; $index -lt $values.Count; $index++) {
        $field = $values[$index]
        if ($field -is [string]) { $fields[$names[$index]] = $field }
        else { $fields[[string]$field.Name] = [string]$field.InnerText }
    }
    return $fields
}
function Find-PlayerCrashes([int]$processId, [datetime]$from, [datetime]$until) {
    # Read by time, then match PID. A different concurrently running Player must not fail this run.
    $queryErrors = @()
    # Get-WinEvent's hashtable date filter interprets the wall time as local even for UTC values.
    $events = @(Get-WinEvent -FilterHashtable @{LogName='Application'; Id=1000; StartTime=$from.ToLocalTime(); EndTime=$until.ToLocalTime()} -ErrorAction SilentlyContinue -ErrorVariable queryErrors)
    foreach ($issue in $queryErrors) {
        if ($issue.FullyQualifiedErrorId -notmatch 'NoMatchingEventsFound') { throw $issue }
    }
    foreach ($event in $events) {
        [xml]$xml = $event.ToXml()
        $fields = Read-CrashFields $xml
        $idText = $fields.ProcessId
        if (-not $idText) { continue }
        # Application Error's ProcessId field is hexadecimal, also without a 0x prefix.
        $eventPid = [Convert]::ToInt64(($idText -replace '^0x',''),16)
        if ($eventPid -eq $processId) {
            [pscustomobject]@{ recordId=$event.RecordId; timeUtc=$event.TimeCreated.ToUniversalTime().ToString('o'); fields=$fields; message=$event.Message }
        }
    }
}
$packageDirectory = Split-Path -Parent $Executable
$manifest = @(Get-ChildItem -LiteralPath $packageDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{path=[IO.Path]::GetRelativePath($packageDirectory,$_.FullName); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
$manifestPath = Join-Path $ArtifactDirectory 'package-manifest.json'
Save-ExitJson $manifest $manifestPath
$summary = [ordered]@{
    executable=$Executable; executableSha256=(Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash
    unityPlayerSha256=(Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $Executable) 'UnityPlayer.dll') -Algorithm SHA256).Hash
    unityVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo($Executable).ProductVersion
    packageManifestSha256=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    mode=$Mode; scenario=$Scenario; requestedGraphics=$Graphics; testAssemblies=@($testAssemblies | ForEach-Object Name)
    timingOrigin=$(if ($Mode -eq 'Manual') { 'operator-enter; elapsed time includes switching to the game and clicking Exit' } else { 'controller signal immediately before clicking home Exit' })
    success=$false; runs=@()
}
Write-Output "Player exit artifacts: $ArtifactDirectory"
try {
    for ($iteration=1; $iteration -le $Iterations; $iteration++) {
        $runId = [Guid]::NewGuid().ToString('N')
        $folder = Join-Path $ArtifactDirectory ('run-' + $iteration)
        New-Item -ItemType Directory -Path $folder | Out-Null
        $log = Join-Path $folder 'player.log'
        $arguments = @('-logFile', ('"' + $log + '"'))
        if ($Graphics -eq 'D3D11') { $arguments += '-force-d3d11' }
        $started = [DateTime]::UtcNow
        # This is the interactive game under test. Hidden launch can prevent DXGI display setup.
        $player = Start-ProjectProcess -FilePath $Executable -ArgumentList $arguments -WindowStyle Normal -PassThru -WorkingDirectory (Split-Path -Parent $Executable)
        $record = [ordered]@{ runId=$runId; iteration=$iteration; processId=$player.Id; startedUtc=$started.ToString('o'); quitRequestedUtc=$null; exitCode=$null; exitSeconds=$null; success=$false; error=''; crashEvents=@(); uiaObservation=$null; actualGraphics=@() }
        $summary.runs += $record
        Save-ExitJson $record (Join-Path $folder 'launch.json')
        $signalPath = Join-Path $folder 'quit-request.json'
        try {
            if ($Mode -eq 'Manual') {
                Write-Host "Run $iteration/${Iterations}: use the game normally WITHOUT a window-element reader. Complete scenario: $Scenario."
                $null = Read-Host 'When ready, press Enter here, then click the game home Exit button within 30 seconds'
                Save-ExitJson @{runId=$runId; processId=$player.Id; requestedUtc=[DateTime]::UtcNow.ToString('o')} $signalPath
            } else {
                Write-Output "Awaiting UI controller: $folder (record uia-observed.json after a real element read; write quit-request.json immediately before clicking Exit)."
            }
            $requestTime = $null
            while ($null -eq $requestTime) {
                $requestTime = Read-QuitSignal $signalPath $runId $player.Id
                if ($null -ne $requestTime) { break }
                if ($player.HasExited) { throw 'Player exited before a recorded quit request.' }
                if (([DateTime]::UtcNow-$started).TotalSeconds -gt $InteractionTimeoutSeconds) { throw 'Timed out waiting for the exit operator.' }
                Start-Sleep -Milliseconds 100
            }
            if ($requestTime -lt $started) { throw 'Quit request predates this launch.' }
            $record.quitRequestedUtc = $requestTime.ToString('o')
            if ($Mode -eq 'UIAutomation') {
                $observation = Get-Content -LiteralPath (Join-Path $folder 'uia-observed.json') -Raw | ConvertFrom-Json
                if ($observation.runId -ne $runId -or $observation.processId -ne $player.Id -or -not $observation.method) { throw 'Missing matching UI Automation observation.' }
                $observedUtc = Convert-ExitUtc $observation.observedUtc
                if ($observedUtc -lt $started -or $observedUtc -gt $requestTime) { throw 'UI observation must precede the quit request in this run.' }
                $record.uiaObservation = $observation
            }
            while (-not $player.HasExited) {
                if (([DateTime]::UtcNow-$requestTime).TotalSeconds -ge 30) { throw 'Player failed to exit within 30 seconds of the quit request.' }
                Start-Sleep -Milliseconds 100
            }
            $player.WaitForExit()
            $record.exitCode = $player.ExitCode
            $record.exitSeconds = ($player.ExitTime.ToUniversalTime()-$requestTime).TotalSeconds
            if ($record.exitSeconds -lt 0 -or $record.exitSeconds -gt 30) { throw 'Exit time is outside the recorded quit window.' }
            # WER events can arrive after the process handle signals exit.
            Start-Sleep -Seconds 3
            $record.crashEvents = @(Find-PlayerCrashes $player.Id $started ([DateTime]::UtcNow))
            if ($record.exitCode -ne 0 -or $record.crashEvents.Count) { throw "Abnormal Player exit: code=$($record.exitCode), crashEvents=$($record.crashEvents.Count)" }
            $record.success = $true
        } catch { $record.error = $_.Exception.Message }
        finally {
            if ($player.HasExited) { $player.WaitForExit(); $record.exitCode = $player.ExitCode }
            else { Stop-ProjectProcess -Id $player.Id -Force; $record.error += ' Process was killed for cleanup; this is not a normal exit.' }
            if (Test-Path -LiteralPath $log) { $record.actualGraphics = @(Select-String -LiteralPath $log -Pattern 'GfxDevice:|Forcing GfxDevice:|Version:\s+Direct3D|Renderer:' | ForEach-Object Line) }
            Save-ExitJson $record (Join-Path $folder 'result.json')
            Save-ExitJson $summary (Join-Path $ArtifactDirectory 'result.json')
        }
        Write-Output "Run $iteration/${Iterations}: success=$($record.success), code=$($record.exitCode)"
        if (-not $record.success) { throw $record.error }
    }
    $summary.success = $true
} finally { Save-ExitJson $summary (Join-Path $ArtifactDirectory 'result.json'); Clear-ProjectProcesses }
