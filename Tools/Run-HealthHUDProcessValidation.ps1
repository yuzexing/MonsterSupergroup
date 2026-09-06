param(
    [string]$Executable = 'Builds/HealthHUDValidation/HealthHUDValidation.exe'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $projectRoot $Executable }
if (-not (Test-Path -LiteralPath $Executable)) { throw "Missing validation build: $Executable" }
$logDirectory = Join-Path $projectRoot ('Logs/HealthHUDProcess/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

function Start-HudProcess([string]$Role) {
    Start-Process -FilePath $Executable -WindowStyle Hidden -PassThru -ArgumentList @(
        '-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
        '-logFile', (Join-Path $logDirectory ($Role + '.log')),
        "--health-hud-role=$Role", "--health-hud-artifacts=$logDirectory"
    )
}

$hostProcess = $null
$clientProcess = $null
try {
    $hostProcess = Start-HudProcess 'host'
    $hostLog = Join-Path $logDirectory 'host.log'
    $deadline = (Get-Date).AddSeconds(45)
    $ready = $false
    while ((Get-Date) -lt $deadline -and -not $hostProcess.HasExited) {
        if ((Test-Path $hostLog) -and
            (Select-String -LiteralPath $hostLog -SimpleMatch '[HealthHUDProcess] event=ready role=Host' -Quiet)) {
            $ready = $true
            break
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw "Host did not load Gameplay and bind its HUD: $hostLog" }
    $clientProcess = Start-HudProcess 'client'
    if (-not $clientProcess.WaitForExit(90000)) { throw 'Client validation timed out.' }
    if (-not $hostProcess.WaitForExit(90000)) { throw 'Host validation timed out.' }
    foreach ($role in @('host', 'client')) {
        $log = Join-Path $logDirectory ($role + '.log')
        if (-not (Select-String -LiteralPath $log -SimpleMatch '[HealthHUDProcess] result=PASS' -Quiet)) {
            throw "HUD validation failed: $log"
        }
    }
    if ($clientProcess.ExitCode -ne 0 -or $hostProcess.ExitCode -ne 0) { throw 'A validation player exited with an error.' }
    Write-Output "Host/Client HUD and reconnect validation passed: $logDirectory"
}
finally {
    foreach ($process in @($clientProcess, $hostProcess)) {
        if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}
