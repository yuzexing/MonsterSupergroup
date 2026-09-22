param(
    [string]$Executable = 'Builds/MusicPrototype/MusicPrototype.exe',
    [ValidateSet('normal', 'impaired')][string]$Profile = 'normal',
    [int]$Port = 7998,
    [string]$ArtifactsRoot = ''
)
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ProjectTools.psm1')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $projectRoot $Executable }
if (-not (Test-Path -LiteralPath $Executable)) { throw "Missing build: $Executable" }
if ([string]::IsNullOrWhiteSpace($ArtifactsRoot)) {
    $ArtifactsRoot = Join-Path $projectRoot 'Logs/PrototypeAbilities'
}
$run = Join-Path $ArtifactsRoot ('music-network-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Profile)
New-Item -ItemType Directory -Path $run -Force | Out-Null
$processes = @{}
function Launch([string]$role) {
    # DSP needs a real audio-enabled player; do not use -nographics or headless batch mode.
    $arguments = @('-force-d3d11', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
        '-logFile', ('"' + (Join-Path $run ($role + '.log')) + '"'), "--music-role=$role",
        ('"--music-artifacts=' + $run + '"'), "--music-port=$Port")
    if ($Profile -eq 'impaired') { $arguments += '--music-impaired' }
    $processes[$role] = Start-ProjectProcess -FilePath $Executable -WindowStyle Hidden -PassThru -ArgumentList $arguments
    $null = $processes[$role].Handle
}
Write-Output "Music Host + Client artifacts: $run"
Get-FileHash -Algorithm SHA256 -LiteralPath $Executable | Format-List | Out-File (Join-Path $run 'build-sha256.txt')
try {
    Launch 'host'
    $deadline = (Get-Date).AddSeconds(60)
    while (-not (Test-Path -LiteralPath (Join-Path $run 'listening'))) {
        if ((Get-Date) -gt $deadline -or $processes.host.HasExited) { throw "Host failed to listen: $run" }
        Start-Sleep -Milliseconds 200
    }
    Launch 'client'
    $deadline = (Get-Date).AddSeconds(180)
    while (@($processes.Values | Where-Object { -not $_.HasExited }).Count -gt 0) {
        if ((Get-Date) -gt $deadline) { throw "Music validation timed out: $run" }
        if (@($processes.Values | Where-Object { $_.HasExited -and $_.ExitCode -ne 0 }).Count -gt 0) {
            throw "Music validation failed: $run"
        }
        Start-Sleep -Milliseconds 200
    }
    foreach ($role in @('host', 'client')) {
        Wait-ProjectProcess -Process $processes[$role]
        if ($processes[$role].ExitCode -ne 0 -or
            -not (Test-Path -LiteralPath (Join-Path $run ('result-' + $role))) -or
            (Get-Content -LiteralPath (Join-Path $run ('result-' + $role)) -Raw).Trim() -ne 'PASS') {
            throw "Music $role did not pass: $run"
        }
        Get-Content -LiteralPath (Join-Path $run ('owner-done-' + $role))
    }
    Get-Content -LiteralPath (Join-Path $run 'server-verified')
    Write-Output "PASS ($Profile): $run"
}
finally {
    foreach ($process in $processes.Values) {
        if (-not $process.HasExited) { Stop-ProjectProcess -Id $process.Id -Force }
    }
}
