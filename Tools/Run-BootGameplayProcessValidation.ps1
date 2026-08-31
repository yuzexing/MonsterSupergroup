param(
    [string]$Executable = "Builds/BootGameplayValidation/MonsterSupergroupBootGameplayValidation.exe",
    [ValidateRange(1, 65535)]
    [int]$Port = 7801,
    [ValidateRange(20, 300)]
    [int]$ProcessTimeoutSeconds = 70
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not [System.IO.Path]::IsPathRooted($Executable)) {
    $Executable = Join-Path $projectRoot $Executable
}
if (-not (Test-Path -LiteralPath $Executable)) {
    throw "Validation executable not found: $Executable"
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$logDirectory = Join-Path $projectRoot "Logs/BootGameplayProcessValidation"
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$hostLog = Join-Path $logDirectory "host-$stamp.log"
$clientLog = Join-Path $logDirectory "client-$stamp.log"
$validationTimeout = [Math]::Max(20, $ProcessTimeoutSeconds - 10)

function Start-ValidationProcess {
    param(
        [string]$Role,
        [string]$LogPath
    )

    $arguments = @(
        "-batchmode",
        "-nographics",
        "-logFile",
        $LogPath,
        "--boot-gameplay-role=$Role",
        "--boot-gameplay-address=127.0.0.1",
        "--boot-gameplay-port=$Port",
        "--boot-gameplay-timeout=$validationTimeout"
    )
    return Start-Process `
        -FilePath $Executable `
        -ArgumentList $arguments `
        -PassThru `
        -WindowStyle Hidden
}

function Wait-ForLogMarker {
    param(
        [string]$Path,
        [string]$Marker,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path -LiteralPath $Path) -and
            (Select-String -LiteralPath $Path -SimpleMatch $Marker -Quiet)) {
            return $true
        }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

$hostProcess = $null
$clientProcess = $null
try {
    $hostProcess = Start-ValidationProcess -Role "host" -LogPath $hostLog
    if (-not (Wait-ForLogMarker `
        -Path $hostLog `
        -Marker "event=host-ready-for-late-join" `
        -TimeoutSeconds 25)) {
        throw "Host did not reach Gameplay before Late Join. Log: $hostLog"
    }

    $clientProcess = Start-ValidationProcess -Role "client" -LogPath $clientLog
    if (-not $clientProcess.WaitForExit($ProcessTimeoutSeconds * 1000)) {
        throw "Client validation timed out. Log: $clientLog"
    }
    if (-not $hostProcess.WaitForExit($ProcessTimeoutSeconds * 1000)) {
        throw "Host validation timed out. Log: $hostLog"
    }

    $hostPassed = Wait-ForLogMarker `
        -Path $hostLog `
        -Marker "result=PASS role=Host" `
        -TimeoutSeconds 1
    $clientPassed = Wait-ForLogMarker `
        -Path $clientLog `
        -Marker "result=PASS role=Client" `
        -TimeoutSeconds 1
    if ($hostProcess.ExitCode -ne 0 -or -not $hostPassed) {
        throw "Host validation failed with exit code $($hostProcess.ExitCode). Log: $hostLog"
    }
    if ($clientProcess.ExitCode -ne 0 -or -not $clientPassed) {
        throw "Client validation failed with exit code $($clientProcess.ExitCode). Log: $clientLog"
    }

    Write-Output "Boot Gameplay process validation passed."
    Write-Output "Host log: $hostLog"
    Write-Output "Client log: $clientLog"
}
finally {
    if ($clientProcess -ne $null -and -not $clientProcess.HasExited) {
        Stop-Process -Id $clientProcess.Id -Force
    }
    if ($hostProcess -ne $null -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force
    }
}
