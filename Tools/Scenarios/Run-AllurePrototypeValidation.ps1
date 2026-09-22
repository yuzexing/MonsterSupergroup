param(
    [string]$Executable = 'Builds/AllurePrototype/AllurePrototype.exe',
    [ValidateSet('normal', 'impaired')][string]$Profile = 'normal',
    [int]$Port = 7999,
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
$run = Join-Path $ArtifactsRoot ('allure-network-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Profile)
New-Item -ItemType Directory -Path $run -Force | Out-Null
$processes = @{}
function Launch([string]$role) {
    # Use actual rendered gameplay cameras so each owner reports its own real view.
    $arguments = @('-force-d3d11', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
        '-logFile', ('"' + (Join-Path $run ($role + '.log')) + '"'), "--allure-role=$role",
        ('"--allure-artifacts=' + $run + '"'), "--allure-port=$Port")
    if ($Profile -eq 'impaired') { $arguments += '--allure-impaired' }
    $processes[$role] = Start-ProjectProcess -FilePath $Executable -WindowStyle Hidden -PassThru -ArgumentList $arguments
    $null = $processes[$role].Handle
}
Write-Output "Allure Host + Client artifacts: $run"
Get-FileHash -Algorithm SHA256 -LiteralPath $Executable | Format-List | Out-File (Join-Path $run 'build-sha256.txt')
# The executable is Unity's launcher stub. Hash the gameplay and probe assemblies as well,
# using stable relative paths so manifests from both profiles can be compared verbatim.
$buildDirectory = Split-Path -Parent (Resolve-Path -LiteralPath $Executable).Path
$dataDirectory = Join-Path $buildDirectory ([IO.Path]::GetFileNameWithoutExtension($Executable) + '_Data')
$managedDirectory = Join-Path $dataDirectory 'Managed'
foreach ($assembly in @('MonsterSupergroup.NetworkCombat.dll', 'MonsterSupergroup.Gameplay.Tests.PlayMode.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $managedDirectory $assembly))) {
        throw "Required validation assembly is missing: $assembly"
    }
}
$manifestFiles = @((Get-Item -LiteralPath $Executable)) + @(Get-ChildItem -LiteralPath $managedDirectory -Filter '*.dll' |
    Where-Object { $_.Name -eq 'MonsterSupergroup.NetworkCombat.dll' -or $_.Name -like '*.Gameplay*.dll' })
$manifest = [ordered]@{
    schemaVersion = 1
    files = @($manifestFiles | Sort-Object FullName -Unique | ForEach-Object {
        [ordered]@{
            path = $_.FullName.Substring($buildDirectory.Length + 1).Replace('\', '/')
            bytes = $_.Length
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
        }
    })
}
[IO.File]::WriteAllText((Join-Path $run 'build-manifest.json'), ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
try {
    Launch 'host'
    $deadline = (Get-Date).AddSeconds(60)
    while (-not (Test-Path -LiteralPath (Join-Path $run 'listening'))) {
        if ((Get-Date) -gt $deadline -or $processes.host.HasExited) { throw "Host failed to listen: $run" }
        Start-Sleep -Milliseconds 200
    }
    Launch 'client'
    $deadline = (Get-Date).AddSeconds(240)
    while (@($processes.Values | Where-Object { -not $_.HasExited }).Count -gt 0) {
        if ((Get-Date) -gt $deadline) { throw "Allure validation timed out: $run" }
        if (@($processes.Values | Where-Object { $_.HasExited -and $_.ExitCode -ne 0 }).Count -gt 0) {
            throw "Allure validation failed: $run"
        }
        Start-Sleep -Milliseconds 200
    }
    foreach ($role in @('host', 'client')) {
        Wait-ProjectProcess -Process $processes[$role]
        if ($processes[$role].ExitCode -ne 0 -or
            -not (Test-Path -LiteralPath (Join-Path $run ('result-' + $role))) -or
            (Get-Content -LiteralPath (Join-Path $run ('result-' + $role)) -Raw).Trim() -ne 'PASS') {
            throw "Allure $role did not pass: $run"
        }
    }
    Get-Content -LiteralPath (Join-Path $run 'server-verified')
    Write-Output "PASS ($Profile): $run"
}
finally {
    foreach ($process in $processes.Values) {
        if (-not $process.HasExited) { Stop-ProjectProcess -Id $process.Id -Force }
    }
}
