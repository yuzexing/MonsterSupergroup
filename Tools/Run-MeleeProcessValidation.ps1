param(
    [string]$Executable = 'Builds/Phase02/MeleeValidation.exe',
    [switch]$Dedicated,
    [int]$Port = 7908,
    [string]$LogDirectory = 'Logs/Phase02'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $projectRoot $Executable }
if (-not (Test-Path -LiteralPath $Executable)) { throw "Missing validation build: $Executable" }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$modeName = if ($Dedicated) { 'Dedicated' } else { 'Host' }
$logRoot = Join-Path (Join-Path $projectRoot $LogDirectory) "Process-$modeName-$Port-$stamp"
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$processes = @{}
function Launch([string]$role) {
    $arguments = @('-batchmode','-nographics','-logFile',"`"$logRoot/$role.log`"",
        "--melee-role=$role","`"--melee-sync=$logRoot`"","--melee-port=$Port")
    if ($role -eq 'server') { $arguments += '--dedicated-server' }
    $processes[$role] = Start-Process -FilePath $Executable -ArgumentList $arguments -PassThru -WindowStyle Hidden
}
try {
    $authority = if ($Dedicated) { 'server' } else { 'host' }
    Launch $authority
    $deadline = (Get-Date).AddSeconds(45)
    while (-not (Test-Path -LiteralPath "$logRoot/listening")) {
        if ($processes[$authority].HasExited -or (Get-Date) -gt $deadline) { throw "Authority failed to start: $logRoot" }
        Start-Sleep -Milliseconds 250
    }
    Launch 'client'
    if ($Dedicated) {
        # Wait for the first participant so the fixture can identify which peer will reconnect.
        Start-Sleep -Seconds 2
        Launch 'client2'
    }
    $deadline = (Get-Date).AddSeconds(170)
    while (($processes.Values | Where-Object { -not $_.HasExited }).Count -gt 0) {
        if (($processes.Values | Where-Object { $_.HasExited -and $_.ExitCode -ne 0 }).Count -gt 0) {
            throw "A validation player failed: $logRoot"
        }
        if ((Get-Date) -gt $deadline) { throw "Validation timed out: $logRoot" }
        Start-Sleep -Milliseconds 500
    }
    foreach ($role in $processes.Keys) {
        $processes[$role].Refresh()
        if ($processes[$role].ExitCode -ne 0 -or -not (Select-String -LiteralPath "$logRoot/$role.log" -SimpleMatch "result=PASS role=$role" -Quiet)) {
            throw "Validation failed for ${role}: $logRoot"
        }
    }
    Write-Output "Melee validation passed: $logRoot"
} finally {
    foreach ($process in $processes.Values) {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}
