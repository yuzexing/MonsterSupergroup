param(
    [string]$Executable = 'Builds/EnemyDefinitions20260920/MonsterSupergroup.exe',
    [int]$Port = 8039,
    [string]$OutputDirectory = '',
    [switch]$Headless,
    [int]$TimeoutSeconds = 240
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$exe = if ([IO.Path]::IsPathRooted($Executable)) { $Executable } else { Join-Path $project $Executable }
if (!(Test-Path -LiteralPath $exe)) { throw "Build the validation Player first: $exe" }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $project ('Logs/EnemyDefinitions/Process-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Use a new output directory to avoid stale handshake markers: $output" }
New-Item -ItemType Directory -Path $output | Out-Null
$started = Get-Date
$children = @{}
$results = @()
try {
    foreach ($role in @('host', 'client')) {
        $arguments = @('-force-d3d11', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
            "--enemy-definition-role=$role", "--enemy-definition-port=$Port", "--enemy-definition-output=`"$output`"",
            '-logFile', "`"$(Join-Path $output ($role + '.log'))`"")
        if ($Headless) { $arguments += @('-batchmode', '-nographics') }
        $children[$role] = Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -ArgumentList $arguments -PassThru
        Write-Output "Started $role PID=$($children[$role].Id)"
    }
    $deadline = $started.AddSeconds($TimeoutSeconds)
    while (($children.Values | Where-Object { !$_.HasExited }).Count -gt 0 -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        foreach ($process in $children.Values) { $process.Refresh() }
    }
    foreach ($role in @('host', 'client')) {
        $process = $children[$role]; $process.Refresh()
        $timedOut = !$process.HasExited
        if ($timedOut) { Stop-Process -Id $process.Id; $process.WaitForExit() }
        $resultPath = Join-Path $output ($role + '-result.json')
        $probe = if (Test-Path -LiteralPath $resultPath) { [IO.File]::ReadAllText($resultPath) | ConvertFrom-Json } else { $null }
        $results += [pscustomobject]@{ role=$role; pid=$process.Id; exitCode=$process.ExitCode; timedOut=$timedOut;
            probePassed=($null -ne $probe -and $probe.passed); detail=$probe.detail }
    }
    $passed = ($results | Where-Object { $_.timedOut -or $_.exitCode -ne 0 -or !$_.probePassed }).Count -eq 0
    $report = [pscustomobject]@{ passed=$passed; started=$started.ToString('O'); finished=(Get-Date).ToString('O');
        executable=$exe; headless=[bool]$Headless; output=$output; peers=$results }
    $report | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $output 'result.json')
    $report | ConvertTo-Json -Depth 5
    if (!$passed) { throw "Enemy definition multi-process verification failed: $output" }
} finally {
    foreach ($process in $children.Values) {
        $process.Refresh()
        if (!$process.HasExited) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
    }
}
