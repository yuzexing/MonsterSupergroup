[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$ReportPath
)
$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
if (-not $ReportPath) { $ReportPath = Join-Path $ProjectRoot 'Logs/RepositoryIntegrity/checkout.json' }
$ReportPath = [IO.Path]::GetFullPath($ReportPath)
$problems = [Collections.Generic.List[object]]::new()
$report = [ordered]@{ success=$false; project=$ProjectRoot; commit=''; unityVersion=''; checkedLfsFiles=0; checkedAssetFiles=0; problems=$problems }

function Add-CheckoutProblem([string]$Code, [string]$Path, [string]$Detail) {
    $problems.Add([pscustomobject]@{code=$Code;path=$Path;detail=$Detail})
}
function Invoke-CheckoutGit([string[]]$Arguments) {
    $result = & git -C $ProjectRoot @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed ($LASTEXITCODE)." }
    return $result
}
function Test-UnityAssetPath([string]$Path) {
    # Unity does not import hidden folders, package samples ending in ~, or bundle internals.
    return $Path -notmatch '(^|/)[.][^/]*|(^|/)[^/]*~(/|$)|[.](bundle|framework)/'
}
function Get-PluginEditorFlag([string]$Text) {
    # PluginImporter v1/v3 maps and v2 pair lists are both present in this repository.
    $match = [regex]::Match($Text, '(?m)^    Editor:\s*\r?\n      enabled: ([01])\s*$')
    if (-not $match.Success) {
        $match = [regex]::Match($Text, '(?m)^      Editor: Editor\s*\r?\n    second:\s*\r?\n      enabled: ([01])\s*$')
    }
    if (-not $match.Success) { return -1 }
    return [int]$match.Groups[1].Value
}
function Get-PluginAnyFlag([string]$Text) {
    $match = [regex]::Match($Text, '(?m)^    Any:\s*\r?\n      enabled: ([01])\s*$')
    if (-not $match.Success) {
        $match = [regex]::Match($Text, '(?m)^      Any:\s*\r?\n    second:\s*\r?\n      enabled: ([01])\s*$')
    }
    if (-not $match.Success) { return -1 }
    return [int]$match.Groups[1].Value
}

