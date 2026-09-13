param(
    [string]$Executable = 'Builds/NordicStaticSample/NordicStaticSample.exe',
    [string]$OutputDirectory = 'Logs/NordicStaticSample/Acceptance'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $projectRoot $Executable }
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory = Join-Path $projectRoot $OutputDirectory }
if (-not (Test-Path -LiteralPath $Executable)) { throw "Build the Nordic standalone preview first: $Executable" }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$arguments = @(
    '-batchmode',
    '-screen-fullscreen', '0', '-screen-width', '1920', '-screen-height', '1080',
    '-force-d3d11', '-logFile', ('"' + (Join-Path $OutputDirectory 'player.log') + '"'),
    ('"--nordic-validate=' + $OutputDirectory + '"')
)
$startedAt = [DateTime]::UtcNow
$process = Start-Process -FilePath $Executable -ArgumentList $arguments -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
$null = $process.Handle
try {
    if (-not $process.WaitForExit(120000)) { throw "Nordic validation timed out: $OutputDirectory" }
    $resultPath = Join-Path $OutputDirectory 'acceptance.json'
    if (-not (Test-Path -LiteralPath $resultPath)) { throw "Validation did not write a result; see $OutputDirectory/player.log" }
    if ((Get-Item -LiteralPath $resultPath).LastWriteTimeUtc -lt $startedAt) { throw "Validation produced no fresh result; see $OutputDirectory/player.log" }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or -not $result.passed) {
        $result.errors | Write-Output
        throw "Nordic validation failed: $resultPath"
    }
    Write-Output ("Nordic acceptance passed: {0} checks; screenshots: {1}" -f $result.checks.Count, $OutputDirectory)
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
