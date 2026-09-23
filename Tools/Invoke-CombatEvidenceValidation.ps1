[CmdletBinding()]
param(
    [string]$Unity = $env:UNITY_EDITOR_PATH,
    [string]$Python = 'python',
    [string]$ProjectPath,
    [string]$Output,
    [string]$OverlayDirectory,
    [ValidateSet('Tests','Replay','Build')][string]$Mode = 'Tests',
    [string[]]$FixturePath = @(),
    [string]$ExecuteMethod,
    [ValidateSet('EditMode','PlayMode')][string]$TestPlatform = 'EditMode',
    [string]$TestFilter = 'MonsterSupergroup.NetworkCombat.Tests.CombatEvidenceTests',
    [hashtable]$Environment = @{},
    [ValidateRange(60,14400)][int]$TimeoutSeconds = 1800,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
if (-not $Unity -or -not (Test-Path -LiteralPath $Unity -PathType Leaf)) { throw 'Specify the Unity Editor matching ProjectSettings/ProjectVersion.txt.' }
$expectedVersion = ((Get-Content -LiteralPath (Join-Path $workspace 'ProjectSettings/ProjectVersion.txt') | Where-Object { $_ -match '^m_EditorVersion:' }) -split ':',2)[1].Trim()
$actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($Unity).ProductVersion
if (-not $actualVersion -or -not $actualVersion.StartsWith($expectedVersion, [StringComparison]::OrdinalIgnoreCase)) { throw "Unity version mismatch: expected $expectedVersion, executable reports $actualVersion" }
if (-not $ProjectPath) { $ProjectPath = Join-Path $workspace 'Logs/CombatEvidenceNextValidation/project' }
if (-not $Output) { $Output = Join-Path $workspace ('Logs/CombatEvidenceNextValidation/runs/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$ProjectPath = [IO.Path]::GetFullPath($ProjectPath)
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw "Use a fresh output directory: $Output" }
if ($Output.StartsWith($ProjectPath.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence output must be outside the validation project.' }
$active = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Replace('/','\').IndexOf($ProjectPath.Replace('/','\'), [StringComparison]::OrdinalIgnoreCase) -ge 0 })
if ($active.Count) { throw 'The independent validation project is already open in Unity; do not synchronize a running project.' }
$helper = Join-Path $PSScriptRoot 'CombatEvidenceValidation.py'
$preparation = @($helper,'prepare','--source',$workspace,'--project',$ProjectPath,'--output',$Output,'--unity',$Unity)
if ($OverlayDirectory) { $preparation += @('--overlay',[IO.Path]::GetFullPath($OverlayDirectory)) }
& $Python @preparation
if ($LASTEXITCODE -ne 0) { throw "Snapshot preparation failed; inspect $Output" }
# Finish with the same audit implementation that prepared this snapshot, even if
# another task updates the workspace tools while Unity is running.
$helper = Join-Path $Output 'tool-sources/Tools/CombatEvidenceValidation.py'
if (-not (Test-Path -LiteralPath $helper -PathType Leaf)) { throw 'Preparation did not archive its validation helper.' }
$results = Join-Path $Output 'results.xml'
$log = Join-Path $Output 'unity.log'
$arguments = @('-batchmode','-nographics','-projectPath',$ProjectPath,'-logFile',$log)
if ($Mode -eq 'Tests') {
    if ($ExecuteMethod -or $FixturePath.Count) { throw 'ExecuteMethod and FixturePath require Replay or Build mode.' }
    $arguments += @('-runTests','-testPlatform',$TestPlatform,'-testFilter',$TestFilter,'-testResults',$results)
}
elseif ($Mode -eq 'Replay') {
    $expectedMethod = 'MonsterSupergroup.NetworkCombat.Editor.CombatReplayBatch.RunDirectory'
    if ($ExecuteMethod -and $ExecuteMethod -ne $expectedMethod) { throw 'Replay mode requires the strict CombatReplayBatch.RunDirectory entry.' }
    if (-not $FixturePath.Count) { throw 'Replay mode requires explicit existing .json FixturePath values.' }
    $fixtureDirectory = Join-Path $Output 'fixtures'
    New-Item -ItemType Directory -Path $fixtureDirectory | Out-Null
    $fixtureFiles = @()
    $fixtureSources = @()
    foreach ($path in $FixturePath) {
        $sourceFixture = (Resolve-Path -LiteralPath $path).Path
        if ([IO.Path]::GetExtension($sourceFixture) -ne '.json') { throw "Expected JSON fixture: $sourceFixture" }
        if ($fixtureSources | Where-Object source -eq $sourceFixture) { throw "Duplicate fixture: $sourceFixture" }
        $name = ('{0:D3}-' -f $fixtureFiles.Count) + [IO.Path]::GetFileName($sourceFixture)
        $destination = Join-Path $fixtureDirectory $name
        Copy-Item -LiteralPath $sourceFixture -Destination $destination
        $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ((Get-FileHash -LiteralPath $sourceFixture -Algorithm SHA256).Hash -ne $hash) { throw 'Fixture changed while freezing it.' }
        $fixtureFiles += $name
        $fixtureSources += [ordered]@{source=$sourceFixture;frozen=$destination;sha256=$hash}
    }
    [ordered]@{fixtures=$fixtureFiles} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $fixtureDirectory 'manifest.json') -Encoding UTF8
    $fixtureSources | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Output 'fixture-sources.json') -Encoding UTF8
    $arguments += @('-executeMethod',$expectedMethod,('--combat-fixture-manifest=' + (Join-Path $fixtureDirectory 'manifest.json')))
}
else {
    $expectedMethod = 'MonsterSupergroup.EditorTools.ProjectToolRunner.Batch'
    if ($ExecuteMethod -and $ExecuteMethod -ne $expectedMethod) { throw 'Build mode requires the unified ProjectToolRunner.Batch entry.' }
    $buildResult = Join-Path $Output 'build-result.json'
    $arguments += @('-executeMethod',$expectedMethod,'-toolId','build.player','-toolProfile','product',
        '-toolBuildKind','Test','-toolDevelopment','false','-toolNetwork','Steam','-toolDistribution','Direct','-toolDiagnostics','Evidence',
        '-toolOutput',(Join-Path $Output 'package/MonsterSupergroup.exe'),'-toolResult',$buildResult)
}
$invocation = [ordered]@{mode=$Mode;executable=$Unity;executableVersion=$actualVersion;expectedUnityVersion=$expectedVersion;arguments=$arguments;environment=$Environment;project=$ProjectPath;preparedOnly=[bool]$PrepareOnly;startedUtc=[DateTime]::UtcNow.ToString('o')}
$invocation | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Output 'invocation.json') -Encoding UTF8
if ($PrepareOnly) { Write-Output "Independent snapshot prepared: $ProjectPath; evidence: $Output"; return }
$previous = @{}
$failure = $null
$exitCode = $null
try {
    foreach ($key in $Environment.Keys) { $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process'); [Environment]::SetEnvironmentVariable($key, [string]$Environment[$key], 'Process') }
    # Arguments contain paths and filter values, never shell commands. Windows paths cannot contain a quote.
    $quoted = @($arguments | ForEach-Object { if ($_ -match '[\s;]') { '"' + $_ + '"' } else { $_ } })
    $process = Start-Process -FilePath $Unity -ArgumentList $quoted -WorkingDirectory $ProjectPath -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { Stop-Process -Id $process.Id; throw 'Unity validation timed out; partial artifacts are preserved.' }
    $exitCode = $process.ExitCode
    if ($Mode -eq 'Tests') {
        if (-not (Test-Path -LiteralPath $results)) { throw 'Unity did not produce results.xml.' }
        [xml]$xml = Get-Content -LiteralPath $results -Raw
        $run = $xml.'test-run'
        if ($exitCode -ne 0 -or $run.result -ne 'Passed' -or [int]$run.passed -le 0 -or [int]$run.failed -ne 0 -or [int]$run.inconclusive -ne 0) {
            throw "Unity did not pass a nonempty test selection: exit=$exitCode result=$($run.result) passed=$($run.passed) failed=$($run.failed) skipped=$($run.skipped)"
        }
    }
    elseif ($Mode -eq 'Replay') {
        $summaryPath = Join-Path $fixtureDirectory 'replay-summary.json'
        $replayPath = Join-Path $fixtureDirectory 'replay-results.json'
        if (-not (Test-Path -LiteralPath $summaryPath) -or -not (Test-Path -LiteralPath $replayPath)) { throw 'Replay did not produce summary and detailed results.' }
        $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        $reports = @(Get-Content -LiteralPath $replayPath -Raw | ConvertFrom-Json)
        if ($exitCode -ne 0 -or $summary.total -ne $FixturePath.Count -or $reports.Count -ne $FixturePath.Count -or
            $summary.passed -ne $FixturePath.Count -or $summary.unreliable -ne 0 -or $summary.diverged -ne 0 -or
            @($reports | Where-Object { -not $_.report.reliable -or -not $_.report.passed -or $_.report.executed -le 0 }).Count -ne 0) {
            throw "Replay not verified: exit=$exitCode outcome=$($summary.outcome) total=$($summary.total) passed=$($summary.passed) diverged=$($summary.diverged) unreliable=$($summary.unreliable)"
        }
    }
    else {
        if (-not (Test-Path -LiteralPath $buildResult)) { throw 'Unified build service did not produce build-result.json.' }
        $built = Get-Content -LiteralPath $buildResult -Raw | ConvertFrom-Json
        if ($exitCode -ne 0 -or -not $built.success -or $built.pending -or -not $built.artifacts.Count) { throw "Unified build failed: $($built.error)" }
        $executable = [IO.Path]::GetFullPath($built.artifacts[0])
        $package = Split-Path -Parent $executable
        $buildInfo = Join-Path $package ([IO.Path]::GetFileNameWithoutExtension($executable) + '_Data/StreamingAssets/BuildInfo.json')
        foreach ($artifact in @($executable,$buildInfo,(Join-Path $package 'build-complete.json'),(Join-Path $package 'combat-build.json'),(Join-Path $package 'steam_appid.txt'))) {
            if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) { throw "Successful build missing required artifact: $artifact" }
        }
        $combatBuild = Get-Content -LiteralPath (Join-Path $package 'combat-build.json') -Raw | ConvertFrom-Json
        $archive = Join-Path $ProjectPath $combatBuild.replaySources
        if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) { throw 'Build did not preserve its replay source archive.' }
        Copy-Item -LiteralPath $archive -Destination (Join-Path $Output 'combat-replay-sources.zip')
        Write-Output "Built product Test / Steam / Direct / Evidence: $executable"
    }
}
catch { $failure = $_.Exception.Message }
finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
    [ordered]@{mode=$Mode;exitCode=$exitCode;success=($null -eq $failure);error=$failure;finishedUtc=[DateTime]::UtcNow.ToString('o')} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'execution.json') -Encoding UTF8
    & $Python $helper finish --output $Output
    if ($LASTEXITCODE -ne 0) { $failure = ($failure + ' Frozen validation input hashes changed or final audit failed.').Trim() }
}
if ($failure) { throw "$failure Artifacts: $Output" }
Write-Output "Unity validation passed with unchanged snapshot hashes; integrity.json also states whether the workspace still matches: $Output"
