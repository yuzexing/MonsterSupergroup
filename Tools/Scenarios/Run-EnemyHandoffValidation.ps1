param(
    [string]$Executable = 'Builds/EnemyHandoff/EnemyHandoff.exe',
    [ValidateSet('normal', 'impaired')][string]$Profile = 'normal',
    [ValidateRange(2, 600)][int]$Duration = 120,
    [ValidateSet('NetworkEnemySkeleton', 'NetworkEnemySkeletonExample', 'NetworkEnemyLustSinner', 'NetworkEnemyImp')][string]$SkeletonPrefab = 'NetworkEnemySkeleton',
    [int]$Port = 7993
)
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ProjectTools.psm1')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $projectRoot $Executable }
if (-not (Test-Path -LiteralPath $Executable)) { throw "Missing build: $Executable" }
$logRoot = Join-Path $projectRoot ('Logs/EnemyHandoff/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Profile)
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
Write-Output "Enemy handoff artifacts: $logRoot"
$processes = @{}
function Launch([string]$role) {
    $arguments = @('-batchmode', '-nographics', '-logFile', ('"' + $logRoot + '/' + $role + '.log"'),
        "--enemy-handoff-role=$role", ('"--enemy-handoff-artifacts=' + $logRoot + '"'),
        "--enemy-handoff-port=$Port", "--enemy-handoff-profile=$Profile", "--enemy-handoff-duration=$Duration", "--enemy-handoff-prefab=$SkeletonPrefab")
    $processes[$role] = Start-ProjectProcess -FilePath $Executable -WindowStyle Hidden -PassThru -ArgumentList $arguments
    $null = $processes[$role].Handle
}
try {
    Launch 'host'
    $deadline = (Get-Date).AddSeconds(60)
    while (-not (Test-Path -LiteralPath (Join-Path $logRoot 'ready-host'))) {
        if ((Get-Date) -gt $deadline -or $processes.host.HasExited) { throw "Host not ready: $logRoot" }
        Start-Sleep -Milliseconds 200
    }
    Launch 'a'
    Launch 'b'
    $deadline = (Get-Date).AddSeconds($Duration * 2 + 250)
    while (@($processes.Values | Where-Object { -not $_.HasExited }).Count -gt 0) {
        if ((Get-Date) -gt $deadline) { throw "Validation timeout: $logRoot" }
        if (@($processes.Values | Where-Object { $_.HasExited -and $_.ExitCode -ne 0 }).Count -gt 0) { throw "Validation process failed: $logRoot" }
        Start-Sleep -Milliseconds 200
    }
    foreach ($role in $processes.Keys) {
        Wait-ProjectProcess -Process $processes[$role]
        if (Select-String -LiteralPath (Join-Path $logRoot ($role + '.log')) -Pattern '^[\w.]*Exception:' -Quiet) {
            throw "Unhandled runtime exception for $role : $logRoot"
        }
        if ($processes[$role].ExitCode -ne 0 -or -not (Select-String -LiteralPath (Join-Path $logRoot ($role + '.log')) -SimpleMatch "[EnemyHandoffProcess] result=PASS role=$role" -Quiet)) {
            throw "Validation failed for $role : $logRoot"
        }
    }
    Get-Content -LiteralPath (Join-Path $logRoot 'results.txt')
    Write-Output "Host + two clients passed: $logRoot"
}
finally {
    foreach ($process in $processes.Values) { if (-not $process.HasExited) { Stop-ProjectProcess -Id $process.Id -Force } }
}
