param(
    [string]$Executable = '',
    [switch]$Dedicated,
    [switch]$Impaired,
    [switch]$Graphics,
    [int]$Port = 7987
)
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ProjectTools.psm1')

$ErrorActionPreference = 'Stop'
$impRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Executable = Resolve-ProjectBuildExecutable -ProjectRoot $impRoot -Recipe 'gameplay-validation' -Executable $Executable -RequireDevelopmentTools -Network Kcp
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $impRoot $Executable }
$impLogs = Join-Path $impRoot ('Logs/Imp/process-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $(if ($Dedicated) { 'dedicated' } else { 'host' }) + '-' + $(if ($Impaired) { 'impaired' } else { 'normal' }))
New-Item -ItemType Directory -Path $impLogs -Force | Out-Null
Write-Output "Imp process artifacts: $impLogs"
$impProcesses = @{}
function Launch-Imp([string]$Role) {
    $arguments = @('-logFile', ('"' + $impLogs + '/' + $Role + '.log"'), "--imp-role=$Role", ('"--imp-output=' + $impLogs + '"'), "--imp-port=$Port", "--imp-dedicated=$([int]$Dedicated.IsPresent)", "--imp-impaired=$([int]$Impaired.IsPresent)")
    if ($Graphics -and $Role -ne 'server') { $arguments += @('-force-d3d11', '-force-gfx-direct', '-screen-width', '1280', '-screen-height', '720', '--imp-graphics=1') }
    else { $arguments += @('-batchmode', '-nographics') }
    $process = Start-ProjectProcess -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $null = $process.Handle
    $impProcesses[$Role] = $process
}
try {
    Launch-Imp $(if ($Dedicated) { 'server' } else { 'host' })
    $impDeadline = (Get-Date).AddSeconds(70)
    while (-not (Test-Path -LiteralPath (Join-Path $impLogs 'ready-server'))) {
        if ((Get-Date) -gt $impDeadline -or @($impProcesses.Values | Where-Object HasExited).Count) { throw 'Server did not start.' }
        Start-Sleep -Milliseconds 200
    }
    Launch-Imp 'a'
    Launch-Imp 'b'
    $impDeadline = (Get-Date).AddSeconds(190)
    while (@($impProcesses.Values | Where-Object { -not $_.HasExited }).Count) {
        if ((Get-Date) -gt $impDeadline -or @($impProcesses.Values | Where-Object { $_.HasExited -and $_.ExitCode -ne 0 }).Count) { throw 'Imp process failed or timed out.' }
        Start-Sleep -Milliseconds 200
    }
    foreach ($role in $impProcesses.Keys) {
        $process = $impProcesses[$role]; Wait-ProjectProcess -Process $process
        $log = Get-Content -LiteralPath (Join-Path $impLogs ($role + '.log')) -Raw
        if ($process.ExitCode -ne 0 -or -not $log.Contains("[ImpProcess] result=PASS role=$role") -or $log -match '(?m)^\w*Exception:|Invalid server simulation snapshot|Duplicate projectile spawn') { throw "Imp validation failed: $role" }
    }
    Write-Output 'Imp launch, local hit, remote termination, disposal, expiry and server simulation PASS.'
}
finally {
    foreach ($process in $impProcesses.Values) { if (-not $process.HasExited) { Stop-ProjectProcess -Id $process.Id -Force } }
}
