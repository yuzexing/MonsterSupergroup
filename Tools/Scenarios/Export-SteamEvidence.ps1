[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][ValidateSet('Host','Client')][string]$Role,
    [Parameter(Mandatory)][ValidateSet('off','local','replicated')][string]$Mode,
    [string[]]$LogPath = @(),
    [string]$ExpectedProof,
    [string]$CaptureManifest,
    [ValidateRange(1,2147483647)][Nullable[int]]$GameProcessId
)
# Read-only package inspection and copies of explicitly selected logs. No game
# launch, process attachment, gameplay control, registry changes or deletion.
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ProjectTools.psm1')
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'SteamEvidenceIdentity.psm1') -Force
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path.TrimEnd('\','/')
$output = [IO.Path]::GetFullPath($ArtifactDirectory).TrimEnd('\','/')
function Within([string]$path, [string]$root) {
    return $path.Equals($root,[StringComparison]::OrdinalIgnoreCase) -or $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
}
function Assert-Physical([string]$path) {
    $item = Get-Item -LiteralPath $path -Force
    while ($item) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked inputs are not accepted: $($item.FullName)" }
        $item = if ($item -is [IO.DirectoryInfo]) { $item.Parent } else { $item.Directory }
    }
}
function Physical-Files([string]$path) {
    Assert-Physical $path
    $item = Get-Item -LiteralPath $path -Force
    if (-not $item.PSIsContainer) { return $item }
    # Inspect each child before recursion, so a junction never escapes the input.
    foreach ($child in Get-ChildItem -LiteralPath $path -Force) {
        if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked input: $($child.FullName)" }
        if ($child.PSIsContainer) { Physical-Files $child.FullName } else { $child }
    }
}
function Save-Json($value, [string]$name) {
    [IO.File]::WriteAllText((Join-Path $output $name), ($value | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
}
function Sha([string]$path) {
    $stream = [IO.File]::OpenRead($path); $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}
function Copy-Verified([string]$source, [string]$destination) {
    $before = Sha $source
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
    $copied = Sha $destination
    if ($before -ne $copied -or $copied -ne (Sha $source)) { throw "Input changed during archival; preserve this failed capture and retry after exit: $source" }
    return [ordered]@{source=$source; archived=$destination.Substring($output.Length+1).Replace('\','/');bytes=(Get-Item -LiteralPath $destination).Length;sha256=$copied}
}
if (Within $output $package) { throw 'ArtifactDirectory must be outside the selected package.' }
if (Test-Path -LiteralPath $output) { throw 'Use a fresh ArtifactDirectory.' }
Assert-Physical $package
$existingParent = Split-Path -Parent $output
while (-not (Test-Path -LiteralPath $existingParent)) { $existingParent = Split-Path -Parent $existingParent }
Assert-Physical $existingParent
New-Item -ItemType Directory -Path $output | Out-Null
$proof = [ordered]@{
    schemaVersion=1; capturedUtc=[DateTime]::UtcNow.ToString('o');machine=$env:COMPUTERNAME;role=$Role;mode=$Mode;
    packageDirectory=$package; packageVerified=$false; packageMatchesExpected=$null;
    identity=$null; archived=@(); requestedLogs=$LogPath; logArchiveComplete=$false;
    runtime=[ordered]@{attributionVerified=$false;captureManifest=$null;liveProcess=$null;reason='No explicit PID or process-bound capture manifest supplied'};
    verdict='NotVerified';errors=@(); limitations=@('Package identity and copied logs do not prove business correctness, coverage completeness or Steam performance.')
}
try {
    $executable = Resolve-ProjectBuildExecutable -ProjectRoot $package -Recipe product -BuildDirectory $package
    $data = Join-Path $package ([IO.Path]::GetFileNameWithoutExtension($executable) + '_Data')
    $infoPath = Join-Path $data 'StreamingAssets/BuildInfo.json'
    $markerPath = Join-Path $package 'build-complete.json'
    $combatPath = Join-Path $package 'combat-build.json'
    foreach ($path in @($infoPath,$markerPath,$combatPath,(Join-Path $package 'UnityPlayer.dll'))) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Incomplete package: $path" }
        Assert-Physical $path
    }
    if ((Sha $infoPath) -ne (Sha $markerPath)) { throw 'BuildInfo does not match build-complete.json.' }
    $info = Get-Content -LiteralPath $infoPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $combat = Get-Content -LiteralPath $combatPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $manifest = $combat.manifest | ConvertFrom-Json
    if (-not $info.buildId -or $info.buildId -cne $combat.buildId -or $info.buildId -cne $manifest.buildId) { throw 'BuildId mismatch or missing.' }
    if ($info.profile -cne 'product' -or $info.kind -cne 'test' -or $info.network -cne 'Steam' -or
        $info.distribution -notin @('Direct','Steam') -or $info.diagnostics -cne 'Evidence' -or
        $info.development -isnot [bool] -or $info.development -or $info.evidence -isnot [bool] -or -not $info.evidence -or
        $info.testAssemblies -isnot [bool] -or $info.testAssemblies -or $info.developmentTools -isnot [bool] -or $info.developmentTools) {
        throw 'Expected a complete product/Test/Steam/Evidence package without Development or test/tool capability.'
    }
    $guid = [Guid]::Empty
    if (-not [Guid]::TryParse([string]$combat.buildGuid,[ref]$guid) -or $guid -eq [Guid]::Empty) { throw 'Missing or invalid Build GUID.' }
    if ([string]$manifest.protocol -notmatch '^\d+$' -or [int]$manifest.protocol -le 0 -or
        $manifest.sourceConfigurationHash -notmatch '^[0-9a-fA-F]{64}$' -or
        $manifest.logFormat -ne 2 -or $manifest.replicationProtocol -ne 2 -or $manifest.replayFormat -ne 2) {
        throw 'Missing source hash/protocol or unsupported evidence formats.'
    }
    $appId = Join-Path $package 'steam_appid.txt'
    if ($info.distribution -eq 'Direct' -and (-not (Test-Path -LiteralPath $appId) -or (Get-Content -LiteralPath $appId -Raw).Trim() -ne '4886160')) { throw 'Direct package Steam AppID is missing or incorrect.' }
    $files = @(Physical-Files $package)
    $names = [string[]]@($files.FullName); [Array]::Sort($names,[StringComparer]::Ordinal)
    $packageFiles = @($names | ForEach-Object { [ordered]@{path=$_.Substring($package.Length+1).Replace('\','/');bytes=(Get-Item -LiteralPath $_).Length;sha256=(Sha $_)} })
    Save-Json $packageFiles 'package-files.json'
    # JSON pretty-printing differs between Windows PowerShell and PowerShell 7.
    # Compare an ordinal, UTF-8 canonical file list, while preserving the actual
    # readable manifest hash separately for archival checks.
    $lines = [string[]]@($packageFiles | ForEach-Object { $_.path + [char]0 + $_.bytes.ToString([Globalization.CultureInfo]::InvariantCulture) + [char]0 + $_.sha256 })
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { $contentHash = [BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes([string]::Join([string][char]10,$lines)))).Replace('-','').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
    $proof.identity = [ordered]@{executable=$executable;executableSha256=(Sha $executable);buildId=$info.buildId;buildGuid=$guid.ToString('N');
        protocol=[string]$manifest.protocol;sourceConfigurationHash=$manifest.sourceConfigurationHash.ToLowerInvariant();
        logFormat=$manifest.logFormat;replicationProtocol=$manifest.replicationProtocol;replayFormat=$manifest.replayFormat;
        buildInfoSha256=(Sha $infoPath);combatBuildSha256=(Sha $combatPath);packageFilesSha256=(Sha (Join-Path $output 'package-files.json'));packageContentSha256=$contentHash}
    foreach ($path in @($infoPath,$markerPath,$combatPath)) { $proof.archived += Copy-Verified $path (Join-Path $output ('package-identity/' + [IO.Path]::GetFileName($path))) }
    if ($info.distribution -eq 'Direct') { $proof.archived += Copy-Verified $appId (Join-Path $output 'package-identity/steam_appid.txt') }
    $proof.packageVerified = $true
    $proof.archived += Copy-Verified $PSCommandPath (Join-Path $output 'tool-sources/Scenarios/Export-SteamEvidence.ps1')
    $proof.archived += Copy-Verified (Join-Path (Split-Path -Parent $PSScriptRoot) 'ProjectTools.psm1') (Join-Path $output 'tool-sources/ProjectTools.psm1')
    $proof.archived += Copy-Verified (Join-Path (Split-Path -Parent $PSScriptRoot) 'SteamEvidenceIdentity.psm1') (Join-Path $output 'tool-sources/SteamEvidenceIdentity.psm1')
    if ($ExpectedProof) {
        $reference = Get-Content -LiteralPath (Resolve-Path -LiteralPath $ExpectedProof).Path -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($reference.packageVerified -ne $true -or -not $reference.identity) { throw 'Reference machine proof did not verify its package.' }
        $mismatches = @('executableSha256','buildId','buildGuid','protocol','sourceConfigurationHash','logFormat','replicationProtocol','replayFormat','buildInfoSha256','combatBuildSha256','packageContentSha256') |
            Where-Object { [string]$proof.identity[$_] -cne [string]$reference.identity.$_ }
        $proof.packageMatchesExpected = @($mismatches).Count -eq 0
        if (-not $proof.packageMatchesExpected) { throw ('Package differs from reference: ' + ($mismatches -join ', ')) }
        $proof.archived += Copy-Verified (Resolve-Path -LiteralPath $ExpectedProof).Path (Join-Path $output 'reference-machine-proof.json')
    }
    if ($CaptureManifest) {
        $capturePath = (Resolve-Path -LiteralPath $CaptureManifest).Path
        $capture = Get-Content -LiteralPath $capturePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $proof.archived += Copy-Verified $capturePath (Join-Path $output 'capture.json')
        $proof.runtime.captureManifest = $capturePath
        if (-not $capture.processId -or -not $capture.startedUtc -or -not $capture.actualCommandLine -or
            -not $capture.executable -or [IO.Path]::GetFullPath([string]$capture.executable) -ne $executable) {
            throw 'Capture manifest does not attribute the process to this exact executable.'
        }
        # Record only what the existing collector observed; do not infer package
        # hashes at past process start or retroactively enable logging.
        $proof.runtime.attributionVerified = $true
        $proof.runtime.reason = 'Existing collector recorded this executable, process ID, start time and command line; archive-time hashes only'
        $proof.runtime.processId = $capture.processId; $proof.runtime.startedUtc = $capture.startedUtc
        $proof.runtime.actualCommandLine = $capture.actualCommandLine; $proof.runtime.normalExit = $capture.normalExit
    }
    if ($GameProcessId) {
        $proof.runtime.attributionVerified = $false
        $proof.runtime.reason = 'Explicit PID verification pending'
        $live = Get-SteamEvidenceRuntimeIdentity -ExpectedExecutable $executable -GameProcessId $GameProcessId
        if ($CaptureManifest -and ($capture.processId -ne $GameProcessId -or
            [Math]::Abs((([DateTime]$capture.startedUtc).ToUniversalTime() - ([DateTime]$live.startedUtc).ToUniversalTime()).TotalSeconds) -gt 1)) {
            throw 'The live PID and creation time do not match the supplied capture manifest.'
        }
        $proof.runtime.liveProcess = $live
        $proof.runtime.attributionVerified = $true
        $proof.runtime.processId = $live.processId; $proof.runtime.startedUtc = $live.startedUtc
        $proof.runtime.reason = $live.reason
    }
    for ($index=0; $index -lt $LogPath.Count; $index++) {
        $source = (Resolve-Path -LiteralPath $LogPath[$index]).Path.TrimEnd('\','/')
        if ((Within $output $source) -or (Within $source $output)) { throw 'Log inputs cannot contain or be inside the artifact output.' }
        $item = Get-Item -LiteralPath $source
        $destination = Join-Path $output (('logs/{0:D3}-' -f $index) + $item.Name)
        $selected = @(Physical-Files $source)
        if (-not $selected.Count) { throw "Selected log directory is empty: $source" }
        foreach ($file in $selected) {
            $target = if ($item.PSIsContainer) { Join-Path $destination $file.FullName.Substring($source.Length+1) } else { $destination }
            $proof.archived += Copy-Verified $file.FullName $target
        }
    }
    $proof.logArchiveComplete = $LogPath.Count -gt 0
    $proof.verdict = if ($proof.logArchiveComplete) { 'PackageVerifiedSelectedLogsArchived' } elseif ($proof.runtime.attributionVerified) { 'PackageAndRuntimeIdentityVerified' } else { 'PackageIdentityOnly' }
}
catch { $proof.errors += $_.Exception.Message; $proof.verdict = 'Failed' }
finally { Save-Json $proof 'machine-proof.json' }
Write-Output "Machine proof: $(Join-Path $output 'machine-proof.json')"
if ($proof.errors.Count) { throw ($proof.errors -join '; ') }
