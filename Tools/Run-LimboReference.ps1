param(
    [ValidateSet('host', 'client')][string]$Role = 'host',
    [ValidateSet('opening', 'full', 'full-validation', 'full-fixture', 'imp', 'imp-validation', 'imp-v0', 'imp-v1','stage2','stage2-validation','stage2-fixture','spatial-b','spatial-barrier','spatial-overlap','spatial-reposition','dash','dash-fixture','dash-validation','art-effects','lostsoul','lostsoul-fixture','lostsoul-validation','ghoul','ghoul-fixture','ghoul-validation','ghoul-motion','audio-wisp','audio-beam','pickup-observe','pickup-drops')][string]$Profile = 'opening',
    [ValidateSet('d3d11', 'd3d12')][string]$GraphicsApi = 'd3d11',
    [int]$Port = 7993,
    [ValidateRange(1,4)][int]$WaitFor = 1,
    [switch]$AutoWalk,
    [switch]$ArtObserve,
    [switch]$Windowed,
    [ValidateSet('720p60','4k144')][string]$PerformancePreset,
    [ValidateRange(0,120000)][int]$ProfilerFrames = 0,
    [ValidateSet('light','detailed')][string]$LogDetail = 'detailed',
    [ValidateSet('normal','warning-only')][string]$AttackEdges = 'normal',
    [string]$BuildDirectory, [string]$Executable,
    [ValidateSet('host', 'client')][string]$FixtureTarget = 'host',
    [ValidateSet('Skeleton0','Skeleton2','Elite0','Elite1','Brotchi0','Brotchi1','Slime0','Slime1','Rusher2','Rusher1')][string]$FixtureEnemy = 'Skeleton0',
    [ValidateSet('mechanism','melee-cleanup','l-hold','l-kill','l-boundary','art-death')][string]$FixtureMode = 'mechanism',
    [ValidateSet('observe','pause-all','occupancy','cancel-Delay','cancel-Framing','cancel-Building','cancel-Shrinking','cancel-Stopping')][string]$SpatialCase = 'observe',
    [ValidateSet('timeout','distance','placement-failure','expiry','framing')][string]$RepositionCase = 'timeout',
    [ValidateRange(0,1)][int]$DashVariant = 0,
    [ValidateSet("main","boundary","reuse")][string]$DashCase="main",
    [ValidateRange(0,1)][int]$LostSoulVariant = 0,
    [ValidateSet("main","burst","limited","boundary","reuse","expiry","barrier")][string]$LostSoulCase="main",
    [ValidateSet("main","boundary","interrupt","limited","curve","imp-burst","soul-burst","rusher")][string]$GhoulCase="main",
    [ValidateSet("idle","busy","chain","disconnect","reconnect","downed","unavailable","barrier-first","burst-first","slime-b","cancel")][string]$FullCase="idle",
    [string]$RunName = (Get-Date -Format 'yyyyMMdd-HHmmss')
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'ProjectTools.psm1')
$executable = Resolve-ProjectBuildExecutable -ProjectRoot $project -Recipe 'gameplay-validation' -BuildDirectory $BuildDirectory -Executable $Executable -RequireDevelopmentTools -Network Kcp
if ($RunName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'RunName must contain only letters, digits, hyphens and underscores.' }
$outputDirectory = Join-Path $project "Logs/LimboReference/$RunName/$Role"
if (Test-Path -LiteralPath (Join-Path $outputDirectory 'player.log')) { throw 'Choose a new RunName to preserve the previous evidence.' }
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
if ($ArtObserve) {
    $buildRoot = Split-Path -Parent $executable
    $playerName = [IO.Path]::GetFileNameWithoutExtension($executable)
    $artFiles = @("$playerName.exe",
        "${playerName}_Data/Managed/MonsterSupergroup.Gameplay.Combat.dll",
        "${playerName}_Data/Managed/MonsterSupergroup.Gameplay.Combat.Runtime.dll",
        "${playerName}_Data/Managed/MonsterSupergroup.Gameplay.Local.dll",
        "${playerName}_Data/Managed/MonsterSupergroup.NetworkCombat.dll",
        "${playerName}_Data/resources.assets", "${playerName}_Data/sharedassets0.assets")
    $artHashes = foreach ($relative in $artFiles) {
        $file = Join-Path $buildRoot $relative
        if (Test-Path -LiteralPath $file) { [pscustomobject]@{ file=$relative; sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash; bytes=(Get-Item -LiteralPath $file).Length } }
    }
    [pscustomobject]@{ build=$buildRoot; role=$Role; profile=$Profile; files=@($artHashes); observedUtc=[DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputDirectory 'art-build.json')
}
$launchArgs = @("-force-$GraphicsApi", '-screen-fullscreen', '0', '-screen-width', '1100', '-screen-height', '700',
    '-logFile', ('"' + (Join-Path $outputDirectory 'player.log') + '"'),
    "--limbo-attack-edges=$AttackEdges", "--limbo-log-detail=$LogDetail", "--limbo-full-case=$FullCase", "--limbo-ghoul-case=$GhoulCase", "--limbo-role=$Role", "--limbo-profile=$Profile", "--limbo-port=$Port", "--limbo-wait-for=$WaitFor", "--limbo-fixture-target=$FixtureTarget", "--limbo-fixture-enemy=$FixtureEnemy", "--limbo-fixture-mode=$FixtureMode", "--limbo-spatial-case=$SpatialCase", "--limbo-reposition-case=$RepositionCase", "--limbo-dash-variant=$DashVariant", "--limbo-dash-case=$DashCase", "--limbo-lostsoul-variant=$LostSoulVariant", "--limbo-lostsoul-case=$LostSoulCase",
    ('"--limbo-output=' + $outputDirectory + '"'), "--limbo-art-observe=$($ArtObserve.IsPresent.ToString().ToLowerInvariant())", "--limbo-autowalk=$($AutoWalk.IsPresent.ToString().ToLowerInvariant())", "--limbo-windowed=$($Windowed.IsPresent.ToString().ToLowerInvariant())")
# Visible by design: this scenario is for rendered Host/Client verification.
if ($PerformancePreset) { $launchArgs += "--limbo-performance-preset=$PerformancePreset" }
if ($ProfilerFrames -gt 0) {
    # Explicit diagnostic capture, never a manual-play shortcut. Profiling overhead
    # means this run must not be pooled with unprofiled performance comparisons.
    $launchArgs += @('-profiler-enable', '-profiler-log-file', ('"' + (Join-Path $outputDirectory 'cpu-profile.raw') + '"'),
        '-profiler-capture-frame-count', [string]$ProfilerFrames)
}
$gameProcess = Start-Process -FilePath $executable -ArgumentList $launchArgs -WindowStyle Normal -PassThru
$gameProcess.Id | Set-Content -LiteralPath (Join-Path $outputDirectory 'process.pid')
Write-Output "Started $Role PID=$($gameProcess.Id). Evidence: $outputDirectory"
