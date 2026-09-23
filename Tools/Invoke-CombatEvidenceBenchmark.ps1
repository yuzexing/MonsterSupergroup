[CmdletBinding()]
param(
    [string]$Unity = $env:UNITY_EDITOR_PATH,
    [string]$Python = 'python',
    [string]$ProjectPath,
    [string]$Output,
    [ValidateRange(0.25,3600)][double]$Seconds = 180,
    [ValidateRange(1,20)][int]$Repeats = 3,
    [ValidateRange(0,600)][double]$CatchupSeconds = 45,
    [ValidateRange(60,14400)][int]$TimeoutSeconds = 14400,
    [switch]$Smoke,
    [switch]$IndependentEngines,
    [switch]$PrepareOnly,
    [switch]$RequireCompleteMeasurements
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
if (-not $ProjectPath) { $ProjectPath = Join-Path $workspace 'Logs/CombatEvidenceNextValidation/project' }
if (-not $Output) { $Output = Join-Path $workspace ('Logs/CombatEvidenceLoadBenchmark/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$Output = [IO.Path]::GetFullPath($Output)
if ($Smoke) { $Seconds = 1; $Repeats = 1; $CatchupSeconds = 15 }
if ($CatchupSeconds -gt 45) { throw 'Acceptance catch-up is limited to 45 seconds; a larger observation is not a passing result.' }
$settings = @{
    COMBAT_EVIDENCE_BENCHMARK = '1'
    COMBAT_EVIDENCE_BENCHMARK_SECONDS = $Seconds.ToString([Globalization.CultureInfo]::InvariantCulture)
    COMBAT_EVIDENCE_BENCHMARK_REPEATS = $Repeats.ToString([Globalization.CultureInfo]::InvariantCulture)
    COMBAT_EVIDENCE_BENCHMARK_CATCHUP_SECONDS = $CatchupSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
    COMBAT_EVIDENCE_BENCHMARK_OUTPUT = Join-Path $Output 'cases'
    COMBAT_EVIDENCE_BENCHMARK_INDEPENDENT_ENGINES = if ($IndependentEngines) { '1' } else { '0' }
}
$failure = $null
try {
    Write-Output "Two-endpoint CPU/storage benchmark: 9 combinations x $Repeats repetition(s) x $Seconds seconds; output: $Output"
    & (Join-Path $PSScriptRoot 'Invoke-CombatEvidenceValidation.ps1') -Unity $Unity -Python $Python -ProjectPath $ProjectPath -Output $Output `
        -TestPlatform EditMode -TestFilter 'MonsterSupergroup.NetworkCombat.Tests.CombatEvidenceLoadBenchmarks' `
        -Environment $settings -TimeoutSeconds $TimeoutSeconds -PrepareOnly:$PrepareOnly
}
catch { $failure = $_.Exception.Message }
if ($PrepareOnly) { if ($failure) { throw $failure }; return }
if (Test-Path -LiteralPath $Output) {
    $frozenTools = Join-Path $Output 'tool-sources/Tools'
    $comparison = @((Join-Path $frozenTools 'CompareCombatEvidence.py'),'--benchmark-root',(Join-Path $Output 'cases'),
        '--output',(Join-Path $Output 'comparison.json'),'--minimum-seconds',$Seconds.ToString([Globalization.CultureInfo]::InvariantCulture),
        '--minimum-repeats',[string]$Repeats,'--catchup-limit',[string]$CatchupSeconds)
    if ($RequireCompleteMeasurements) { $comparison += '--require-complete-measurements' }
    & $Python @comparison
    if ($LASTEXITCODE -ne 0) { $failure = ($failure + ' CPU/storage or requested measurement gate failed; inspect comparison.json.').Trim() }
    & $Python (Join-Path $frozenTools 'CombatEvidenceValidation.py') finish --output $Output
    if ($LASTEXITCODE -ne 0) { $failure = ($failure + ' Final artifact/input audit failed.').Trim() }
}
if ($failure) { throw "$failure Artifacts: $Output" }
Write-Output 'CPU/storage gate passed. Allocation availability is reported separately. This is not full-frame/GPU/physics/Steam acceptance.'
