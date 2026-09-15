param(
    [ValidateSet('host', 'client')][string]$Role = 'host',
    [ValidateSet('opening', 'full', 'imp', 'imp-validation', 'imp-v0', 'imp-v1','stage2','stage2-validation','stage2-fixture','spatial-b','spatial-barrier','spatial-overlap','spatial-reposition','dash','dash-fixture','dash-validation','art-effects')][string]$Profile = 'opening',
    [ValidateSet('d3d11', 'd3d12')][string]$GraphicsApi = 'd3d11',
    [int]$Port = 7993,
    [ValidateRange(1,4)][int]$WaitFor = 1,
    [switch]$AutoWalk,
    [switch]$ArtObserve,
    [switch]$Windowed,
    [string]$BuildDirectory = 'Builds/LimboReference',
    [ValidateSet('host', 'client')][string]$FixtureTarget = 'host',
    [ValidateSet('Skeleton0','Skeleton2','Elite0','Elite1','Brotchi0','Brotchi1','Slime0','Slime1','Rusher2','Rusher1')][string]$FixtureEnemy = 'Skeleton0',
    [ValidateSet('mechanism','melee-cleanup','l-hold','l-kill','l-boundary','art-death')][string]$FixtureMode = 'mechanism',
    [ValidateSet('observe','pause-all','occupancy','cancel-Delay','cancel-Framing','cancel-Building','cancel-Shrinking','cancel-Stopping')][string]$SpatialCase = 'observe',
    [ValidateSet('timeout','distance','placement-failure','expiry','framing')][string]$RepositionCase = 'timeout',
    [ValidateRange(0,1)][int]$DashVariant = 0,
    [ValidateSet("main","boundary","reuse")][string]$DashCase="main",
    [string]$RunName = (Get-Date -Format 'yyyyMMdd-HHmmss')
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$executable = Join-Path (Join-Path $project $BuildDirectory) 'MonsterSupergroupLimbo.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Build first with LimboReferenceAssets.CreateAndBuildBatch in Unity.' }
if ($RunName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'RunName must contain only letters, digits, hyphens and underscores.' }
$outputDirectory = Join-Path $project "Logs/LimboReference/$RunName/$Role"
if (Test-Path -LiteralPath (Join-Path $outputDirectory 'player.log')) { throw 'Choose a new RunName to preserve the previous evidence.' }
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
if ($ArtObserve) {
    $buildRoot = Split-Path -Parent $executable
    $artFiles = @('MonsterSupergroupLimbo.exe',
        'MonsterSupergroupLimbo_Data/Managed/MonsterSupergroup.Gameplay.Combat.dll',
        'MonsterSupergroupLimbo_Data/Managed/MonsterSupergroup.Gameplay.Combat.Runtime.dll',
        'MonsterSupergroupLimbo_Data/Managed/MonsterSupergroup.Gameplay.Local.dll',
        'MonsterSupergroupLimbo_Data/Managed/MonsterSupergroup.NetworkCombat.dll',
        'MonsterSupergroupLimbo_Data/resources.assets', 'MonsterSupergroupLimbo_Data/sharedassets0.assets')
    $artHashes = foreach ($relative in $artFiles) {
        $file = Join-Path $buildRoot $relative
        if (Test-Path -LiteralPath $file) { [pscustomobject]@{ file=$relative; sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash; bytes=(Get-Item -LiteralPath $file).Length } }
    }
    [pscustomobject]@{ build=$buildRoot; role=$Role; profile=$Profile; files=@($artHashes); observedUtc=[DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputDirectory 'art-build.json')
}
$launchArgs = @("-force-$GraphicsApi", '-screen-fullscreen', '0', '-screen-width', '1100', '-screen-height', '700',
    '-logFile', ('"' + (Join-Path $outputDirectory 'player.log') + '"'),
    "--limbo-role=$Role", "--limbo-profile=$Profile", "--limbo-port=$Port", "--limbo-wait-for=$WaitFor", "--limbo-fixture-target=$FixtureTarget", "--limbo-fixture-enemy=$FixtureEnemy", "--limbo-fixture-mode=$FixtureMode", "--limbo-spatial-case=$SpatialCase", "--limbo-reposition-case=$RepositionCase", "--limbo-dash-variant=$DashVariant", "--limbo-dash-case=$DashCase",
    ('"--limbo-output=' + $outputDirectory + '"'), "--limbo-art-observe=$($ArtObserve.IsPresent.ToString().ToLowerInvariant())", "--limbo-autowalk=$($AutoWalk.IsPresent.ToString().ToLowerInvariant())", "--limbo-windowed=$($Windowed.IsPresent.ToString().ToLowerInvariant())")
# Visible by design: this scenario is for rendered Host/Client verification.
$gameProcess = Start-Process -FilePath $executable -ArgumentList $launchArgs -WindowStyle Normal -PassThru
$gameProcess.Id | Set-Content -LiteralPath (Join-Path $outputDirectory 'process.pid')
Write-Output "Started $Role PID=$($gameProcess.Id). Evidence: $outputDirectory"
