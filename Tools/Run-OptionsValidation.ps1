param(
    [string]$Executable = 'Builds/OptionsValidation/MonsterSupergroup.exe',
    [string]$Artifacts = 'Logs/OptionsStandalone'
)
$ErrorActionPreference = 'Stop'
$optionsRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($Executable)) { $Executable = Join-Path $optionsRoot $Executable }
if (-not [IO.Path]::IsPathRooted($Artifacts)) { $Artifacts = Join-Path $optionsRoot $Artifacts }
$Artifacts = Join-Path $Artifacts (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Path $Artifacts -Force | Out-Null
Write-Output "Options validation artifacts: $Artifacts"
function Run-OptionsPhase([string]$phase) {
    $arguments = @('-force-d3d11', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
        "--options-probe=$phase", ('"--options-artifacts=' + $Artifacts + '"'), '-logFile', ('"' + $Artifacts + '/' + $phase + '.log"'))
    if ($phase -eq 'dedicated') { $arguments += @('-batchmode', '-nographics', '--dedicated-server') }
    $optionsProcess = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        $optionsDeadline = (Get-Date).AddSeconds(240)
        while (-not $optionsProcess.HasExited) {
            if ((Get-Date) -gt $optionsDeadline) { throw "Options validation timed out: $phase" }
            Start-Sleep -Milliseconds 500
        }
        $optionsProcess.WaitForExit()
        if ($optionsProcess.ExitCode -ne 0 -or -not (Select-String -LiteralPath (Join-Path $Artifacts "$phase.log") -SimpleMatch "[OptionsProcess] result=PASS phase=$phase" -Quiet)) {
            throw "Options validation failed: $phase. See $Artifacts"
        }
        if (Select-String -LiteralPath (Join-Path $Artifacts "$phase.log") -Pattern 'NullReferenceException|MissingReferenceException|InvalidOperationException|ArgumentException|error CS' -Quiet) {
            throw "Unexpected exception in options validation: $phase. See $Artifacts"
        }
    }
    finally { if (-not $optionsProcess.HasExited) { Stop-Process -Id $optionsProcess.Id -Force } }
}
try {
    Run-OptionsPhase 'exercise'
    Run-OptionsPhase 'restart'
    Run-OptionsPhase 'dedicated'
    Write-Output "Options graphics, audio, menu and restart validation passed: $Artifacts"
}
finally {
    # Restore the user's prior preference even if either validation process crashes.
    if (Test-Path -LiteralPath (Join-Path $Artifacts 'preference-backup.json')) { Run-OptionsPhase 'restore' }
}
