$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $project 'Tools/ProjectTools.psm1') -Force
$fixture = Join-Path $project ('Logs/ProjectTools/NativeBuildResolution-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$package = Join-Path $fixture 'Package With Spaces'
$guid = '0123456789abcdef0123456789abcdef'
$profile = 'Assets/Settings/Build Profiles/Test Profile.asset'
$profileFile = Join-Path $fixture $profile
$embedded = Join-Path $package 'Custom Player_Data/StreamingAssets/BuildInfo.json'
$player = Join-Path $package 'Custom Player.exe'
$marker = Join-Path $package 'build-complete.json'
$resultPath = Join-Path $fixture "Library/ProjectTools/BuildResults/$guid.json"
New-Item -ItemType Directory -Path (Split-Path -Parent $embedded),(Split-Path -Parent $profileFile),(Split-Path -Parent $resultPath) -Force | Out-Null
[IO.File]::WriteAllBytes($player,[byte[]]@(1,2,3))
Set-Content -LiteralPath $profileFile -Value 'fixture'
Set-Content -LiteralPath ($profileFile + '.meta') -Value "guid: $guid"
$script:checks = 0
function Assert-Rejected([scriptblock]$Operation, [string]$Name) {
    $rejected = $false
    try { & $Operation | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Expected rejection: $Name" }
    $script:checks++
}
function Write-ValidPackage {
    $script:info = [ordered]@{schema=3;gameVersion='0.0.1';kind='test';buildId='20260923T120000000Z-1234abcd';profile='gameplay-validation';profileGuid=$guid;contentHash=('a'*64);inputHash=('b'*64);network='Kcp';distribution='Direct';diagnostics='Normal';developmentTools=$true}
    $script:info | ConvertTo-Json | Set-Content -LiteralPath $embedded -Encoding UTF8
    Copy-Item -LiteralPath $embedded -Destination $marker -Force
    $script:result = [ordered]@{success=$true;profileGuid=$guid;recipe='gameplay-validation';executable=$player;buildInfoPath=$embedded;buildId=$script:info.buildId;contentHash=$script:info.contentHash;inputHash=$script:info.inputHash}
    $script:result | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
}
$options = @{ProjectRoot=$fixture;BuildProfile=$profile;RequireDevelopmentTools=$true;Network='Kcp'}
Assert-Rejected { Resolve-ProjectBuildExecutable @options } 'no result cannot fall back'
Assert-Rejected { Resolve-ProjectBuildExecutable -ProjectRoot $fixture -Recipe gameplay-validation } 'legacy recipe pointers are disabled'
Assert-Rejected { Get-ProjectBuildProfileGuid -ProjectRoot $fixture -BuildProfile 'Assets/../../outside.asset' } 'path traversal'
Write-ValidPackage
if ((Resolve-ProjectBuildExecutable @options) -ne $player) { throw 'Must use actual EXE, not a guessed filename.' }; $script:checks++
foreach ($bad in @($false,'true')) {
    Write-ValidPackage; $result.success=$bad; $result | ConvertTo-Json | Set-Content -LiteralPath $resultPath
    Assert-Rejected { Resolve-ProjectBuildExecutable @options } 'failed or malformed success'
}
foreach ($field in @('buildId','profileGuid','contentHash','inputHash')) {
    Write-ValidPackage; $result[$field]='wrong'; $result | ConvertTo-Json | Set-Content -LiteralPath $resultPath
    Assert-Rejected { Resolve-ProjectBuildExecutable @options } "mismatched $field"
}
foreach ($schema in @($null,2,99,'3')) {
    Write-ValidPackage; $info.schema=$schema
    $info | ConvertTo-Json | Set-Content -LiteralPath $embedded
    Copy-Item -LiteralPath $embedded -Destination $marker -Force
    Assert-Rejected { Resolve-ProjectBuildExecutable @options } "automatic selection requires native schema 3: $schema"
}
foreach ($field in @('contentHash','inputHash')) {
    foreach ($digest in @($null,'',('a'*63),('g'*64),(('a'*64)+"`n"),123)) {
        Write-ValidPackage; $info[$field]=$digest; $result[$field]=$digest
        $info | ConvertTo-Json | Set-Content -LiteralPath $embedded
        Copy-Item -LiteralPath $embedded -Destination $marker -Force
        $result | ConvertTo-Json | Set-Content -LiteralPath $resultPath
        Assert-Rejected { Resolve-ProjectBuildExecutable @options } "matching but invalid $field"
    }
}
Write-ValidPackage; '{}' | Set-Content -LiteralPath $marker
Assert-Rejected { Resolve-ProjectBuildExecutable @options } 'marker mismatch'
Write-ValidPackage; $info.developmentTools=$false; $info | ConvertTo-Json | Set-Content -LiteralPath $embedded; Copy-Item $embedded $marker -Force
Assert-Rejected { Resolve-ProjectBuildExecutable @options } 'product package cannot satisfy validation'
Write-ValidPackage; $info.network='Steam'; $info | ConvertTo-Json | Set-Content -LiteralPath $embedded; Copy-Item $embedded $marker -Force
Assert-Rejected { Resolve-ProjectBuildExecutable @options } 'network mismatch'
Write-ValidPackage
Assert-Rejected { Resolve-ProjectBuildExecutable @options -Diagnostics Evidence } 'diagnostics mismatch'
if ((Resolve-ProjectBuildExecutable -ProjectRoot $fixture -Recipe gameplay-validation -Executable $player) -ne $player) { throw 'Explicit frozen package selection must remain available.' }; $script:checks++
if ((Resolve-ProjectBuildExecutable -ProjectRoot $fixture -BuildDirectory $package) -ne $player) { throw 'Explicit directory must retain actual filename.' }; $script:checks++
Write-ValidPackage; $info.schema=2
foreach ($field in @('profileGuid','contentHash','inputHash')) { $info.Remove($field) }
$info | ConvertTo-Json | Set-Content -LiteralPath $embedded
Copy-Item -LiteralPath $embedded -Destination $marker -Force
if ((Resolve-ProjectBuildExecutable -ProjectRoot $fixture -Executable $player) -ne $player) { throw 'Explicit schema 2 historical Player selection must remain available.' }; $script:checks++
if ((Resolve-ProjectBuildExecutable -ProjectRoot $fixture -BuildDirectory $package) -ne $player) { throw 'Explicit schema 2 historical directory selection must remain available.' }; $script:checks++
$buildTool = (Get-Content -LiteralPath (Join-Path $project 'docs/editor-tools/catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json).tools | Where-Object id -eq 'build.player'
foreach ($legacy in @('Profile','BuildKind','Development','Network','Distribution','Diagnostics','ScriptsOnly','UniqueOutput')) {
    $values=@{BuildProfile=$profile}; $values[$legacy]='old'
    Assert-Rejected { Convert-ProjectToolParameters -Tool $buildTool -Values $values } "legacy parameter $legacy"
}
Assert-Rejected { Convert-ProjectToolParameters -Tool $buildTool -Values @{} } 'no default profile'
$values=Convert-ProjectToolParameters -Tool $buildTool -Values @{BuildProfile=$profile;CleanBuildCache='false';RunAfterBuild='true'}
if ($values.CleanBuildCache -or -not $values.RunAfterBuild -or $values.BuildProfile -cne $profile) { throw 'Execution parameter parsing failed.' }; $script:checks++
Write-Output "Native build resolution: $script:checks checks passed. $fixture"
