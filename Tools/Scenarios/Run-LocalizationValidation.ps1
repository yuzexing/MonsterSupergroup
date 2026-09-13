param(
    [string]$Unity = '',
    [switch]$BuildPlayers,
    [switch]$Network
)
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ProjectTools.psm1')

$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Unity = Resolve-ProjectUnity -ProjectRoot $project -Unity $Unity
$artifacts = Join-Path $project ('Logs/LocalizationValidation/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
function Run-Unity([string]$name, [string[]]$extra) {
    $arguments = @('-batchmode','-nographics','-projectPath', ('"' + $project + '"'), '-logFile', ('"' + $artifacts + '/' + $name + '.log"')) + $extra
    $process = Start-ProjectProcess -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Wait-ProjectProcess -Process $process
    if ($process.ExitCode -ne 0) { throw "$name failed. See $artifacts" }
}
$filter = @('ContentLocalizationTests','LocalizationCsvTests','GameOptionsTests','GameplayMenuTests','CardPickMenuTests','SteamInviteUiTests') |
    ForEach-Object { 'MonsterSupergroup.Gameplay.Tests.' + $_ }
Run-Unity 'tests' @('-runTests','-testPlatform','PlayMode','-testFilter',($filter -join ';'),'-testResults',('"' + $artifacts + '/results.xml"'))
[xml]$results = Get-Content -LiteralPath (Join-Path $artifacts 'results.xml')
if ($results.'test-run'.result -ne 'Passed') { throw "Localization tests failed. See $artifacts" }
Run-Unity 'tables' @('-quit','-executeMethod','MonsterSupergroup.NetworkCombat.Editor.GameLocalizationAssets.Validate')
if ($BuildPlayers) {
    $previousRelease = $env:MENU_RELEASE
    try {
        $env:MENU_RELEASE = '0'
        Run-Unity 'development' @('-quit','-executeMethod','MonsterSupergroup.Gameplay.Tests.PreparationMenuValidationBuild.Build')
        $env:MENU_RELEASE = '1'
        Run-Unity 'release' @('-quit','-executeMethod','MonsterSupergroup.Gameplay.Tests.PreparationMenuValidationBuild.Build')
    }
    finally { $env:MENU_RELEASE = $previousRelease }
}
if ($Network) {
    & (Join-Path $PSScriptRoot 'Run-PreparationMenuValidation.ps1') -Profile local-party -Port 7796 -MixedLanguages
    & (Join-Path $PSScriptRoot 'Run-PreparationMenuValidation.ps1') -Profile combat-menu-solo -Port 7797 -MixedLanguages
}
Write-Output "Localization validation passed: $artifacts"
