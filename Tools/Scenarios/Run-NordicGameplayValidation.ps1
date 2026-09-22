param(
    [string]$Executable = '',
    [string]$OutputDirectory = 'Logs/NordicGameplay/Acceptance',
    [switch]$FullSuite,
    [switch]$FullPerformance
)
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ProjectTools.psm1')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Executable = Resolve-ProjectBuildExecutable -ProjectRoot $projectRoot -Recipe 'gameplay-validation' -Executable $Executable -RequireDevelopmentTools -Network Kcp
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $projectRoot $Executable }
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory = Join-Path $projectRoot $OutputDirectory }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$arguments = @('-screen-fullscreen', '0', '-screen-width', '1920', '-screen-height', '1080', '-force-d3d11',
    '-logFile', ('"' + (Join-Path $OutputDirectory 'player.log') + '"'), ('"--nordic-gameplay-check=' + $OutputDirectory + '"'))
if ($FullPerformance) { $arguments += '--nordic-full-performance' }
$started = [DateTime]::UtcNow
$process = Start-ProjectProcess -FilePath $Executable -ArgumentList $arguments -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
$null = $process.Handle
if (-not ('NordicAcceptanceWindow' -as [type])) {
    Add-Type 'public static class NordicAcceptanceWindow { [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetWindowPos(System.IntPtr window, System.IntPtr after, int x, int y, int width, int height, uint flags); }'
}
try {
    $deadline = (Get-Date).AddSeconds($(if ($FullPerformance) { 350 } else { 200 }))
    while (-not $process.HasExited) {
        if ((Get-Date) -gt $deadline) { throw 'Nordic Gameplay acceptance timed out.' }
        # Keep Unity behind other windows without activation. SW_HIDE freezes its rendered frame.
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $null = [NordicAcceptanceWindow]::SetWindowPos($process.MainWindowHandle, [IntPtr]1, 0, 0, 0, 0, 0x13) }
        Start-Sleep -Milliseconds 250
    }
    Wait-ProjectProcess -Process $process
    $resultPath = Join-Path $OutputDirectory 'acceptance.json'
    if (-not (Test-Path -LiteralPath $resultPath) -or (Get-Item -LiteralPath $resultPath).LastWriteTimeUtc -lt $started) { throw 'No fresh acceptance result.' }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or -not $result.passed) { $result.failures | Write-Output; throw "Nordic Gameplay acceptance failed: $resultPath" }
    $frameHashes = foreach ($frame in @('center', 'boundary', 'occlusion-behind', 'occlusion-front')) {
        $framePath = Join-Path $OutputDirectory ($frame + '.png')
        if (-not (Test-Path -LiteralPath $framePath) -or (Get-Item -LiteralPath $framePath).LastWriteTimeUtc -lt $started) { throw "Missing fresh frame: $frame" }
        (Get-FileHash -LiteralPath $framePath -Algorithm SHA256).Hash
    }
    if (@($frameHashes | Select-Object -Unique).Count -ne 4) { throw 'Unity screenshots are stale; render validation is incomplete.' }
    Write-Output "Nordic Gameplay PASS: $($result.checks.Count) checks. $resultPath"
}
finally { if (-not $process.HasExited) { Stop-ProjectProcess -Id $process.Id -Force } }
if ($FullSuite) {
    $runner = Join-Path $projectRoot 'Tools/Invoke-ProjectTool.ps1'
    & $runner -ToolId test.camera -Parameters @{ Executable=$Executable; CaptureFrames=$true; ForceD3D11=$true }
    & $runner -ToolId test.preparation-menu -Parameters @{ Executable=$Executable; Profile='party' }
    & $runner -ToolId test.dash -Parameters @{ Executable=$Executable; Dedicated=$true; LogDirectory='Logs/NordicGameplay' }
    & $runner -ToolId test.timeline-waves -Parameters @{ Executable=$Executable; Dedicated=$true }
    & $runner -ToolId test.enemy-handoff -Parameters @{ Executable=$Executable; Duration=30 }
    & $runner -ToolId test.upgrade-selection -Parameters @{ Executable=$Executable; Dedicated=$true }
    & $runner -ToolId test.preparation-menu -Parameters @{ Executable=$Executable; Profile='run-end' }
}
