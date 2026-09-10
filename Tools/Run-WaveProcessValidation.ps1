param(
    [string]$Executable = 'Builds/M5Waves/M5Waves.exe',
    [switch]$Dedicated,
    [switch]$Simulation,
    [switch]$CaptureFrames,
    [switch]$VisibleWindows,
    [int]$Port = 7986
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $projectRoot $Executable }
if (-not (Test-Path -LiteralPath $Executable)) { throw "Missing M5 build: $Executable" }
$modeName = if ($Dedicated) { 'Dedicated' } else { 'Host' }
$logRoot = Join-Path $projectRoot ('Logs/M5/' + $modeName + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-p' + $Port)
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
[ordered]@{ executable = $Executable; dedicated = [bool]$Dedicated; simulation = [bool]$Simulation; port = $Port; captureFrames = [bool]$CaptureFrames } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logRoot 'run.json')
Write-Output "M5 process artifacts: $logRoot"
$processes = @{}
function Launch([string]$role) {
    $arguments = @('-logFile', ('"' + $logRoot + '/' + $role + '.log"'), "--m5-role=$role",
        ('"--m5-artifacts=' + $logRoot + '"'), "--m5-port=$Port", '-screen-fullscreen', '0', '-screen-width', '1920', '-screen-height', '1080', '-force-d3d11')
    if ($Dedicated) { $arguments += '--m5-dedicated' }
    if ($Simulation) { $arguments += '--m5-simulation' }
    if ($CaptureFrames) { $arguments += '--m5-capture' }
    else { $arguments += @('-batchmode', '-nographics') }
    if ($role -eq 'server') { $arguments += '--dedicated-server' }
    $style = if ($VisibleWindows) { 'Normal' } else { 'Hidden' }
    $processes[$role] = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle $style -PassThru
    $null = $processes[$role].Handle
}
function AwaitMarker([string]$marker) {
    $until = (Get-Date).AddSeconds(45)
    while (-not (Test-Path -LiteralPath (Join-Path $logRoot $marker))) {
        if ((Get-Date) -gt $until -or @($processes.Values | Where-Object HasExited).Count -gt 0) { throw "M5 failed waiting for $marker : $logRoot" }
        Start-Sleep -Milliseconds 200
    }
}
try {
    Launch $(if ($Dedicated) { 'server' } else { 'host' })
    AwaitMarker 'listening'
    Launch 'client'
    if ($Dedicated) { AwaitMarker 'ready-client-1'; Launch 'client2' }
    $until = (Get-Date).AddSeconds(330)
    while (@($processes.Values | Where-Object { -not $_.HasExited }).Count -gt 0) {
        if ((Get-Date) -gt $until) { throw "M5 timed out: $logRoot" }
        if (@($processes.Values | Where-Object { $_.HasExited -and $_.ExitCode -ne 0 }).Count -gt 0) { throw "M5 process failed: $logRoot" }
        Start-Sleep -Milliseconds 250
    }
    foreach ($role in $processes.Keys) {
        $processes[$role].WaitForExit()
        $code = $processes[$role].ExitCode
        Write-Output "M5 role=$role exit=$code"
        if ($code -ne 0 -or -not (Select-String -LiteralPath (Join-Path $logRoot ($role + '.log')) -SimpleMatch "[M5Process] result=PASS role=$role" -Quiet)) {
            throw "M5 gameplay validation failed for $role : $logRoot"
        }
    }
    Write-Output "M5 validation passed: $logRoot"
}
finally {
    foreach ($role in $processes.Keys) {
        $process = $processes[$role]
        if ($process.HasExited) {
            $process.WaitForExit()
            Set-Content -LiteralPath (Join-Path $logRoot ($role + '.exit.txt')) -Value $process.ExitCode
        } else { Stop-Process -Id $process.Id -Force }
    }
}
