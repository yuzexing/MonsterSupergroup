[CmdletBinding()]
param(
    [string]$Unity = $env:UNITY_EDITOR_PATH,
    [string]$ProjectPath,
    [string]$Output,
    [ValidateRange(0.25,3600)][double]$Seconds = 180,
    [ValidateRange(1,20)][int]$Repeats = 3,
    [ValidateRange(0,600)][double]$CatchupSeconds = 45,
    [ValidateRange(60,14400)][int]$TimeoutSeconds = 14400,
    [switch]$Smoke,
    [switch]$IndependentEngines
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
if (-not $Unity -or -not (Test-Path -LiteralPath $Unity -PathType Leaf)) { throw 'Specify -Unity pointing to Unity 6000.3.21f1 Editor/Unity.exe, or set UNITY_EDITOR_PATH.' }
if (-not $ProjectPath) { $ProjectPath = Join-Path $workspace 'Logs/SteamNetworkValidation/project' }
if (-not (Test-Path -LiteralPath (Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt'))) { throw 'Specify an existing validation Unity project with -ProjectPath.' }
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not $Output) { $Output = Join-Path $workspace ('Logs/CombatEvidenceLoadBenchmark/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force -Path $Output | Out-Null
if ($Smoke) { $Seconds = 1; $Repeats = 1; $CatchupSeconds = 15 }
$settings = @{
    COMBAT_EVIDENCE_BENCHMARK = '1'
    COMBAT_EVIDENCE_BENCHMARK_SECONDS = $Seconds.ToString([Globalization.CultureInfo]::InvariantCulture)
    COMBAT_EVIDENCE_BENCHMARK_REPEATS = $Repeats.ToString([Globalization.CultureInfo]::InvariantCulture)
    COMBAT_EVIDENCE_BENCHMARK_CATCHUP_SECONDS = $CatchupSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
    COMBAT_EVIDENCE_BENCHMARK_OUTPUT = $Output
    COMBAT_EVIDENCE_BENCHMARK_INDEPENDENT_ENGINES = if ($IndependentEngines) { '1' } else { '0' }
}
$previous = @{}
try {
    foreach ($key in $settings.Keys) { $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process'); [Environment]::SetEnvironmentVariable($key, $settings[$key], 'Process') }
    $results = Join-Path $Output 'results.xml'
    $log = Join-Path $Output 'unity.log'
    $arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $ProjectPath + '"'), '-runTests', '-testPlatform', 'EditMode',
        '-testFilter', 'MonsterSupergroup.NetworkCombat.Tests.CombatEvidenceLoadBenchmarks', '-testResults', ('"' + $results + '"'), '-logFile', ('"' + $log + '"'))
    Write-Output "Status CPU/storage benchmark: 9 combinations x $Repeats repetition(s) x $Seconds seconds; output: $Output"
    $process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { Stop-Process -Id $process.Id; throw "Benchmark timed out. Existing evidence and partial reports remain in $Output" }
    $rows = @(Get-ChildItem -LiteralPath $Output -Filter benchmark.json -Recurse | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json })
    $comparisons = @($rows | ForEach-Object {
        $row = $_
        $baseline = $rows | Where-Object { $_.controllers -eq $row.controllers -and $_.repeat -eq $row.repeat -and $_.mode -eq 'off' } | Select-Object -First 1
        [pscustomobject]@{
            controllers = $row.controllers; mode = $row.mode; repeat = $row.repeat
            p95Ms = $row.mainFrame.p95Ms; p99Ms = $row.mainFrame.p99Ms
            p95IncrementMs = if ($baseline) { $row.mainFrame.p95Ms - $baseline.mainFrame.p95Ms } else { $null }
            p99IncrementMs = if ($baseline) { $row.mainFrame.p99Ms - $baseline.mainFrame.p99Ms } else { $null }
            allocations = $row.mainThreadAllocatedBytes; allocationMeasurementAvailable = $row.allocationMeasurementAvailable; budgetPeak = $row.diagnosticBudgetPeakBytes
            eventBytes = $row.eventBytes; dropped = ($row.dropped | Measure-Object -Sum).Sum
            businessMatches = $row.businessMatches; catchupComplete = $row.catchupComplete; elapsedSeconds = $row.elapsedSeconds
        }
    })
    $comparisons | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Output 'comparison.json') -Encoding UTF8
    $comparisons | Format-Table -AutoSize
    if (-not (Test-Path -LiteralPath $results)) { throw "Unity did not produce test results; inspect $log" }
    [xml]$testResults = Get-Content -LiteralPath $results -Raw
    if ($testResults.'test-run'.result -ne 'Passed') { throw "Benchmark assertions failed; inspect $results and each benchmark.json" }
    Write-Output 'This is an EditMode status/Gateway/Replica CPU probe with a synthetic transport. Game frame time, GPU, real Steam, and actual enemy physics still require the gameplay tests.'
}
finally { foreach ($key in $settings.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') } }
