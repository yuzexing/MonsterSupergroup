$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fixture = Join-Path $root ('Logs/CheckoutTests/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
function Write-Fixture([string]$Path, [string]$Text) {
    $file = Join-Path $fixture $Path
    New-Item -ItemType Directory -Path (Split-Path -Parent $file) -Force | Out-Null
    [IO.File]::WriteAllText($file, $Text)
}
function Invoke-FixtureGit([string[]]$Arguments) {
    $value = & git -C $fixture @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Fixture git failed: $Arguments" }
    return $value
}
$core = 'Assets/_Project/Gameplay/Combat/Rewired/Internal/Libraries/Runtime/Rewired_Core.dll'
$win = 'Assets/Plugins/FMOD/platforms/win/lib/x86_64/fmodstudioL.dll'
$uwp = 'Assets/Plugins/FMOD/platforms/uwp/lib/x64/fmodstudioL.dll'
$internal = 'Assets/_Project/Gameplay/Combat/Rewired/Internal.meta'
Write-Fixture 'ProjectSettings/ProjectVersion.txt' "m_EditorVersion: 6000.3.21f1`n"
Write-Fixture 'Packages/manifest.json' '{}'
Write-Fixture 'Packages/packages-lock.json' '{}'
Write-Fixture '.gitattributes' "*.dll filter=lfs diff=lfs merge=lfs -text`n"
foreach ($path in @('Assets/_Project/Scenes/Boot.unity','Assets/_Project/Scenes/MainMenu.unity','Assets/_Project/Scenes/Gameplay.unity',
    'Assets/_Project/Audio/FMODBanks/Master.bank','Assets/_Project/Audio/FMODBanks/Master.strings.bank',$core,$win,$uwp)) {
    Write-Fixture $path 'original fixture content'
    $enabled = if ($path -eq $uwp) { 0 } else { 1 }
    Write-Fixture ($path + '.meta') "fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))`nPluginImporter:`n  platformData:`n    Any:`n      enabled: 0`n    Editor:`n      enabled: $enabled`n"
}
Write-Fixture $internal "fileFormatVersion: 2`nguid: 8cdcfdd141b51d5458f981aaf449fd9f`nfolderAsset: yes`n"
Invoke-FixtureGit @('init','--quiet') | Out-Null
Invoke-FixtureGit @('lfs','install','--local') | Out-Null
Invoke-FixtureGit @('add','.') | Out-Null
Invoke-FixtureGit @('-c','user.name=Checkout Test','-c','user.email=checkout-test@localhost','commit','--quiet','-m','Fixture') | Out-Null
$check = Join-Path $root 'Tools/Scenarios/Check-ProjectCheckout.ps1'
function Assert-Checkout([string]$Code) {
    $failed = $false
    try { & $check -ProjectRoot $fixture | Out-Null } catch { $failed = $true }
    $result = Get-Content (Join-Path $fixture 'Logs/RepositoryIntegrity/checkout.json') -Raw | ConvertFrom-Json
    if (-not $Code) {
        if ($failed -or -not $result.success) { throw 'Healthy checkout rejected.' }
    } elseif (-not $failed -or $result.success -or $Code -notin $result.problems.code) {
        throw "Expected failure was not detected: $Code"
    }
}
Assert-Checkout ''
$customReport = Join-Path $fixture 'custom-report.json'
& (Join-Path $root 'Tools/Invoke-ProjectTool.ps1') -ToolId validate.checkout -Parameters @{ProjectRoot=$fixture;ReportPath=$customReport} | Out-Null
if (-not (Get-Content -LiteralPath $customReport -Raw | ConvertFrom-Json).success) { throw 'Unified entry did not write the requested report.' }
$original = [IO.File]::ReadAllBytes((Join-Path $fixture $core))
Write-Fixture $core ((Invoke-FixtureGit @('show',"HEAD:$core")) -join "`n")
Assert-Checkout 'lfs-pointer-not-downloaded'
[IO.File]::WriteAllBytes((Join-Path $fixture $core),$original)
$damaged = $original.Clone(); $damaged[0] = $damaged[0] -bxor 1
[IO.File]::WriteAllBytes((Join-Path $fixture $core),$damaged)
Assert-Checkout 'lfs-hash-mismatch'
[IO.File]::WriteAllBytes((Join-Path $fixture $core),$original)
$coreMeta = [IO.File]::ReadAllText((Join-Path $fixture ($core + '.meta')))
Remove-Item -LiteralPath (Join-Path $fixture ($core + '.meta'))
Assert-Checkout 'tracked-file-missing'
Write-Fixture ($core + '.meta') $coreMeta
$uwpMeta = [IO.File]::ReadAllText((Join-Path $fixture ($uwp + '.meta')))
Write-Fixture ($uwp + '.meta') $uwpMeta.Replace('enabled: 0','enabled: 1')
Assert-Checkout 'plugin-editor-compatibility'
Write-Fixture ($uwp + '.meta') $uwpMeta
Write-Fixture ($uwp + '.meta') $uwpMeta.Replace("Any:`n      enabled: 0", "Any:`n      enabled: 1")
Assert-Checkout 'plugin-any-platform'
Write-Fixture ($uwp + '.meta') $uwpMeta
$internalMeta = [IO.File]::ReadAllText((Join-Path $fixture $internal))
Write-Fixture $internal $internalMeta.Replace('8cdcfdd141b51d5458f981aaf449fd9f','11111111111111111111111111111111')
Assert-Checkout 'rewired-locator-guid'
Write-Fixture $internal $internalMeta
Write-Fixture 'Assets/Plugins/FMOD/platforms/uwp/lib/x86_64/fmodstudioL.dll' 'leftover DLL'
Assert-Checkout 'untracked-plugin'
Write-Output "Checkout validation: 9 fixture and unified-entry checks passed. Fixture retained: $fixture"

