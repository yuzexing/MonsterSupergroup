param(
    [string]$Executable = '',
    [ValidateSet('party','offline','invites','combat-menu','combat-menu-solo','run-end','run-end-solo','local-party','local-solo','local-errors','local-admission','local-release')][string]$Profile = 'party',
    [int]$Width = 1280, [int]$Height = 720, [int]$Port = 7998,
    [switch]$Headless,
    [switch]$MixedLanguages,
    [string]$Language = '',
    [switch]$Visible
)
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ProjectTools.psm1')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Executable = Resolve-ProjectBuildExecutable -ProjectRoot $projectRoot -Recipe 'gameplay-validation' -Executable $Executable -RequireDevelopmentTools
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $projectRoot $Executable }
if (-not (Test-Path -LiteralPath $Executable)) { throw "Missing validation build: $Executable" }
$logRoot = Join-Path $projectRoot ('Logs/PreparationMenu/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Profile + '-p' + $Port)
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
Write-Output "Preparation menu artifacts: $logRoot"
$processes = @{}
$headlessMode = $Headless -or -not $Visible
function Launch([string]$role) {
    $roleArgument = if ($Profile.StartsWith('local-')) { "--local-menu-role=$role" } else { "--menu-role=$role" }
    $arguments = @('-force-d3d11', '-screen-fullscreen', '0', '-screen-width', "$Width", '-screen-height', "$Height",
        '-logFile', ('"' + $logRoot + '/' + $role + '.log"'), $roleArgument, "--menu-profile=$Profile",
        ('"--menu-artifacts=' + $logRoot + '"'), "--menu-port=$Port")
    if ($headlessMode) { $arguments += @('-batchmode', '-nographics') }
    if ($MixedLanguages) { $arguments += $(if ($role -eq 'host') { '--menu-locale=zh-CN' } else { '--menu-locale=en' }) }
    elseif (-not [string]::IsNullOrWhiteSpace($Language)) { $arguments += "--menu-locale=$Language" }
    $windowStyle = if ($Visible -and -not $headlessMode) { 'Normal' } else { 'Hidden' }
    $processes[$role] = Start-ProjectProcess -FilePath $Executable -WindowStyle $windowStyle -PassThru -ArgumentList $arguments
    $null = $processes[$role].Handle
}
function Wait-Signal([string]$name) {
    $until = (Get-Date).AddSeconds(80)
    while (-not (Test-Path -LiteralPath (Join-Path $logRoot $name))) {
        if ((Get-Date) -gt $until -or @($processes.Values | Where-Object { $_.HasExited -and $_.ExitCode -ne 0 }).Count -gt 0) {
            throw "Menu validation stopped before $name : $logRoot"
        }
        Start-Sleep -Milliseconds 250
    }
}
try {
    Launch 'host'
    if ($Profile -eq 'party' -or $Profile -eq 'local-admission') {
        Wait-Signal 'host-open'
        Launch 'a'
        Launch 'b'
        Launch 'c'
        Wait-Signal 'four-seats'
        Launch 'fifth'
    }
    if ($Profile -eq 'combat-menu' -or $Profile -eq 'run-end' -or $Profile -eq 'local-party') {
        Wait-Signal 'host-open'
        Launch 'a'
        Launch 'b'
    }
    $deadline = (Get-Date).AddSeconds(420)
    while (@($processes.Values | Where-Object { -not $_.HasExited }).Count -gt 0) {
        if ((Get-Date) -gt $deadline) { throw "Menu validation timed out: $logRoot" }
        if (@($processes.Values | Where-Object { $_.HasExited -and $_.ExitCode -ne 0 }).Count -gt 0) { throw "Menu validation process failed: $logRoot" }
        Start-Sleep -Milliseconds 250
    }
    foreach ($role in $processes.Keys) {
        Wait-ProjectProcess -Process $processes[$role]
        $log = Join-Path $logRoot ($role + '.log')
        if ($processes[$role].ExitCode -ne 0 -or -not (Select-String -LiteralPath $log -SimpleMatch "[MenuProcess] result=PASS role=$role" -Quiet)) {
            throw "Menu validation failed for $role (exit=$($processes[$role].ExitCode)) : $logRoot"
        }
        if (Select-String -LiteralPath $log -Pattern 'NullReferenceException|MissingReferenceException|InvalidOperationException|error CS' -Quiet) {
            throw "Unexpected exception for $role : $logRoot"
        }
    }
    Write-Output "Preparation menu $Profile validation passed: $logRoot"
}
finally {
    foreach ($process in $processes.Values) { if (-not $process.HasExited) { Stop-ProjectProcess -Id $process.Id -Force -ErrorAction SilentlyContinue } }
}
