[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildDirectory, [Parameter(Mandatory)][string]$Destination,
    [ValidatePattern('^[a-zA-Z0-9._-]+$')][string]$Version)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'ProjectTools.psm1')
$build = [IO.Path]::GetFullPath($BuildDirectory)
$player = Resolve-ProjectBuildExecutable -ProjectRoot $project -Recipe product -BuildDirectory $build
$playerName = [IO.Path]::GetFileName($player)
$playerData = [IO.Path]::GetFileNameWithoutExtension($player) + '_Data'
$package = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $package) { throw 'Use a new destination; do not replace a frozen package.' }
$buildInfoPath = Join-Path $build "$playerData/StreamingAssets/BuildInfo.json"
$completedPath = Join-Path $build 'build-complete.json'
if (-not (Test-Path -LiteralPath $buildInfoPath) -or -not (Test-Path -LiteralPath $completedPath)) { throw 'BuildInfo and successful build marker are required. Rebuild through the project build tool.' }
$buildInfo = Get-Content -LiteralPath $buildInfoPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $buildInfoPath).Hash -ne (Get-FileHash -LiteralPath $completedPath).Hash) { throw 'Build identity does not match completion marker.' }
if ($buildInfo.PSObject.Properties.Name -contains 'developmentTools' -and -not $buildInfo.developmentTools) {
    throw 'Limbo uses an explicit reference launch capability. Build product Dev / Kcp / Direct; ordinary Test and Shipping intentionally cannot run mechanism launchers.'
}
if ($buildInfo.PSObject.Properties.Name -contains 'network' -and $buildInfo.network -ne 'Kcp') { throw 'The portable local Limbo package requires a Kcp build.' }
if (Get-ChildItem -LiteralPath $build -Recurse -File -Filter '*.Tests*.dll') { throw 'Use product Dev for the portable reference package; technical test assemblies are not included in this package.' }
New-Item -ItemType Directory -Path $package | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $build) {
    if ($item.Name -like '*BackUpThisFolder_ButDontShipItWithYourGame*' -or $item.Name -like '*BurstDebugInformation_DoNotShip*') { continue }
    Copy-Item -LiteralPath $item.FullName -Destination $package -Recurse
}
if (Get-ChildItem -LiteralPath $package -Recurse -File -Filter '*.Tests*.dll') { throw 'Test assemblies must not ship.' }
Copy-Item -Path (Join-Path $PSScriptRoot 'LimboManual/*') -Destination $package
Copy-Item -LiteralPath (Join-Path $project 'docs/limbo-manual-playtest.md') -Destination (Join-Path $package 'README.md')
$legacyLabel = $Version
$Version = 'v' + $buildInfo.gameVersion + '-' + $buildInfo.kind + '-' + $buildInfo.buildId
$files = foreach ($file in Get-ChildItem -LiteralPath $package -Recurse -File | Sort-Object FullName) {
    [pscustomobject]@{ path=$file.FullName.Substring($package.Length+1).Replace('\','/'); bytes=$file.Length; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
}
$manifest = [ordered]@{ version=$Version;executable=$playerName;legacyTestLabel=$legacyLabel;createdUtc=[DateTime]::UtcNow.ToString('o');
    buildInfo=$buildInfo;sourceCommit=$buildInfo.gitCommit;sourceDirty=$buildInfo.dirty;buildProfile=$buildInfo.profile;testAssemblies=$false;
    baseline=@{ enemyClips=31; transitionRequest=720.9; hp=500;moveSpeed=4.55;weaponId=1;xpModifier=2;xpDenominator=841.5766649882;seed=14301;map='Nordic';graphics='D3D11';width=1280;height=720;targetFps=60;manualHelpers=$false };
    files=@($files); validation='See technical-verification.json; a manifest alone is not a passed test.' }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $package 'build-manifest.json') -Encoding UTF8
Write-Output "Package prepared: $package ($Version). Hash and archive after verification records are finalized."
