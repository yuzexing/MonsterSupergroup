param(
    [string]$Executable = 'Builds/M3Knockback/M3Knockback.exe',
    [switch]$Dedicated,
    [switch]$ImpairedNetwork,
    [switch]$CaptureFrames,
    [switch]$VisibleWindows,
    [int]$Port = 7962
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $projectRoot $Executable }
if (-not (Test-Path -LiteralPath $Executable)) { throw "Missing M3 validation build: $Executable" }
$modeName = if ($Dedicated) { 'Dedicated' } else { 'Host' }
$logRoot = Join-Path $projectRoot ('Logs/M3Knockback/' + $modeName + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
Write-Output "M3 process artifacts: $logRoot"
$processes = @{}
function Launch([string]$role) {
    $arguments = @('-logFile', ('"' + $logRoot + '/' + $role + '.log"'), "--m3-role=$role",
        ('"--m3-artifacts=' + $logRoot + '"'), "--m3-port=$Port", '-screen-fullscreen','0','-screen-width','1920','-screen-height','1080')
    if ($Dedicated) { $arguments += '--m3-dedicated' }
    if ($ImpairedNetwork) { $arguments += '--m3-impaired' }
    if ($CaptureFrames -and $role -ne 'server') { $arguments += '--m3-capture' }
    else { $arguments += @('-batchmode','-nographics') }
    if ($role -eq 'server') { $arguments += '--dedicated-server' }
    $style = if ($VisibleWindows -and $role -ne 'server') { 'Normal' } else { 'Hidden' }
    $processes[$role] = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle $style -PassThru
    $null = $processes[$role].Handle
}
function AwaitMarker([string]$marker) {
    $deadline = (Get-Date).AddSeconds(60)
    while (-not (Test-Path -LiteralPath (Join-Path $logRoot $marker))) {
        if ((Get-Date) -gt $deadline -or @($processes.Values | Where-Object HasExited).Count -gt 0) { throw "M3 failed waiting for $marker : $logRoot" }
        Start-Sleep -Milliseconds 200
    }
}
try {
    Launch $(if ($Dedicated) { 'server' } else { 'host' })
    AwaitMarker 'listening'
    Launch 'client'
    if ($Dedicated) { AwaitMarker 'ready-client'; Launch 'client2' }
    $deadline = (Get-Date).AddSeconds(240)
    while (@($processes.Values | Where-Object { -not $_.HasExited }).Count -gt 0) {
        if ((Get-Date) -gt $deadline) { throw "M3 timed out: $logRoot" }
        if (@($processes.Values | Where-Object { $_.HasExited -and $_.ExitCode -ne 0 }).Count -gt 0) { throw "M3 player exited with an error: $logRoot" }
        Start-Sleep -Milliseconds 250
    }
    foreach ($role in $processes.Keys) {
        $processes[$role].WaitForExit()
        $code = $processes[$role].ExitCode
        Write-Output "M3 role=$role exit=$code"
        Set-Content -LiteralPath (Join-Path $logRoot ($role + '.exit.txt')) -Value $code
        if ($code -ne 0 -or -not (Select-String -LiteralPath (Join-Path $logRoot ($role + '.log')) -SimpleMatch "[M3Process] result=PASS role=$role" -Quiet)) {
            throw "M3 validation failed for $role : $logRoot"
        }
    }
    Write-Output "M3 validation passed: $logRoot"
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