try {
    $report.commit = (Invoke-CheckoutGit @('rev-parse','HEAD')).Trim()
    $versionFile = Join-Path $ProjectRoot 'ProjectSettings/ProjectVersion.txt'
    $report.unityVersion = (Select-String -LiteralPath $versionFile -Pattern '^m_EditorVersion: (.+)$').Matches.Groups[1].Value.Trim()
    $tracked = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in (Invoke-CheckoutGit @('-c','core.quotepath=false','ls-files','--','Assets','Packages','ProjectSettings'))) {
        $null = $tracked.Add($path)
        if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot $path) -PathType Leaf)) {
            Add-CheckoutProblem 'tracked-file-missing' $path 'Restore the tracked file; do not let Unity regenerate it.'
        }
    }
    foreach ($path in $tracked) {
        if (-not $path.StartsWith('Assets/') -or -not (Test-UnityAssetPath $path)) { continue }
        if (-not $path.EndsWith('.meta')) {
            $report.checkedAssetFiles++
            if (-not $tracked.Contains($path + '.meta')) { Add-CheckoutProblem 'meta-not-tracked' $path 'Commit the asset and its original .meta together.' }
        }
    }
    foreach ($required in @('Packages/manifest.json','Packages/packages-lock.json','ProjectSettings/ProjectVersion.txt',
        'Assets/_Project/Scenes/Boot.unity','Assets/_Project/Scenes/MainMenu.unity','Assets/_Project/Scenes/Gameplay.unity',
        'Assets/_Project/Audio/FMODBanks/Master.bank','Assets/_Project/Audio/FMODBanks/Master.strings.bank')) {
        if (-not $tracked.Contains($required)) { Add-CheckoutProblem 'required-file-not-tracked' $required 'Required project input is absent from Git.' }
    }

    $lfs = ((Invoke-CheckoutGit @('lfs','ls-files','--json')) -join "`n" | ConvertFrom-Json).files
    if (@($lfs).Count -eq 0) { Add-CheckoutProblem 'lfs-list-empty' '.gitattributes' 'This project requires Git LFS; an empty inventory is invalid.' }
    foreach ($entry in $lfs) {
        $file = Join-Path $ProjectRoot $entry.name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }
        $report.checkedLfsFiles++
        $length = (Get-Item -LiteralPath $file).Length
        if ($length -ne $entry.size) {
            $pointer = $length -lt 1024 -and (Get-Content -LiteralPath $file -TotalCount 1) -eq 'version https://git-lfs.github.com/spec/v1'
            $code = if ($pointer) { 'lfs-pointer-not-downloaded' } else { 'lfs-size-mismatch' }
            Add-CheckoutProblem $code $entry.name "Expected $($entry.size) bytes; found $length. Run git lfs pull, then rerun this check."
        } elseif ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.oid) {
            Add-CheckoutProblem 'lfs-hash-mismatch' $entry.name 'Bytes differ from the committed LFS object. Preserve edits before restoring.'
        }
    }

    $core = 'Assets/_Project/Gameplay/Combat/Rewired/Internal/Libraries/Runtime/Rewired_Core.dll'
    $win = 'Assets/Plugins/FMOD/platforms/win/lib/x86_64/fmodstudioL.dll'
    $uwp = 'Assets/Plugins/FMOD/platforms/uwp/lib/x64/fmodstudioL.dll'
    foreach ($plugin in @($core,$win,$uwp)) {
        if (-not $tracked.Contains($plugin) -or -not $tracked.Contains($plugin + '.meta')) {
            Add-CheckoutProblem 'plugin-not-tracked' $plugin 'The DLL and original importer .meta must both be in Git.'
        }
    }
    foreach ($pair in @(@($core,1),@($win,1),@($uwp,0))) {
        $meta = Join-Path $ProjectRoot ($pair[0] + '.meta')
        if ((Test-Path -LiteralPath $meta) -and (Get-PluginEditorFlag (Get-Content -LiteralPath $meta -Raw)) -ne $pair[1]) {
            Add-CheckoutProblem 'plugin-editor-compatibility' ($pair[0] + '.meta') "Expected Editor enabled=$($pair[1]); restore the committed importer settings."
        }
    }
    foreach ($plugin in @($win,$uwp)) {
        $meta = Join-Path $ProjectRoot ($plugin + '.meta')
        if ((Test-Path -LiteralPath $meta) -and (Get-PluginAnyFlag (Get-Content -LiteralPath $meta -Raw)) -ne 0) {
            Add-CheckoutProblem 'plugin-any-platform' ($plugin + '.meta') 'FMOD native binaries must have explicit platform selection, not Any Platform.'
        }
    }
    $internal = 'Assets/_Project/Gameplay/Combat/Rewired/Internal.meta'
    if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot $internal)) -or
        (Get-Content -LiteralPath (Join-Path $ProjectRoot $internal) -Raw) -notmatch '(?m)^guid: 8cdcfdd141b51d5458f981aaf449fd9f\s*$') {
        Add-CheckoutProblem 'rewired-locator-guid' $internal 'Rewired locates its installation by this folder GUID; preserve the original .meta.'
    }
    foreach ($extra in (Invoke-CheckoutGit @('-c','core.quotepath=false','ls-files','--others','--exclude-standard','--','Assets/Plugins/FMOD','Assets/_Project/Gameplay/Combat/Rewired'))) {
        if ($extra -match '[.]dll([.]meta)?$') { Add-CheckoutProblem 'untracked-plugin' $extra 'A leftover or regenerated plugin may conflict with the committed installation.' }
    }
    $report.success = $problems.Count -eq 0
} catch {
    Add-CheckoutProblem 'check-failed' $ProjectRoot $_.Exception.Message
} finally {
    $report.success = $problems.Count -eq 0
    New-Item -ItemType Directory -Path (Split-Path -Parent $ReportPath) -Force | Out-Null
    $report | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
Write-Output "Checkout success=$($report.success), commit=$($report.commit), Unity=$($report.unityVersion), LFS=$($report.checkedLfsFiles). Report: $ReportPath"
if (-not $report.success) {
    $problems | Select-Object -First 12 | Format-Table -Wrap | Out-Host
    throw "Checkout validation failed: $ReportPath"
}
