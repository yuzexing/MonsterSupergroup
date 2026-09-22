param(
    [string]$Executable = '',
    [switch]$Dedicated,
    [switch]$Impaired,
    [int]$Port = 7990
)
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ProjectTools.psm1')

$ErrorActionPreference = 'Stop'
$waveRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Executable = Resolve-ProjectBuildExecutable -ProjectRoot $waveRoot -Recipe 'gameplay-validation' -Executable $Executable -RequireDevelopmentTools -Network Kcp
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $waveRoot $Executable }
$waveLogs = Join-Path $waveRoot ('Logs/TimelineWaves/process-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $(if ($Dedicated) { 'dedicated' } else { 'host' }) + '-' + $(if ($Impaired) { 'impaired' } else { 'normal' }))
New-Item -ItemType Directory -Path $waveLogs -Force | Out-Null
[ordered]@{ executable=$Executable; dedicated=[bool]$Dedicated; impaired=[bool]$Impaired; port=$Port } | ConvertTo-Json | Set-Content (Join-Path $waveLogs 'run.json')
Write-Output "Timeline wave artifacts: $waveLogs"
$waveProcesses = @{}
function Launch-Wave([string]$Role) {
    $arguments = @('-batchmode', '-nographics', '-logFile', ('"' + $waveLogs + '/' + $Role + '.log"'), "--timeline-wave-role=$Role", ('"--timeline-wave-output=' + $waveLogs + '"'), "--timeline-wave-port=$Port", "--timeline-wave-dedicated=$([int]$Dedicated.IsPresent)", "--timeline-wave-impaired=$([int]$Impaired.IsPresent)")
    if ($Role -eq 'server') { $arguments += '--dedicated-server' }
    $process = Start-ProjectProcess -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $null = $process.Handle
    $waveProcesses[$Role] = $process
}
try {
    Launch-Wave $(if ($Dedicated) { 'server' } else { 'host' })
    $waveDeadline = (Get-Date).AddSeconds(60)
    while (-not (Test-Path -LiteralPath (Join-Path $waveLogs 'listening'))) {
        if ((Get-Date) -gt $waveDeadline -or @($waveProcesses.Values | Where-Object HasExited).Count) { throw 'Wave server failed to start.' }
        Start-Sleep -Milliseconds 200
    }
    Launch-Wave 'a'
    if ($Dedicated) { Launch-Wave 'b' }
    $waveDeadline = (Get-Date).AddSeconds(280)
    while (@($waveProcesses.Values | Where-Object { -not $_.HasExited }).Count) {
        if ((Get-Date) -gt $waveDeadline -or @($waveProcesses.Values | Where-Object { $_.HasExited -and $_.ExitCode -ne 0 }).Count) { throw 'Timeline wave process failed or timed out.' }
        Start-Sleep -Milliseconds 200
    }
    foreach ($role in $waveProcesses.Keys) {
        $process = $waveProcesses[$role]; Wait-ProjectProcess -Process $process
        $log = Get-Content -LiteralPath (Join-Path $waveLogs ($role + '.log')) -Raw
        if ($process.ExitCode -ne 0 -or -not $log.Contains("[TimelineWaveProcess] result=PASS role=$role") -or $log -match '(?m)^\w*Exception:|Invalid server simulation snapshot') { throw "Timeline wave validation failed: $role" }
        Write-Output "Timeline waves role=$role exit=$($process.ExitCode)"
    }
    Write-Output 'Six real waves, prefab identities, repeating tail, melee and projectile lifecycle PASS.'
}
finally {
    foreach ($role in $waveProcesses.Keys) {
        $process = $waveProcesses[$role]
        if (-not $process.HasExited) { Stop-ProjectProcess -Id $process.Id -Force }
        else { Wait-ProjectProcess -Process $process; Set-Content (Join-Path $waveLogs ($role + '.exit.txt')) $process.ExitCode }
    }
}
