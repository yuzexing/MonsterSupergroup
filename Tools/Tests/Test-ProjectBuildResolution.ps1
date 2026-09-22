$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $project 'Tools/ProjectTools.psm1') -Force
$fixture = Join-Path $project ('Logs/ProjectTools/BuildResolution-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$package = Join-Path $fixture 'Package With Spaces'
$results = Join-Path $fixture 'Library/ProjectTools/BuildResults'
$embedded = Join-Path $package 'Custom Player_Data/StreamingAssets/BuildInfo.json'
$player = Join-Path $package 'Custom Player.exe'
$marker = Join-Path $package 'build-complete.json'
$resultPath = Join-Path $results 'gameplay-validation.json'
New-Item -ItemType Directory -Path (Split-Path -Parent $embedded),$results -Force | Out-Null
[IO.File]::WriteAllBytes($player,[byte[]]@(1,2,3))
$script:checks = 0
$commandProject = 'F:\Unity Store\MonsterSupergroup'
$commandCases = @(
    @('"D:\Unity Editor\Unity.exe" -batchmode -projectPath "F:\Unity Store\MonsterSupergroup" -logFile other.log', $true),
    @('Unity.exe -PROJECTPATH "f:/unity store/MonsterSupergroup/"', $true),
    @('Unity.exe -projectPath "F:\Unity Store\Other\..\MonsterSupergroup"', $true),
    @('Unity.exe -projectPath "F:/Unity Store/MonsterSupergroup/Logs/SteamNetworkValidation/project"', $false),
    @('Unity.exe -projectPath "F:\Unity Store\MonsterSupergroup-copy"', $false),
    @('Unity.exe -projectPath "F:\Other" -logFile "F:\Unity Store\MonsterSupergroup\test.log"', $false),
    @('Unity.exe -logFile "F:\Unity Store\MonsterSupergroup\-projectPath report.log"', $false),
    @('Unity.exe -projectPath', $false),
    @('Unity.exe -projectPath -batchmode', $false),
    @('Unity.exe -projectPath ""', $false),
    @('Unity.exe -projectPath "F:\Other" -projectPath "F:\Unity Store\MonsterSupergroup"', $false),
    @('', $false)
)
foreach ($case in $commandCases) {
    $actual = Test-ProjectUnityCommandLine -CommandLine $case[0] -ProjectRoot $commandProject
    if ($actual -ne $case[1]) { throw "Wrong project process selection: $($case[0])" }
    $script:checks++
}
if (-not (Test-ProjectUnityCommandLine -CommandLine 'Unity.exe -projectPath F:/UnityStore/MonsterSupergroup -logFile F:/Other/log.txt' -ProjectRoot 'F:\UnityStore\MonsterSupergroup')) { throw 'Unquoted absolute project path was not accepted.' }; $script:checks++
function Assert-Rejected([scriptblock]$Operation, [string]$Name) {
    $rejected = $false
    try { & $Operation | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Expected rejection: $Name" }
    $script:checks++
}
function Write-ValidPackage {
    $script:info = [ordered]@{ gameVersion='0.0.0';kind='test';buildId='20260922T120000000Z-1234abcd';profile='gameplay-validation';network='Kcp';distribution='Direct';diagnostics='Normal';developmentTools=$true }
    $script:info | ConvertTo-Json | Set-Content -LiteralPath $embedded -Encoding UTF8
    Copy-Item -LiteralPath $embedded -Destination $marker -Force
    $script:result = [ordered]@{success=$true;requestedProfile='beam';recipe='gameplay-validation';executable=$player;buildInfoPath=$embedded;buildId=$script:info.buildId}
    $script:result | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
}
$options = @{ProjectRoot=$fixture;Recipe='gameplay-validation';RequireDevelopmentTools=$true;Network='Kcp'}
Assert-Rejected { Resolve-ProjectBuildExecutable @options } 'no latest-success result must not search old packages'
Write-ValidPackage
if ((Resolve-ProjectBuildExecutable @options) -ne $player) { throw 'Automatic selection did not use the actual result path.' }; $script:checks++
$result.success=$false; $result | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
Assert-Rejected { Resolve-ProjectBuildExecutable @options } 'failed build cannot fall back'
Write-ValidPackage
$result.success='true'; $result | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
Assert-Rejected { Resolve-ProjectBuildExecutable @options } 'non-boolean success'
Write-ValidPackage
$result.buildId='old'; $result | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
Assert-Rejected { Resolve-ProjectBuildExecutable @options } 'stale build identity'
Write-ValidPackage
'{}' | Set-Content -LiteralPath $marker -Encoding UTF8
Assert-Rejected { Resolve-ProjectBuildExecutable @options } 'mismatched package marker'
Write-ValidPackage
$info.developmentTools=$false; $info | ConvertTo-Json | Set-Content -LiteralPath $embedded -Encoding UTF8; Copy-Item $embedded $marker -Force
Assert-Rejected { Resolve-ProjectBuildExecutable @options } 'ordinary Test must not be selected for a mechanism scenario'
Write-ValidPackage
$info.network='Steam'; $info | ConvertTo-Json | Set-Content -LiteralPath $embedded -Encoding UTF8; Copy-Item $embedded $marker -Force
Assert-Rejected { Resolve-ProjectBuildExecutable @options } 'different network cannot silently replace a Kcp scenario'
Write-ValidPackage
Assert-Rejected { Resolve-ProjectBuildExecutable @options -Diagnostics Evidence } 'explicit evidence requirement'
$result.recipe='wisp-validation'; $result | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
Assert-Rejected { Resolve-ProjectBuildExecutable @options } 'wrong recipe'
if ((Resolve-ProjectBuildExecutable @options -Executable $player) -ne $player) { throw 'Explicit frozen package path not preserved.' }; $script:checks++
if ((Resolve-ProjectBuildExecutable @options -BuildDirectory $package) -ne $player) { throw 'Explicit directory must discover its actual Unity Player filename.' }; $script:checks++
Assert-Rejected { Resolve-ProjectBuildExecutable @options -Executable $player -BuildDirectory $package } 'ambiguous explicit selection'
Assert-Rejected { Resolve-ProjectBuildExecutable @options -Executable (Join-Path $package 'missing.exe') } 'missing explicit player'
New-Item -ItemType Directory -Path (Join-Path $package 'Another_Data') | Out-Null
[IO.File]::WriteAllBytes((Join-Path $package 'Another.exe'),[byte[]]@())
Assert-Rejected { Resolve-ProjectBuildExecutable @options -BuildDirectory $package } 'multiple Players in explicit directory'
$catalog = Get-Content -LiteralPath (Join-Path $project 'docs/editor-tools/catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$buildTool = $catalog.tools | Where-Object id -eq 'build.player'
$mapped = Convert-ProjectToolParameters -Tool $buildTool -Values @{Profile='product';BuildKind='Test';Network='Steam';Distribution='Steam';Diagnostics='Evidence';Development='false'}
foreach ($pair in @(@('Profile','product'),@('Network','Steam'),@('Distribution','Steam'),@('Diagnostics','Evidence'),@('Development','false'))) {
    if ($mapped[$pair[0]] -cne $pair[1]) { throw "Parameter mapping failed: $($pair[0])" }; $script:checks++
}
# Exercise the real wrapper merge path without starting Unity. The deliberately missing
# Editor must be the first rejection; omitted ValidateSet options must not reject defaults.
foreach ($request in @(@{}, @{BuildKind='Test';Network='Steam';Distribution='Steam';Diagnostics='Normal'})) {
    $wrapperResult = Join-Path $fixture ('wrapper-defaults-' + $request.Count + '.json')
    try {
        & (Join-Path $project 'Tools/Invoke-ProjectTool.ps1') -ToolId build.player -Profile product @request -Unity (Join-Path $fixture 'missing-unity.exe') -ResultPath $wrapperResult | Out-Null
    } catch { }
    $status = Get-Content -LiteralPath $wrapperResult -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($status.success -or $status.error -notlike '*Specify -Unity or UNITY_EDITOR_PATH*') { throw "Optional build options failed during parameter merging: $($status.error)" }
    $script:checks++
}
$parseCount=0
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $project 'Tools') -Recurse -File | Where-Object Extension -in @('.ps1','.psm1')) {
    $tokens=$null;$parseErrors=$null
    [Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$parseErrors) | Out-Null
    if ($parseErrors) { throw "PowerShell parse failure $($file.FullName): $($parseErrors.Message -join '; ')" }
    $parseCount++
}
[ordered]@{passed=$true;checks=$script:checks;parsedScripts=$parseCount;fixture=$fixture} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixture 'result.json') -Encoding UTF8
Write-Output "PASS: $script:checks build selection/parameter checks; $parseCount PowerShell files parsed. $fixture"
