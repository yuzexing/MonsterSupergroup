param(
    [string]$Executable = 'Builds/PlayerDebugDevelopment/PlayerDebug.exe',
    [int]$Width = 1280,
    [int]$Height = 720,
    [int]$Port = 7988,
    [switch]$Headless
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $projectRoot $Executable }
if (-not (Test-Path -LiteralPath $Executable)) { throw "Missing validation build: $Executable" }
$logRoot = Join-Path $projectRoot ('Logs/PlayerDebug/Process-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
Write-Output "Player Debug artifacts: $logRoot"
$processes = @{}
function Launch([string]$role) {
    $arguments = @('-force-d3d11', '-screen-fullscreen', '0', '-screen-width', "$Width", '-screen-height', "$Height",
        '-logFile', ('"' + $logRoot + '/' + $role + '.log"'), "--player-debug-role=$role",
        ('"--player-debug-artifacts=' + $logRoot + '"'), "--player-debug-port=$Port")
    if ($Headless) { $arguments += @('-batchmode', '-nographics') }
    $processes[$role] = Start-Process -FilePath $Executable -WindowStyle Hidden -PassThru -ArgumentList $arguments
    $null = $processes[$role].Handle
}
try {
    Launch 'host'
    $deadline = (Get-Date).AddSeconds(70)
    while (-not (Test-Path -LiteralPath (Join-Path $logRoot 'ready-host'))) {
        if ((Get-Date) -gt $deadline -or $processes.host.HasExited) { throw "Host did not become ready: $logRoot" }
        Start-Sleep -Milliseconds 250
    }
    Launch 'client'
    Launch 'client2'
    $deadline = (Get-Date).AddSeconds(200)
    while (@($processes.Values | Where-Object { -not $_.HasExited }).Count -gt 0) {
        if ((Get-Date) -gt $deadline) { throw "Player Debug validation timed out: $logRoot" }
        if (@($processes.Values | Where-Object { $_.HasExited -and $_.ExitCode -ne 0 }).Count -gt 0) { throw "Validation process failed: $logRoot" }
        Start-Sleep -Milliseconds 250
    }
    foreach ($role in $processes.Keys) {
        $processes[$role].WaitForExit()
        if ($processes[$role].ExitCode -ne 0 -or -not (Select-String -LiteralPath (Join-Path $logRoot ($role + '.log')) -SimpleMatch "[PlayerDebugProcess] result=PASS role=$role" -Quiet)) {
            throw "Validation failed for $role : $logRoot"
        }
    }
    Write-Output "Player Debug Host + two clients validation passed: $logRoot"
}
finally {
    foreach ($process in $processes.Values) { if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force } }
}
