param(
    [ValidateSet('host', 'client', 'both')][string]$Role = 'both',
    [string]$Executable = 'Builds/AllurePrototype/AllurePrototype.exe',
    [string]$Address = '127.0.0.1',
    [ValidateRange(1, 65535)][int]$Port = 8000,
    [ValidateRange(1, 4)][int]$WaitFor = 2,
    [string]$LogsRoot = ''
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ProjectTools.psm1')
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $projectRoot $Executable }
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw "Missing prototype build: $Executable" }
if ([string]::IsNullOrWhiteSpace($Address)) { throw 'Address cannot be empty.' }
if ([string]::IsNullOrWhiteSpace($LogsRoot)) { $LogsRoot = Join-Path $projectRoot 'Logs/PrototypeAbilities' }
$session = Join-Path $LogsRoot ('manual-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $session -Force | Out-Null
$readyFile = Join-Path $session 'host-listening'
$processes = @{}

function Start-ManualPrototype([string]$playerRole) {
    $arguments = @('-force-d3d11', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
        '-logFile', ('"' + (Join-Path $session ($playerRole + '.log')) + '"'),
        "--prototype-playtest=$playerRole", ('"--prototype-address=' + $Address + '"'),
        "--prototype-port=$Port", "--prototype-wait-for=$WaitFor")
    if ($playerRole -eq 'host') { $arguments += ('"--prototype-ready-file=' + $readyFile + '"') }
    # This command explicitly starts visible manual playtest windows for the user.
    $processes[$playerRole] = Start-ProjectProcess -FilePath $Executable -WindowStyle Normal -PassThru -ArgumentList $arguments
    $null = $processes[$playerRole].Handle
    Write-Output "$playerRole PID: $($processes[$playerRole].Id)"
}

if ($Role -eq 'host' -or $Role -eq 'both') { Start-ManualPrototype 'host' }
if ($Role -eq 'both') {
    $deadline = (Get-Date).AddSeconds(90)
    while (-not (Test-Path -LiteralPath $readyFile)) {
        if ($processes.host.HasExited) { throw "Host exited during startup. Inspect $session" }
        if ((Get-Date) -gt $deadline) { throw "Host is not ready yet. Its window remains open; inspect $session" }
        Start-Sleep -Milliseconds 200
    }
}
if ($Role -eq 'client' -or $Role -eq 'both') { Start-ManualPrototype 'client' }
Get-FileHash -Algorithm SHA256 -LiteralPath $Executable | Format-List | Out-File (Join-Path $session 'build-sha256.txt')
Write-Output "Manual prototype session: $session"
Write-Output 'Click the desired window to control that player. 1 Gluttony; 2 Music (R, Space); 3 Allure (R throw, T take, F decoy).'
Write-Output 'Normal attacks, damage, drops, XP, and upgrades remain active. Close each game window when finished.'
Write-Output 'For a solo decoy test, use -Role host -WaitFor 1. For another computer, use -Role client -Address <host IP>.'
