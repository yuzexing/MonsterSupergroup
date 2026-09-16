[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildDirectory, [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9._-]+$')][string]$Version)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$build = [IO.Path]::GetFullPath($BuildDirectory)
$package = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $package) { throw 'Use a new destination; do not replace a frozen package.' }
if (-not (Test-Path -LiteralPath (Join-Path $build 'MonsterSupergroupLimbo.exe'))) { throw 'A complete Limbo player build is required.' }
New-Item -ItemType Directory -Path $package | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $build) {
    if ($item.Name -like '*BackUpThisFolder_ButDontShipItWithYourGame*' -or $item.Name -like '*BurstDebugInformation_DoNotShip*') { continue }
    Copy-Item -LiteralPath $item.FullName -Destination $package -Recurse
}
if (Get-ChildItem -LiteralPath $package -Recurse -File -Filter '*.Tests*.dll') { throw 'Test assemblies must not ship.' }
Copy-Item -Path (Join-Path $PSScriptRoot 'LimboManual/*') -Destination $package
Copy-Item -LiteralPath (Join-Path $project 'docs/limbo-manual-playtest.md') -Destination (Join-Path $package 'README.md')
$commit = (& git -C $project rev-parse HEAD).Trim()
$dirty = @(& git -C $project status --porcelain)
# Include tracked diffs plus hashes of changed/untracked source; Python caches and logs are not source.
$diff = & git -C $project diff --binary HEAD
$diff | Set-Content -LiteralPath (Join-Path $package 'source.patch') -Encoding UTF8
$changed = @(& git -C $project diff --name-only HEAD) + @(& git -C $project ls-files --others --exclude-standard)
$sourceFiles = foreach ($relative in $changed | Sort-Object -Unique) {
    if ($relative -match '(__pycache__|\.pyc$)' -or -not (Test-Path -LiteralPath (Join-Path $project $relative) -PathType Leaf)) { continue }
    [pscustomobject]@{ path=$relative; sha256=(Get-FileHash -LiteralPath (Join-Path $project $relative) -Algorithm SHA256).Hash }
}
$configs = foreach ($relative in @('Assets/_Project/Content/NetworkCombat/Limbo/Resources/LimboReference/Full.asset',
    'Assets/_Project/Content/NetworkCombat/Limbo/Resources/LimboReference/FullValidation.asset',
    'Assets/_Project/ScriptObject/DataBase/BaseStatsDB.asset','docs/evidence/hellmaiden-attacks/runtime-assets.json')) {
    [pscustomobject]@{ path=$relative;sha256=(Get-FileHash -LiteralPath (Join-Path $project $relative) -Algorithm SHA256).Hash }
}
$files = foreach ($file in Get-ChildItem -LiteralPath $package -Recurse -File | Sort-Object FullName) {
    [pscustomobject]@{ path=$file.FullName.Substring($package.Length+1).Replace('\','/'); bytes=$file.Length; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
}
$manifest = [ordered]@{ version=$Version;createdUtc=[DateTime]::UtcNow.ToString('o');sourceCommit=$commit; workingTreeStatus=$dirty;
    sourceChanges=@($sourceFiles);configurations=@($configs); buildProfile='kcp-development';testAssemblies=$false;
    baseline=@{ enemyClips=31; transitionRequest=720.9; hp=500;moveSpeed=4.55;weaponId=1;xpModifier=2;xpDenominator=841.5766649882;seed=14301;map='Nordic';graphics='D3D11';width=1280;height=720;targetFps=60;manualHelpers=$false };
    files=@($files); validation='See technical-verification.json; a manifest alone is not a passed test.' }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $package 'build-manifest.json') -Encoding UTF8
Write-Output "Package prepared: $package ($Version). Hash and archive after verification records are finalized."
