param(
    [string]$Executable = 'Builds/GameplayCameraValidation/GameplayCameraValidation.exe',
    [switch]$CaptureFrames,
    [switch]$VisibleWindows,
    [switch]$ForceD3D11
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $projectRoot $Executable }
if (-not (Test-Path -LiteralPath $Executable)) { throw "Missing camera validation build: $Executable" }
$logDirectory = Join-Path $projectRoot ('Logs/GameplayCameraProcess/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
Write-Output "Camera process artifacts: $logDirectory"
$processes = @()
try {
    foreach ($role in @('host', 'client')) {
        if ($role -eq 'client') {
            $deadline = (Get-Date).AddSeconds(45)
            while (-not (Test-Path -LiteralPath (Join-Path $logDirectory 'host-ready.ok'))) {
                if ((Get-Date) -gt $deadline -or $processes[0].HasExited) { throw 'Host did not bind the formal Gameplay camera.' }
                Start-Sleep -Milliseconds 250
            }
        }
        $playerArguments = @(
            '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
            '-logFile', ('"' + (Join-Path $logDirectory ($role + '.log')) + '"'),
            "--camera-role=$role", ('"--camera-artifacts=' + $logDirectory + '"')
        )
        if ($CaptureFrames) { $playerArguments += '--camera-capture-frames' }
        else { $playerArguments += '-batchmode' }
        if ($ForceD3D11) { $playerArguments += '-force-d3d11' }
        $windowStyle = if ($VisibleWindows) { 'Normal' } else { 'Hidden' }
        $process = Start-Process -FilePath $Executable -WindowStyle $windowStyle -PassThru -ArgumentList $playerArguments
        # Retain the handle before a short-lived player exits; otherwise ExitCode can be unavailable.
        $null = $process.Handle
        $processes += $process
    }
    $deadline = (Get-Date).AddSeconds(140)
    while (@($processes | Where-Object { -not $_.HasExited }).Count -gt 0) {
        if ((Get-Date) -gt $deadline) { throw 'Camera validation timed out.' }
        Start-Sleep -Milliseconds 250
    }
    foreach ($process in $processes) { $process.WaitForExit() }
    Write-Output ('Camera process exit codes: ' + (($processes | ForEach-Object { "PID=$($_.Id):$($_.ExitCode)" }) -join ', '))
    foreach ($role in @('host', 'client')) {
        if (-not (Select-String -LiteralPath (Join-Path $logDirectory ($role + '.log')) -SimpleMatch "[GameplayCameraProcess] role=$role result=PASS" -Quiet)) {
            throw "Camera validation failed for $role. See $logDirectory"
        }
    }
    if (@($processes | Where-Object { $_.ExitCode -ne 0 }).Count -gt 0) { throw 'A validation player returned an error.' }
    Write-Output "Host/Client camera validation passed: $logDirectory"
}
finally {
    foreach ($process in $processes) { if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force } }
}
