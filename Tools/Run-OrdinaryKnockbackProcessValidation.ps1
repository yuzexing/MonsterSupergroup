param(
    [string]$Executable = 'Builds/M3Knockback/M3Knockback.exe',
    [switch]$Dedicated,
    [switch]$ImpairedNetwork,
    [switch]$CaptureFrames,
    [switch]$VisibleWindows,
    [switch]$ForceD3D11,
    [switch]$IsolateTemporaryCache,
    [string]$PsoCacheSeed,
    [int]$Port = 7962
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $projectRoot $Executable }
if (-not (Test-Path -LiteralPath $Executable)) { throw "Missing M3 validation build: $Executable" }
if ($IsolateTemporaryCache -and -not (Get-Command Start-Process).Parameters.ContainsKey('Environment')) {
    throw 'IsolateTemporaryCache requires PowerShell 7.4 or later.'
}
if ($PsoCacheSeed) {
    if (-not $IsolateTemporaryCache) { throw 'PsoCacheSeed requires IsolateTemporaryCache.' }
    if (-not [IO.Path]::IsPathRooted($PsoCacheSeed)) { $PsoCacheSeed = Join-Path $projectRoot $PsoCacheSeed }
    $PsoCacheSeed = (Resolve-Path -LiteralPath $PsoCacheSeed).Path
}
$modeName = if ($Dedicated) { 'Dedicated' } else { 'Host' }
$logRoot = Join-Path $projectRoot ('Logs/M3Knockback/' + $modeName + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-p' + $Port)
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
[ordered]@{
    executable = $Executable
    dedicated = [bool]$Dedicated
    impairedNetwork = [bool]$ImpairedNetwork
    captureFrames = [bool]$CaptureFrames
    forceD3D11 = [bool]$ForceD3D11
    isolateTemporaryCache = [bool]$IsolateTemporaryCache
    psoCacheSeedSha256 = $(if ($PsoCacheSeed) { (Get-FileHash -LiteralPath $PsoCacheSeed -Algorithm SHA256).Hash } else { $null })
    port = $Port
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logRoot 'run.json')
Write-Output "M3 process artifacts: $logRoot"
$processes = @{}
function Launch([string]$role) {
    $arguments = @('-logFile', ('"' + $logRoot + '/' + $role + '.log"'), "--m3-role=$role",
        ('"--m3-artifacts=' + $logRoot + '"'), "--m3-port=$Port", '-screen-fullscreen','0','-screen-width','1920','-screen-height','1080')
    if ($Dedicated) { $arguments += '--m3-dedicated' }
    if ($ForceD3D11) { $arguments += '-force-d3d11' }
    if ($ImpairedNetwork) { $arguments += '--m3-impaired' }
    if ($CaptureFrames -and $role -ne 'server') { $arguments += '--m3-capture' }
    else { $arguments += @('-batchmode','-nographics') }
    if ($role -eq 'server') { $arguments += '--dedicated-server' }
    $style = if ($VisibleWindows -and $role -ne 'server') { 'Normal' } else { 'Hidden' }
    $launchOptions = @{}
    if ($IsolateTemporaryCache) {
        # Diagnostic isolation: override only child-process TEMP/TMP.
        # Leave the user's environment and shared cache intact.
        $temporaryCache = Join-Path $logRoot ($role + '-temp')
        New-Item -ItemType Directory -Path $temporaryCache -Force | Out-Null
        if ($PsoCacheSeed) {
            # Diagnostic control: use identical cache bytes without sharing a writable file between players.
            $cacheDirectory = Join-Path $temporaryCache 'DefaultCompany/Monster Supergroup'
            New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
            Copy-Item -LiteralPath $PsoCacheSeed -Destination (Join-Path $cacheDirectory 'dx12_pso_cache_lib.bin')
        }
        $launchOptions.Environment = @{ TEMP = $temporaryCache; TMP = $temporaryCache }
    }
    $processes[$role] = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle $style -PassThru @launchOptions
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
