param(
    [string]$Executable = 'Builds/ModifierSelectionValidation/ModifierSelectionValidation.exe',
    [switch]$Keyboard
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $projectRoot $Executable }
if (-not (Test-Path -LiteralPath $Executable)) { throw "Missing validation build: $Executable" }
$logDirectory = Join-Path $projectRoot ('Logs/ModifierSelectionProcess/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

function Start-SelectionProcess([string]$Role) {
    $processArgs = @('-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
        '-logFile', (Join-Path $logDirectory ($Role + '.log')), "--modifier-selection-role=$Role")
    if ($Keyboard) { $processArgs += '--modifier-selection-keyboard' }
    else { $processArgs += '-batchmode' }
    $windowStyle = if ($Keyboard) { 'Normal' } else { 'Hidden' }
    Start-Process -FilePath $Executable -WindowStyle $windowStyle -PassThru -ArgumentList $processArgs
}

$hostProcess = $null
$clientProcess = $null
try {
    $hostProcess = Start-SelectionProcess 'host'
    $hostLog = Join-Path $logDirectory 'host.log'
    $deadline = (Get-Date).AddSeconds(45)
    $ready = $false
    while ((Get-Date) -lt $deadline -and -not $hostProcess.HasExited) {
        if ((Test-Path -LiteralPath $hostLog) -and
            (Select-String -LiteralPath $hostLog -SimpleMatch '[ModifierSelectionProcess] event=ready role=Host' -Quiet)) {
            $ready = $true
            break
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw "Host failed to load Gameplay offers: $hostLog" }
    $clientProcess = Start-SelectionProcess 'client'
    Write-Output "Validation logs: $logDirectory; Host PID: $($hostProcess.Id); Client PID: $($clientProcess.Id)"
    $deadline = (Get-Date).AddSeconds($(if ($Keyboard) { 310 } else { 90 }))
    while ((-not $clientProcess.HasExited -or -not $hostProcess.HasExited) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }
    if (-not $clientProcess.HasExited -or -not $hostProcess.HasExited) { throw 'Selection validation timed out.' }
    foreach ($role in @('host', 'client')) {
        $log = Join-Path $logDirectory ($role + '.log')
        if (-not (Select-String -LiteralPath $log -SimpleMatch '[ModifierSelectionProcess] result=PASS' -Quiet)) {
            throw "Selection validation failed: $log"
        }
    }
    if ($clientProcess.ExitCode -ne 0 -or $hostProcess.ExitCode -ne 0) { throw 'A validation player exited with an error.' }
    Write-Output "Host/Client selection, native network hit, and reconnect validation passed: $logDirectory"
}
finally {
    foreach ($process in @($clientProcess, $hostProcess)) {
        if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}
