$ErrorActionPreference = 'Stop'
function Read-CaseJson([string]$Path) { Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json }
function Write-CaseJson($Value, [string]$Path) { $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Path -Encoding UTF8 }
function Assert-CaseRelease {
    $release = Read-CaseJson (Join-Path $PSScriptRoot 'release.json')
    $reference = Join-Path $PSScriptRoot 'package-reference.json'
    if ($release.readyToRun -isnot [bool] -or -not $release.readyToRun) { throw 'This kit has not been released. Do not launch the game.' }
    if ($release.packageReferenceSha256 -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $reference -Algorithm SHA256).Hash -ine $release.packageReferenceSha256) { throw 'Released package reference is missing or changed.' }
    if ((Read-CaseJson $reference).packageVerified -ne $true) { throw 'Package reference was not verified.' }
    return $reference
}
function Assert-CaseOutside([string]$Path, [string]$Root) {
    $candidate = [IO.Path]::GetFullPath($Path).TrimEnd('\','/')
    $parent = [IO.Path]::GetFullPath($Root).TrimEnd('\','/')
    if ($candidate.Equals($parent,[StringComparison]::OrdinalIgnoreCase) -or $candidate.StartsWith($parent + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Case outputs must be outside the package and kit.' }
}
function New-CaseSuffix { (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8) }
function Write-CurrentCase([string]$CaseDirectory, [string]$PackageDirectory) {
    $path = Join-Path (Split-Path -Parent $PSScriptRoot) 'current-case.json'
    Write-CaseJson ([ordered]@{schemaVersion=1;caseDirectory=$(if ($CaseDirectory) { $CaseDirectory } else { $null });packageDirectory=$(if ($PackageDirectory) { $PackageDirectory } else { $null })}) $path
}
function Resolve-CaseArguments([string]$CaseDirectory, [string]$PackageDirectory) {
    if (-not $CaseDirectory) {
        $path = Join-Path (Split-Path -Parent $PSScriptRoot) 'current-case.json'
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'No current case. Start Host-Local.ps1 or Client-Local.ps1 first, or specify -CaseDirectory and -PackageDirectory for an older case.' }
        $current = Read-CaseJson $path
        if ($current.schemaVersion -ne 1 -or $current.caseDirectory -isnot [string] -or
            [string]::IsNullOrWhiteSpace($current.caseDirectory) -or $current.packageDirectory -isnot [string] -or
            [string]::IsNullOrWhiteSpace($current.packageDirectory)) { throw 'Current case is not ready. Wait for the launcher to print CaseDirectory, or specify the older case explicitly.' }
        if (-not [IO.Path]::IsPathRooted($current.caseDirectory) -or -not [IO.Path]::IsPathRooted($current.packageDirectory)) { throw 'Current case paths are invalid. Start a new case or specify the case explicitly.' }
        $CaseDirectory = $current.caseDirectory
        if (-not $PackageDirectory) { $PackageDirectory = $current.packageDirectory }
    }
    if (-not $PackageDirectory) { $PackageDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'product' }
    return @{CaseDirectory=$CaseDirectory;PackageDirectory=$PackageDirectory}
}
function Read-CaseContext([string]$CaseDirectory, [string]$PackageDirectory) {
    $case = (Resolve-Path -LiteralPath $CaseDirectory).Path
    $package = (Resolve-Path -LiteralPath $PackageDirectory).Path
    Assert-CaseOutside $case $package
    $metadata = Read-CaseJson (Join-Path $case 'case.json')
    if (-not [string]::Equals($metadata.packageDirectory,$package,[StringComparison]::OrdinalIgnoreCase)) { throw 'Case belongs to a different package location.' }
    $manifest = Join-Path $case 'capture/capture.json'
    $capture = Read-CaseJson $manifest
    if (-not $capture.processId -or -not $capture.startedUtc -or -not $capture.executable -or -not $capture.launcherArgumentsApplied -or $capture.attached) { throw 'Capture has no launched process identity.' }
    if ($capture.requestedEvidenceMode -cne $metadata.mode -or $capture.expectedRole -ine $metadata.role) { throw 'Capture mode or role differs from case metadata.' }
    if ($metadata.requestedEvidenceProfile -and $capture.requestedEvidenceProfile -cne $metadata.requestedEvidenceProfile) { throw 'Capture evidence profile differs from case metadata.' }
    return @{Case=$case;Package=$package;Metadata=$metadata;Manifest=$manifest;Capture=$capture}
}
